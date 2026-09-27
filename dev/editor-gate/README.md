# Editor gate

Runs the Collaborator library end to end inside a real s&box editor against a live Collaborator
server, with a scripted teammate, and stops at the first problem. Same approach as the humanoid
retargeter's editor rig: a PowerShell driver, a one-shot armed in-editor hook, a JSON result.

```powershell
powershell -ExecutionPolicy Bypass -File dev\editor-gate\run_editor_gate.ps1
#   -Clean       rebuild the scratch project from templates\game.minimal
#   -KeepGoing   collect every failure instead of stopping at the first
#   -KeepServer  leave the test server running (URL + keys in %TEMP%\collab-editor-gate\server\keys.json)
```

Exit codes: `0` pass, `1` a check or scan failed, `2` the gate could not run.
Needs Steam running, a desktop session, Node 22+ and the server repo next to the library
(`..\..\02_Server`, or `-ServerRoot`).

## What it does

1. **Offline compile check** – `dotnet build dev\CompileCheck.csproj`; C# errors fail before the
   editor even starts.
2. **Test server** – builds `02_Server` if its sources changed, seeds a throwaway SQLite database
   (admin `gate`, teammate `mate`, project `collabgate`, a server key) and starts it on
   `127.0.0.1:18787`.
3. **Scratch project** – `%TEMP%\collab-editor-gate\scratch` (outside the library, so no junction
   cycle) with this library junctioned in as `Libraries\local.collaborator`.
4. **Launch** – writes the one-shot arming marker and starts `sbox-dev.exe -project …`.
5. **Watch** – every 250 ms:
   - prints each check the moment the gate records it; the **first failed check stops the run**;
   - scans `sbox-dev.log`: a compile error / SB500 / whitelist violation or an exception that
     mentions our code **kills the editor immediately** (so a broken library fails in seconds,
     not at the timeout);
   - scans the server log: any error-level line fails the run (contract bugs show up as 500s);
   - answers the gate's requests: window screenshots (`Collaborator Gate` window) and stopping /
     restarting the server for the resilience checks.

## The in-editor checks (`Editor/Collaborator/Dev/EditorGate.cs`)

| check | proves |
|---|---|
| `settings.isolated` | a gate run never reads or writes your real server address / access key |
| `dock.opens` | the dock builds and shows the sign-in wizard |
| `signin.server_address`, `signin.server_key`, `signin.device_flow` | the wizard's three steps against the real server (device approval done by the admin key, as the browser would) |
| `session.connects`, `session.live_stream` | presence as `sbox-editor`, project auto-linked by package ident, SSE connected |
| `teammate.appears`, `teammate.message_toast` | another developer's agent, task, reservation and message reach the editor live |
| `guard.warns_on_reserved_file` | editing a file inside a teammate's reservation raises the warning |
| `actions.*` | task create/claim/start/complete, claim conflicts, reservations and conflicts, messages + acknowledge, test results |
| `assets.sync` | the asset scan uploads and the server lists the assets |
| `ui.page.*`, `ui.narrow.*` | every page renders with real data, and fits a 340 px dock without clipping (screenshots) |
| `resilience.server_down`, `resilience.reconnects` | the dock goes offline cleanly and reconnects by itself |
| `session.sign_out` | sign-out forgets the key and brings the wizard back |

Screenshots land in `%TEMP%\collab-editor-gate\out\*.png`, the result in `gate_result.json`.

## Safety

- The hook only arms when `COLLAB_GATE_RESULT` is set **and** the `.arm` marker exists (consumed on
  first use), so a leaked environment variable never arms a normal editor session.
- While armed, per-user settings live in memory only (`Settings.InMemory`).
- It refuses to run unless the open project is the gate's scratch project.
