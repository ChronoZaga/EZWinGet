# EZWinGet

A simple and lightweight Windows tray application that makes managing package upgrades with **winget** (Windows Package Manager) much easier.

EZWinGet runs quietly in the system tray and gives you quick access to check for updates, install upgrades, and block/unblock packages.

## Features

- System tray icon with easy-to-use context menu
- **Check for Upgrades Now** – Shows available updates in a clean popup
- **Install Upgrades** – One-click install of all available upgrades
- **Block/UnBlock Upgrade** (toggleable) – Block or unblock specific apps using `winget pin`
- **Open WinGet Console** (toggleable) – Opens an elevated interactive winget window
- Automatic upgrade checks:
  - Periodic timer (configurable)
  - Optional check when the user unlocks their PC
- Auto-starts with Windows
- Fully configurable via `settings.ini`

## Configuration

On first run, the app creates a `settings.ini` file in the same folder. You can edit it to change behavior:

[Settings]
UpdateIntervalHours=8
ShowExitOption=true
ShowWinGetConsoleOption=true
CheckUpdatesOnUnlock=true
ShowBlockUnblockOption=true

### Setting Options

| Setting                   | Description                                      | Default |
|---------------------------|--------------------------------------------------|---------|
| UpdateIntervalHours       | Hours between automatic checks (0 = disabled)    | 8       |
| ShowExitOption            | Show "Exit" in tray menu                         | true    |
| ShowWinGetConsoleOption   | Show "Open WinGet Console" in tray menu          | true    |
| CheckUpdatesOnUnlock      | Check upgrades when PC is unlocked               | true    |
| ShowBlockUnblockOption    | Show "Block/UnBlock Upgrade" in tray menu        | true    |

## How It Works

- Starts minimized to the tray
- Automatically registers itself to run on Windows startup
- Performs an initial upgrade check on launch
- Shows upgrade results in a readable window with monospace font
- Supports elevated commands via UAC when needed
- Single instance enforcement (only one copy runs at a time)

## Requirements

- Windows 10 or 11
- winget installed (included with App Installer)
- .NET Framework

## Installation

1. Download the compiled EXE, DLL, JSON, and (optional) INI
2. Put all files in the same folder
3. Run `EZWinGet.exe`
4. The app will create the settings file and add itself to startup

## Notes

- Uses an embedded resource for the tray icon
- All winget operations that require elevation will prompt UAC
- Designed to be minimal and non-intrusive

---

**So simple a user can use it.**

______________________________________________________________________________________________________________________

## If you don't have WinGet installed, install it from Microsoft:

Invoke-WebRequest -Uri https://aka.ms/getwinget -OutFile winget.msixbundle

Add-AppxPackage winget.msixbundle

Remove-Item winget.msixbundle

______________________________________________________________________________________________________________________

## If WinGet is downloading slowly, do this:

Run "winget settings"

In the settings.json file that appears, add to the end...

"network": {"downloader": "wininet"}
