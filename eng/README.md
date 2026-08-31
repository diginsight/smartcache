# SmartCache release and upstream tooling

SmartCache consumes Diginsight Telemetry packages from the upstream **GitHub Release** before they
finish propagating to NuGet.org and the corporate proxy, and publishes its own packages the same way.

## Everyday developer flow

```powershell
./eng/Download-PackageRelease.ps1 https://github.com/diginsight/telemetry
dotnet restore src/Diginsight.SmartCache.slnx --force-evaluate
```

The first command resolves the GitHub Release matching the pinned `DiginsightCoreVersion`, verifies
every asset against the release manifest and `SHA256SUMS`, and publishes the `.nupkg` files into
`artifacts/packages`.

That folder is declared as a package source in [../NuGet.Config](../NuGet.Config), so an ordinary
`dotnet restore` — and an ordinary Visual Studio or VS Code build — picks the packages up with no
extra arguments. Everything not in the folder still comes from the corporate proxy.

## Bumping the upstream version

```powershell
./eng/Download-PackageRelease.ps1 https://github.com/diginsight/telemetry -Version 3.8.0.2
```

`-Version` repins `DiginsightCoreVersion` in [../src/Directory.Build.props](../src/Directory.Build.props)
and downloads that release. Review the property change and the regenerated `packages.lock.json` files
before committing.

Tags are four-part (`v3.8.0.1`), while NuGet drops a **zero** fourth component — so `v3.8.0.0`
publishes as `3.8.0`. The script tries both spellings and, if neither exists, lists the available
releases.

## Why artifacts/packages/.gitkeep is committed

NuGet fails **every** restore with `NU1301` if a configured local source folder does not exist, even
when the packages are available from the proxy. The error cannot be suppressed with `NoWarn`,
`RestoreNoWarn`, or `WarningsNotAsErrors`. An *empty* folder is fine.

So the folder is committed and must stay: delete the marker and the repository stops restoring for
everyone, including people who never use this tooling. Downloaded packages themselves are ignored.

## Publishing SmartCache

Pushing a matching tag builds version `3.8.0.1` from `v3.8.0.1` once, validates the six packages
listed in [package-manifest.json](package-manifest.json), publishes and re-verifies a GitHub Release,
and only then pushes the same `.nupkg` bytes to NuGet.org. The `publish-nuget` job cannot start until
`build-and-release` proves the remote release matches the staged bytes.

Run the workflow manually for a dry run: it stops after staging and validation, and never creates a
release or publishes packages.

## Reruns and recovery

A rerun inspects every existing release asset before uploading. Matching assets are kept, missing
assets are uploaded, and any same-name byte mismatch or unexpected asset fails the run. If the
release verifies but NuGet publication fails, rerun the same tag; pushes use `--skip-duplicate`.

Never replace assets or rebuild a published version — package versions are immutable. Ship a new
version instead.

## If a build fails with NU1403

`NU1403: Package content hash validation failed` means the global package cache holds different bytes
for a version than the lock file expects. It happens when the same version was previously restored
from somewhere else — typically a locally built copy of an upstream package.

Purge just that version and restore again:

```powershell
Remove-Item "$env:USERPROFILE\.nuget\packages\diginsight.*\<version>" -Recurse -Force
dotnet restore src/Diginsight.SmartCache.slnx --force-evaluate
```


## Local validation without a published release

`-FromDirectory` treats an already staged release directory as the release, so the whole chain can be
exercised before anything is pushed:

```powershell
./eng/Download-PackageRelease.ps1 https://github.com/diginsight/telemetry -FromDirectory ../telemetry.02/artifacts/release/v3.8.0.1
```

## Tests

```powershell
pwsh ./eng/tests/Publish-Packages.Tests.ps1
pwsh ./eng/tests/Download-PackageRelease.Tests.ps1
```
