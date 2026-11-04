param([switch]$Attach)
# Starts SSMS 22 connected to the Docker test instance (login from %LOCALAPPDATA%\SqlPrompter\testdb.json) and waits for a query window.
# The password is typed into the login dialog through UI Automation; it is never printed.
Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public class MS { [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y); [DllImport("user32.dll")] public static extern void mouse_event(uint f,uint x,uint y,uint d,UIntPtr e); [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f); }
"@
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$cfg = Get-Content "$env:LOCALAPPDATA\SqlPrompter\testdb.json" | ConvertFrom-Json
$exe = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe"
$p = if ($Attach) { Get-Process SSMS | Select-Object -First 1 } else { Start-Process $exe -ArgumentList "-S", $cfg.server, "-U", $cfg.user, "-d", "master", "-C", "-N", "Optional" -PassThru }
function Win { $A::RootElement.FindAll($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$p.Id))) }
function Named($name, $type = $null) {
    $c = New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)
    if ($type) { $c = New-Object System.Windows.Automation.AndCondition($c, (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type))) }
    $A::RootElement.FindAll($T::Descendants, $c) | Where-Object { $_.Current.ProcessId -eq $p.Id } | Select-Object -First 1
}
function Invoke($e) { $o = $null; if ($e -and $e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$o)) { ([System.Windows.Automation.InvokePattern]$o).Invoke(); $true } else { $false } }
function Click($e) {
    $r = $e.Current.BoundingRectangle
    [MS]::SetCursorPos([int]($r.X + $r.Width / 2), [int]($r.Y + $r.Height / 2)) | Out-Null; Start-Sleep -Milliseconds 400
    [MS]::mouse_event(2, 0, 0, 0, [UIntPtr]::Zero); Start-Sleep -Milliseconds 80; [MS]::mouse_event(4, 0, 0, 0, [UIntPtr]::Zero)
}
# 0. crash-recovery dialog (SSMS was killed before): do not recover
for ($i = 0; $i -lt 10; $i++) { Start-Sleep 1; $dn = Named "Do Not Recover"; if ($dn) { Invoke $dn | Out-Null; "recovery dialog dismissed"; break } }
# 1. "Connect to the following server?" -> Yes (a Button with Invoke, or a plain Pane that needs a real click with SSMS on top)
for ($i = 0; $i -lt 60; $i++) {
    Start-Sleep 1
    $yes = Named "Yes"
    if ($yes) {
        if (-not (Invoke $yes)) { [MS]::SetWindowPos($p.MainWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null; Start-Sleep 1; Click $yes }
        "confirmed"; break
    }
}
# 2. login failed (no password) -> OK; then password + Connect
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep 1
    $pw = Named "Password" ([System.Windows.Automation.ControlType]::Edit)
    if ($pw) {
        $ok = Named "OK" ([System.Windows.Automation.ControlType]::Button); if ($ok) { Invoke $ok | Out-Null; Start-Sleep 1 }
        $o = $null; $null = $pw.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$o)
        ([System.Windows.Automation.ValuePattern]$o).SetValue($cfg.password)
        Invoke (Named "Connect" ([System.Windows.Automation.ControlType]::Button)) | Out-Null; "connecting"; break
    }
}
Start-Sleep 6
$w = Win
$names = $w[0].FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
"query window: " + [bool]($names | Where-Object { $_ -like "SQLQuery*" })
