#requires -Version 7.0

<#
.SYNOPSIS
Downloads a verified Diginsight package release into the local package source.

.DESCRIPTION
Resolves the GitHub Release matching the pinned DiginsightCoreVersion, downloads its .nupkg assets,
and verifies every one against the release manifest and SHA256SUMS before publishing them into
artifacts/packages. That folder is declared in NuGet.Config, so a plain `dotnet restore` picks the
packages up while everything else continues to come from the corporate proxy.

.EXAMPLE
./eng/Download-PackageRelease.ps1 https://github.com/diginsight/telemetry

.EXAMPLE
./eng/Download-PackageRelease.ps1 https://github.com/diginsight/telemetry -Version 3.8.0.2
Pins DiginsightCoreVersion to 3.8.0.2 and downloads that release.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string] $RepositoryUrl,

    # When supplied, DiginsightCoreVersion is repinned to this version before downloading.
    [string] $Version,

    [string] $Destination,

    # Offline validation only: treat an already staged local release directory as the release.
    [string] $FromDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Publish-Packages.ps1')

$RepositoryRoot = Get-FullPath (Join-Path $PSScriptRoot '..')
$VersionPropsPath = Join-Path $RepositoryRoot 'src' 'Directory.Build.props'
$VersionProperty = 'DiginsightCoreVersion'
$VersionProjectPath = Join-Path $RepositoryRoot 'src' 'Diginsight.SmartCache' 'Diginsight.SmartCache.csproj'
$KeepFileName = '.gitkeep'

function ConvertTo-RepositorySlug {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Url
    )

    $value = $Url.Trim().TrimEnd('/')
    $value = $value -replace '\.git$', ''
    $match = [regex]::Match($value, '^(?:https?://github\.com/)?(?<owner>[A-Za-z0-9._-]+)/(?<repo>[A-Za-z0-9._-]+)$')
    if (-not $match.Success) {
        throw "'$Url' is not a GitHub repository URL such as https://github.com/diginsight/telemetry."
    }

    return "$($match.Groups['owner'].Value)/$($match.Groups['repo'].Value)"
}

function Get-PinnedVersion {
    [CmdletBinding()]
    param()

    $evaluated = & dotnet msbuild $VersionProjectPath "-getProperty:$VersionProperty" -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Could not evaluate '$VersionProperty' from '$VersionProjectPath'."
    }

    $value = ([string] $evaluated).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "'$VersionProperty' is not set in '$VersionPropsPath'."
    }

    return ConvertTo-NormalizedPackageVersion -Version $value
}

function Set-PinnedVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Value
    )

    $lines = @(Get-Content -LiteralPath $VersionPropsPath)
    $pattern = "^(?<indent>\s*)<$VersionProperty>(?<value>[^<]*)</$VersionProperty>\s*$"
    $indexes = @(0..($lines.Count - 1) | Where-Object { $lines[$_] -match $pattern })
    if ($indexes.Count -ne 1) {
        throw "Expected exactly one active '$VersionProperty' element in '$VersionPropsPath'; found $($indexes.Count)."
    }

    $index = $indexes[0]
    if ($lines[$index] -notmatch $pattern) {
        throw "Could not parse '$VersionProperty' in '$VersionPropsPath'."
    }

    $current = $Matches['value']
    if ($current -ceq $Value) {
        return $false
    }

    $lines[$index] = "$($Matches['indent'])<$VersionProperty>$Value</$VersionProperty>"
    $encoding = [System.Text.UTF8Encoding]::new((Test-Utf8Bom $VersionPropsPath))
    [System.IO.File]::WriteAllText($VersionPropsPath, (($lines -join "`r`n") + "`r`n"), $encoding)
    Write-Host "Repinned $VersionProperty from $current to $Value in '$VersionPropsPath'."
    return $true
}

function Test-Utf8Bom {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    return ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
}

function Resolve-ReleaseTag {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string] $PackageVersion,

        [switch] $Offline
    )

    # NuGet drops a zero fourth component, so 3.8.0 may correspond to tag v3.8.0 or v3.8.0.0.
    $match = [regex]::Match($PackageVersion, '^(?<numbers>[0-9]+(?:\.[0-9]+){0,3})(?<suffix>-.+)?$')
    if (-not $match.Success) {
        throw "'$PackageVersion' is not a supported package version."
    }

    $numbers = $match.Groups['numbers'].Value
    $suffix = $match.Groups['suffix'].Value
    $candidates = [System.Collections.Generic.List[string]]::new()
    $candidates.Add("v$numbers$suffix")
    if ($numbers.Split('.').Count -eq 3) {
        $candidates.Add("v$numbers.0$suffix")
    }

    if ($Offline) {
        return $candidates[0]
    }

    foreach ($candidate in $candidates) {
        & gh release view $candidate --repo $Repository --json tagName *> $null
        if ($LASTEXITCODE -eq 0) {
            return $candidate
        }
    }

    $available = & gh release list --repo $Repository --limit 10 --json tagName 2>$null
    $known = if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace($available)) {
        (($available | ConvertFrom-Json).tagName) -join ', '
    }
    else {
        '(none)'
    }

    throw "No GitHub Release in '$Repository' matches version $PackageVersion (tried $($candidates -join ', ')). Available releases: $known."
}

function Test-ReleaseDownload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string] $Tag,

        [Parameter(Mandatory)]
        [string] $PackageVersion
    )

    $manifestPath = Join-Path $Path 'release-manifest.json'
    $checksumsPath = Join-Path $Path 'SHA256SUMS'
    foreach ($required in @($manifestPath, $checksumsPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Release '$Tag' is missing '$([System.IO.Path]::GetFileName($required))'."
        }
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json -Depth 20
    foreach ($property in @('schemaVersion', 'repository', 'sourceTag', 'packageVersion', 'assets')) {
        if ($null -eq $manifest.PSObject.Properties[$property]) {
            throw "Release manifest is missing '$property'."
        }
    }
    if ([int] $manifest.schemaVersion -ne 1) {
        throw "Release manifest schema version '$($manifest.schemaVersion)' is not supported."
    }
    if (-not [string]::Equals([string] $manifest.repository, $Repository, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Release manifest repository '$($manifest.repository)' is not '$Repository'."
    }
    if (-not [string]::Equals([string] $manifest.sourceTag, $Tag, [System.StringComparison]::Ordinal)) {
        throw "Release manifest tag '$($manifest.sourceTag)' is not '$Tag'."
    }
    $manifestVersion = ConvertTo-NormalizedPackageVersion -Version ([string] $manifest.packageVersion)
    if ($manifestVersion -cne $PackageVersion) {
        throw "Release manifest version '$manifestVersion' is not the pinned '$PackageVersion'."
    }

    $checksums = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in @(Get-Content -LiteralPath $checksumsPath | Where-Object { $_ -ne '' })) {
        $entry = [regex]::Match($line, '^(?<hash>[0-9a-fA-F]{64})  (?<name>[^\\/]+)$')
        if (-not $entry.Success) {
            throw "Invalid SHA256SUMS line '$line'."
        }
        $checksums[$entry.Groups['name'].Value] = $entry.Groups['hash'].Value.ToLowerInvariant()
    }

    $declared = @($manifest.assets | Where-Object { [string]::Equals([string] $_.role, 'package', [System.StringComparison]::Ordinal) })
    if ($declared.Count -eq 0) {
        throw "Release '$Tag' declares no package assets."
    }

    $expected = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    foreach ($asset in $declared) {
        $fileName = [string] $asset.fileName
        if ($fileName.Contains('/') -or $fileName.Contains('\')) {
            throw "Release manifest declares an unsafe asset name '$fileName'."
        }
        [void] $expected.Add($fileName)

        $assetPath = Join-Path $Path $fileName
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            throw "Release '$Tag' is missing declared package '$fileName'."
        }

        $file = Get-Item -LiteralPath $assetPath
        if ([long] $file.Length -ne [long] $asset.size) {
            throw "Package '$fileName' has size $($file.Length), expected $([long] $asset.size)."
        }

        $hash = Get-Sha256 $assetPath
        if ($hash -cne ([string] $asset.sha256).ToLowerInvariant()) {
            throw "Package '$fileName' failed SHA-256 verification against the release manifest."
        }
        if (-not $checksums.ContainsKey($fileName) -or $checksums[$fileName] -cne $hash) {
            throw "Package '$fileName' failed SHA-256 verification against SHA256SUMS."
        }

        $metadata = Get-PackageArchiveMetadata $file
        if (-not [string]::Equals($metadata.Id, [string] $asset.packageId, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Package '$fileName' contains id '$($metadata.Id)', expected '$($asset.packageId)'."
        }
        if ($metadata.Version -cne $PackageVersion) {
            throw "Package '$fileName' contains version '$($metadata.RawVersion)', expected '$PackageVersion'."
        }
    }

    foreach ($file in @(Get-ChildItem -LiteralPath $Path -File -Filter '*.nupkg')) {
        if (-not $expected.Contains($file.Name)) {
            throw "Release '$Tag' contains undeclared package '$($file.Name)'."
        }
    }

    return $expected.Count
}

function Publish-LocalSource {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $StagedPath,

        [Parameter(Mandatory)]
        [string] $DestinationPath
    )

    $null = New-Item -ItemType Directory -Path $DestinationPath -Force
    foreach ($existing in @(Get-ChildItem -LiteralPath $DestinationPath -File)) {
        if ($existing.Name -ne $KeepFileName) {
            Remove-Item -LiteralPath $existing.FullName -Force
        }
    }
    foreach ($file in @(Get-ChildItem -LiteralPath $StagedPath -File)) {
        Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $DestinationPath $file.Name)
    }
}

if ($MyInvocation.InvocationName -eq '.') {
    return
}
if ([string]::IsNullOrWhiteSpace($RepositoryUrl)) {
    throw 'RepositoryUrl is required, for example https://github.com/diginsight/telemetry.'
}

if ([string]::IsNullOrWhiteSpace($Destination)) {
    $Destination = Join-Path $RepositoryRoot 'artifacts' 'packages'
}
$Destination = Get-FullPath $Destination

$repository = ConvertTo-RepositorySlug -Url $RepositoryUrl

if (-not [string]::IsNullOrWhiteSpace($Version)) {
    $null = Set-PinnedVersion -Value (ConvertTo-NormalizedPackageVersion -Version $Version)
}
$packageVersion = Get-PinnedVersion

$offline = -not [string]::IsNullOrWhiteSpace($FromDirectory)
if (-not $offline -and $null -eq (Get-Command gh -CommandType Application -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required to download release assets.'
}

$tag = Resolve-ReleaseTag -Repository $repository -PackageVersion $packageVersion -Offline:$offline
$staging = Join-Path ([System.IO.Path]::GetTempPath()) "diginsight-release-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $staging -Force

try {
    if ($offline) {
        Write-Host "Using local release directory '$FromDirectory'."
        foreach ($file in @(Get-ChildItem -LiteralPath (Get-FullPath $FromDirectory) -File)) {
            if (-not $file.Name.EndsWith('.snupkg', [System.StringComparison]::OrdinalIgnoreCase)) {
                Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $staging $file.Name)
            }
        }
    }
    else {
        Write-Host "Downloading $repository release $tag."
        & gh release download $tag --repo $repository --dir $staging --pattern '*.nupkg' --pattern 'SHA256SUMS' --pattern 'release-manifest.json'
        if ($LASTEXITCODE -ne 0) {
            throw "Could not download release '$tag' from '$repository'."
        }
    }

    $count = Test-ReleaseDownload -Path $staging -Repository $repository -Tag $tag -PackageVersion $packageVersion
    Publish-LocalSource -StagedPath $staging -DestinationPath $Destination
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
Write-Host "Repository : $repository"
Write-Host "Release    : $tag"
Write-Host "Version    : $packageVersion"
Write-Host "Packages   : $count verified"
Write-Host "Local source: $Destination"
Write-Host ''
Write-Host 'Run: dotnet restore src/Diginsight.SmartCache.slnx --force-evaluate'
