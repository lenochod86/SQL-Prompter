Add-Type -AssemblyName UIAutomationClient, UIAutomationTypes
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$cfg = Get-Content "$env:LOCALAPPDATA\SqlPrompter\testdb.json" | ConvertFrom-Json
$exe = "C:\Program Files\Microsoft SQL Server Management Studio 22\Release\Common7\IDE\SSMS.exe"
$p = Start-Process $exe -ArgumentList "-S", $cfg.server, "-U", $cfg.user, "-d", "master", "-C", "-N", "Optional" -PassThru
function Win { $A::RootElement.FindAll($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$p.Id))) }
function Find($scope, $name, $type) {
    $c = New-Object System.Windows.Automation.AndCondition((New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, $name)), (New-Object System.Windows.Automation.PropertyCondition($A::ControlTypeProperty, $type)))
    foreach ($s in @($scope)) { $r = $s.FindFirst($T::Descendants, $c); if ($r) { return $r } }
}
function Invoke($e) { $o = $null; if ($e -and $e.TryGetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern, [ref]$o)) { ([System.Windows.Automation.InvokePattern]$o).Invoke(); $true } else { $false } }
$btn = [System.Windows.Automation.ControlType]::Button
# 0. crash-recovery dialog (SSMS was killed before): do not recover
for ($i = 0; $i -lt 12; $i++) {
    Start-Sleep 1
    $all = $A::RootElement.FindAll($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$p.Id)))
    foreach ($ww in $all) { $dn = $ww.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, "Do Not Recover"))); if ($dn) { Invoke $dn | Out-Null; "recovery dialog dismissed" } }
}
# 1. "Connect to the following server?" -> Yes
for ($i = 0; $i -lt 60; $i++) { Start-Sleep 1; $w = Win; if ($w.Count) { $yes = Find $w "Yes" $btn; if ($yes) { Invoke $yes | Out-Null; "confirmed"; break } } }
# 2. login failed (no password) -> OK; then password + Connect
for ($i = 0; $i -lt 30; $i++) {
    Start-Sleep 1; $w = Win; if (-not $w.Count) { continue }
    $pw = Find $w "Password" ([System.Windows.Automation.ControlType]::Edit)
    if ($pw) {
        $ok = Find $w "OK" $btn; if ($ok) { Invoke $ok | Out-Null; Start-Sleep 1 }
        $o = $null; $null = $pw.TryGetCurrentPattern([System.Windows.Automation.ValuePattern]::Pattern, [ref]$o)
        ([System.Windows.Automation.ValuePattern]$o).SetValue($cfg.password)
        Invoke (Find $w "Connect" $btn) | Out-Null; "connecting"; break
    }
}
Start-Sleep 6
$w = Win
$names = $w[0].FindAll($T::Descendants, [System.Windows.Automation.Condition]::TrueCondition) | ForEach-Object { $_.Current.Name }
"query window: " + [bool]($names | Where-Object { $_ -like "SQLQuery*" })

