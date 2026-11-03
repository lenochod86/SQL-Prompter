<#
.SYNOPSIS
Checks that a pull request bumps the versions SSMS relies on.

.DESCRIPTION
- Any change under src/ changes the VSIX, so the version in source.extension.vsixmanifest must be higher
  than on the base branch; otherwise the new VSIX does not install over the old one.
- A change to SqlPrompterCommands.vsct must raise the menu version in ProvideMenuResource;
  otherwise SSMS keeps showing its cached menus.

.PARAMETER Base
The git ref to compare with, e.g. origin/main.
#>
param(
    [Parameter(Mandatory)]
    [string] $Base
)

$ErrorActionPreference = 'Stop'

$manifestPath = 'src/SqlPrompter.Ssms/source.extension.vsixmanifest'
$packagePath = 'src/SqlPrompter.Ssms/SqlPrompterPackage.cs'
$vsctPath = 'src/SqlPrompter.Ssms/SqlPrompterCommands.vsct'

function Get-BaseFile([string] $path) {
    $content = git show "${Base}:$path" 2>$null
    if ($LASTEXITCODE -ne 0) {
        return $null
    }

    return $content -join "`n"
}

function Get-ManifestVersion([string] $xml) {
    if (-not $xml) {
        return $null
    }

    return [version]([xml]$xml).PackageManifest.Metadata.Identity.Version
}

function Get-MenuVersion([string] $code) {
    if ($code -and $code -match 'ProvideMenuResource\(\s*"[^"]+"\s*,\s*(\d+)\s*\)') {
        return [int]$Matches[1]
    }

    return $null
}

$mergeBase = git merge-base $Base HEAD
$changed = @(git diff --name-only $mergeBase HEAD)
$failures = @()

$sourceChanges = @($changed | Where-Object { $_ -like 'src/*' })
if ($sourceChanges.Count -gt 0) {
    $before = Get-ManifestVersion (Get-BaseFile $manifestPath)
    $after = Get-ManifestVersion ((Get-Content $manifestPath) -join "`n")
    if ($before -and $after -le $before) {
        $failures += "src/ changed but the VSIX version in $manifestPath is $after (base $before). Raise it so the VSIX installs over the old one."
    }
    else {
        Write-Host "VSIX version: $before -> $after"
    }
}
else {
    Write-Host 'No changes under src/; VSIX version not checked.'
}

if ($changed -contains $vsctPath) {
    $before = Get-MenuVersion (Get-BaseFile $packagePath)
    $after = Get-MenuVersion ((Get-Content $packagePath) -join "`n")
    if ($null -eq $after) {
        $failures += "ProvideMenuResource not found in $packagePath."
    }
    elseif ($null -ne $before -and $after -le $before) {
        $failures += "$vsctPath changed but the menu version in ProvideMenuResource is $after (base $before). Raise it so SSMS reloads the menus."
    }
    else {
        Write-Host "Menu version: $before -> $after"
    }
}

foreach ($failure in $failures) {
    Write-Host "::error::$failure"
}

if ($failures.Count -gt 0) {
    exit 1
}
