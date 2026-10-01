# Yengi v1.14 Release Notes

### 🚀 Yenilikler / New Features
- complete one-click release automation script with automated dotnet publish, Inno Setup EXE creation, and git push (`b3467bc`)
- add user permission gate for unselected tool requests and self-correction hint for missing tool arguments (with TR/EN/ZH localization) (`cb22799`)

### 🐛 Düzeltmeler / Bug Fixes
- process stdout streaming and keep-alive for dev servers to prevent 5-minute terminal hangs (`c9248a9`)
- refine dev server detection to exclude Start-Sleep timeout test commands (`cf44427`)

### 🔧 Sürüm & Altyapı / Maintenance
- remove yedekReadmeler and telemetry_dashboard.html from git tracking and add to gitignore (`109e9f6`)
- release v1.13 (`1c14c65`)
- remove tek_tik_release.bat from github tracking and add to gitignore (`d5a58ee`)
- bump version to 1.12 (`ea66d92`)
- ignore exekurulum.iss and DOCS_BLENDER_UNITY_COPILOT.md from repository (`23fe516`)
- bump version to 1.11 (`ab50f2d`)
