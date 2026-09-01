#requires -Version 7.0

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'Download-PackageRelease.ps1')

$script:Passed = 0
$script:Failed = 0

function Invoke-Test {
    param(
        [Parameter(Mandatory)]
        [string] $Name,

        [Parameter(Mandatory)]
        [scriptblock] $Body
    )

    try {
        & $Body
        $script:Passed++
        Write-Host "PASS: $Name"
    }
    catch {
        $script:Failed++
        Write-Host "FAIL: $Name - $($_.Exception.Message)" -ForegroundColor Red
    }
}

function Assert-Equal {
    param(
        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [object] $Expected,

        [Parameter(Mandatory)]
        [AllowEmptyString()]
        [object] $Actual
    )

    if ([string] $Expected -cne [string] $Actual) {
        throw "Expected '$Expected', got '$Actual'."
    }
}

function Assert-Throws {
    param(
        [Parameter(Mandatory)]
        [scriptblock] $Body,

        [string] $MessageLike = '*'
    )

    try {
        & $Body
    }
    catch {
        if ($_.Exception.Message -notlike $MessageLike) {
            throw "Expected error like '$MessageLike', got '$($_.Exception.Message)'."
        }
        return
    }
    throw 'Expected an exception, but the operation succeeded.'
}

function New-TestNupkg {
    param(
        [Parameter(Mandatory)]
        [string] $Path,

        [Parameter(Mandatory)]
        [string] $Id,

        [Parameter(Mandatory)]
        [string] $Version
    )

    $null = New-Item -ItemType Directory -Path (Split-Path -Parent $Path) -Force
    $fileStream = [System.IO.File]::Open($Path, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
    try {
        $archive = [System.IO.Compression.ZipArchive]::new($fileStream, [System.IO.Compression.ZipArchiveMode]::Create, $true)
        try {
            $entry = $archive.CreateEntry("$Id.nuspec")
            $entryStream = $entry.Open()
            try {
                $writer = [System.IO.StreamWriter]::new($entryStream, [System.Text.UTF8Encoding]::new($false), 1024, $true)
                try {
                    $writer.Write("<?xml version=`"1.0`"?><package><metadata><id>$Id</id><version>$Version</version><authors>t</authors><description>t</description></metadata></package>")
                }
                finally { $writer.Dispose() }
            }
            finally { $entryStream.Dispose() }
        }
        finally { $archive.Dispose() }
    }
    finally { $fileStream.Dispose() }
}

function New-ReleaseFixture {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [string] $Tag = 'v3.8.0.1',

        [string] $Version = '3.8.0.1',

        [string] $Repository = 'diginsight/telemetry'
    )

    $path = Join-Path $Root 'release'
    $null = New-Item -ItemType Directory -Path $path -Force

    $ids = @('Diginsight.Core', 'Diginsight.Diagnostics')
    $assets = foreach ($id in $ids) {
        $fileName = "$id.$Version.nupkg"
        $packagePath = Join-Path $path $fileName
        New-TestNupkg -Path $packagePath -Id $id -Version $Version
        [ordered]@{
            fileName       = $fileName
            role           = 'package'
            packageId      = $id
            packageVersion = $Version
            sha256         = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash.ToLowerInvariant()
            size           = [long] (Get-Item -LiteralPath $packagePath).Length
        }
    }

    $manifest = [ordered]@{
        schemaVersion  = 1
        repository     = $Repository
        sourceTag      = $Tag
        packageVersion = $Version
        packages       = @($ids | ForEach-Object { [ordered]@{ id = $_; version = $Version; symbolsRequired = $true } })
        assets         = @($assets)
    }

    $utf8 = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText((Join-Path $path 'release-manifest.json'), (($manifest | ConvertTo-Json -Depth 10) + "`n"), $utf8)
    [System.IO.File]::WriteAllText((Join-Path $path 'SHA256SUMS'), ((@($assets | ForEach-Object { "$($_.sha256)  $($_.fileName)" }) -join "`n") + "`n"), $utf8)

    return $path
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "diginsight-download-tests-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $tempRoot -Force
try {
    Invoke-Test 'repository urls are parsed into owner/repo' {
        Assert-Equal 'diginsight/telemetry' (ConvertTo-RepositorySlug -Url 'https://github.com/diginsight/telemetry')
        Assert-Equal 'diginsight/telemetry' (ConvertTo-RepositorySlug -Url 'https://github.com/diginsight/telemetry/')
        Assert-Equal 'diginsight/telemetry' (ConvertTo-RepositorySlug -Url 'https://github.com/diginsight/telemetry.git')
        Assert-Equal 'diginsight/telemetry' (ConvertTo-RepositorySlug -Url 'diginsight/telemetry')
    }

    Invoke-Test 'a non-GitHub url is rejected' {
        Assert-Throws -MessageLike '*is not a GitHub repository URL*' -Body {
            ConvertTo-RepositorySlug -Url 'https://example.com/foo/bar/baz'
        }
    }

    Invoke-Test 'tag candidates account for the dropped zero component' {
        Assert-Equal 'v3.8.0' (Resolve-ReleaseTag -Repository 'x/y' -PackageVersion '3.8.0' -Offline)
        Assert-Equal 'v3.8.0.1' (Resolve-ReleaseTag -Repository 'x/y' -PackageVersion '3.8.0.1' -Offline)
    }

    Invoke-Test 'a valid release verifies' {
        $release = New-ReleaseFixture -Root (Join-Path $tempRoot 'valid')
        $count = Test-ReleaseDownload -Path $release -Repository 'diginsight/telemetry' -Tag 'v3.8.0.1' -PackageVersion '3.8.0.1'
        Assert-Equal 2 $count
    }

    Invoke-Test 'tampered package bytes are rejected' {
        $release = New-ReleaseFixture -Root (Join-Path $tempRoot 'tampered')
        $target = Join-Path $release 'Diginsight.Core.3.8.0.1.nupkg'
        $bytes = [System.IO.File]::ReadAllBytes($target)
        $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
        [System.IO.File]::WriteAllBytes($target, $bytes)
        Assert-Throws -MessageLike '*SHA-256*' -Body {
            Test-ReleaseDownload -Path $release -Repository 'diginsight/telemetry' -Tag 'v3.8.0.1' -PackageVersion '3.8.0.1'
        }
    }

    Invoke-Test 'a release from another repository is rejected' {
        $release = New-ReleaseFixture -Root (Join-Path $tempRoot 'repo') -Repository 'someone/else'
        Assert-Throws -MessageLike '*is not*' -Body {
            Test-ReleaseDownload -Path $release -Repository 'diginsight/telemetry' -Tag 'v3.8.0.1' -PackageVersion '3.8.0.1'
        }
    }

    Invoke-Test 'a version mismatch is rejected' {
        $release = New-ReleaseFixture -Root (Join-Path $tempRoot 'version')
        Assert-Throws -MessageLike '*is not the pinned*' -Body {
            Test-ReleaseDownload -Path $release -Repository 'diginsight/telemetry' -Tag 'v3.8.0.1' -PackageVersion '3.8.0.2'
        }
    }

    Invoke-Test 'a missing declared package is rejected' {
        $release = New-ReleaseFixture -Root (Join-Path $tempRoot 'missing')
        Remove-Item -LiteralPath (Join-Path $release 'Diginsight.Diagnostics.3.8.0.1.nupkg')
        Assert-Throws -MessageLike '*missing declared package*' -Body {
            Test-ReleaseDownload -Path $release -Repository 'diginsight/telemetry' -Tag 'v3.8.0.1' -PackageVersion '3.8.0.1'
        }
    }

    Invoke-Test 'an undeclared package is rejected' {
        $release = New-ReleaseFixture -Root (Join-Path $tempRoot 'undeclared')
        New-TestNupkg -Path (Join-Path $release 'Rogue.Package.3.8.0.1.nupkg') -Id 'Rogue.Package' -Version '3.8.0.1'
        Assert-Throws -MessageLike '*undeclared package*' -Body {
            Test-ReleaseDownload -Path $release -Repository 'diginsight/telemetry' -Tag 'v3.8.0.1' -PackageVersion '3.8.0.1'
        }
    }

    Invoke-Test 'publishing keeps the .gitkeep marker and replaces packages' {
        $release = New-ReleaseFixture -Root (Join-Path $tempRoot 'publish')
        $destination = Join-Path $tempRoot 'publish' 'packages'
        $null = New-Item -ItemType Directory -Path $destination -Force
        '' | Set-Content -Path (Join-Path $destination '.gitkeep')
        New-TestNupkg -Path (Join-Path $destination 'Stale.Package.1.0.0.nupkg') -Id 'Stale.Package' -Version '1.0.0'

        Publish-LocalSource -StagedPath $release -DestinationPath $destination

        if (-not (Test-Path -LiteralPath (Join-Path $destination '.gitkeep'))) { throw 'The .gitkeep marker was removed.' }
        if (Test-Path -LiteralPath (Join-Path $destination 'Stale.Package.1.0.0.nupkg')) { throw 'A stale package survived.' }
        $packages = @(Get-ChildItem -LiteralPath $destination -File -Filter '*.nupkg')
        if ($packages.Count -ne 2) { throw "Expected 2 packages, found $($packages.Count)." }
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Tests passed: $script:Passed; failed: $script:Failed."
if ($script:Failed -ne 0) {
    exit 1
}
