# Project conventions

## Build & deploy
- Final build artifacts MUST always be placed in `I:\Programs Inventory`:
  - `Windows Program Inventory_0.1.0_x64-setup.exe` (NSIS installer)
  - `wpi.exe` (standalone binary)
- Debug build: `cargo build` (target-dir `C:\Users\HP Book\.wpi-target`); dev exe: `C:\Users\HP Book\.wpi-target\debug\wpi.exe`
- Release/installer: `cargo tauri build` (requires `cargo tauri` CLI installed).
- Frontend (`../src`) is embedded at build time — JS/CSS/HTML edits require a rebuild + relaunch.
- Before building kill any running `wpi` process.