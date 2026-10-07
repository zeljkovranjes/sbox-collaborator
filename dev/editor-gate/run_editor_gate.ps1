# Collaborator editor gate: the library end to end inside a real s&box editor, against a live
# Collaborator server, with a scripted teammate. Fails the moment anything goes wrong.
#
#   powershell -ExecutionPolicy Bypass -File dev\editor-gate\run_editor_gate.ps1
#   ... -Clean        rebuild the scratch project from the template
#   ... -KeepGoing    do not stop at the first failed check (collect every failure)
#   ... -KeepServer   leave the test server running afterwards (prints its URL and keys)
#
# Steps:
#   1. offline compile check (dotnet build dev\CompileCheck.csproj)      - instant fail on C# errors
#   2. builds/seeds/starts a throwaway Collaborator server (SQLite in %TEMP%)
#   3. creates a scratch s&box project in %TEMP% with this library junctioned into Libraries/
#   4. arms the in-editor gate (Editor/Dev/EditorGate.cs) and launches sbox-dev.exe
#   5. while it runs: prints each check as it lands, answers screenshot / server stop-start
#      requests, and tails sbox-dev.log + the server log. The FIRST compile error, SB500 /
#      whitelist violation, exception from our code, server error or failed check kills the
#      editor and fails the run immediately.
#
# Exit codes: 0 pass, 1 a check or scan failed, 2 the gate could not run / produced no result.
# Needs: Steam running, a desktop session, Node 22+, the server repo (default ..\..\02_Server).

[CmdletBinding()]
param(
    [string]$SboxRoot = "C:\Program Files (x86)\Steam\steamapps\common\sbox",
    [string]$ServerRoot = "",
    [int]$Port = 18787,
    [int]$TimeoutSec = 900,
    [int]$StartTimeoutSec = 240,
    [switch]$Clean,
    [switch]$KeepGoing,
    [switch]$KeepServer,
    [switch]$RealGitHub,             # sign in with a real GitHub login in the browser (needs an OAuth app, see README)
    [string]$GithubClientId = "",
    [string]$GithubClientSecret = ""
)

$ErrorActionPreference = "Stop"
if ($RealGitHub) {
    if ($GithubClientId -eq "" -or $GithubClientSecret -eq "") { Write-Host "-RealGitHub needs -GithubClientId and -GithubClientSecret (an OAuth app with callback http://127.0.0.1:$Port/auth/github/callback)"; exit 2 }
    $TimeoutSec += 300
}
$libRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\..")).Path
if ($ServerRoot -eq "") { $ServerRoot = Join-Path $libRoot "..\..\02_Server" }
$ServerRoot = (Resolve-Path $ServerRoot).Path

$work     = Join-Path $env:TEMP "collab-editor-gate"          # outside the library: no junction cycle
$scratch  = Join-Path $work "scratch"
$sbproj   = Join-Path $scratch "collabgate.sbproj"
$libLink  = Join-Path $scratch "Libraries\local.collaborator"
$outDir   = Join-Path $work "out"
$srvDir   = Join-Path $work "server"
$result   = Join-Path $outDir "gate_result.json"
$sboxExe  = Join-Path $SboxRoot "sbox-dev.exe"
$sboxLog  = Join-Path $SboxRoot "logs\sbox-dev.log"
$template = Join-Path $SboxRoot "templates\game.minimal"
$baseUrl  = "http://127.0.0.1:$Port"

$script:editor = $null
$script:server = $null
$script:failures = New-Object System.Collections.Generic.List[string]

function Say([string]$text, [string]$color = "Gray") { Write-Host $text -ForegroundColor $color }
function Stop-Editor {
    if ($script:editor -and -not $script:editor.HasExited) {
        taskkill /PID $script:editor.Id /T /F 2>$null | Out-Null
    }
}
function Stop-Server {
    if ($script:server -and -not $script:server.HasExited) {
        taskkill /PID $script:server.Id /T /F 2>$null | Out-Null
        $script:server.WaitForExit(5000) | Out-Null
    }
}
function Finish([int]$code, [string]$message) {
    Stop-Editor
    if (-not $KeepServer) { Stop-Server }
    Say ""
    if ($script:failures.Count -gt 0) {
        Say "===== failures =====" Red
        $script:failures | ForEach-Object { Say $_ Red }
    }
    $shots = Get-ChildItem $outDir -Filter *.png -ErrorAction SilentlyContinue
    if ($shots) { Say "screenshots: $outDir ($($shots.Count) png)" Cyan }
    Say "result json: $result" Cyan
    if ($KeepServer -and $script:server -and -not $script:server.HasExited) { Say "server still running at $baseUrl (admin key in $srvDir\keys.json)" Cyan }
    Say "RESULT: $message" $(if ($code -eq 0) { "Green" } else { "Red" })
    exit $code
}
function Fail-Now([string]$why) {
    $script:failures.Add($why)
    if (-not $KeepGoing) { Finish 1 "FAIL - $why" }
}

if (-not (Test-Path $sboxExe)) { Write-Host "sbox-dev.exe not found at $sboxExe"; exit 2 }
if (-not (Get-Command node -ErrorAction SilentlyContinue)) { Write-Host "node is not on PATH"; exit 2 }
if (-not (Get-Process steam -ErrorAction SilentlyContinue)) { Write-Warning "steam.exe is not running - sbox-dev.exe may fail to boot." }
New-Item -ItemType Directory -Force $work, $outDir, $srvDir | Out-Null
Get-ChildItem $outDir -File -ErrorAction SilentlyContinue | Remove-Item -Force

# ---------------------------------------------------------------- 1. offline compile check
Say "[1/5] offline compile check" Cyan
$build = & dotnet build (Join-Path $libRoot "dev\CompileCheck.csproj") --nologo -v:q 2>&1
$errors = @($build | Where-Object { $_ -match ' error ' } | Sort-Object -Unique)
if ($errors.Count -gt 0) {
    $errors | Select-Object -First 30 | ForEach-Object { Say $_ Red }
    Write-Host "RESULT: FAIL - the library does not compile (see above)" -ForegroundColor Red
    exit 1
}
Say "  compiles (0 errors)" Green

# ---------------------------------------------------------------- 2. test server
Say "[2/5] test server ($baseUrl)" Cyan
$distIndex = Join-Path $ServerRoot "dist\src\index.js"
$newestSrc = (Get-ChildItem (Join-Path $ServerRoot "src") -Recurse -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
if (-not (Test-Path $distIndex) -or (Get-Item $distIndex).LastWriteTime -lt $newestSrc) {
    Say "  building server (npm run build:server)"
    Push-Location $ServerRoot
    try { & npm run build:server 2>&1 | Out-Null; if ($LASTEXITCODE -ne 0) { throw "server build failed" } } finally { Pop-Location }
}

$dbPath = Join-Path $srvDir "gate.sqlite"
Get-ChildItem $srvDir -Filter "gate.sqlite*" -ErrorAction SilentlyContinue | Remove-Item -Force
$secret = -join ((1..64) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
$webhookSecret = -join ((1..40) | ForEach-Object { '{0:x}' -f (Get-Random -Maximum 16) })
$serverEnv = @{
    SECRET_KEY = $secret; SQLITE_PATH = $dbPath; PORT = "$Port"; HOST = "127.0.0.1"; PUBLIC_URL = $baseUrl
    LOG_LEVEL = "info"; SERVER_NAME = "Collaborator Gate"; WEB_DIR = (Join-Path $ServerRoot "dist\web")
    GITHUB_WEBHOOK_SECRET = $webhookSecret
}
if ($RealGitHub) { $serverEnv.GITHUB_OAUTH_CLIENT_ID = $GithubClientId; $serverEnv.GITHUB_OAUTH_CLIENT_SECRET = $GithubClientSecret }
function With-ServerEnv([scriptblock]$body) {
    $saved = @{}
    foreach ($k in $serverEnv.Keys) { $saved[$k] = [Environment]::GetEnvironmentVariable($k); [Environment]::SetEnvironmentVariable($k, $serverEnv[$k]) }
    try { & $body } finally { foreach ($k in $saved.Keys) { [Environment]::SetEnvironmentVariable($k, $saved[$k]) } }
}
$cli = Join-Path $ServerRoot "dist\src\cli.js"
function Invoke-ServerCli([string[]]$cliArgs) { With-ServerEnv { & node $cli @cliArgs 2>&1 | Out-String } }
# The CLI prints the key's metadata (with a short "prefix") and then the full key on its own line.
function TokenFrom([string]$text) {
    $found = [regex]::Matches($text, '(?m)^\s+(sb[cj]_[0-9a-z]{12}_[A-Za-z0-9_-]{20,})\s*$')
    if ($found.Count -eq 0) { throw "no key in CLI output:`n$text" }
    return $found[$found.Count - 1].Groups[1].Value
}

Invoke-ServerCli @("developer", "add", "gate", "--name", "Gate Dev", "--admin") | Out-Null
Invoke-ServerCli @("developer", "add", "mate", "--name", "Teammate") | Out-Null
Invoke-ServerCli @("project", "create", "collabgate", "--name", "Collab Gate", "--kind", "game", "--ident", "collabgate", "--repo", "gatefixture/collabgate") | Out-Null
$adminKey  = TokenFrom (Invoke-ServerCli @("key", "create", "gate", "--name", "gate approver"))
$mateKey   = TokenFrom (Invoke-ServerCli @("key", "create", "mate", "--name", "teammate codex"))
$serverKey = TokenFrom (Invoke-ServerCli @("server-key", "create", "--name", "gate", "--uses", "5"))
@{ admin = $adminKey; mate = $mateKey; serverKey = $serverKey; url = $baseUrl } | ConvertTo-Json | Set-Content (Join-Path $srvDir "keys.json")

$srvOut = Join-Path $srvDir "server.out.log"
$srvErr = Join-Path $srvDir "server.err.log"
$script:srvLogPos = 0
function Start-TestServer {
    With-ServerEnv {
        $script:server = Start-Process -FilePath "node" -ArgumentList @("`"$distIndex`"") -WorkingDirectory $ServerRoot `
            -RedirectStandardOutput $srvOut -RedirectStandardError $srvErr -WindowStyle Hidden -PassThru
    }
    $deadline = (Get-Date).AddSeconds(30)
    while ((Get-Date) -lt $deadline) {
        try { if ((Invoke-RestMethod "$baseUrl/healthz" -TimeoutSec 2).ok) { return $true } } catch { }
        Start-Sleep -Milliseconds 300
    }
    return $false
}
if (-not (Start-TestServer)) { Get-Content $srvErr -ErrorAction SilentlyContinue | Select-Object -Last 20 | ForEach-Object { Say $_ Red }; Finish 2 "test server did not start" }
Say "  server up; developers gate/mate, project collabgate, server key issued" Green

# ---------------------------------------------------------------- 3. scratch project
Say "[3/5] scratch project $scratch" Cyan
if ($Clean -and (Test-Path $scratch)) {
    if (Test-Path $libLink) { cmd /c rmdir "$libLink" | Out-Null }   # never recurse into the library
    Remove-Item -Recurse -Force $scratch
}
if (-not (Test-Path $sbproj)) {
    New-Item -ItemType Directory -Force $scratch | Out-Null
    foreach ($d in "Assets", "Code", "Editor") { if (Test-Path (Join-Path $template $d)) { Copy-Item (Join-Path $template $d) (Join-Path $scratch $d) -Recurse -Force } }
    $proj = Get-Content (Join-Path $template "`$ident.sbproj") -Raw
    $proj = $proj -replace '"Title":\s*"[^"]*"', '"Title": "Collab Gate"'
    $proj = $proj -replace '"Ident":\s*"[^"]*"', '"Ident": "collabgate"'
    [System.IO.File]::WriteAllText($sbproj, $proj)
}
foreach ($leftover in "Assets\weather", "Assets\ships") { $p = Join-Path $scratch $leftover; if (Test-Path $p) { Remove-Item -Recurse -Force $p } }
New-Item -ItemType Directory -Force (Join-Path $scratch "Libraries") | Out-Null
if (Test-Path $libLink) {
    $item = Get-Item $libLink -Force
    if (-not ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $item.Target -ne $libRoot) { cmd /c rmdir "$libLink" 2>$null | Out-Null; if (Test-Path $libLink) { Remove-Item -Recurse -Force $libLink } }
}
if (-not (Test-Path $libLink)) { New-Item -ItemType Junction -Path $libLink -Value $libRoot | Out-Null }
Say "  library junctioned -> $libRoot" Green

# ---------------------------------------------------------------- 4. launch
Say "[4/5] launching the editor" Cyan
Set-Content -Path "$result.arm" -Value (Get-Date -Format o) -Encoding ascii
$gateEnv = @{
    COLLAB_GATE_RESULT = $result; COLLAB_GATE_SERVER = $baseUrl; COLLAB_GATE_SERVER_KEY = $serverKey
    COLLAB_GATE_ADMIN_TOKEN = $adminKey; COLLAB_GATE_MATE_TOKEN = $mateKey; COLLAB_GATE_PROJECT = "collabgate"; COLLAB_GATE_SCRATCH = $scratch
    COLLAB_GATE_WEBHOOK_SECRET = $webhookSecret
}
if ($RealGitHub) { $gateEnv.COLLAB_GATE_REAL_GITHUB = "1" }
$logStart = 0
if (Test-Path $sboxLog) { $logStart = (Get-Item $sboxLog).Length }
foreach ($k in $gateEnv.Keys) { [Environment]::SetEnvironmentVariable($k, $gateEnv[$k]) }
try {
    $script:editor = Start-Process -FilePath $sboxExe -ArgumentList @("-project", "`"$sbproj`"") -WorkingDirectory $SboxRoot -PassThru
} finally {
    foreach ($k in $gateEnv.Keys) { [Environment]::SetEnvironmentVariable($k, $null) }
}

# ---------------------------------------------------------------- window capture
Add-Type -AssemblyName System.Drawing
Add-Type @"
using System;
using System.Text;
using System.Runtime.InteropServices;
public static class GateWin {
    public delegate bool EnumProc(IntPtr h, IntPtr l);
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    public static IntPtr Find(string title) {
        IntPtr found = IntPtr.Zero;
        EnumWindows((h, l) => {
            if (!IsWindowVisible(h)) return true;
            var sb = new StringBuilder(256); GetWindowText(h, sb, 256);
            if (sb.ToString() == title) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }
}
"@
function Capture([string]$target, [string]$title = "Collaborator") {
    $h = [GateWin]::Find($title)
    if ($h -eq [IntPtr]::Zero) { return $false }
    # No SetForegroundWindow: pulling the window under the user's cursor lets a stray click land on it.
    # PrintWindow with PW_RENDERFULLCONTENT draws the window even when other windows cover it.
    $r = New-Object GateWin+RECT
    [GateWin]::GetWindowRect($h, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $hh = $r.Bottom - $r.Top
    if ($w -le 0 -or $hh -le 0) { return $false }
    $bmp = New-Object System.Drawing.Bitmap $w, $hh
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $dc = $g.GetHdc(); $ok = [GateWin]::PrintWindow($h, $dc, 2); $g.ReleaseHdc($dc)
    $g.Dispose()
    if ($ok) { $bmp.Save($target, [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    return $ok
}

# ---------------------------------------------------------------- 5. watch
Say "[5/5] watching (first error stops the run)" Cyan
# Our code, as it appears in compiler output and stack traces.
$ours = '(?i)(local\.collaborator|[\\/]collaborator[\\/]Editor|Collaborator\.(EditorTools|UI|Net|Dev|CollabSession|AssetSync|AssetGuard|Settings|Toasts|ProjectPaths|EditorThread)|\bcollaborator\.editor\b)'
$compileError = '(?i)(error\s+CS\d{4}|\bSB500\b|whitelist|failed to compile|compile(r)? error)'
$exceptionLine = '(?i)(exception|unhandled)'
$logPos = $logStart
$seenChecks = 0
$started = $false
$launchedAt = Get-Date
$deadline = $launchedAt.AddSeconds($TimeoutSec)
$logTail = New-Object System.Collections.Generic.List[string]

function Read-New([string]$path, [ref]$pos) {
    if (-not (Test-Path $path)) { return @() }
    $fs = [System.IO.File]::Open($path, 'Open', 'Read', 'ReadWrite')
    try {
        if ($fs.Length -lt $pos.Value) { $pos.Value = 0 }          # rotated / truncated on boot
        if ($fs.Length -eq $pos.Value) { return @() }
        $fs.Seek($pos.Value, 'Begin') | Out-Null
        $sr = New-Object System.IO.StreamReader($fs)
        $text = $sr.ReadToEnd()
        $pos.Value = $fs.Length
        return $text -split "`r?`n" | Where-Object { $_ -ne "" }
    } finally { $fs.Dispose() }
}

while ($true) {
    # --- editor log: compile errors and exceptions from our code fail instantly
    foreach ($line in (Read-New $sboxLog ([ref]$logPos))) {
        $logTail.Add($line); if ($logTail.Count -gt 60) { $logTail.RemoveAt(0) }
        if ($line -match '\[collab-gate\]') { continue }                     # the gate's own narration
        if ($line -match $ours -and $line -match $compileError) { Say $line Red; Fail-Now "editor compile error: $line" }
        elseif ($line -match $ours -and $line -match $exceptionLine) { Say $line Red; Fail-Now "exception in the library: $line" }
        elseif ($line -match '(?i)\[collaborator\].*(fail|error)') { Say $line Yellow; Fail-Now "library reported an error: $line" }
    }
    # --- server log: any error-level line is a contract/server bug
    foreach ($line in (Read-New $srvErr ([ref]$script:srvLogPos))) {
        if ($line -match '"level":"error"') { Say "server: $line" Red; Fail-Now "server error: $line" }
    }

    # --- result file: print checks as they land, stop on the first failure
    if (Test-Path $result) {
        $json = $null
        try { $json = Get-Content $result -Raw | ConvertFrom-Json } catch { }
        if ($json) {
            if (-not $started -and $json.started) { $started = $true; Say "  gate armed after $([int]((Get-Date) - $launchedAt).TotalSeconds) s" Green }
            $checks = @($json.checks)
            for ($i = $seenChecks; $i -lt $checks.Count; $i++) {
                $c = $checks[$i]
                if ($c.ok) { Say ("  PASS {0,-34} {1,6} ms  {2}" -f $c.name, $c.ms, ($c.detail -split "`n")[0]) Green }
                else { Say ("  FAIL {0,-34} {1,6} ms  {2}" -f $c.name, $c.ms, $c.detail) Red; Fail-Now "check $($c.name): $(($c.detail -split "`n")[0])" }
            }
            $seenChecks = $checks.Count
            if ($json.fatal) { Say "  FATAL $($json.fatal)" Red; Fail-Now "gate fatal: $(($json.fatal -split "`n")[0])" }
            if ($json.completed) { break }
        }
    }

    # --- requests from the gate
    foreach ($req in Get-ChildItem $outDir -Filter "*.req" -ErrorAction SilentlyContinue) {
        $name = [IO.Path]::GetFileNameWithoutExtension($req.Name)
        $payload = (Get-Content $req.FullName -Raw -Encoding UTF8 -ErrorAction SilentlyContinue)
        Remove-Item $req.FullName -Force -ErrorAction SilentlyContinue
        $ok = $true
        if ($name -like "shot_*") {
            # payload: "<png path>" or "<png path>|<window title>"
            $parts = $payload.Trim() -split '\|', 2
            $ok = if ($parts.Count -gt 1) { Capture $parts[0] $parts[1] } else { Capture $parts[0] }
            if ($ok) { Say "  shot  $([IO.Path]::GetFileName($parts[0]))" DarkGray } else { Say "  shot  $([IO.Path]::GetFileName($parts[0])) FAILED (window not found)" Yellow }
        }
        elseif ($name -eq "server_stop") { Stop-Server; Say "  server stopped (resilience check)" DarkGray }
        elseif ($name -eq "server_start") { $ok = Start-TestServer; Say "  server restarted: $ok" DarkGray }
        if ($ok) { Set-Content (Join-Path $outDir "$name.ok") "ok" }
    }

    if ($script:editor.HasExited) { Fail-Now "the editor exited before the gate completed (exit code $($script:editor.ExitCode))"; break }
    if (-not $started -and ((Get-Date) - $launchedAt).TotalSeconds -gt $StartTimeoutSec) {
        $logTail | Select-Object -Last 30 | ForEach-Object { Say $_ DarkGray }
        Fail-Now "the gate never started within $StartTimeoutSec s (library failed to compile or load? log tail above)"
        break
    }
    if ((Get-Date) -gt $deadline) { Fail-Now "timed out after $TimeoutSec s"; break }
    Start-Sleep -Milliseconds 250
}

if ($script:editor -and -not $script:editor.HasExited) { $script:editor.WaitForExit(20000) | Out-Null }
if (-not (Test-Path $result)) { Finish 2 "NO RESULT - the gate never wrote $result" }
$final = Get-Content $result -Raw | ConvertFrom-Json
if ($final.toasts) { Say "toasts seen: $(@($final.toasts).Count)" DarkGray }
$failedChecks = @($final.checks | Where-Object { -not $_.ok })
if ($script:failures.Count -eq 0 -and $final.passed) { Finish 0 "PASS - $(@($final.checks).Count) checks passed in the real editor" }
Finish 1 "FAIL - $($failedChecks.Count) failed check(s), $($script:failures.Count) failure(s)"
