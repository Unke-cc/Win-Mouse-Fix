# Repository Guidelines

## Project Structure & Module Organization

This repository is starting from an empty baseline. Keep the desktop interface independent from the mouse engine:

- `src/gui/`: desktop window, tray controls, settings pages, and user notifications.
- `src/engine/`: the required AutoHotkey v2 mouse engine, application rules, profiles, and action mappings.
- `src/shared/`: configuration schema and messages exchanged between the GUI and engine.
- `config/default.json`: documented default settings. User settings belong in `%AppData%\WinMouseFix`, not in the repository.
- `assets/`: icons and other files embedded in the packaged application.
- `tests/manual/`: repeatable test checklists for mouse behavior and supported applications.
- `scripts/`: PowerShell scripts for validation and packaging. Generated files belong in `dist/` and must not be committed.

## Build, Test, and Development Commands

AutoHotkey v2 is the required mouse engine, and .NET 8 with WPF is the selected GUI technology. Once the corresponding scripts exist, use these stable project commands instead of technology-specific commands:

```powershell
pwsh -File .\scripts\dev.ps1
pwsh -File .\scripts\validate.ps1
pwsh -File .\scripts\build.ps1
```

`dev.ps1` starts the GUI and AutoHotkey v2 engine together. `validate.ps1` checks both parts and their shared configuration; `build.ps1` packages the GUI, engine, and AutoHotkey runtime so users install no separate dependency.

## Coding Style & Naming Conventions

C# files follow standard .NET formatting with four-space indentation, nullable reference types enabled, and `PascalCase` for public members. AutoHotkey v2 files also use four-space indentation, `PascalCase` for functions, and `camelCase` for variables. Keep GUI code separate from mouse behavior, map form choices to predefined engine actions, and place shared message formats in `src/shared/`.

## Testing Guidelines

No automated test framework is configured yet. Every change must pass `validate.ps1` and its relevant checklist under `tests/manual/`. Test normal windows, full-screen applications, profile switching, excluded applications, configuration persistence, automatic reload, tray controls, and the packaged executable. Confirm excluded applications receive the original mouse behavior.

## Commit & Pull Request Guidelines

There is no established Git history. Use concise Conventional Commit messages, such as `feat: add application exclusion rules` or `fix: restore mouse button after profile switch`. Pull requests must describe user-visible behavior, list verification steps, note configuration changes, and include screenshots for UI changes. Keep each pull request focused on one feature or correction.
