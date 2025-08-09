# EZWinGet

A simple app that runs in the Windows tray and enables easy WinGet application upgrades with it's right-click menu.

So simple a user can use it.

______________________________________________________________________________________________________________________

If you don't have WinGet installed, install in from Microsoft:

Invoke-WebRequest -Uri https://aka.ms/getwinget -OutFile winget.msixbundle

Add-AppxPackage winget.msixbundle

Remove-Item winget.msixbundle

______________________________________________________________________________________________________________________

If WinGet is downloading slowly, do this:

Run "winget settings"

In the settings.json file that appears, add to the end...

"network": {"downloader": "wininet"}
