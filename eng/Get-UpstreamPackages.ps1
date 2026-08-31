#requires -Version 7.0

<#
.SYNOPSIS
Downloads and verifies an exact upstream Diginsight GitHub Release into a local package feed.

.DESCRIPTION
Resolves the pinned upstream release from eng/upstream-releases.json, downloads its release
manifest, checksums, and .nupkg assets, and verifies every asset before the feed becomes visible.
The verified feed path is written to the pipeline.
#>

[CmdletBinding()]
param(
    [string] $Name = 'telemetry',

    [string] $ManifestPath = (Join-Path $PSScriptRoot 'upstream-releases.json'),

    [string] $DestinationRoot,

    [string] $GitHubToken,

    # Offline validation only: treat an already staged local release directory as the release.
    [string] $FromDirectory,

    [switch] $Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'Publish-Packages.ps1')

$RepositoryRoot = (Get-FullPath (Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($DestinationRoot)) {
    $DestinationRoot = Join-Path $RepositoryRoot 'artifacts' 'upstream'
}

function Get-UpstreamPin {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Name
    )

    $fullPath = Get-FullPath $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Upstream pin file '$fullPath' does not exist."
    }

    $document = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json -Depth 20
    if ($null -eq $document.PSObject.Properties['schemaVersion'] -or [int] $document.schemaVersion -ne 1) {
        throw "Upstream pin file '$fullPath' has an unsupported schema version."
    }

    $selected = @(@($document.upstreams) | Where-Object { [string]::Equals([string] $_.name, $Name, [System.StringComparison]::OrdinalIgnoreCase) })
    if ($selected.Count -ne 1) {
        throw "Upstream pin file '$fullPath' must declare exactly one upstream named '$Name'; found $($selected.Count)."
    }

    $pin = $selected[0]
    foreach ($propertyName in @('repository', 'sourceTag', 'packageVersion', 'releaseManifestSchemaVersion', 'versionProperty', 'versionPropertyFile', 'packageSourceMappingPattern', 'requiredPackages')) {
        if ($null -eq $pin.PSObject.Properties[$propertyName]) {
            throw "Upstream '$Name' is missing '$propertyName'."
        }
    }

    $sourceTag = ([string] $pin.sourceTag).Trim()
    if ([string]::IsNullOrWhiteSpace($sourceTag) -or [string]::Equals($sourceTag, 'latest', [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Upstream '$Name' must pin an exact immutable tag; '$sourceTag' is not allowed."
    }

    $expectedVersion = ConvertTo-NormalizedPackageVersion -Version $sourceTag -SourceTag
    $pinnedVersion = ConvertTo-NormalizedPackageVersion -Version ([string] $pin.packageVersion)
    if ($pinnedVersion -cne $expectedVersion) {
        throw "Upstream '$Name' pins package version '$pinnedVersion', but tag '$sourceTag' normalizes to '$expectedVersion'."
    }

    $requiredPackages = @($pin.requiredPackages | ForEach-Object { ([string] $_).Trim() } | Where-Object { $_ -ne '' })
    if ($requiredPackages.Count -eq 0) {
        throw "Upstream '$Name' must list at least one required package."
    }

    return [pscustomobject]@{
        Name                        = [string] $pin.name
        Repository                  = ([string] $pin.repository).Trim()
        SourceTag                   = $sourceTag
        PackageVersion              = $pinnedVersion
        ReleaseManifestSchemaVersion = [int] $pin.releaseManifestSchemaVersion
        VersionProperty             = [string] $pin.versionProperty
        VersionPropertyFile         = [string] $pin.versionPropertyFile
        PackageSourceMappingPattern = [string] $pin.packageSourceMappingPattern
        RequiredPackages            = @($requiredPackages | Sort-Object)
    }
}

function Test-UpstreamFeed {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [object] $Pin
    )

    $fullPath = Get-FullPath $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) {
        throw "Upstream feed '$fullPath' does not exist."
    }

    $releaseManifestPath = Join-Path $fullPath 'release-manifest.json'
    $checksumsPath = Join-Path $fullPath 'SHA256SUMS'
    foreach ($requiredPath in @($releaseManifestPath, $checksumsPath)) {
        if (-not (Test-Path -LiteralPath $requiredPath -PathType Leaf)) {
            throw "Upstream feed '$fullPath' is missing '$([System.IO.Path]::GetFileName($requiredPath))'."
        }
    }

    $manifest = Get-Content -LiteralPath $releaseManifestPath -Raw | ConvertFrom-Json -Depth 20
    foreach ($propertyName in @('schemaVersion', 'repository', 'sourceTag', 'packageVersion', 'packages', 'assets')) {
        if ($null -eq $manifest.PSObject.Properties[$propertyName]) {
            throw "Upstream release manifest is missing '$propertyName'."
        }
    }
    if ([int] $manifest.schemaVersion -ne $Pin.ReleaseManifestSchemaVersion) {
        throw "Upstream release manifest schema version '$($manifest.schemaVersion)' is not the pinned '$($Pin.ReleaseManifestSchemaVersion)'."
    }
    if (-not [string]::Equals([string] $manifest.repository, $Pin.Repository, [System.StringComparison]::Ordinal)) {
        throw "Upstream release manifest repository '$($manifest.repository)' is not the pinned '$($Pin.Repository)'."
    }
    if (-not [string]::Equals([string] $manifest.sourceTag, $Pin.SourceTag, [System.StringComparison]::Ordinal)) {
        throw "Upstream release manifest tag '$($manifest.sourceTag)' is not the pinned '$($Pin.SourceTag)'."
    }
    $manifestVersion = ConvertTo-NormalizedPackageVersion -Version ([string] $manifest.packageVersion)
    if ($manifestVersion -cne $Pin.PackageVersion) {
        throw "Upstream release manifest version '$manifestVersion' is not the pinned '$($Pin.PackageVersion)'."
    }

    $declaredPackageAssets = @($manifest.assets | Where-Object { [string]::Equals([string] $_.role, 'package', [System.StringComparison]::Ordinal) })
    if ($declaredPackageAssets.Count -eq 0) {
        throw 'Upstream release manifest declares no package assets.'
    }

    $declaredNames = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::Ordinal)
    $verifiedIds = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($asset in $declaredPackageAssets) {
        foreach ($propertyName in @('fileName', 'packageId', 'packageVersion', 'sha256', 'size')) {
            if ($null -eq $asset.PSObject.Properties[$propertyName]) {
                throw "Upstream release manifest contains an asset missing '$propertyName'."
            }
        }

        $fileName = [string] $asset.fileName
        if ($fileName.Contains('/') -or $fileName.Contains('\') -or $fileName -eq '..') {
            throw "Upstream release manifest declares an unsafe asset name '$fileName'."
        }
        if (-not $declaredNames.Add($fileName)) {
            throw "Upstream release manifest declares duplicate asset '$fileName'."
        }

        $assetPath = Join-Path $fullPath $fileName
        if (-not (Test-Path -LiteralPath $assetPath -PathType Leaf)) {
            throw "Upstream feed is missing declared package asset '$fileName'."
        }

        $file = Get-Item -LiteralPath $assetPath
        if ([long] $file.Length -ne [long] $asset.size) {
            throw "Upstream asset '$fileName' has size $($file.Length), expected $([long] $asset.size)."
        }
        $actualHash = Get-Sha256 $assetPath
        if ($actualHash -cne ([string] $asset.sha256).ToLowerInvariant()) {
            throw "Upstream asset '$fileName' failed SHA-256 verification."
        }

        $metadata = Get-PackageArchiveMetadata $file
        if ($metadata.Role -ne 'package') {
            throw "Upstream asset '$fileName' is not a package archive."
        }
        if (-not [string]::Equals($metadata.Id, [string] $asset.packageId, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Upstream asset '$fileName' contains package id '$($metadata.Id)', expected '$($asset.packageId)'."
        }
        if ($metadata.Version -cne $Pin.PackageVersion) {
            throw "Upstream asset '$fileName' contains version '$($metadata.RawVersion)', expected '$($Pin.PackageVersion)'."
        }
        [void] $verifiedIds.Add($metadata.Id)
    }

    $presentPackages = @(Get-ChildItem -LiteralPath $fullPath -File | Where-Object { $_.Name.EndsWith('.nupkg', [System.StringComparison]::OrdinalIgnoreCase) })
    foreach ($file in $presentPackages) {
        if (-not $declaredNames.Contains($file.Name)) {
            throw "Upstream feed contains undeclared package '$($file.Name)'."
        }
    }

    $missingRequired = @($Pin.RequiredPackages | Where-Object { -not $verifiedIds.Contains($_) })
    if ($missingRequired.Count -ne 0) {
        throw "Upstream release is missing required packages: $($missingRequired -join ', ')."
    }

    $checksumByName = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    foreach ($line in @(Get-Content -LiteralPath $checksumsPath | Where-Object { $_ -ne '' })) {
        $match = [regex]::Match($line, '^(?<hash>[0-9a-fA-F]{64})  (?<name>[^\\/]+)$', [System.Text.RegularExpressions.RegexOptions]::CultureInvariant)
        if (-not $match.Success) {
            throw "Invalid upstream SHA256SUMS line '$line'."
        }
        $checksumByName[$match.Groups['name'].Value] = $match.Groups['hash'].Value.ToLowerInvariant()
    }
    foreach ($fileName in $declaredNames) {
        if (-not $checksumByName.ContainsKey($fileName)) {
            throw "Upstream SHA256SUMS has no entry for '$fileName'."
        }
        if ($checksumByName[$fileName] -cne (Get-Sha256 (Join-Path $fullPath $fileName))) {
            throw "Upstream SHA256SUMS disagrees with the bytes of '$fileName'."
        }
    }

    return [pscustomobject]@{
        Path           = $fullPath
        PackageCount   = $declaredNames.Count
        PackageVersion = $Pin.PackageVersion
        SourceTag      = $Pin.SourceTag
    }
}

function Invoke-UpstreamDownload {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Pin,

        [Parameter(Mandatory)]
        [string] $TargetPath,

        [string] $FromDirectory
    )

    $null = New-Item -ItemType Directory -Path $TargetPath -Force

    if (-not [string]::IsNullOrWhiteSpace($FromDirectory)) {
        $sourcePath = Get-FullPath $FromDirectory
        if (-not (Test-Path -LiteralPath $sourcePath -PathType Container)) {
            throw "Local release directory '$sourcePath' does not exist."
        }
        Write-Host "Using local release directory '$sourcePath' instead of a GitHub download."
        foreach ($file in @(Get-ChildItem -LiteralPath $sourcePath -File)) {
            if ($file.Name.EndsWith('.snupkg', [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $TargetPath $file.Name)
        }
        return
    }

    $gh = Get-Command gh -CommandType Application -ErrorAction SilentlyContinue
    if ($null -eq $gh) {
        throw 'GitHub CLI (gh) is required to download upstream release assets.'
    }

    $arguments = @(
        'release', 'download', $Pin.SourceTag,
        '--repo', $Pin.Repository,
        '--dir', $TargetPath,
        '--pattern', '*.nupkg',
        '--pattern', 'SHA256SUMS',
        '--pattern', 'release-manifest.json'
    )

    Write-Host "Downloading $($Pin.Repository) release $($Pin.SourceTag)."
    & $gh.Source @arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Could not download release '$($Pin.SourceTag)' from '$($Pin.Repository)' (exit code $LASTEXITCODE)."
    }
}

function Get-UpstreamFeed {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [object] $Pin,

        [Parameter(Mandatory)]
        [string] $DestinationRoot,

        [string] $FromDirectory,

        [switch] $Force
    )

    $upstreamRoot = Join-Path (Get-FullPath $DestinationRoot) $Pin.Name
    $feedPath = Join-Path $upstreamRoot $Pin.SourceTag

    if ((Test-Path -LiteralPath $feedPath -PathType Container) -and -not $Force) {
        try {
            $existing = Test-UpstreamFeed -Path $feedPath -Pin $Pin
            Write-Host "Reusing verified upstream feed '$feedPath' ($($existing.PackageCount) packages)."
            return $existing
        }
        catch {
            Write-Host "Existing upstream feed is not usable and will be replaced: $($_.Exception.Message)"
        }
    }

    $null = New-Item -ItemType Directory -Path $upstreamRoot -Force
    $temporaryPath = Join-Path $upstreamRoot ".tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        Invoke-UpstreamDownload -Pin $Pin -TargetPath $temporaryPath -FromDirectory $FromDirectory
        $result = Test-UpstreamFeed -Path $temporaryPath -Pin $Pin

        if (Test-Path -LiteralPath $feedPath) {
            Remove-Item -LiteralPath $feedPath -Recurse -Force
        }
        Move-Item -LiteralPath $temporaryPath -Destination $feedPath
        $result.Path = Get-FullPath $feedPath
        Write-Host "Verified upstream feed '$($result.Path)' ($($result.PackageCount) packages, version $($result.PackageVersion))."
        return $result
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

if ($MyInvocation.InvocationName -eq '.') {
    return
}

if (-not [string]::IsNullOrWhiteSpace($GitHubToken)) {
    $env:GH_TOKEN = $GitHubToken
}

$pin = Get-UpstreamPin -Path $ManifestPath -Name $Name
$feed = Get-UpstreamFeed -Pin $pin -DestinationRoot $DestinationRoot -FromDirectory $FromDirectory -Force:$Force

if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
    "feed-path=$($feed.Path)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
    "package-version=$($feed.PackageVersion)" | Out-File -FilePath $env:GITHUB_OUTPUT -Append -Encoding utf8
}

$feed.Path
