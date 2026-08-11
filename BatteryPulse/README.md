# BatteryPulse

BatteryPulse is a Windows desktop status tool for battery power, system power, temperatures, storage, and battery charge-limit status.

## Project layout

```text
BatteryPulse/
├─ src/                         C# source and application manifest
├─ assets/
│  ├─ branding/                 icons and lightning images
│  └─ installer/                installer languages and license files
├─ runtime/LibreHardwareMonitor/ third-party sensor runtime shipped with the app
├─ build/                       build scripts and local compiler tools
├─ dist/current/                latest local build, not a version archive
├─ releases/YYYY-MM-DD/vX.Y.Z/  immutable local release snapshot
├─ archive/                     historical test builds kept for traceability
└─ docs/
   ├─ design-system/            UI design references
   └─ updates/YYYY/YYYY-MM-DD.md daily change record
```

## Naming rule

Release files use:

```text
BatteryPulse.TopBar-v<version>-<yyyymmdd>.exe
BatteryPulse-Setup-v<version>-<yyyymmdd>.exe
```

For example: `BatteryPulse.TopBar-v2.1.0-20260812.exe`.

## Build and package

Run these commands from the `BatteryPulse` directory:

```powershell
powershell -ExecutionPolicy Bypass -File .\build\build-topbar.ps1
& .\build\tools\Tools.InnoSetup.6.7.3\tools\ISCC.exe .\BatteryPulse.Installer.iss
powershell -ExecutionPolicy Bypass -File .\build\package-release.ps1 -Version 2.1.0 -Date 2026-08-12 -InstallerPath '.\dist\current\installer\BatteryPulse-Setup-v2.1.0-20260812.exe'
```

The first command writes the current executable to `dist\current`. The second command compiles the installer into `dist\current\installer`. The third command creates the dated version folder, copies the executable, installer, and symbols, attaches the daily update note, and writes `SHA256SUMS.txt`.

The Inno Setup entry point is `BatteryPulse.Installer.iss`. Its output is configured for the matching dated release folder.

## Update records

Each working day has one update record at `docs\updates\YYYY\YYYY-MM-DD.md`. The record lists the intent, user-visible changes, verification status, and the exact files involved.
