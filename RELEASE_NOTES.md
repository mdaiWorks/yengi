# Yengi v1.13 Release Notes

### 🚀 Yenilikler / New Features
- complete one-click release automation script with automated dotnet publish, Inno Setup EXE creation, and git push (`b3467bc`)
- add user permission gate for unselected tool requests and self-correction hint for missing tool arguments (with TR/EN/ZH localization) (`cb22799`)
- add informative modal popups with 'do not show again' option for Research Mode and AI Image Generation toggles (`f1e26cf`)

### 🐛 Düzeltmeler / Bug Fixes
- AI continuation loop now actually re-enters after user approves 10 more steps (`702f841`)
- scrollbar thumb unblocked and viewport indicator removed from overview margin (`eb7107c`)

### 🔧 Sürüm & Altyapı / Maintenance
- remove tek_tik_release.bat from github tracking and add to gitignore (`d5a58ee`)
- bump version to 1.12 (`ea66d92`)
- ignore exekurulum.iss and DOCS_BLENDER_UNITY_COPILOT.md from repository (`23fe516`)
- bump version to 1.11 (`ab50f2d`)
- bump version to 1.10 (`111d768`)
