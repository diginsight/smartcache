# Build and publish dependent packages without NuGet propagation delays

**Date Reported:** 2026-08-31  
**Reporter:** Dario Airoldi  
**Status:** 🔶 Open — implementation proposed  
**Severity:** High for release operations; no runtime impact  
**Components:** `diginsight/telemetry`, `diginsight/smartcache`, and other dependent repositories

---

## 📑 Table of Contents

- [📝 Goal](#-goal)
- [🔍 Confirmed Current State](#-confirmed-current-state)
- [🔬 Root Cause](#-root-cause)
- [🎯 Recommended Release Model](#-recommended-release-model)
- [📦 Telemetry Workflow Changes](#-telemetry-workflow-changes)
- [⬇️ Dependent Repository Bootstrap](#️-dependent-repository-bootstrap)
- [🔁 End-to-End Release Procedure](#-end-to-end-release-procedure)
- [🤖 Optional Automation](#-optional-automation)
- [⚖️ Alternatives Considered](#️-alternatives-considered)
- [🛡️ Integrity and Failure Handling](#️-integrity-and-failure-handling)
- [☑️ Implementation Checklist](#️-implementation-checklist)

---

## 📝 Goal

Publishing a new Telemetry version must not block publication of SmartCache or other dependent
components while the new packages propagate through:

1. NuGet.org ingestion and indexing;
2. the corporate `packagefeedproxy.microsoft.io` upstream/cache; and
3. any local NuGet HTTP cache.

The public feeds remain the normal source for component users. Release maintainers need a separate,
immediately available bootstrap path containing the exact package files produced by the Telemetry
release build.

---

## 🔍 Confirmed Current State

The following was verified on 2026-08-31 after Telemetry 3.8.0 was published:

| Check | Result |
|-------|--------|
| NuGet.org gallery | `Diginsight.Core`, `Diginsight.Diagnostics`, and `Diginsight.AspNetCore` 3.8.0 were visible |
| Corporate proxy service index | Reachable (`HTTP 200`) |
| Corporate proxy package indexes | Only 3.8.0-alpha.3 through alpha.5 were visible; stable 3.8.0 was absent |
| SmartCache forced restore | Failed with `NU1102` for packages requiring `>= 3.8.0` |
| Direct NuGet.org access from this machine | Blocked by the corporate network path |
| Telemetry GitHub Releases | Source tag `v3.8.0.0` exists, but no GitHub Releases exist |
| Telemetry publish workflow | Builds packages and pushes only to NuGet.org |

Disabling the local NuGet HTTP cache did not change the result. The failure was therefore caused by
upstream propagation, not by a stale local cache.

The current Telemetry workflow has `contents: read`, so it cannot create a GitHub Release. The
current SmartCache workflow restores only from the configured corporate proxy and has no bootstrap
package source.

---

## 🔬 Root Cause

A NuGet push and package availability are separate events:

```text
Telemetry tag
		→ build `.nupkg` files
		→ push accepted by NuGet.org
		→ NuGet.org validation/indexing
		→ corporate proxy synchronization/cache refresh
		→ SmartCache restore can finally see the version
```

The downstream build currently starts near the beginning of this timeline but depends on its final
step. Retrying restore merely polls an eventually consistent distribution path and cannot provide a
deterministic release process.

A GitHub Actions artifact alone is not a sufficient long-term solution: artifacts expire, are tied
to a workflow run, and are less convenient to consume. A GitHub Release asset is versioned by tag,
stable, directly downloadable, and suitable as the canonical bootstrap copy of the exact `.nupkg`
files sent to NuGet.org.

---

## 🎯 Recommended Release Model

For every Telemetry tag, produce one package set once and distribute that same set to two channels:

```text
														┌→ GitHub Release assets (immediate bootstrap channel)
Telemetry tag → pack once ──┤
														└→ NuGet.org (eventual public consumption channel)
```

The GitHub Release should contain:

- every produced `Diginsight.*.<version>.nupkg` required by downstream builds;
- the matching `.snupkg` files for diagnostics;
- `SHA256SUMS` covering all uploaded package files; and
- generated release notes or a short release description.

The package files must not be rebuilt independently for each channel. Both channels must receive the
same files and therefore the same hashes.

The GitHub Release bootstrap channel removes publishing latency from maintainers' builds. It does
not change the package source used by ordinary consumers.

---

## 📦 Telemetry Workflow Changes

Update the Telemetry tag workflow as follows:

1. Change workflow permission from `contents: read` to `contents: write`.
2. Derive and normalize the package version from the tag once (`v3.8.0.0` → NuGet `3.8.0`).
3. Restore in locked mode and build/package once.
4. Collect all `.nupkg` and `.snupkg` outputs into one staging directory.
5. Assert that:
	 - at least one package was produced;
	 - every package has the tag's exact normalized version;
	 - package IDs are unique; and
	 - the expected core package set is present.
6. Generate `SHA256SUMS`.
7. Create the GitHub Release for the tag and upload the staged files.
8. Push the same staged `.nupkg` files to NuGet.org with `--skip-duplicate`.

Publishing the GitHub Release before the NuGet push gives downstream maintainers an immediate source
even if NuGet.org is slow. The release notes should state clearly if the NuGet push subsequently
fails. A stricter alternative is to create a draft first, push to NuGet.org, and publish the release
last, but a draft cannot serve as the immediate unauthenticated bootstrap channel.

The workflow must fail rather than publish a partial package set. The expected package IDs should be
kept in the workflow or a repository-owned manifest so adding a new packable project requires an
intentional release update.

### Suggested release asset names

Keep the native NuGet filenames so the directory can be used directly as a NuGet source:

```text
Diginsight.Core.3.8.0.nupkg
Diginsight.Diagnostics.3.8.0.nupkg
Diginsight.AspNetCore.3.8.0.nupkg
...
Diginsight.Core.3.8.0.snupkg
...
SHA256SUMS
```

Do not upload only a ZIP archive. NuGet restore expects `.nupkg` files directly in a local folder
source. A ZIP may be uploaded additionally for convenience, but it must not replace the individual
assets.

---

## ⬇️ Dependent Repository Bootstrap

Before updating and restoring SmartCache, download the Telemetry release's `.nupkg` assets into an
ignored local directory, for example:

```text
artifacts/upstream/telemetry/3.8.0/
```

The repository already ignores `artifacts/` and package files, preventing accidental commits.
Download from the exact release tag, never from `latest`, because dependent releases must be
reproducible.

For a public repository, the GitHub CLI can download the assets without introducing a package-feed
credential:

```powershell
gh release download v3.8.0.0 `
	--repo diginsight/telemetry `
	--pattern '*.nupkg' `
	--dir artifacts/upstream/telemetry/3.8.0
```

Then restore with two explicit sources:

```powershell
dotnet restore src/Diginsight.SmartCache.slnx `
	--source artifacts/upstream/telemetry/3.8.0 `
	--source https://packagefeedproxy.microsoft.io/nuget/v3/index.json `
	--force-evaluate
```

The local source supplies the exact new Diginsight packages. The corporate proxy continues to supply
all third-party dependencies. This does not bypass the corporate policy for public feeds: GitHub is
used only to obtain first-party release assets from the organization's own repository.

Do not add a machine-specific absolute folder to committed `NuGet.Config`, and do not add
`api.nuget.org` as a temporary source. A missing committed local source would break ordinary clones,
and a direct public source would violate the intended network policy.

A repository script should eventually own the download, hash verification, expected-package check,
and restore invocation. This avoids different maintainers manually constructing different source
configurations.

### Lock-file handling

SmartCache uses package lock files. Updating `DiginsightCoreVersion` from 3.7.x to 3.8.0 intentionally
changes the dependency graph, so the first restore must use `--force-evaluate`, not `--locked-mode`.
After reviewing and committing all updated lock files, release CI should return to `--locked-mode`.

The dependency property should use exact version `3.8.0`. The existing Telemetry source-tag
convention uses four parts (`v3.8.0.0`), while NuGet normalizes that version and package filenames to
three parts (`3.8.0`). Scripts must compare normalized NuGet versions rather than requiring the raw
tag text to equal the package filename. Wildcards such as `3.8.*` should not be used for a release
dependency because they make a later restore non-reproducible.

### Why every Telemetry package is downloaded

Telemetry packages depend on other packages from the same release. Downloading only
`Diginsight.Core` is insufficient when SmartCache also directly or transitively needs
`Diginsight.Diagnostics`, `Diginsight.AspNetCore`, `Diginsight.Stringify`, or other same-version
packages. The bootstrap directory should contain the complete Telemetry `.nupkg` release set.

---

## 🔁 End-to-End Release Procedure

### Phase A — publish Telemetry

1. Merge and validate Telemetry changes.
2. Create and push the exact source release tag, for example `v3.8.0.0` (NuGet version `3.8.0`).
3. The Telemetry workflow builds the package set once.
4. The workflow validates package IDs and versions.
5. The workflow publishes the package set and hashes as GitHub Release assets.
6. The workflow pushes the same package files to NuGet.org.
7. Verify both publication steps independently; do not wait for proxy propagation before continuing.

### Phase B — update SmartCache or another dependent repository

1. Select the exact Telemetry release tag.
2. Download and verify all `.nupkg` assets into the ignored bootstrap directory.
3. Set the dependency property to the exact normalized version (`3.8.0`).
4. Restore with local bootstrap directory plus corporate proxy and `--force-evaluate`.
5. Build and test.
6. Review and commit the regenerated package lock files.
7. Tag the dependent component.
8. Its release workflow restores in locked mode and publishes its own package artifacts.

### Phase C — consumer availability

Consumers continue restoring exclusively through their normal NuGet sources. If Telemetry or the
dependent package has not propagated yet, consumers wait; this latency no longer blocks maintainers
from building and publishing the dependency chain.

---

## 🤖 Optional Automation

There are two useful automation levels.

### Level 1 — deterministic helper script (recommended first)

Add a cross-repository convention such as:

```text
eng/Get-UpstreamReleasePackages.ps1
```

Inputs should include repository, tag/version, destination, expected package IDs, and checksum
asset. The script should be idempotent and fail on missing assets or hash mismatches. Both local
maintainers and GitHub Actions can call the same script.

The SmartCache tag workflow can derive its pinned Telemetry version and invoke this helper before
locked restore. In CI, `GH_TOKEN: ${{ github.token }}` is sufficient to read release assets from a
public repository.

### Level 2 — downstream release orchestration (optional later)

After a successful Telemetry release, trigger dependency-update workflows in SmartCache and other
repositories. Cross-repository dispatch cannot be performed by the default repository-scoped
`GITHUB_TOKEN`; use a narrowly scoped GitHub App installation token or fine-grained token. The
downstream workflow should open a pull request containing the exact version and lock-file updates,
not publish blindly.

Start with Level 1. It removes the publishing-latency problem without adding cross-repository token
management or automatic release risk.

---

## ⚖️ Alternatives Considered

| Alternative | Decision | Reason |
|-------------|----------|--------|
| Retry the proxy until propagation | Rejected | Slow and nondeterministic; retains the root dependency on publication latency |
| Temporarily add direct NuGet.org | Rejected | Corporate access is blocked and it conflicts with source policy |
| GitHub Actions artifact only | Rejected as canonical channel | Artifacts expire and are workflow-run-oriented |
| GitHub Packages NuGet feed | Possible future option | Native NuGet source, but adds authentication/source-mapping complexity and another feed |
| Build Telemetry from source via project references | Useful for development only | Existing direct-import support helps local coding, but does not validate the exact released `.nupkg` artifacts |
| Copy packages manually between repositories | Rejected | Error-prone, unaudited, and not reproducible |
| GitHub Release `.nupkg` assets | Selected | Immediate, versioned, hashable, durable, and simple to use as a local folder source |

SmartCache already supports direct Telemetry project references through
`DiginsightCoreDirectImport` and `DiginsightCoreSolutionDirectory`. That is useful while developing
both repositories togetwher, but release validation must consume the packaged artifacts to catch
package metadata, dependency, signing, and content problems.

---

## 🛡️ Integrity and Failure Handling

- **Tag/version invariant:** the four-part Git/release tag and the three-part package filename and
	dependency pin must normalize to the same NuGet version.
- **Single build:** upload and push the same staged bytes; never repack between channels.
- **Checksums:** verify `SHA256SUMS` after download and before restore.
- **Completeness:** fail if any expected first-party package is absent.
- **No `latest`:** always address an immutable exact tag.
- **No package overwrite:** NuGet versions are immutable; release assets for a published version
	should also never be replaced. Fixes require a new version.
- **Partial NuGet push:** `--skip-duplicate` makes reruns safe, but package enumeration should be
	deterministic and errors must fail the job.
- **Partial GitHub Release:** stage and validate the complete set before release creation. On upload
	failure, fail the workflow and rerun against the same tag.
- **Source isolation:** use command-line sources or a generated temporary NuGet configuration; do not
	mutate the user's global NuGet sources.
- **Locked release builds:** after dependency-update lock files are committed, downstream tag builds
	must use `--locked-mode` to prove reproducibility.

---

## ☑️ Implementation Checklist

### Telemetry repository

- [x] Grant the tag workflow `contents: write`.
- [x] Stage `.nupkg` and `.snupkg` files after the single build.
- [x] Validate exact versions and the expected package manifest.
- [x] Generate `SHA256SUMS`.
- [x] Implement GitHub Release `v<version>` creation and individual asset upload.
- [x] Implement pushing the same staged `.nupkg` files to NuGet.org after release verification.
- [x] Make duplicate-safe reruns and partial-failure behavior explicit.

> Telemetry workflow implementation is complete locally and its six release-tool tests pass. Live
> publication and remote verification of `v3.8.0.1` remain acceptance tests, not completed items.

### SmartCache repository

- [x] Set `DiginsightCoreVersion` to exact Telemetry version `3.8.0.1`.
- [x] Add an ignored-download helper for an exact Telemetry GitHub Release.
- [x] Verify checksums and expected package IDs.
- [x] Restore dependency updates from local assets plus corporate proxy with `--force-evaluate`.
- [x] Regenerate complete lock-file updates at `3.8.0.1` for review.
- [x] Make tag CI bootstrap the pinned Telemetry release before locked restore when the proxy lacks it.
- [x] Keep committed `NuGet.Config` proxy-based with no direct NuGet.org source.
- [x] Publish SmartCache packages as a verified GitHub Release before pushing to NuGet.org.

> Validated locally against Telemetry `3.8.0.1`: the upstream feed verified, a plain `dotnet build`
> succeeded, and staging produced the six SmartCache packages depending on `Diginsight.Core 3.8.0.1`.

### Other dependent repositories

- [ ] Reuse the same helper and exact-version convention.
- [ ] Keep each dependency update in a reviewed pull request.
- [ ] Publish only after package-based build and tests pass.

---

**Conclusion:** GitHub Release assets should become the immediate maintainer-to-maintainer artifact
channel, while NuGet.org and the corporate proxy remain the eventual consumer distribution channel.
This cleanly limits feed latency to consumer availability and removes it from the Diginsight
component release chain.
I