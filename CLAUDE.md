<!-- sbox-standard: v1 | type: library | root: Collaborator -->
# Collaborator

**Standard: sbox-standard v1** (library, root namespace `Collaborator`). This package follows the package layout and
code standard in E:\.sbox\CLAUDE.md (loaded automatically), enforced by `sbox-check`. Run it before
calling work done.

s&box library `local.collaborator`.

## Package facts

Only what the code can't tell you: design decisions, engine gotchas found here, where test assets live.

- Editor-only library: everything is in `Editor/` (namespace `Collaborator.EditorTools.<Folder>`).
  There is deliberately no `Code/` and no `dev/Collaborator.Core.csproj`: no engine-free logic worth a
  runtime assembly, and an empty one would only change what ships. Re-running `new-sbox` recreates
  `Code/Collaborator/Assembly.cs` and the Core harness; delete them again.
- Compile check: `dotnet build dev\CompileCheck.csproj` (globs `Editor\**\*.cs` against the installed
  s&box). No editor-generated `Editor\*.csproj` exists on this machine. Only the in-editor compile
  proves the whitelist.
- `Editor.Chip` and `Editor.IconLabel` exist in the engine. Files that use our widgets of the same
  name carry `using Chip = Collaborator.EditorTools.UI.Widgets.Chip;` / `using IconLabel = ...;`.
  Keep them, or the build fails with CS0104.
- Settings persist through `EditorCookie` / `ProjectCookie` string keys (`collaborator.*`) and the
  window's `StateCookie` `collaborator.window`; nothing is stored by type name, so moving types
  needs no `[Alias]`. Changing those keys loses users' settings and sign-in.
- Org is `local` (not published). Never change `Org`/`Ident`; the server links projects by package ident.
- End-to-end test: `dev\editor-gate\run_editor_gate.ps1` (real editor + throwaway server, needs the
  server repo; default `..\..\02_Server`, else pass `-ServerRoot`). Its in-editor half is
  `Editor/Dev/EditorGate.cs`; `$ours` in the script matches our namespaces in logs.
- Never commit credentials: the access key lives encrypted in per-user editor storage (`SecureStore`).
