# Repository Guidelines

## Project Structure & Module Organization

This repository is starting from an empty baseline. Keep the AutoHotkey v2 application organized around these paths:

- `src/WinMouseFix.ahk`: application entry point and startup coordination.
- `src/core/`: mouse actions, active-application rules, profiles, and configuration handling.
- `src/ui/`: settings window, tray menu, and user notifications.
- `config/default.json`: documented default settings. User settings belong in `%AppData%\WinMouseFix`, not in the repository.
- `assets/`: icons and other files embedded in the packaged application.
- `tests/manual/`: repeatable test checklists for mouse behavior and supported applications.
- `scripts/`: PowerShell scripts for validation and packaging. Generated files belong in `dist/` and must not be committed.

## Build, Test, and Development Commands

Use AutoHotkey v2 throughout the project. Once the corresponding scripts exist, the standard commands are:

```powershell
AutoHotkey64.exe .\src\WinMouseFix.ahk
pwsh -File .\scripts\validate.ps1
pwsh -File .\scripts\build.ps1
```

The first command runs the application locally. `validate.ps1` checks syntax and configuration files; `build.ps1` uses Ahk2Exe to create `dist/WinMouseFix.exe`. The packaged program must run without a separate AutoHotkey installation.

## Coding Style & Naming Conventions

Indent with four spaces and target AutoHotkey v2 only. Use `PascalCase` for classes and functions, `camelCase` for variables and parameters, and `UPPER_SNAKE_CASE` for fixed values. Keep UI code separate from mouse behavior. Prefer small functions with explicit parameters over shared global state. Add comments only when the reason for a decision is not apparent from the code.

## Testing Guidelines

No automated test framework is configured yet. Every change must pass `validate.ps1` and its relevant checklist under `tests/manual/`. Test normal windows, full-screen applications, profile switching, excluded applications, configuration persistence, automatic reload, tray controls, and the packaged executable. Confirm excluded applications receive the original mouse behavior.

## Commit & Pull Request Guidelines

There is no established Git history. Use concise Conventional Commit messages, such as `feat: add application exclusion rules` or `fix: restore mouse button after profile switch`. Pull requests must describe user-visible behavior, list verification steps, note configuration changes, and include screenshots for UI changes. Keep each pull request focused on one feature or correction.
