@echo off
rem Double-click to remove SQL Prompter from SSMS 22. The script is copied out first because the removal deletes this folder.
set "work=%TEMP%\SqlPrompterUninstall"
if not exist "%work%" mkdir "%work%"
copy /y "%~dp0Uninstall-SqlPrompter.ps1" "%work%\Uninstall-SqlPrompter.ps1" >nul
start "" powershell.exe -NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -File "%work%\Uninstall-SqlPrompter.ps1"
