# Collaborator

An s&box editor library that connects the editor to a self-hosted Collaborator server
(`02_Server`). You see the same shared state your coding agents see: who is online, what each
person's agents are working on, the task board, file and asset reservations, messages, test
results and recent commits.

## Install

Copy (or clone, or symlink) this folder into your project's `Libraries/` folder:

```
<your project>/Libraries/collaborator/collaborator.sbproj
```

The editor picks it up and compiles it; it then shows in **Library Manager**. Open
**View ▸ Collaborator**: it opens in its own window (dark title bar, Collaborator logo, minimise /
maximise / close) and remembers its size and position. The library is editor-only and adds nothing to
your game.

## Sign in

The first time, the window walks you through three steps:

1. **Server**: the address of your team's server, for example `mcp.example.com` or `192.168.1.20:8080`.
   HTTPS is used unless you typed `http://`. Plain HTTP is only accepted for IP addresses and
   `localhost`.
2. **Server key**: the join key (`sbj_…`) the server admin gave you. If you already have an
   account, choose *I already have an account*.
3. **GitHub**: click *Login with GitHub*. Your browser opens, and after you approve, the editor
   receives its own personal access key. There is nothing to copy.

Only the server address and your personal access key are stored. They live in your editor
settings, outside every project, so they never end up in a repository. The server key is not
stored. *Use an access key instead* lets you paste a key (`sbc_…`) from the dashboard.

Each s&box project remembers which server project it belongs to. A project whose package ident
matches a server project is linked automatically.

## What it does

- **Home**: blockers, who is online and what their agents are doing (task, branch, files), tasks
  in progress, reserved files, recent changes and commits, and the last test result.
- **Tasks**: the shared board. You can claim, start, block, release, complete and create tasks.
- **Files**: reserve a file or a folder (ending in `/`) before a big edit, and see who holds what.
  Reservations are advisory: they warn and never lock.
- **Messages**: unread messages first, which you can acknowledge. You can send to one teammate or
  to everyone.
- **Activity**: the team's feed, grouped by day.
- **Tests**: log a playtest (pass/fail, scene, errors). A failure warns the team.
- **Asset browser**: right-click ▸ *Collaborator* to reserve, release, or ask who's editing.
- **Warnings**: a toast appears when you open or change a file a teammate has reserved, when
  someone messages you, when a build breaks, and when a teammate publishes a breaking change.
- **Presence**: the editor shows up as an `sbox-editor` agent with your branch and open scene.
- **Asset sync**: uploads asset paths and their references (model → materials → textures) so
  agents can ask what uses what. File contents are never uploaded. You can turn it off under
  Settings.

Updates arrive live over the server's event stream. If the stream is down, the window polls instead.

## Development

`dotnet build dev/CompileCheck.csproj` compiles `Editor/` against your installed s&box
(set `SboxRoot` if s&box isn't in the default Steam folder). The in-editor compile is the
authoritative check.

**Editor gate** (the real test): `powershell -ExecutionPolicy Bypass -File dev\editor-gate\run_editor_gate.ps1`
starts a throwaway Collaborator server, opens a scratch project in the real s&box editor with this
library, and runs 34 end-to-end checks: sign-in, live teammate sync, reservation warnings, every
action, every page at normal and narrow widths, the server going down and coming back, and sign-out.
It stops at the first compile error, exception, server error or failed check. See
[dev/editor-gate/README.md](dev/editor-gate/README.md).
