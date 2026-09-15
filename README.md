# Windows Program Inventory

A lightweight, modern Windows desktop app that inventories everything installed on your machine —
Start Menu shortcuts, Registry-installed programs and Store / AppX packages. It extracts real icons,
classifies each entry (**Installed**, **Shortcut**, **Store App**, **Broken**), and lets you browse,
search, filter, launch, and export rich reports.

**100% local.** No telemetry, no cloud, no external APIs. Read-only during scans.

---

## Features

- **Full system scan** — merges start-menu shortcuts, registry uninstall entries and Store/AppX packages
  into one unified program list with icon, version, publisher, architecture, location, executable and
  install date.
- **Real icons** — extracts and caches each app's actual icon (no placeholders), with an on-disk cache
  so re-scans are fast.
- **Smart classification** — every entry is tagged `installed`, `shortcut`, `store` or `broken`.
- **Clean, honest list** — hardware drivers and Windows system components (rundll32, wscript, host
  binaries, SDKs, runtimes…) are filtered out so you only see real user apps.
- **Cards & list views** — toggle between a colorful card grid and a compact list; resize icon sizes.
- **Instant search & filters** — filter by status, search across name / publisher / location.
- **Select programs** — select all or just the ones you care about (checkbox on every card).
- **Reports in 3 formats** — export the full inventory or only your selection as **HTML**, **Markdown**
  or **PDF**, each with identical rich detail per program.
- **Launch & locate** — launch any program or open its file location in Explorer.
- **Dark & light themes** — the app remembers your choice.
- **No startup flash** — the dark splash appears instantly and fades into the app, with no window flicker.

## Tech stack

| Decision | Choice |
|---|---|
| Framework | **Tauri 2** (Rust core + system WebView2) |
| Frontend | Vanilla HTML / CSS / JavaScript (embedded at build time) |
| Backend | Rust (`src-tauri`), async commands, `spawn_blocking` for scans |
| Scanning | Registry, Start Menu shortcuts (`IShellLink`), AppX / Store enumeration via PowerShell |
| Icons | `ExtractIconEx` / shell APIs, cached under `%APPDATA%\com.wpi.inventory\icons-v3` |
| Packaging | NSIS installer + portable `wpi.exe` |

No Node bundler, no Electron, no extra runtime downloads.

## Install

Grab the latest **`Windows.Program.Inventory_<version>_x64-setup.exe`** from the
[Releases](https://github.com/Ward1965/Programs-Inventory/releases) page. The installer also ships a
portable `wpi.exe` you can copy anywhere and run directly.

## Build from source

Run the Tauri build inside `tauri-app`:

```bash
cd tauri-app
npm run tauri build   # or: cargo tauri build --project src-tauri
```

Artifacts are produced in the Tauri target directory (bundle/nsis for the installer) and embedded
frontend assets are compiled into the binary at build time.

## Privacy & safety

- The app is **read-only** toward the system during scans.
- No Administrator privileges required.
- No telemetry, no analytics, no network calls. All data stays on your machine.