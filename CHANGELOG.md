# Changelog

## 2026-10-06
### Breaking
- Editor code moved to namespaces under `Collaborator.EditorTools`. Only matters if your own editor
  code calls into this library; update its `using` lines:
  - `Collaborator.Net` -> `Collaborator.EditorTools.Net` (`CollabClient`, `CollabException`,
    `CollabJson`, `EventStream` and all server models such as `TaskItem`, `Reservation`, `TeamMessage`)
  - `Collaborator` -> `Collaborator.EditorTools.Session` (`CollabSession`, `ConnectionState`,
    `AssetGuard`, `AssetSync`, `EditorAutomation`, `EditorThread`, `ProjectPaths`, `SecureStore`,
    `Settings`)
  - `Collaborator.Toasts` -> `Collaborator.EditorTools.UI.Toasts`
  - `Collaborator.UI` -> `Collaborator.EditorTools.UI` (`CollaboratorWindow`, `CollaboratorView`,
    `ProjectPickerView`, `MainView`, `SignInView`, `HandoffDialog`, `HistoryWindow`, `Rows`,
    `Browser`, `UiStyle`)
  - `Collaborator.UI` -> `Collaborator.EditorTools.UI.Pages` (`Page`, `HomePage`, `TasksPage`,
    `FilesPage`, `MessagesPage`, `ActivityPage`, `TestsPage`, `SettingsPage`)
  - `Collaborator.UI` -> `Collaborator.EditorTools.UI.Widgets` (`Card`, `StatusDot`, `SectionHeader`,
    `SectionCaption`, `IconLabel`, `Avatar`, `TimelineRow`, `LinkLabel`, `EmptyState`, `Pill`, `Chip`,
    `ProcessingIndicator`, `StepIndicator`, `TabStrip`, `AvatarStack`, `UiButton`, `ClickRow`)
  - `Collaborator.Dev.EditorGate` -> `Collaborator.EditorTools.Dev.EditorGate`
### Changed
- Brought under the workspace package standard: editor code now lives directly in `Editor/` (was
  `Editor/Collaborator/`), one type per file. No behaviour change; settings, sign-in and stored keys
  carry over.
- README rewritten in the standard format; package summary, description, tags and website added.
