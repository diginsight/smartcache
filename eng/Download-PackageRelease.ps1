#requires -Version 7.0

<#
.SYNOPSIS
Downloads verified Diginsight package releases into the local package source.

.DESCRIPTION
Resolves the GitHub Release of each configured upstream repository, matching the version pinned in
src/Directory.Build.props, downloads its .nupkg assets, and verifies every one against the release
manifest and SHA256SUMS. Only when every upstream has validated is artifacts/packages replaced, in a
single atomic step. That folder is declared in NuGet.Config, so a plain `dotnet restore` picks the
packages up while everything else continues to come from the corporate proxy.

Upstreams and the MSBuild property that pins each of them are listed in eng/upstream-releases.json.
Versions live only in src/Directory.Build.props and are never duplicated here.

A pin may be exact (3.8.0.1) or floating (3.8.*, 1.*, *). A floating pin is resolved against the
upstream's release list and always selects the newest matching stable release; prereleases are
never selected by a floating pin.

.EXAMPLE
./eng/Download-PackageRelease.ps1
Downloads every upstream listed in eng/upstream-releases.json.

.EXAMPLE
./eng/Download-PackageRelease.ps1 https://github.com/diginsight/telemetry
Downloads only the telemetry release.

.EXAMPLE
./eng/Download-PackageRelease.ps1 https://github.com/diginsight/telemetry -Version 3.8.0.2
Repins DiginsightCoreVersion to 3.8.0.2 and downloads that release.
#>

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string[]] $RepositoryUrl,

    # When supplied, the upstream's version property is repinned before downloading.
    # Requires exactly one repository.
    [string] $Version,

    # Overrides the version property for a repository not listed in eng/upstream-releases.json.
    [string] $VersionProperty,

    [string] $Destination,

    [string] $UpstreamManifestPath,

    # Offline validation only: treat an already staged local release directory as the release.
    [string] $FromDirectory
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

# Captured before dot-sourcing: Publish-Packages.ps1 declares its own param() block in this scope.
$InputRepositoryUrl = $RepositoryUrl
$InputVersion = $Version
$InputVersionProperty = $VersionProperty
$InputDestination = $Destination
$InputUpstreamManifestPath = $UpstreamManifestPath
$InputFromDirectory = $FromDirectory
$IsDotSourced = $MyInvocation.InvocationName -eq '.'

. (Join-Path $PSScriptRoot 'Publish-Packages.ps1')

$RepositoryRoot = Get-FullPath (Join-Path $PSScriptRoot '..')
$VersionPropsPath = Join-Path $RepositoryRoot 'src' 'Directory.Build.props'
$VersionProjectPath = Join-Path $RepositoryRoot 'src' 'Diginsight.SmartCache' 'Diginsight.SmartCache.csproj'
$DefaultUpstreamManifestPath = Join-Path $PSScriptRoot 'upstream-releases.json'
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

function Get-UpstreamConfiguration {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Path
    )

    $fullPath = Get-FullPath $Path
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        throw "Upstream inventory '$fullPath' does not exist."
    }

    $configuration = Get-Content -LiteralPath $fullPath -Raw | ConvertFrom-Json -Depth 20
    foreach ($property in @('schemaVersion', 'upstreams')) {
        if ($null -eq $configuration.PSObject.Properties[$property]) {
            throw "Upstream inventory '$fullPath' is missing '$property'."
        }
    }
    if ([int] $configuration.schemaVersion -ne 1) {
        throw "Upstream inventory '$fullPath' has unsupported schema version '$($configuration.schemaVersion)'."
    }

    $entries = @($configuration.upstreams)
    if ($entries.Count -eq 0) {
        throw "Upstream inventory '$fullPath' does not list any upstreams."
    }

    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    $result = [System.Collections.Generic.List[object]]::new()
    foreach ($entry in $entries) {
        foreach ($property in @('repository', 'versionProperty')) {
            if ($null -eq $entry.PSObject.Properties[$property] -or [string]::IsNullOrWhiteSpace([string] $entry.$property)) {
                throw "Upstream inventory '$fullPath' contains an entry with no '$property'."
            }
        }

        $repository = ConvertTo-RepositorySlug -Url ([string] $entry.repository)
        if (-not $seen.Add($repository)) {
            throw "Upstream inventory '$fullPath' contains duplicate repository '$repository'."
        }
        $result.Add([pscustomobject]@{
            Repository      = $repository
            VersionProperty = ([string] $entry.versionProperty).Trim()
        })
    }

    return @($result)
}

function Get-UpstreamSelection {
    [CmdletBinding()]
    param(
        [string[]] $Url,

        [string] $Property,

        [Parameter(Mandatory)]
        [string] $ManifestPath
    )

    $configured = @(Get-UpstreamConfiguration -Path $ManifestPath)
    if ($null -eq $Url -or $Url.Count -eq 0) {
        if (-not [string]::IsNullOrWhiteSpace($Property)) {
            throw 'VersionProperty requires an explicit repository.'
        }
        return $configured
    }

    $selected = [System.Collections.Generic.List[object]]::new()
    $seen = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($candidate in $Url) {
        $repository = ConvertTo-RepositorySlug -Url $candidate
        if (-not $seen.Add($repository)) {
            throw "Repository '$repository' was requested more than once."
        }

        if (-not [string]::IsNullOrWhiteSpace($Property)) {
            $selected.Add([pscustomobject]@{ Repository = $repository; VersionProperty = $Property.Trim() })
            continue
        }

        $match = @($configured | Where-Object { [string]::Equals($_.Repository, $repository, [System.StringComparison]::OrdinalIgnoreCase) })
        if ($match.Count -eq 1) {
            $selected.Add($match[0])
            continue
        }

        $known = ($configured.Repository -join ', ')
        throw "Repository '$repository' is not listed in '$ManifestPath' (known: $known). Supply -VersionProperty to override."
    }

    return @($selected)
}

function Get-PinnedVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Property
    )

    $evaluated = & dotnet msbuild $VersionProjectPath "-getProperty:$Property" -nologo
    if ($LASTEXITCODE -ne 0) {
        throw "Could not evaluate '$Property' from '$VersionProjectPath'."
    }

    $value = ([string] $evaluated).Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        throw "'$Property' is not set in '$VersionPropsPath'."
    }

    # A floating range is returned as written; the caller resolves it against the release list.
    if (Test-FloatingVersion -Version $value) {
        return $value
    }

    return ConvertTo-NormalizedPackageVersion -Version $value
}

function Test-FloatingVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Version
    )

    return [regex]::IsMatch($Version.Trim(), '^(?:\*|[0-9]+(?:\.[0-9]+){0,2}\.\*)$')
}

function Resolve-FloatingVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Repository,

        [Parameter(Mandatory)]
        [string] $Pattern
    )

    $trimmed = $Pattern.Trim()
    # Typed as an array so a one-component prefix does not unwrap to a scalar and an empty prefix
    # does not collapse to $null, both of which make .Count throw under Set-StrictMode.
    [string[]] $prefix = @()
    if ($trimmed -ne '*') {
        $prefix = [string[]] ($trimmed.Substring(0, $trimmed.Length - 2).Split('.'))
    }

    $listed = & gh release list --repo $Repository --limit 200 --json tagName
    if ($LASTEXITCODE -ne 0) {
        throw "Could not list the releases of '$Repository'."
    }

    $best = $null
    $bestParts = $null
    foreach ($tagName in @(($listed | ConvertFrom-Json).tagName)) {
        if (-not $tagName.StartsWith('v', [System.StringComparison]::Ordinal)) { continue }
        $candidate = $tagName.Substring(1)

        # A floating range never selects a prerelease.
        if (-not [regex]::IsMatch($candidate, '^[0-9]+(?:\.[0-9]+){0,3}$')) { continue }

        $parts = [System.Collections.Generic.List[long]]::new()
        foreach ($component in $candidate.Split('.')) { $parts.Add([long] $component) }
        while ($parts.Count -lt 4) { $parts.Add(0L) }

        $isMatch = $true
        for ($i = 0; $i -lt $prefix.Count; $i++) {
            if ($parts[$i] -ne [long] $prefix[$i]) { $isMatch = $false; break }
        }
        if (-not $isMatch) { continue }

        if ($null -eq $bestParts) {
            $best = $candidate
            $bestParts = $parts
            continue
        }
        for ($i = 0; $i -lt 4; $i++) {
            if ($parts[$i] -gt $bestParts[$i]) { $best = $candidate; $bestParts = $parts; break }
            if ($parts[$i] -lt $bestParts[$i]) { break }
        }
    }

    if ($null -eq $best) {
        throw "No GitHub Release in '$Repository' matches the floating version '$Pattern'."
    }

    return ConvertTo-NormalizedPackageVersion -Version $best
}

function Set-PinnedVersion {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string] $Property,

        [Parameter(Mandatory)]
        [string] $Value
    )

    $lines = @(Get-Content -LiteralPath $VersionPropsPath)
    $pattern = "^(?<indent>\s*)<$Property>(?<value>[^<]*)</$Property>\s*$"
    $indexes = @(0..($lines.Count - 1) | Where-Object { $lines[$_] -match $pattern })
    if ($indexes.Count -ne 1) {
        throw "Expected exactly one active '$Property' element in '$VersionPropsPath'; found $($indexes.Count)."
    }

    $index = $indexes[0]
    if ($lines[$index] -notmatch $pattern) {
        throw "Could not parse '$Property' in '$VersionPropsPath'."
    }

    $current = $Matches['value']
    if ($current -ceq $Value) {
        return $false
    }

    $lines[$index] = "$($Matches['indent'])<$Property>$Value</$Property>"
    $encoding = [System.Text.UTF8Encoding]::new((Test-Utf8Bom $VersionPropsPath))
    [System.IO.File]::WriteAllText($VersionPropsPath, (($lines -join "`r`n") + "`r`n"), $encoding)
    Write-Host "Repinned $Property from $current to $Value in '$VersionPropsPath'."
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

function Merge-UpstreamStaging {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory)]
        [string[]] $StagedPath,

        [Parameter(Mandatory)]
        [string] $MergedPath
    )

    $null = New-Item -ItemType Directory -Path $MergedPath -Force
    $owners = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in $StagedPath) {
        foreach ($file in @(Get-ChildItem -LiteralPath $path -File -Filter '*.nupkg')) {
            if ($owners.ContainsKey($file.Name)) {
                throw "Package '$($file.Name)' is provided by more than one upstream ('$($owners[$file.Name])' and '$path')."
            }
            $owners[$file.Name] = $path
            Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $MergedPath $file.Name)
        }
    }

    return $owners.Count
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

if ($IsDotSourced) {
    return
}

$manifestPath = if ([string]::IsNullOrWhiteSpace($InputUpstreamManifestPath)) {
    $DefaultUpstreamManifestPath
}
else {
    Get-FullPath $InputUpstreamManifestPath
}

$upstreams = Get-UpstreamSelection -Url $InputRepositoryUrl -Property $InputVersionProperty -ManifestPath $manifestPath

$destinationPath = if ([string]::IsNullOrWhiteSpace($InputDestination)) {
    Join-Path $RepositoryRoot 'artifacts' 'packages'
}
else {
    Get-FullPath $InputDestination
}

$offline = -not [string]::IsNullOrWhiteSpace($InputFromDirectory)
if ($offline -and $upstreams.Count -ne 1) {
    throw 'FromDirectory requires exactly one repository.'
}
if (-not [string]::IsNullOrWhiteSpace($InputVersion)) {
    if ($upstreams.Count -ne 1) {
        throw 'Version requires exactly one repository.'
    }
    $null = Set-PinnedVersion -Property $upstreams[0].VersionProperty -Value (ConvertTo-NormalizedPackageVersion -Version $InputVersion)
}
if (-not $offline -and $null -eq (Get-Command gh -CommandType Application -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) is required to download release assets.'
}

$staging = Join-Path ([System.IO.Path]::GetTempPath()) "diginsight-release-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $staging -Force
$results = [System.Collections.Generic.List[object]]::new()
$total = 0

try {
    $index = 0
    foreach ($upstream in $upstreams) {
        $index++
        $packageVersion = Get-PinnedVersion -Property $upstream.VersionProperty
        if (Test-FloatingVersion -Version $packageVersion) {
            if ($offline) {
                throw "Floating version '$packageVersion' for '$($upstream.Repository)' cannot be resolved with FromDirectory."
            }
            $resolvedVersion = Resolve-FloatingVersion -Repository $upstream.Repository -Pattern $packageVersion
            Write-Host "Floating $($upstream.VersionProperty)=$packageVersion resolved to $resolvedVersion."
            $packageVersion = $resolvedVersion
        }
        $tag = Resolve-ReleaseTag -Repository $upstream.Repository -PackageVersion $packageVersion -Offline:$offline
        $upstreamPath = Join-Path $staging "$index-$($upstream.Repository -replace '[^A-Za-z0-9._-]', '-')"
        $null = New-Item -ItemType Directory -Path $upstreamPath -Force

        if ($offline) {
            Write-Host "Using local release directory '$InputFromDirectory' for $($upstream.Repository)."
            foreach ($file in @(Get-ChildItem -LiteralPath (Get-FullPath $InputFromDirectory) -File)) {
                if (-not $file.Name.EndsWith('.snupkg', [System.StringComparison]::OrdinalIgnoreCase)) {
                    Copy-Item -LiteralPath $file.FullName -Destination (Join-Path $upstreamPath $file.Name)
                }
            }
        }
        else {
            Write-Host "Downloading $($upstream.Repository) release $tag."
            & gh release download $tag --repo $upstream.Repository --dir $upstreamPath --pattern '*.nupkg' --pattern 'SHA256SUMS' --pattern 'release-manifest.json'
            if ($LASTEXITCODE -ne 0) {
                throw "Could not download release '$tag' from '$($upstream.Repository)'."
            }
        }

        $count = Test-ReleaseDownload -Path $upstreamPath -Repository $upstream.Repository -Tag $tag -PackageVersion $packageVersion
        $results.Add([pscustomobject]@{
            Repository = $upstream.Repository
            Property   = $upstream.VersionProperty
            Version    = $packageVersion
            Tag        = $tag
            Packages   = $count
            Path       = $upstreamPath
        })
    }

    # Every upstream has validated: only now is the local source replaced, in one step.
    $merged = Join-Path $staging '_merged'
    $total = Merge-UpstreamStaging -StagedPath @($results.Path) -MergedPath $merged
    Publish-LocalSource -StagedPath $merged -DestinationPath $destinationPath
}
finally {
    Remove-Item -LiteralPath $staging -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host ''
foreach ($result in $results) {
    Write-Host ("{0,-24} {1,-12} {2,-24} {3} packages" -f $result.Repository, $result.Tag, $result.Property, $result.Packages)
}
Write-Host ''
Write-Host "Local source: $destinationPath ($total packages)"
Write-Host ''
Write-Host 'Run: dotnet restore src/Diginsight.SmartCache.slnx --force-evaluate'
