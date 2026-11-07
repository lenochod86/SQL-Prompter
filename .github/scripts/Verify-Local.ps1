<#
.SYNOPSIS
Runs on the developer's machine what CI would run, so that a branch is pushed only when it is green (and no billed Actions minutes are spent on a red push).

.DESCRIPTION
1. The tests with warnings as errors (the same command as the CI job).
2. The version checks against the base branch (Check-Versions.ps1).
3. On Windows, the VSIX build with warnings as errors and the version inside the .vsix.

.PARAMETER Base
The git ref to compare the versions with; default origin/main (fetched first).

.PARAMETER SkipVsix
Skip the VSIX build (for a change that does not touch src/SqlPrompter.Ssms).
#>
param(
    [string] $Base = 'origin/main',
    [switch] $SkipVsix
)

$ErrorActionPreference = 'Stop'
Set-Location (Resolve-Path (Join-Path $PSScriptRoot '..\..'))

function Step([string] $name, [scriptblock] $action) {
    Write-Host "== $name" -ForegroundColor Cyan
    & $action
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $name" -ForegroundColor Red
        exit 1
    }
}

git fetch --quiet origin
Step 'Tests (warnings as errors)' { dotnet test tests/SqlPrompter.Tests/SqlPrompter.Tests.csproj -v:minimal -warnaserror }
Step "Versions against $Base" { & ./.github/scripts/Check-Versions.ps1 -Base $Base }

$onWindows = [System.Environment]::OSVersion.Platform -eq 'Win32NT'
if ($SkipVsix -or -not $onWindows) {
    Write-Host '== VSIX build skipped' -ForegroundColor Yellow
}
else {
    Step 'VSIX build (warnings as errors)' { dotnet build src/SqlPrompter.Ssms/SqlPrompter.Ssms.csproj -c Release -v:minimal -warnaserror }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $vsix = Get-Item src/SqlPrompter.Ssms/bin/Release/net48/*.vsix
    $zip = [IO.Compression.ZipFile]::OpenRead($vsix.FullName)
    try {
        $entry = $zip.Entries | Where-Object FullName -eq 'extension.vsixmanifest'
        $reader = New-Object IO.StreamReader($entry.Open())
        $version = ([xml]$reader.ReadToEnd()).PackageManifest.Metadata.Identity.Version
        $reader.Dispose()
    }
    finally {
        $zip.Dispose()
    }

    Write-Host "VSIX $($vsix.Name), version $version" -ForegroundColor Green
}

Write-Host 'All green: the branch can be pushed.' -ForegroundColor Green
