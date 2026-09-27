<p align="center"><img src="logo.png" width="96" alt=""></p>

# Collaborator for s&box

See what your teammates and their coding agents are doing – right inside the s&box editor.
Tasks, file reservations, messages, test results and "what changed while you were away",
shared through your team's [Collaborator server](https://github.com/zeljkovranjes/sbox-collaborator-server).

## Setup (2 minutes)

You need: the **server address** and a **server key** from whoever runs your team's server, and a
GitHub account.

1. **Add the library to your project.** Copy (or `git clone`) this folder into your s&box
   project's `Libraries` folder, so you have `Libraries/collaborator/collaborator.sbproj`.
   Open the project in s&box – it compiles the library automatically.
2. **Open the window:** **View ▸ Collaborator**.
3. **Enter the server address**, e.g. `mcp.example.com`, and click *Continue*.
4. **Paste the server key** you were given (`sbj_…`) and click *Continue*.
   (Already have an account? Click *I already have an account*.)
5. **Click *Login with GitHub*.** Your browser opens – log in and you're done. The editor
   connects by itself.

That's it. Next time you open s&box you're signed in automatically.

## Everyday use

| you want to… | do this |
|---|---|
| see who's online and what they're doing | the **Home** tab |
| know what changed since you last looked | the *While you were away* card on Home, or **⋯ ▸ Catch me up** |
| take a task | **Tasks** tab ▸ click a task ▸ *Claim* (the branch command is copied for you) |
| stop mid-task and leave a note | open your task ▸ *Hand off…* |
| stop teammates editing a file you're changing | right-click it in the Asset Browser ▸ **Collaborator ▸ Reserve file for editing** |
| see who touched a file | right-click it ▸ **Collaborator ▸ History…** |
| message a teammate | the **Messages** tab |
| log a playtest by hand | the **Tests** tab |

**Happens automatically** (turn off in the ⚙ tab):
- scenes and prefabs you have open are reserved, and released when you close them;
- when your code breaks or compiles again, the team sees it;
- playtests are logged, with any errors that happened while you played;
- you get a warning when you open or change something a teammate has reserved.

## Good to know

- Nothing is ever locked – reservations warn, they don't block.
- Your personal key is stored encrypted outside your project (Windows DPAPI, macOS Keychain,
  Linux keyring, or an encrypted file), so it can never end up in git. The server key is not
  stored at all.
- Lost your connection? The window shows it and reconnects by itself.
- Sign out or switch project from the **⋯** menu.

## For developers

- `dotnet build dev/CompileCheck.csproj` – compiles the library against your installed s&box.
- `powershell -ExecutionPolicy Bypass -File dev\editor-gate\run_editor_gate.ps1` – the full
  end-to-end test in the real editor against a throwaway server (needs the server repo next to
  this one). See [dev/editor-gate/README.md](dev/editor-gate/README.md).
