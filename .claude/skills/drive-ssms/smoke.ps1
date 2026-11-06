# Smoke test after an SSMS update (or a new VSIX): the compatibility check of the running SSMS and screenshots of a few completion scenarios.
# Usage: .\smoke.ps1 [-Database SqlPrompterTest_Shop] [-Out <folder for the screenshots>]
# SSMS must run connected to the test instance (start-ssms.ps1) and the test database of verify-schema.sql must exist.
# PASS / FAIL is decided only for the compatibility check (read from the log). The completion scenarios produce screenshots that are to be
# looked at: drive.ps1 cannot read the items of the list through UI Automation any more (the list is our own WPF popup), so each scenario
# says what the picture must show. Exit code: the number of failed checks.
param([string]$Database = "SqlPrompterTest_Shop", [string]$Out = (Join-Path $env:TEMP "sqlprompter-smoke"))
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$log = Join-Path $env:LOCALAPPDATA "SqlPrompter\sqlprompter.log"
New-Item -ItemType Directory -Force $Out | Out-Null
$failures = 0

# 1. The compatibility check of the last start: every undocumented requirement found.
$line = Get-Content $log -ErrorAction SilentlyContinue | Where-Object { $_ -match "SSMS build .*undocumented requirements found" } | Select-Object -Last 1
if ($line -match "(\d+) of (\d+) undocumented" -and $Matches[1] -eq $Matches[2]) { "PASS  compatibility: all requirements found" } else { "FAIL  compatibility: $line"; $failures++ }
if ($line) { "      $line" }
$all = Get-Content $log
$last = ($all | Select-String "SSMS build .*undocumented requirements found" | Select-Object -Last 1).LineNumber
if ($last) { $all | Select-Object -Skip $last -First 12 | Where-Object { $_ -match "MISSING|Switched off" } | ForEach-Object { "      $_" } }

# 2. Completion scenarios: a screenshot each.
$scenarios = @(
    @{ Name = "objects"; Sql = "SELECT * FROM dbo."; Keys = "^{END}p"; Expect = "a list with Products (and other tables starting with p), a definition panel on the right" },
    @{ Name = "columns"; Sql = "SELECT p. FROM dbo.Products p"; Keys = "^{HOME}{DOWN 2}{HOME}{RIGHT 9}i"; Expect = "columns of Products (IdProduct, IdCategory ...)" },
    @{ Name = "update"; Sql = "UPDATE dbo.Products "; Keys = "^{END}^( )"; Expect = "SET first and selected" },
    @{ Name = "where"; Sql = "SELECT * FROM dbo.Products WHERE IdProduct = 1 "; Keys = "^{END}^( )"; Expect = "AND, GROUP BY, OR, ORDER BY first" }
)
# The first use of a database loads its metadata in the background.
& "$here\drive.ps1" -ClearAll -Paste "USE $Database`r`nGO`r`nSELECT * FROM dbo.Products" -Keys "^{END}" -MaxItems 0 | Out-Null
Start-Sleep 5
foreach ($s in $scenarios) {
    $file = Join-Path $Out ($s.Name + ".png")
    & "$here\drive.ps1" -ClearAll -Paste "USE $Database`r`nGO`r`n$($s.Sql)" -Keys $s.Keys -Wait 3 -MaxItems 0 -Shot $file | Out-Null
    "SHOT  $file  must show: $($s.Expect)"
}

""
if ($failures -eq 0) { "Compatibility check passed; look at the screenshots above." } else { "$failures check(s) failed." }
exit $failures
