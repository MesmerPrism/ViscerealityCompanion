<#
.SYNOPSIS
    Copies the latest local Sussex visual profiles into the repo's bundled release profile folder.
#>
[CmdletBinding()]
param(
    [string]$SourceRoot = (Join-Path $env:LOCALAPPDATA 'ViscerealityCompanion\sussex-visual-profiles'),
    [string]$DestinationRoot = '',
    [string]$StudyId = 'sussex-university'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function ConvertTo-Slug {
    param([Parameter(Mandatory = $true)][string]$Text)

    $trimmed = $Text.Trim().ToLowerInvariant()
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return 'sussex-visual-profile'
    }

    $builder = New-Object System.Text.StringBuilder
    $previousWasDash = $false
    foreach ($character in $trimmed.ToCharArray()) {
        if ([char]::IsLetterOrDigit($character)) {
            [void]$builder.Append($character)
            $previousWasDash = $false
            continue
        }

        if ($previousWasDash) {
            continue
        }

        [void]$builder.Append('-')
        $previousWasDash = $true
    }

    $slug = $builder.ToString().Trim('-')
    if ([string]::IsNullOrWhiteSpace($slug)) {
        return 'sussex-visual-profile'
    }

    return $slug
}

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $repoRoot ("samples\study-shells\{0}\visual-profiles" -f $StudyId)
}

if (-not (Test-Path $SourceRoot)) {
    throw "Local Sussex visual profile root not found: $SourceRoot"
}

New-Item -ItemType Directory -Force -Path $DestinationRoot | Out-Null

$candidates = @()
foreach ($file in Get-ChildItem -Path $SourceRoot -Filter *.json -File | Sort-Object LastWriteTimeUtc -Descending) {
    if ($file.Name.StartsWith('zzz-', [System.StringComparison]::OrdinalIgnoreCase)) {
        continue
    }

    try {
        $document = Get-Content -Path $file.FullName -Raw | ConvertFrom-Json
        $profileName = [string]$document.profile.name
        if ([string]::IsNullOrWhiteSpace($profileName)) {
            continue
        }

        $candidates += [pscustomobject]@{
            ProfileName = $profileName.Trim()
            SourcePath = $file.FullName
            LastWriteTimeUtc = $file.LastWriteTimeUtc
        }
    }
    catch {
        # Ignore malformed files already on disk.
    }
}

$latestByName = $candidates |
    Group-Object -Property ProfileName |
    ForEach-Object {
        $_.Group | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    } |
    Sort-Object ProfileName

Get-ChildItem -Path $DestinationRoot -Filter *.json -File -ErrorAction SilentlyContinue |
    Remove-Item -Force

$written = New-Object System.Collections.Generic.List[string]
foreach ($profile in $latestByName) {
    $targetName = (ConvertTo-Slug -Text $profile.ProfileName) + '.json'
    $targetPath = Join-Path $DestinationRoot $targetName
    Copy-Item -Path $profile.SourcePath -Destination $targetPath -Force
    $written.Add($targetPath)
}

Write-Host ("Bundled {0} Sussex visual profile(s) into {1}" -f $written.Count, $DestinationRoot) -ForegroundColor Green
foreach ($path in $written) {
    Write-Host ("  - {0}" -f (Split-Path -Leaf $path)) -ForegroundColor Cyan
}
