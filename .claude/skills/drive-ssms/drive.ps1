param([string]$Paste, [string]$Keys, [int]$Wait = 2, [string]$Shot, [switch]$ClearAll, [switch]$NoFocus, [int]$MaxItems = 30)
Add-Type -AssemblyName System.Windows.Forms, System.Drawing, UIAutomationClient, UIAutomationTypes
Add-Type @"
using System; using System.Runtime.InteropServices;
public class F { [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h); [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int c);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr a, int x, int y, int cx, int cy, uint f); }
"@
$p = Get-Process SSMS | Select-Object -First 1
[F]::ShowWindow($p.MainWindowHandle, 9) | Out-Null
[F]::SetWindowPos($p.MainWindowHandle, [IntPtr](-1), 0, 0, 0, 0, 0x43) | Out-Null
[F]::SetForegroundWindow($p.MainWindowHandle) | Out-Null
Start-Sleep -Milliseconds 500
$A = [System.Windows.Automation.AutomationElement]; $T = [System.Windows.Automation.TreeScope]
$w = $A::RootElement.FindFirst($T::Children, (New-Object System.Windows.Automation.PropertyCondition($A::ProcessIdProperty, [int]$p.Id)))
$doc = $w.FindFirst($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::NameProperty, "Text Editor")))
if ($doc -and -not $NoFocus) { try { $doc.SetFocus() } catch {} }
Start-Sleep -Milliseconds 300
if ($ClearAll) { [System.Windows.Forms.SendKeys]::SendWait("^a{DEL}") }
if ($Paste) { Set-Clipboard -Value $Paste; [System.Windows.Forms.SendKeys]::SendWait("^v"); Start-Sleep -Milliseconds 400 }
if ($Keys) { foreach ($part in ($Keys -split "@@")) { [System.Windows.Forms.SendKeys]::SendWait($part); Start-Sleep -Milliseconds 2500 } }
Start-Sleep -Seconds $Wait
if ($Shot) {
    $s = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $s.Width, $s.Height
    [System.Drawing.Graphics]::FromImage($bmp).CopyFromScreen($s.Location, [System.Drawing.Point]::Empty, $s.Size)
    $bmp.Save($Shot)
}
$o = $null
if ($doc -and $doc.TryGetCurrentPattern([System.Windows.Automation.TextPattern]::Pattern, [ref]$o)) { "TEXT>>`n" + ([System.Windows.Automation.TextPattern]$o).DocumentRange.GetText(5000) + "<<" } else { "no text pattern" }
$menu = $A::RootElement.FindAll($T::Descendants, (New-Object System.Windows.Automation.PropertyCondition($A::ClassNameProperty, "IntellisenseMenuItem")))
"LIST (" + $menu.Count + ")"
$n = 0
foreach ($m in $menu) {
    if ($n++ -ge $MaxItems) { break }
    $texts = $m.FindAll([System.Windows.Automation.TreeScope]::Descendants, (New-Object System.Windows.Automation.PropertyCondition([System.Windows.Automation.AutomationElement]::ControlTypeProperty, [System.Windows.Automation.ControlType]::Text)))
    $line = @(); foreach ($t in $texts) { $line += $t.Current.Name }
    "  " + ($line -join " | ")
}
