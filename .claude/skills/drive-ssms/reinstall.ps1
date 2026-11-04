# Closes SSMS without saving (if it runs), installs the VSIX built in Release and starts SSMS connected to the test instance.
# Usage: .\reinstall.ps1 [-NoStart]   (build first: dotnet build src/SqlPrompter.Ssms/SqlPrompter.Ssms.csproj -c Release -warnaserror)
param([switch]$NoStart, [string]$Database = "master")
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$repo = Resolve-Path (Join-Path $here "..\..\..")
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]; $Sc = [System.Windows.Automation.TreeScope]
$running = Get-Process SSMS -ErrorAction SilentlyContinue
if ($running) {
    & "$here\drive.ps1" -NoFocus -ClearAll -Keys "{ESC}%{F4}" -Wait 2 -MaxItems 0 | Out-Null
    $pidS = (Get-Process SSMS -ErrorAction SilentlyContinue).Id
    if ($pidS) {
        $b = $A::RootElement.FindAll($Sc::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, "Don't Save"))) | Where-Object { $_.Current.ProcessId -eq $pidS } | Select-Object -First 1
        $o = $null
        if ($b -and $b.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$o)) { ([System.Windows.Automation.InvokePattern]$o).Invoke() }
    }
    Start-Sleep 6
    if (Get-Process SSMS -ErrorAction SilentlyContinue) { "SSMS did not close"; return }
}
$installer = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\VSIXInstaller.exe"
$vsix = Join-Path $repo "src\SqlPrompter.Ssms\bin\Release\net48\SqlPrompter.Ssms.vsix"
$result = Start-Process $installer -ArgumentList "/quiet", "/shutdownprocesses", "`"$vsix`"" -Wait -PassThru
"install exit $($result.ExitCode)"
if (-not $NoStart) { & "$here\start-ssms.ps1" -Database $Database | Select-Object -Last 1 }
