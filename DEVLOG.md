# Collaborator

Journal for this library. Keep it current: decisions, engine gotchas, what failed and why, the next step.

## Package

| | |
|---|---|
| Ident | `local.collaborator` |
| Type | library |
| Root namespace | `Collaborator` |
| Depends on | none (needs a running Collaborator server: github.com/zeljkovranjes/sbox-collaborator-server) |
| Published | |

## Log

- 2026-10-06: brought under the workspace standard. Editor code moved from `Editor/Collaborator/` to
  `Editor/`, namespaces `Collaborator.EditorTools.<Folder>`, multi-type files split one type per file
  (no code edits). Two name clashes with the engine's `Editor` namespace (`Editor.Chip`,
  `Editor.IconLabel`) surfaced once widgets left the `UI` namespace; fixed with file-level aliases
  to our widgets so every file binds to the same type as before. No `Code/` and no Core harness:
  the library is editor-only and nothing in it is engine-free logic worth shipping in a runtime
  assembly (an empty runtime assembly would only change what ships). `logo.png` moved to `docs/`
  (README only; the window icon is embedded in `CollaboratorWindow`). Verified with
  `dev/CompileCheck.csproj`; the editor gate was not run.
