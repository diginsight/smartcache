# SmartCache release and upstream tooling

SmartCache consumes Diginsight Telemetry packages from the upstream **GitHub Release** before they
finish propagating to NuGet.org and the corporate proxy, and publishes its own packages the same way.

`NuGet.Config` at the repository root stays corporate-proxy-only and is never modified. Local package
priority comes from a generated, git-ignored `src/NuGet.config` overlay.

## Everyday developer flow

```powershell
./eng/Restore-WithUpstream.ps1 -Mode Update
```

This downloads the exact release pinned in [upstream-releases.json](upstream-releases.json), verifies
every asset, writes the overlay, and refreshes the lock files. Afterwards the solution builds normally
in the CLI, Visual Studio, and VS Code — no extra flags — because NuGet discovers the overlay next to
the projects and maps `Diginsight.*` to the verified local feed while everything else keeps coming
from the corporate proxy.

```powershell
./eng/Restore-WithUpstream.ps1 -Mode Locked   # hermetic verification restore, used by CI
./eng/Restore-WithUpstream.ps1 -Mode Clean    # remove overlay, feed, and isolated cache
```

`Locked` restores with `--locked-mode`, `--no-http-cache`, and a tag-specific package cache under
`artifacts/restore-cache/`, so a warm machine cache cannot mask a misconfigured source. Run `Clean`
once the version is publicly available so nobody keeps building against a stale local feed.

Every mode fails closed: if the pinned release is missing, incomplete, tampered with, or from the
wrong repository, restore never starts and there is no silent fallback to the proxy.

## Updating the pinned upstream version

1. Set `sourceTag` and `packageVersion` in [upstream-releases.json](upstream-releases.json).
2. Set `DiginsightCoreVersion` in [../src/Directory.Build.props](../src/Directory.Build.props) to the
   same normalized version. The bootstrap refuses to run if the two disagree.
3. Run `./eng/Restore-WithUpstream.ps1 -Mode Update`.
4. Review and commit the regenerated `packages.lock.json` files.

Tags are four-part (`v3.8.0.1`); NuGet drops only a **zero** fourth component, so `v3.8.0.0` becomes
`3.8.0` while `v3.8.0.1` stays `3.8.0.1`. Always take the value from
`Publish-Packages.ps1 -Command ResolveVersion`.

## Publishing SmartCache

Pushing a matching tag builds version `3.8.0.1` from `v3.8.0.1` once, validates the six packages
listed in [package-manifest.json](package-manifest.json), publishes and re-verifies a GitHub Release,
and only then pushes the same `.nupkg` bytes to NuGet.org. The `publish-nuget` job cannot start until
`build-and-release` proves the remote release matches the staged bytes.

Run the workflow manually to get a dry run: it stops after staging and validation, and never creates
a release or publishes packages.

## Reruns and recovery

A rerun inspects every existing release asset before uploading. Matching assets are kept, missing
assets are uploaded, and any same-name byte mismatch or unexpected asset fails the run. If the
release verifies but NuGet publication fails, rerun the same tag; pushes use `--skip-duplicate`.

Never replace assets or rebuild a published version — package versions are immutable. Ship a new
version instead.

## Local validation without a published release

`-FromDirectory` treats an already staged release directory as the upstream release, so the whole
chain can be exercised before anything is pushed:

```powershell
./eng/Restore-WithUpstream.ps1 -Mode Update -FromDirectory ../telemetry.02/artifacts/release/v3.8.0.1
```

## Tests

```powershell
pwsh ./eng/tests/Publish-Packages.Tests.ps1
pwsh ./eng/tests/Get-UpstreamPackages.Tests.ps1
```
