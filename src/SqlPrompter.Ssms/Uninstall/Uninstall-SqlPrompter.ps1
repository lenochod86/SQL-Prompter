# Removes the SQL Prompter extension from SSMS 22. Started by "Uninstall SQL Prompter.cmd" (double-click) or by the
# SQL Prompter > Uninstall SQL Prompter... command (which passes the process id of the SSMS it closes).
param([int]$WaitForPid = 0, [string]$InstallerDir = "")
Add-Type -AssemblyName System.Windows.Forms
$title = "Uninstall SQL Prompter"
$extensionId = "SqlPrompter.0d66644a-dd92-4b87-ae9d-5d20a2df11c6"
function Show($text, $buttons = "OK", $icon = "Information") {
    [System.Windows.Forms.MessageBox]::Show($text, $title, $buttons, $icon).ToString()
}

if ($WaitForPid -gt 0) {
    $p = Get-Process -Id $WaitForPid -ErrorAction SilentlyContinue
    if ($p -and -not $p.WaitForExit(600000)) {
        Show "SSMS is still open, so SQL Prompter was not removed. Close SSMS and run the uninstall again." "OK" "Warning" | Out-Null
        return
    }
}
while (Get-Process SSMS -ErrorAction SilentlyContinue) {
    $r = Show "SSMS is running. Save your work and close SSMS, then click OK to continue.`n`nClick Cancel to stop without removing anything." "OKCancel" "Warning"
    if ($r -ne "OK") { return }
}

$installers = @()
if ($InstallerDir) { $installers += Join-Path $InstallerDir "VSIXInstaller.exe" }
else {
    foreach ($pf in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if (-not $pf) { continue }
        $installers += Get-ChildItem (Join-Path $pf "Microsoft SQL Server Management Studio 22\*\Common7\IDE\VSIXInstaller.exe") -ErrorAction SilentlyContinue | ForEach-Object FullName
    }
}
$installers = @($installers | Where-Object { Test-Path $_ })
if ($installers.Count -eq 0) {
    Show "No SSMS 22 installation was found, so there is nothing to remove." "OK" "Warning" | Out-Null
    return
}

$failed = $false
foreach ($installer in $installers) {
    $run = Start-Process $installer -ArgumentList "/quiet", "/uninstall:$extensionId" -Wait -PassThru -WindowStyle Hidden
    # Exit code 1002 means the extension is not installed in that instance; that is fine.
    if ($run.ExitCode -ne 0 -and $run.ExitCode -ne 1002) { $failed = $true }
}
if ($failed) {
    Show "The removal did not finish. Make sure SSMS is closed and try again. Details are in the newest *.log file in %TEMP% (VSIXInstaller)." "OK" "Error" | Out-Null
    return
}

$message = "SQL Prompter was removed. Start SSMS as usual."
$dataFolders = @((Join-Path $env:APPDATA "SqlPrompter"), (Join-Path $env:LOCALAPPDATA "SqlPrompter")) | Where-Object { Test-Path $_ }
if ($dataFolders) {
    $ask = Show ($message + "`n`nYour settings, snippets, history and log are kept in:`n" + ($dataFolders -join "`n") + "`n`nDelete them as well?`nClick No to keep them (a later reinstall finds them again).") "YesNo" "Question"
    if ($ask -eq "Yes") {
        foreach ($f in $dataFolders) { Remove-Item $f -Recurse -Force -ErrorAction SilentlyContinue }
        Show "SQL Prompter and its data were removed." | Out-Null
    }
}
else { Show $message | Out-Null }
