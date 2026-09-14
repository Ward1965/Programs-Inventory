# Windows Program Inventory

A lightweight, modern desktop app that inventories everything installed on your Windows machine —
Start Menu shortcuts, Registry-installed programs and Store/AppX packages — extracts icons, classifies
each entry (Installed / Shortcut Only / Broken…) and lets you browse, search, filter, launch and export it.

100% local. No telemetry, no cloud, no external APIs. Read-only regarding the system during scans.

> Status: **Phase 1 complete** — project + GUI skeleton (sidebar, search, grid, settings, themes, architecture, tests).
> The real discovery scanners (Start Menu / Registry / AppX), icon extraction, SQLite cache, export and
> classification arrive in the following phases (see [The Plan](#the-plan)).

---

## Tech stack

| Decision | Choice | Why |
|---|---|---|
| Framework | **C# + .NET 8 (LTS) + WPF** | Native Windows performance, low RAM, fast startup; easiest full access to Windows APIs (COM `IShellLink`, `SHGetFileInfo`, `ExtractIconEx`, AppX, PE/version info). Much lighter than WinUI 3 for this kind of tool; no Electron (explicitly rejected). |
| MVVM | **Home-grown, zero dependencies** | `ViewModelBase`, `RelayCommand`, `AsyncRelayCommand` (~120 LOC). Keeps the app dependency-free per design rules. |
| Persistence | JSON settings (Phase 1) → **SQLite** planned (Phase 10) | SQLite enables scan cache, history, statistics and fast search later. |
| Icons | WPF Imaging + P/Invoke (Phase 7) with on-disk `IconCache` | Never re-extract what did not change (SHA256 of path + modified time + icon index). |

Because .NET 8 runtimes already exist on target machines, we ship **framework-dependent** for a tiny EXE,
or **self-contained single-file** for a zero-prerequisite EXE (see `BUILD.md`).

## Project layout

```
src/
├── WindowsProgramInventory/            WPF app
│   ├── App.xaml(.cs)                   Entry point, exception logging, startup flow
│   ├── Startup/                        Composition root (manual DI), themes, dialogs
│   ├── Core/                           Constants, centralized strings, all business interfaces
│   │   └── Interfaces/                 IScanner, IScanEngine, IProgramRepository, ILogger, …
│   ├── Models/                         ProgramInfo, enums (Status, Type, SourceType, Architecture, ViewMode)
│   ├── Services/                       ScanEngine (pipeline skeleton), logging
│   ├── Storage/                        InMemoryProgramRepository, JsonSettingsStore
│   └── UI/                             MainWindow, Views, ViewModels, Themes, Converters
├── WindowsProgramInventory.Tests/      xUnit unit tests (42 passing)
└── WindowsProgramInventory.sln
```

Key principles honored so far (and kept going forward):

- **Interfaces everywhere** — UI and ViewModels depend on `IProgramRepository`, `IScanEngine`,
  `IThemeService`, `IDialogService`, `ILogger`, never on Windows APIs directly.
- **Evidence over guessing** — `ProgramInfo` only stores facts; statuses are produced by the
  Classification Engine (Phase 9) from evidence, never assumed from a shortcut alone.
- **Async + cancelable + progress-aware** scan pipeline already scaffolded.
- **Strings centralized** in `Core/Strings.cs` for future English/Arabic + RTL localization.
- **No per-program Process/PowerShell** anywhere (performance rule).

## Run

```bash
cd src/WindowsProgramInventory
dotnet run
```

## Test

```bash
dotnet test src/WindowsProgramInventory.Tests
```

Phase 1 test coverage: ViewModelBase, RelayCommand/AsyncRelayCommand, ProgramsViewModel
(search/filter/view state), MainViewModel (navigation + async scan lifecycle + cancellation),
SettingsViewModel (theme/startup/persistence), ScanEngine (progress + cancellation), models.

## Publish

See `BUILD.md` for `dotnet publish` recipes (framework-dependent, self-contained and single-file).

## The Plan

Per the spec the project is built in small stable phases. Status:

- [x] **Phase 1** Project + GUI skeleton (sidebar, search, grid, settings, theme, architecture, tests)
- [ ] **Phase 2** Start Menu Scanner
- [ ] **Phase 3** Shortcut Analyzer (`IShellLink`)
- [ ] **Phase 4** Registry Installed Programs Scanner
- [ ] **Phase 5** AppX / MSIX Scanner
- [ ] **Phase 6** EXE Metadata Analyzer (version info, architecture)
- [ ] **Phase 7** Icon Extraction + sizes (16…256)
- [ ] **Phase 8** Identity Resolution (duplicate merging)
- [ ] **Phase 9** Classification Engine (`Installed`, `Shortcut Only`, `Installed + Shortcut`, `Broken Shortcut`, `Unknown`)
- [ ] **Phase 10** SQLite cache layer
- [ ] **Phase 11** Search / Filter / Sort (foundations already in `ProgramsViewModel`)
- [ ] **Phase 12** Grid / List virtualized UI + program cards + badge colors
- [ ] **Phase 13** Details Panel
- [ ] **Phase 14** Export (JSON / CSV / TXT / HTML)
- [ ] **Phase 15** Settings polish (start-with-Windows, diagnostics viewer)
- [ ] **Phase 16** Performance: 100 → 2000+ programs
- [ ] **Phase 17** Extended test matrix
- [ ] **Phase 18** Packaging (self-contained single-file)

## Privacy & safety

- The app is **read-only** toward the system during scans.
- Never requests Administrator by default; offers a documented "rescan as Administrator" path later.
- No telemetry, no analytics, no network calls. All data stays under `%LOCALAPPDATA%\WindowsProgramInventory\`.