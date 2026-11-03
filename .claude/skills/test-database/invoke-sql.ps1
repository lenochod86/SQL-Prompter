<#
.SYNOPSIS
Runs T-SQL against the local SQL Prompter test server and prints messages and result sets.

.DESCRIPTION
Reads the connection from a JSON file outside the repository (default
%LOCALAPPDATA%\SqlPrompter\testdb.json, override with $env:SQLPROMPTER_TESTDB_CONFIG):

  { "server": "localhost,1433", "user": "claude", "password": "...",
    "encrypt": "False", "trustServerCertificate": "True" }

The script is split into batches on lines containing only GO (like sqlcmd).
Exits with code 1 when a batch fails.

.EXAMPLE
./invoke-sql.ps1 -Query "SELECT name FROM sys.databases"

.EXAMPLE
./invoke-sql.ps1 -File tests/TestDatabase/schema.sql -Database SqlPrompterTest_Main
#>
[CmdletBinding(DefaultParameterSetName = 'Query')]
param(
    [Parameter(Mandatory, ParameterSetName = 'Query', Position = 0)]
    [string] $Query,

    [Parameter(Mandatory, ParameterSetName = 'File')]
    [string] $File,

    [string] $Database = 'master',

    [int] $MaxRows = 200,

    [int] $TimeoutSeconds = 60
)

$ErrorActionPreference = 'Stop'

$configPath = if ($env:SQLPROMPTER_TESTDB_CONFIG) { $env:SQLPROMPTER_TESTDB_CONFIG } else { Join-Path $env:LOCALAPPDATA 'SqlPrompter\testdb.json' }
if (-not (Test-Path $configPath)) {
    throw "Test database config not found: $configPath"
}

$config = Get-Content $configPath -Raw | ConvertFrom-Json
$builder = [System.Data.SqlClient.SqlConnectionStringBuilder]::new()
$builder['Data Source'] = $config.server
$builder['Initial Catalog'] = $Database
$builder['User ID'] = $config.user
$builder['Password'] = $config.password
$builder['Encrypt'] = [bool]::Parse($config.encrypt)
$builder['TrustServerCertificate'] = [bool]::Parse($config.trustServerCertificate)
$builder['Application Name'] = 'SQL Prompter test-database skill'

$text = if ($PSCmdlet.ParameterSetName -eq 'File') { Get-Content $File -Raw } else { $Query }
$batches = [regex]::Split($text, '(?im)^\s*GO\s*(?:--.*)?$') | Where-Object { $_.Trim() }

$connection = [System.Data.SqlClient.SqlConnection]::new($builder.ConnectionString)
$connection.add_InfoMessage({ param($sender, $e) foreach ($m in $e.Errors) { Write-Host "-- $($m.Message)" } })
$connection.FireInfoMessageEventOnUserErrors = $false
$failed = $false
try {
    $connection.Open()
    $index = 0
    foreach ($batch in $batches) {
        $index++
        $command = $connection.CreateCommand()
        $command.CommandText = $batch
        $command.CommandTimeout = $TimeoutSeconds
        try {
            $reader = $command.ExecuteReader()
            while (-not $reader.IsClosed) {
                if ($reader.FieldCount -gt 0) {
                    # DataTable.Load moves the reader to the next result set (and closes it after the last one).
                    $table = [System.Data.DataTable]::new()
                    $table.Load($reader)
                    $table | Select-Object -First $MaxRows -Property ($table.Columns | ForEach-Object ColumnName) | Format-Table -AutoSize | Out-String -Width 4096 | Write-Output
                    if ($table.Rows.Count -gt $MaxRows) { Write-Output "($($table.Rows.Count) rows, first $MaxRows shown)" }
                    else { Write-Output "($($table.Rows.Count) rows)" }
                }
                elseif (-not $reader.NextResult()) {
                    break
                }
            }

            if ($reader.RecordsAffected -ge 0) { Write-Output "($($reader.RecordsAffected) rows affected)" }
            $reader.Close()
        }
        catch {
            $failed = $true
            $inner = $_.Exception.InnerException ?? $_.Exception
            Write-Output "Batch $index failed: $($inner.Message)"
        }
    }
}
finally {
    $connection.Dispose()
}

exit ([int]$failed)
