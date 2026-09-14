# Build & Publish — Windows Program Inventory

Requires the **.NET 8 SDK** (check with `dotnet --list-sdks`; install from
<https://dotnet.microsoft.com/download/dotnet/8.0>). Windows 10/11 x64 (net8.0-windows).

## Build (Debug)

```powershell
cd "I:\Programs Inventory\WindowsProgramInventory"
dotnet build WindowsProgramInventory.sln -c Debug
```

Output: `src\WindowsProgramInventory\bin\Debug\net8.0-windows\WindowsProgramInventory.exe`

## Run

```powershell
dotnet run --project src\WindowsProgramInventory
```

## Tests

```powershell
dotnet test WindowsProgramInventory.sln -c Debug
```

## Publish a Release EXE

### 1. Framework-dependent (small EXE, ~400 KB; requires installed .NET 8 Desktop Runtime)

```powershell
dotnet publish src\WindowsProgramInventory -c Release -r win-x64 --self-contained false`
```

Output: `src\WindowsProgramInventory\bin\Release\net8.0-windows\win-x64\publish\`

### 2. Self-contained, single-file EXE (zero prerequisites, ~150 MB)

```powershell
dotnet publish src\WindowsProgramInventory -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

### 3. ARM64 build (if needed)

```powershell
dotnet publish src\WindowsProgramInventory -c Release -r win-arm64 --self-contained true -p:PublishSingleFile=true
```

## App data locations

| Data | Path |
|---|---|
| Icons cache | `%LOCALAPPDATA%\WindowsProgramInventory\IconCache\` |
| Inventory database (SQLite, Phase 10) | `%LOCALAPPDATA%\WindowsProgramInventory\inventory.db` |
| Settings | `%LOCALAPPDATA%\WindowsProgramInventory\settings.json` |
| Logs | `%LOCALAPPDATA%\WindowsProgramInventory\Logs\` |

Everything is removable from **Settings → Cache → Clear All Data**.