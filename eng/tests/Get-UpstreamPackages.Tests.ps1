#requires -Version 7.0

[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot '..' 'Get-UpstreamPackages.ps1')

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
                finally {
                    $writer.Dispose()
                }
            }
            finally {
                $entryStream.Dispose()
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    finally {
        $fileStream.Dispose()
    }
}

function New-UpstreamFixture {
    param(
        [Parameter(Mandatory)]
        [string] $Root,

        [string] $Tag = 'v3.8.0.1',

        [string] $Version = '3.8.0.1',

        [string] $Repository = 'diginsight/telemetry'
    )

    $feedPath = Join-Path $Root 'release'
    $null = New-Item -ItemType Directory -Path $feedPath -Force

    $ids = @('Diginsight.Core', 'Diginsight.Diagnostics')
    $assets = foreach ($id in $ids) {
        $fileName = "$id.$Version.nupkg"
        $packagePath = Join-Path $feedPath $fileName
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
    [System.IO.File]::WriteAllText((Join-Path $feedPath 'release-manifest.json'), (($manifest | ConvertTo-Json -Depth 10) + "`n"), $utf8)
    $checksums = @($assets | ForEach-Object { "$($_.sha256)  $($_.fileName)" })
    [System.IO.File]::WriteAllText((Join-Path $feedPath 'SHA256SUMS'), (($checksums -join "`n") + "`n"), $utf8)

    $pinPath = Join-Path $Root 'upstream-releases.json'
    $pin = [ordered]@{
        schemaVersion = 1
        upstreams     = @(
            [ordered]@{
                name                         = 'telemetry'
                repository                   = 'diginsight/telemetry'
                sourceTag                    = $Tag
                packageVersion               = $Version
                releaseManifestSchemaVersion = 1
                versionProperty              = 'DiginsightCoreVersion'
                versionPropertyFile          = 'src/Directory.Build.props'
                packageSourceMappingPattern  = 'Diginsight.*'
                requiredPackages             = @($ids)
            }
        )
    }
    [System.IO.File]::WriteAllText($pinPath, (($pin | ConvertTo-Json -Depth 10) + "`n"), $utf8)

    return [pscustomobject]@{
        FeedPath = $feedPath
        PinPath  = $pinPath
        Root     = $Root
    }
}

$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "diginsight-upstream-tests-$([guid]::NewGuid().ToString('N'))"
$null = New-Item -ItemType Directory -Path $tempRoot -Force
try {
    Invoke-Test 'pinned upstream release verifies' {
        $fixture = New-UpstreamFixture -Root (Join-Path $tempRoot 'valid')
        $pin = Get-UpstreamPin -Path $fixture.PinPath -Name 'telemetry'
        $result = Test-UpstreamFeed -Path $fixture.FeedPath -Pin $pin
        if ($result.PackageCount -ne 2) {
            throw "Expected 2 packages, got $($result.PackageCount)."
        }
    }

    Invoke-Test 'tampered package bytes are rejected' {
        $fixture = New-UpstreamFixture -Root (Join-Path $tempRoot 'tampered')
        $pin = Get-UpstreamPin -Path $fixture.PinPath -Name 'telemetry'
        $target = Join-Path $fixture.FeedPath 'Diginsight.Core.3.8.0.1.nupkg'
        $bytes = [System.IO.File]::ReadAllBytes($target)
        $bytes[$bytes.Length - 1] = $bytes[$bytes.Length - 1] -bxor 0xFF
        [System.IO.File]::WriteAllBytes($target, $bytes)
        Assert-Throws -MessageLike '*failed SHA-256 verification*' -Body {
            Test-UpstreamFeed -Path $fixture.FeedPath -Pin $pin
        }
    }

    Invoke-Test 'missing required package is rejected' {
        $fixture = New-UpstreamFixture -Root (Join-Path $tempRoot 'missing')
        $pin = Get-UpstreamPin -Path $fixture.PinPath -Name 'telemetry'
        Remove-Item -LiteralPath (Join-Path $fixture.FeedPath 'Diginsight.Diagnostics.3.8.0.1.nupkg')
        Assert-Throws -MessageLike '*missing declared package asset*' -Body {
            Test-UpstreamFeed -Path $fixture.FeedPath -Pin $pin
        }
    }

    Invoke-Test 'undeclared package in the feed is rejected' {
        $fixture = New-UpstreamFixture -Root (Join-Path $tempRoot 'undeclared')
        $pin = Get-UpstreamPin -Path $fixture.PinPath -Name 'telemetry'
        New-TestNupkg -Path (Join-Path $fixture.FeedPath 'Rogue.Package.3.8.0.1.nupkg') -Id 'Rogue.Package' -Version '3.8.0.1'
        Assert-Throws -MessageLike '*undeclared package*' -Body {
            Test-UpstreamFeed -Path $fixture.FeedPath -Pin $pin
        }
    }

    Invoke-Test 'release from a different repository is rejected' {
        $fixture = New-UpstreamFixture -Root (Join-Path $tempRoot 'repository') -Repository 'someone/else'
        $pin = Get-UpstreamPin -Path $fixture.PinPath -Name 'telemetry'
        Assert-Throws -MessageLike '*is not the pinned*' -Body {
            Test-UpstreamFeed -Path $fixture.FeedPath -Pin $pin
        }
    }

    Invoke-Test 'tag and pinned version must agree' {
        $fixture = New-UpstreamFixture -Root (Join-Path $tempRoot 'mismatch')
        $pinDocument = Get-Content -LiteralPath $fixture.PinPath -Raw | ConvertFrom-Json -Depth 20
        $pinDocument.upstreams[0].packageVersion = '3.8.0.2'
        [System.IO.File]::WriteAllText($fixture.PinPath, (($pinDocument | ConvertTo-Json -Depth 10) + "`n"), [System.Text.UTF8Encoding]::new($false))
        Assert-Throws -MessageLike '*normalizes to*' -Body {
            Get-UpstreamPin -Path $fixture.PinPath -Name 'telemetry'
        }
    }

    Invoke-Test 'latest is rejected as a pin' {
        $fixture = New-UpstreamFixture -Root (Join-Path $tempRoot 'latest')
        $pinDocument = Get-Content -LiteralPath $fixture.PinPath -Raw | ConvertFrom-Json -Depth 20
        $pinDocument.upstreams[0].sourceTag = 'latest'
        [System.IO.File]::WriteAllText($fixture.PinPath, (($pinDocument | ConvertTo-Json -Depth 10) + "`n"), [System.Text.UTF8Encoding]::new($false))
        Assert-Throws -MessageLike '*exact immutable tag*' -Body {
            Get-UpstreamPin -Path $fixture.PinPath -Name 'telemetry'
        }
    }

    Invoke-Test 'local release directory bootstraps into a verified feed' {
        $fixture = New-UpstreamFixture -Root (Join-Path $tempRoot 'bootstrap')
        $pin = Get-UpstreamPin -Path $fixture.PinPath -Name 'telemetry'
        $destination = Join-Path $fixture.Root 'upstream'
        $feed = Get-UpstreamFeed -Pin $pin -DestinationRoot $destination -FromDirectory $fixture.FeedPath
        $expected = Join-Path $destination 'telemetry' 'v3.8.0.1'
        if ($feed.Path -ne [System.IO.Path]::GetFullPath($expected)) {
            throw "Expected feed at '$expected', got '$($feed.Path)'."
        }
        if (@(Get-ChildItem -LiteralPath $destination -Directory -Recurse | Where-Object Name -like '.tmp-*').Count -ne 0) {
            throw 'Temporary download directory was not removed.'
        }
    }
}
finally {
    Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
}

Write-Host "Tests passed: $script:Passed; failed: $script:Failed."
if ($script:Failed -ne 0) {
    exit 1
}
