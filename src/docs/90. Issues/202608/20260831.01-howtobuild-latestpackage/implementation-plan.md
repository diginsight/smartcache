# CI/CD implementation plan — propagation-independent package releases

**Created:** 2026-08-31  
**Status:** 🟢 Implemented and validated end to end locally — awaiting push of the two workflows  
**Repositories:** `telemetry.02`, `smartcache.02`  
**Rollout version:** Telemetry `v3.8.0.1` → SmartCache consumes `3.8.0.1` → SmartCache `v3.8.0.1`  
**Scope:** Workstreams A–C are implemented; Workstream D remains an optional follow-up

---

## 📑 Table of Contents

- [🎯 Outcome](#-outcome)
- [📌 Required Scope and Non-Goals](#-required-scope-and-non-goals)
- [🔍 Verified Baseline](#-verified-baseline)
- [🏗️ Target Architecture](#-target-architecture)
- [📋 Shared Release Contract](#-shared-release-contract)
- [📦 Workstream A — Telemetry Producer Pipeline](#-workstream-a--telemetry-producer-pipeline)
- [🔗 Workstream B — SmartCache Upstream Bootstrap](#-workstream-b--smartcache-upstream-bootstrap)
- [🚀 Workstream C — SmartCache Producer Pipeline](#-workstream-c--smartcache-producer-pipeline)
- [⬆️ Optional Workstream D — Dependency Update Workflow](#-optional-workstream-d--dependency-update-workflow)
- [🧪 Validation Plan](#-validation-plan)
- [🔐 Security and Permissions](#-security-and-permissions)
- [🔁 Failure and Rerun Semantics](#-failure-and-rerun-semantics)
- [🗂️ File Change Map](#-file-change-map)
- [🪜 Rollout Sequence](#-rollout-sequence)
- [✅ Acceptance Criteria](#-acceptance-criteria)
- [❓ Decisions to Confirm](#-decisions-to-confirm)

---

## 🎯 Outcome

A tagged component release will generate its package files once and publish those exact bytes through
two independent channels, in this mandatory order:

1. **Publish and verify the GitHub Release assets successfully** — immediate build-time channel for
  maintainers of dependent components;
2. **Only then start the NuGet.org push** — eventual public channel for component consumers.

A GitHub Release upload that has not passed remote asset-inventory verification does not satisfy
step 1. The NuGet push must not begin in that state.

SmartCache will restore its pinned Telemetry dependency from the matching GitHub Release assets,
while every unrelated third-party package continues to come from the corporate NuGet proxy. It can
therefore build and publish immediately after Telemetry, without waiting for NuGet.org or proxy
propagation.

SmartCache will also publish its own package files as GitHub Release assets, making the same pattern
available to repositories that depend on SmartCache.

---

## 📌 Required Scope and Non-Goals

### Required for the goal

1. Update the Telemetry tag workflow to publish a complete GitHub Release before pushing to
  NuGet.org.
2. Update the SmartCache tag workflow to publish a complete GitHub Release before pushing to
  NuGet.org.
3. Add a SmartCache developer/CI bootstrap command that downloads an exact Telemetry release into
  `artifacts/upstream/telemetry/<tag>/`.
4. Verify the downloaded package set before restore.
5. Restore `Diginsight.*` from that local folder and all unrelated packages from the corporate
  proxy, without direct NuGet.org access, in command-line **and** IDE builds alike.
6. Guarantee the local source is actually used, even if the machine already has a Diginsight package
  in its global package cache.

### Useful but not required for the first delivery

- automatic dependency-update pull requests;
- cross-repository dispatch from Telemetry to SmartCache;
- migration of existing NuGet authentication;
- a GitHub REST fallback when `gh` is unavailable; and
- extraction of duplicated scripts into shared remote build tooling.

These optional items must not delay or complicate the core producer and local-bootstrap flow.

---

## 🔍 Verified Baseline

### Telemetry

Repository: `C:\dev\darioa\Diginsight\telemetry.02`

- `.github/workflows/v3.yml` runs on release-like `v1*` through `v9*` tags.
- The workflow restores with `--locked-mode`, builds with `GeneratePackageOnBuild`, authenticates by
  `NuGet/login@v1`, and pushes `.nupkg` files only to NuGet.org.
- Workflow permissions are `contents: read` and `id-token: write`.
- `src/Directory.Build.targets` enables package creation, Source Link, `.snupkg` generation, and
  assembly signing.
- `NuGet.Config` contains only the corporate proxy.
- The solution contains 11 packable projects and has one tracked `packages.lock.json` file per
  project, consistent with the workflow's locked restore.
- The established source-tag convention is four-part, for example `v3.8.0.0`; NuGet normalizes the
  resulting package version to `3.8.0`.
- The repository has source tags but currently has no GitHub Releases.

### SmartCache

Repository: `C:\dev\darioa\Diginsight\smartcache.02`

- `.github/workflows/v3.yml` restores through the corporate proxy, builds six packages, and pushes
  only `.nupkg` files to NuGet.org.
- The workflow has no explicit permissions block and does not use `--skip-duplicate`.
- `src/Directory.Build.props` pins Telemetry through `DiginsightCoreVersion` and currently contains
  `3.8.0.0`.
- All six projects have tracked lock files.
- Projects can alternatively reference a local Telemetry source tree through
  `DiginsightCoreDirectImport` and `DiginsightCoreSolutionDirectory`. This remains useful for
  development but is not suitable for release-package validation.
- `NuGet.Config` contains only the corporate proxy.
- `artifacts/` and all `.nupkg`/`.snupkg` files are ignored, so an upstream local feed can be created
  without changing `.gitignore`.

---

## 🏗️ Target Architecture

```text
Telemetry source tag (v3.8.0.1)
  │
  ├─ restore + build/package once
  ├─ validate package manifest, versions, symbols and hashes
  ├─ publish immutable GitHub Release assets
  └─ push the same staged .nupkg bytes to NuGet.org
          │
          │ GitHub Release is immediately available
          ▼
SmartCache developer or release CI
  │
  ├─ read pinned Telemetry tag + normalized NuGet version
  ├─ download exact Telemetry release assets
  ├─ verify checksums, package IDs and package versions
  ├─ write the git-ignored src/NuGet.config overlay:
  │    Diginsight.* → verified local feed
  │    everything else → corporate proxy
  ├─ restore and build/package once (CLI and IDE both honor the overlay)
  ├─ validate SmartCache package set
  ├─ publish immutable SmartCache GitHub Release assets
  └─ push the same staged .nupkg bytes to NuGet.org
```

GitHub Release availability, NuGet.org availability, and corporate proxy availability are separate
states. A successful package build and GitHub Release are sufficient for the next Diginsight
maintainer pipeline. Public consumption still waits for NuGet propagation.

---

## 📋 Shared Release Contract

Implement the same producer contract in both repositories.

### Version contract

- Source tag: the existing four-part form, such as `v3.8.0.0` or `v3.8.0.1`.
- Package version: the NuGet-normalized form of the tag without its leading `v`.
- GitHub Release tag: the exact source tag.
- Package filename: the actual filename emitted by the build, using that normalized version.
- Validation must parse each embedded `.nuspec`; filename-only validation is insufficient.

Normalization drops a **zero** fourth component only. It never truncates a nonzero one:

| Source tag | Normalized package version | Package filename |
|------------|---------------------------|------------------|
| `v3.8.0.0` | `3.8.0` | `Diginsight.Core.3.8.0.nupkg` |
| `v3.8.0.1` | `3.8.0.1` | `Diginsight.Core.3.8.0.1.nupkg` |

Both behaviors are covered by the Telemetry release-tool tests. Any downstream pin, path, or
comparison must use the normalized version produced by `Publish-Packages.ps1 -Command ResolveVersion`,
never a hard-coded three-part assumption. For this rollout SmartCache therefore pins `3.8.0.1`, not
`3.8.0`.

### Asset contract

Each GitHub Release contains individual files, not only a ZIP:

- all expected `.nupkg` files;
- all expected `.snupkg` files;
- `SHA256SUMS`, covering both package and symbol-package files; and
- `release-manifest.json` containing schema version, repository, source tag, normalized package
  version, package IDs, filenames, SHA-256 hashes, and asset roles.

`release-manifest.json` is machine-readable authority; `SHA256SUMS` remains convenient for humans and
standard tooling. The two must agree.

### Build-once contract

- Start from a clean staging directory under `artifacts/release/<tag>/`.
- Build/package once.
- Move or copy package outputs into the staging directory once.
- Generate the manifest and checksums from staged bytes.
- Upload staged bytes to the GitHub Release.
- Push the same staged `.nupkg` files to NuGet.org.
- Never rebuild or repack between the two publication channels.

### Package inventory contract

Keep expected package IDs in a tracked manifest rather than relying only on recursive wildcard
search. This detects a package silently missing because a project became non-packable or changed its
output location.

The versioned inventory manifest is authoritative. Its initial Telemetry revision lists the current
11 solution packages and its initial SmartCache revision lists the current six solution packages;
the scripts must not separately hard-code those counts. Adding or removing a package requires an
intentional manifest change in the same reviewed commit. Unexpected package outputs fail validation.
One `.snupkg` is required for every listed package.

---

## 📦 Workstream A — Telemetry Producer Pipeline

Repository: `C:\dev\darioa\Diginsight\telemetry.02`

### A1. Add release tooling

Create a repository-owned PowerShell script, proposed as `eng/Publish-Packages.ps1`, with commands or
parameter sets for:

- normalizing and validating the source tag;
- cleaning and populating the staging directory;
- reading package identity/version from embedded `.nuspec` files;
- comparing outputs with the tracked package inventory;
- detecting duplicate package IDs or filenames;
- requiring corresponding `.snupkg` files;
- generating `SHA256SUMS` and `release-manifest.json`;
- validating a staged release without publishing; and
- pushing staged `.nupkg` files to a supplied NuGet source.

Keep GitHub Release creation in the workflow through the preinstalled `gh` CLI. This avoids coupling
package validation logic to GitHub APIs and avoids a third-party release action. The workflow passes
`GH_TOKEN: ${{ github.token }}` only to the release step.

### A2. Add the Telemetry package inventory

Create `eng/package-manifest.json` with:

- all 11 expected package IDs;
- whether each symbol package is required;
- the solution path;
- the staging path; and
- manifest schema version.

The validator must fail on missing expected packages. Unexpected `Diginsight.*` package outputs
should also fail by default so package surface changes are intentional and reviewed.

### A3. Refactor the existing tag workflow

Modify `.github/workflows/v3.yml`; do not create a second workflow with the same tag trigger.

Planned step order:

1. checkout;
2. set up the SDKs required by all target frameworks;
3. validate tag shape and derive normalized package version;
4. restore deterministically;
5. build/package once with the tag-derived version;
6. stage and validate all package outputs;
7. generate manifest and checksums;
8. upload the staging directory as a short-lived workflow artifact for diagnostics/recovery;
9. create the GitHub Release and upload individual staged assets;
10. authenticate to NuGet.org;
11. push the same staged `.nupkg` files with `--skip-duplicate`;
12. run a final summary step that reports GitHub Release and NuGet push outcomes separately.

Implement steps 1–9 in a `build-and-release` job. That job owns remote release verification: after
`gh release create`/`gh release upload`, it downloads every remote asset by exact tag into a fresh
verification directory and compares its name, size, and SHA-256 hash with `release-manifest.json`.
It emits a `release-verified=true` job output only after the comparison succeeds.

Implement steps 10–12 in a separate `publish-nuget` job with both:

```yaml
needs: build-and-release
if: needs.build-and-release.outputs.release-verified == 'true'
```

The NuGet job downloads the immutable workflow artifact produced by `build-and-release`, verifies
its hashes again, and pushes those bytes. GitHub Actions job dependencies are the authorization gate;
no NuGet credential is available to `build-and-release`, and no NuGet push command exists in it.

Change workflow permissions to:

```yaml
permissions:
  contents: write
  id-token: write
```

Retain `NuGet/login@v1` initially because the current trusted-publishing setup already uses it and
`id-token: write`. Authentication migration is a separate concern and should not be mixed into this
release-flow change without confirming current NuGet guidance and repository configuration.

### A4. Preserve locked restore

Retain `--locked-mode` in the Telemetry release workflow. Its 11 tracked lock files already provide
one lock file per packable project. The dry run must verify that restore does not modify them, and
the release workflow should fail if the working tree becomes dirty before publication.

### A5. Dry-run mode

The script must support validation without GitHub or NuGet publication. A workflow-dispatch input or
separate pull-request validation workflow can exercise build, staging, manifest, checksum, and
package-content validation while skipping both external publication steps.

---

## 🔗 Workstream B — SmartCache Upstream Bootstrap

Repository: `C:\dev\darioa\Diginsight\smartcache.02`

### B1. Track upstream release metadata

Create `eng/upstream-releases.json` with a Telemetry entry containing:

- GitHub repository: `diginsight/telemetry`;
- exact source/release tag, for example `v3.8.0.0`;
- normalized NuGet version, for example `3.8.0`;
- expected release-manifest schema version; and
- required Telemetry package IDs.

Keep `DiginsightCoreVersion` in `src/Directory.Build.props` because MSBuild consumes it. Normalize it
to the NuGet spelling (`3.8.0`). The bootstrap validator must assert that this property equals the
normalized version in `eng/upstream-releases.json`, preventing drift between build metadata and
release-download metadata.

Do not infer a GitHub tag by simply prepending `v` to the NuGet version. The established tag has a
fourth component, and future tag conventions may differ. Pin the exact tag explicitly.

### B2. Add upstream download and validation script

Create `eng/Get-UpstreamPackages.ps1` with this interface:

```text
-ManifestPath <tracked upstream-releases.json>
-DestinationRoot <ignored artifacts directory>
-GitHubToken <optional; read from GH_TOKEN when omitted>
-Force <optional redownload>
```

Responsibilities:

1. resolve the exact repository and tag from the tracked manifest;
2. download `release-manifest.json`, `SHA256SUMS`, and every `.nupkg` asset into a temporary sibling
   directory;
3. reject `latest`, redirects to a different tag, missing assets, duplicate names, and unexpected
   manifest schema versions;
4. verify every SHA-256 hash before making the feed visible;
5. inspect embedded `.nuspec` identity/version for every package;
6. assert all required Telemetry package IDs are present at the pinned normalized version;
7. atomically replace `artifacts/upstream/telemetry/<tag>/` only after complete validation;
8. write the absolute verified feed path to GitHub Actions output when running in CI; and
9. be idempotent: reuse an already complete, valid directory and redownload an incomplete one.

Both the developer wrapper and SmartCache CI invoke this same script. Its final destination is
exactly `artifacts/upstream/telemetry/<exact-source-tag>/`; temporary downloads use
`artifacts/upstream/telemetry/.tmp-<unique-id>/` and are removed after success or failure. A validated
destination is replaced only by an atomic directory rename and is never merged with another tag.

Use `gh release download` in CI and local PowerShell when `gh` is available. If portability without
`gh` is required, add a GitHub REST fallback later; do not implement two download clients in the
first iteration unless a confirmed environment lacks GitHub CLI.

Download `.snupkg` assets only when needed for archival verification. NuGet restore uses `.nupkg`
files; symbol packages are not package-source inputs.

### B3. Generate the local-priority NuGet configuration

The committed root `NuGet.Config` stays proxy-only. Adding a permanent local source there breaks a
fresh clone: once restore actually queries sources, a not-yet-created folder fails with
`error NU1301: The local source '<path>' doesn't exist`. Verified 2026-08-31.

Instead the bootstrap writes a **generated, git-ignored overlay** at `src/NuGet.config`, next to the
projects. NuGet merges configuration from the project directory upward, and the nearer file wins, so
an overlay containing `<clear />` replaces the root proxy-only source list for everything under
`src/`. Verified 2026-08-31: with a root config declaring the proxy and an overlay declaring a
different mapped source, restore used the overlay.

This matters because it is the only form of local priority that `dotnet build`, `dotnet restore`,
Visual Studio, and VS Code all honor **without** a special command line. A `--configfile` passed only
by a wrapper script would leave an ordinary IDE build still failing during propagation latency, which
is precisely the situation this work must fix.

The overlay contains:

- the verified local Telemetry directory, as an **absolute** path, because relative paths resolve
  against the configuration file's own location;
- the existing corporate proxy URL; and
- package source mapping giving the local source the specific `Diginsight.*` pattern and the proxy
  the lower-precedence `*` default pattern.

The generated mapping is structurally equivalent to:

```xml
<packageSourceMapping>
  <packageSource key="local-upstream">
    <package pattern="Diginsight.*" />
  </packageSource>
  <packageSource key="corporate-proxy">
    <package pattern="*" />
  </packageSource>
</packageSourceMapping>
```

NuGet gives the longer `Diginsight.*` prefix precedence over the generic `*`, so the local folder is
authoritative for those package IDs and the proxy can never supply them. It must never add direct
`api.nuget.org` access.

Because the overlay exists only after a successful bootstrap, a fresh clone is unaffected. The
bootstrap must therefore write the overlay **only** after full download verification, and
`eng/Restore-WithUpstream.ps1 -Mode Clean` must delete it together with the downloaded feed.

### B3.1 Two restore surfaces

The same overlay serves two deliberately different restore modes.

| | Developer surface | Verification surface |
|---|---|---|
| **Used by** | daily local work, IDE builds | CI, lock-file regeneration, release validation |
| **Config** | generated `src/NuGet.config` overlay | overlay plus explicit `--configfile` |
| **Package cache** | default global cache | isolated `artifacts/restore-cache/<tag>/` via `NUGET_PACKAGES` |
| **HTTP cache** | default | `--no-http-cache` |
| **Goal** | fast, IDE-transparent, correct source priority | hermetic and reproducible |

The isolated cache is mandatory on the verification surface and deliberately absent from the
developer surface. NuGet performs **no** source lookup and applies **no** source mapping when a
requested package already exists in the package cache. Verified 2026-08-31: an identical restore that
failed with `NU1301` against an empty cache succeeded in about 8 ms against a warm one. A previously
cached proxy copy could therefore hide a misconfigured source, so every correctness check must run
against a clean, tag-specific cache. Forcing that isolation on the developer surface would instead
re-download every third-party dependency on each restore, so it is scoped to verification only.

The verification wrapper must additionally reject conflicting caller-supplied restore-source
arguments and `<clear />` inherited sources and fallback folders, so user-, machine-, and
environment-level settings cannot alter the intended resolution.

After any bootstrap restore, verify the generated `.nupkg.metadata` files or diagnostic restore
output and fail if any `Diginsight.*` package came from a source other than the verified local
directory.

### B4. Preserve local developer flow

Provide `eng/Restore-WithUpstream.ps1` that:

1. downloads and validates the pinned Telemetry release;
2. writes the `src/NuGet.config` overlay;
3. restores in the requested mode; and
4. reports the exact upstream tag, normalized package version, local feed path, restore mode, and
  package-source provenance.

The headline developer scenario is a single command from the SmartCache repository root:

```powershell
./eng/Restore-WithUpstream.ps1 -Mode Update
```

`Update` downloads the exact tag pinned in `eng/upstream-releases.json`, leaves the verified packages
in `artifacts/upstream/telemetry/<tag>/`, writes the overlay, and refreshes lock files with
`--force-evaluate`.

Afterwards the solution builds normally — `dotnet build`, Visual Studio, and VS Code all pick up the
overlay automatically and resolve `Diginsight.*` from the local folder while the release is still
propagating to NuGet.org and the corporate proxy. No further flags, arguments, or IDE settings are
required. This is the acceptance test for the whole feature.

Two further modes complete the surface:

```powershell
./eng/Restore-WithUpstream.ps1 -Mode Locked   # hermetic verification restore, used by CI
./eng/Restore-WithUpstream.ps1 -Mode Clean    # remove overlay, feed, and isolated cache
```

`Locked` reuses the same verified release and restores with `--locked-mode` on the verification
surface. `Clean` returns the working copy to proxy-only behavior once the version is publicly
available, so nobody keeps building against a stale local feed. Neither `Update` nor `Locked` ever
falls back to the proxy for `Diginsight.*` packages.

The existing project-reference direct-import mechanism remains unchanged for source-level debugging.
The package bootstrap path is mandatory for validating release packaging and lock files.

---

## 🚀 Workstream C — SmartCache Producer Pipeline

Repository: `C:\dev\darioa\Diginsight\smartcache.02`

### C1. Bootstrap before restore

Modify `.github/workflows/v3.yml` so its `build-and-release` job performs:

1. checkout;
2. set up supported SDKs;
3. download and verify the exact pinned Telemetry GitHub Release;
4. write the `src/NuGet.config` overlay;
5. restore SmartCache on the verification surface with `--locked-mode`;
6. assert lock files resolve the exact pinned Telemetry NuGet version.

The bootstrap must be required, not `continue-on-error`. Falling back silently to the proxy restores
the publication-latency dependency that this change is intended to remove. If the pinned Telemetry
GitHub Release is absent or invalid, fail with an actionable error.

### C2. Apply the shared producer contract

After successful upstream restore:

1. build/package SmartCache once;
2. stage the six SmartCache `.nupkg` and `.snupkg` outputs;
3. validate them against SmartCache's tracked `eng/package-manifest.json`;
4. generate `release-manifest.json` and `SHA256SUMS`;
5. upload a workflow artifact for diagnostics;
6. create the SmartCache GitHub Release and upload individual assets;
7. push the same staged `.nupkg` files to NuGet.org with `--skip-duplicate`; and
8. summarize both publication outcomes.

Use the same two-job gate as Telemetry: `build-and-release` must remotely verify the SmartCache
GitHub Release and emit `release-verified=true`; `publish-nuget` must declare
`needs: build-and-release`, require that output, reverify downloaded artifact hashes, and only then
push to NuGet.org. The workflow structure—not a comment or step convention—enforces the required
ordering.

Reuse the same script contract as Telemetry. Initially the script may be copied into each repository
so releases are self-contained and do not depend on a mutable remote script. A later dedicated build
tooling package or pinned reusable workflow can remove duplication after the contract stabilizes.

### C3. Align workflow permissions and SDKs

Add explicit permissions:

```yaml
permissions:
  contents: write
```

SmartCache currently uses a long-lived NuGet API key and does not require `id-token: write`. Keep the
existing secret for the first rollout. Trusted publishing can be adopted in a separate hardening
change.

Reconcile workflow SDK setup with `src/global.json` and actual target frameworks. The current
workflow installs 6.x through 9.x while the solution targets through .NET 10 and `global.json` pins a
10.x SDK. The updated workflow must install the SDK selected by `global.json` and only additional SDKs
that builds genuinely require.

---

## ⬆️ Optional Workstream D — Dependency Update Workflow

This workstream is an optional follow-up. The required first delivery already supports dependency
updates through the local `Restore-WithUpstream.ps1 -Mode Update` command. Automation below may be
added after that manual flow is stable.

### D1. Add a manual dependency-update workflow

Create `.github/workflows/update-telemetry.yml` in SmartCache with `workflow_dispatch` and a required
Telemetry release-tag input.

Planned behavior:

1. checkout a new update branch;
2. download and validate the requested Telemetry GitHub Release;
3. read its normalized package version from `release-manifest.json`;
4. update both `eng/upstream-releases.json` and `DiginsightCoreVersion`;
5. restore with the verified local source plus corporate proxy using `--force-evaluate`;
6. build and test against the resulting dependency graph;
7. assert all six lock files contain the expected direct and transitive Diginsight versions;
8. create a pull request containing only the intended metadata and lock-file changes.

Do not use `--locked-mode` while regenerating lock files. The pull request validation and later tag
workflow use `--locked-mode`.

### D2. Delay cross-repository automatic dispatch

Do not make Telemetry automatically publish SmartCache in the first implementation. Start with the
manual update workflow so package/version changes and lock-file diffs receive review.

After the flow is stable, Telemetry may dispatch a SmartCache dependency-update workflow or a GitHub
App may open the update pull request. Cross-repository dispatch requires a GitHub App installation
token or narrowly scoped fine-grained token; the default repository `GITHUB_TOKEN` is not sufficient
for arbitrary cross-repository writes.

### D3. Prevent unintended updates

The update workflow must:

- require an exact immutable tag, never `latest`;
- reject a release whose manifest repository is not `diginsight/telemetry`;
- reject version regressions unless an explicit override is provided;
- reject prerelease-to-stable or stable-to-prerelease transitions that were not explicitly requested;
- fail if unrelated files change during restore/build; and
- produce a concise dependency and lock-file diff in the pull-request body.

---

## 🧪 Validation Plan

### Script tests

Add Pester tests where practical, using synthetic `.nupkg` ZIPs containing minimal `.nuspec` files.
Cover:

| Scenario | Expected result |
|----------|-----------------|
| Four-part tag normalizes to three-part NuGet version | Pass |
| Every package and symbol file exists | Pass |
| Expected package missing | Fail before publication |
| Unexpected package present | Fail before publication |
| Duplicate package ID | Fail |
| Package `.nuspec` version differs from tag | Fail |
| Filename differs from embedded identity | Fail |
| Checksum mismatch after download | Fail and do not expose local feed |
| Partial previous download | Delete/rebuild temporary download; final feed remains untouched |
| Already valid local feed | Reuse without downloading |
| Manifest schema unsupported | Fail with clear remediation |
| `latest` requested | Fail |

Run tests under PowerShell 7 on both Windows and Ubuntu when feasible. Windows PowerShell 5.1 support
is desirable for the local bootstrap helper but must not force use of APIs unavailable on Linux.

### Telemetry dry run

Before changing a real release:

1. run restore/build/package for a non-publishing test version;
2. validate that exactly 11 `.nupkg` and expected `.snupkg` files are staged;
3. inspect embedded package IDs, versions, dependencies, symbols, signatures, and hashes;
4. validate generated manifest/checksum consistency;
5. upload only a workflow artifact, not a GitHub Release or NuGet package.

Then validate GitHub Release creation with a new prerelease tag/version. Never reuse or replace the
already published `3.8.0` package bytes.

### SmartCache integration test

1. prepare a Telemetry test GitHub Release whose version is absent from the corporate proxy;
2. run the SmartCache bootstrap and locked restore;
3. inspect diagnostic restore logs and prove `Diginsight.*` came from the local folder;
4. prove third-party dependencies came from the corporate proxy;
5. block or simulate failure of the proxy only after its third-party packages are cached if testing
   source mapping independently;
6. corrupt one downloaded asset and verify restore never starts;
7. remove one required asset and verify the workflow fails rather than falling back;
8. run SmartCache package validation without publication;
9. rerun to prove idempotence.

### Workflow syntax and policy checks

- Validate YAML syntax and GitHub Actions expressions.
- Pin every third-party action to an approved major version or immutable commit according to
  repository policy.
- Confirm `gh` is present on `ubuntu-latest` and log its version.
- Confirm both workflows operate under least-privilege explicit permissions.
- Confirm secrets are never passed to scripts that print command arguments or environment dumps.

---

## 🔐 Security and Permissions

| Repository/workflow | Required permission | Reason |
|---------------------|---------------------|--------|
| Telemetry tag release | `contents: write` | Create GitHub Release and upload assets |
| Telemetry tag release | `id-token: write` | Existing NuGet trusted-publishing login |
| SmartCache tag release | `contents: write` | Read upstream public release and create SmartCache release |
| SmartCache dependency-update | `contents: write`, `pull-requests: write` | Create update branch and pull request |

Additional controls:

- keep workflow permissions at job level if a narrower split-job design is adopted;
- run package-building steps without NuGet publishing credentials;
- expose the NuGet credential only to the push step;
- expose `GH_TOKEN` only to release download/create steps;
- do not execute code from downloaded packages;
- validate release metadata, embedded package identity, and hashes before restore;
- use exact release tags and repository names; and
- retain existing signing and Source Link validation.

GitHub-hosted checksums protect download integrity and accidental corruption, but they are not an
independent trust root because the assets and checksums share the same release permissions. Repository
access control, immutable tags/releases, package signing, and protected environments remain the trust
controls.

---

## 🔁 Failure and Rerun Semantics

### Before GitHub Release creation

Any restore, build, inventory, version, symbol, or checksum failure stops publication. No external
release state exists.

### GitHub Release succeeds, NuGet push fails

This is an acceptable recoverable state and the key reason for the dual channel:

- the GitHub Release remains available to dependent maintainer builds;
- the workflow fails visibly;
- rerunning pushes the same staged/re-downloaded release bytes with `--skip-duplicate`;
- do not rebuild and silently replace assets for the same version.

For robust reruns, if the GitHub Release already exists, download its manifest/assets, compare hashes
with the newly staged package set, and continue only when they are byte-identical. Any mismatch fails
and requires a new version.

### Partial GitHub Release upload

The release step should create/upload only after complete local validation. If upload fails midway,
the rerun should:

1. inspect existing assets;
2. reject any same-name asset with a different hash;
3. upload missing byte-identical assets; and
4. verify the final remote asset inventory before NuGet push.

### NuGet partial push

Package versions are immutable. `--skip-duplicate` makes retry safe for already accepted packages.
Every non-duplicate push failure must fail the job. The final job summary lists each package's result.

### Upstream GitHub Release unavailable

SmartCache fails before restore with the expected repository/tag and missing asset details. It must
not silently switch to the corporate proxy.

---

## 🗂️ File Change Map

### Telemetry repository

| Path | Action | Purpose |
|------|--------|---------|
| `.github/workflows/v3.yml` | Modified ✅ | Build once, validate, publish and verify GitHub Release, then push identical packages to NuGet |
| `eng/package-manifest.json` | Added ✅ | Expected 11-package inventory and symbol requirements |
| `eng/Publish-Packages.ps1` | Added ✅ | Stage, inspect, hash, manifest, validate, compare, and push packages |
| `eng/tests/Publish-Packages.Tests.ps1` | Added ✅ | Unit tests for release validation and failure cases |
| `eng/README.md` | Added ✅ | Dry-run, rerun, and recovery runbook |
| `src/**/packages.lock.json` | Keep; update only through reviewed dependency changes | Preserve deterministic release restore |

### SmartCache repository

| Path | Action | Purpose |
|------|--------|---------|
| `.github/workflows/v3.yml` | Modify | Required upstream bootstrap plus dual-channel SmartCache release |
| `.github/workflows/update-telemetry.yml` | Add | Reviewed, manually triggered dependency and lock-file update |
| `eng/upstream-releases.json` | Add | Pin Telemetry repository, exact tag, normalized version, and IDs |
| `eng/package-manifest.json` | Add | Expected six-package SmartCache inventory |
| `eng/Get-UpstreamPackages.ps1` | Added ✅ | Download and verify immutable upstream release assets |
| `eng/Restore-WithUpstream.ps1` | Added ✅ | Write the overlay and run the Update/Locked/Clean restore modes |
| `eng/Publish-Packages.ps1` | Added ✅ | Same self-contained producer validation used by Telemetry |
| `eng/upstream-releases.json` | Added ✅ | Pin Telemetry repository, exact tag, normalized version, and IDs |
| `eng/package-manifest.json` | Added ✅ | Expected six-package SmartCache inventory |
| `eng/tests/*.Tests.ps1` | Added ✅ | Bootstrap and producer validation tests (14 tests) |
| `eng/README.md` | Added ✅ | Developer, release, rerun, and recovery runbook |
| `src/NuGet.config` | Generated; never committed ✅ | Overlay giving `Diginsight.*` local priority in CLI and IDE builds |
| `src/Directory.Build.props` | Modified ✅ | `DiginsightCoreVersion` pinned to `3.8.0.1` |
| `src/**/packages.lock.json` | Regenerated ✅ | Pin the validated Telemetry package graph at `3.8.0.1` |
| `NuGet.Config` | Unchanged ✅ | Ordinary restores remain corporate-proxy-only |
| `.gitignore` | Modified ✅ | Ignores the generated `src/NuGet.config` overlay |

---

## 🪜 Rollout Sequence

### Phase 1 — shared tooling contract ✅

1. Agree on manifest schemas and four-part-tag/normalized-NuGet-version rules.
2. Implement package staging/inspection/checksum scripts in Telemetry.
3. Add tests and dry-run validation.
4. Verify locked restore leaves all 11 tracked lock files unchanged.

**Exit gate met:** locked restore reported no changes, the build produced all 11 packages as
`3.8.0.1`, and staging validated 22 assets plus manifest and checksums.

### Phase 2 — Telemetry dual publication — workflow ready, live run pending

1. Refactor the existing Telemetry workflow.
2. Test with a new prerelease version/tag.
3. Verify GitHub Release assets and hashes.
4. Verify the exact same `.nupkg` hashes were pushed to NuGet.org.
5. Exercise a safe rerun.

**Exit gate:** a new Telemetry prerelease is independently consumable from its GitHub Release before
it appears through the corporate proxy. Steps 2–5 require pushing `v3.8.0.1`.

### Phase 3 — SmartCache bootstrap ✅

1. Add pinned upstream metadata and download verification.
2. Add the overlay generator and wrapper restore.
3. Pin the Telemetry package version property to `3.8.0.1`.
4. Regenerate and review SmartCache lock files from the verified release source.
5. Prove locked restore succeeds while the pinned Telemetry version is unavailable from the proxy.

**Exit gate met:** `Update` and `Locked` both restored successfully, provenance confirmed five
upstream packages came from the verified feed, and a plain `dotnet build` succeeded against a version
that exists on neither NuGet.org nor the proxy.

### Phase 4 — SmartCache dual publication — workflow ready, live run pending

1. Add SmartCache producer inventory/tooling.
2. Refactor the SmartCache tag workflow.
3. Test with a new prerelease tag.
4. Verify SmartCache GitHub Release and NuGet bytes match.
5. Exercise partial-failure and rerun behavior.

**Exit gate:** SmartCache itself provides the same immediate downstream artifact channel.

### Phase 5 — optional dependency update automation

1. Add manual `update-telemetry.yml` workflow.
2. Validate PR creation and lock-file review.
3. Document maintainer usage.
4. Consider GitHub App-based cross-repository dispatch only after several successful manual cycles.

**Exit gate:** a maintainer can submit a reviewed SmartCache dependency-update PR by supplying one
exact Telemetry release tag.

---

## ✅ Acceptance Criteria

Verified locally on 2026-08-31 by building Telemetry `3.8.0.1`, staging it as a release, and
bootstrapping SmartCache from it. `3.8.0.1` exists on neither NuGet.org nor the corporate proxy.

- [x] Telemetry creates exactly one validated package set per source tag.
- [x] Every Telemetry GitHub Release contains all expected individual `.nupkg` and `.snupkg` assets,
      `SHA256SUMS`, and `release-manifest.json`.
- [x] The Telemetry NuGet.org push cannot start until the GitHub Release exists and its complete
  remote asset inventory has been verified.
- [x] Telemetry pushes the exact GitHub Release `.nupkg` bytes to NuGet.org.
- [x] A NuGet push delay or corporate proxy delay does not block SmartCache restore/build/package.
- [x] SmartCache pins an exact Telemetry source tag and normalized NuGet version.
- [x] SmartCache verifies repository, tag, manifest, package IDs, embedded versions, and hashes before
      restore.
- [x] `Diginsight.*` resolves only from the verified local feed during bootstrap builds.
- [x] Third-party dependencies resolve only through the corporate proxy.
- [x] After one `Restore-WithUpstream.ps1 -Mode Update`, an ordinary `dotnet build` and an ordinary
  Visual Studio / VS Code build both succeed against the pinned version while it is still absent from
  NuGet.org and the corporate proxy, with no extra flags or IDE settings.
- [x] A clean, tag-specific global-packages folder prevents an existing machine cache from bypassing
  package source mapping on the verification surface.
- [x] `-Mode Clean` removes the overlay, feed, and isolated cache and returns the working copy to
  proxy-only behavior.
- [x] A fresh clone that has never run the bootstrap is unaffected by the overlay mechanism.
- [x] Restore provenance proves which source supplied every `Diginsight.*` package.
- [x] SmartCache release CI uses `--locked-mode`; dependency updates use `--force-evaluate`.
- [x] Missing or corrupt upstream assets fail closed; there is no silent proxy fallback.
- [x] SmartCache publishes its own identical GitHub Release and NuGet package bytes.
- [x] Both producer workflows are safely rerunnable after partial GitHub or NuGet publication.
- [x] No workflow publishes a package when inventory/version/checksum validation fails.
- [x] No direct NuGet.org source is added to SmartCache restore configuration.
- [x] No generated package or bootstrap directory is committed.
- [x] Maintainer documentation covers normal release, manual local dependency update, retry, and
  recovery paths.
- [ ] If optional Workstream D is implemented, its automated dependency-update procedure is also
  documented.

Pending because they require pushing the workflows:

- [ ] A real Telemetry `v3.8.0.1` GitHub Release is produced by CI and verified remotely.
- [ ] SmartCache CI bootstraps that real release and publishes `v3.8.0.1`.

---

## ❓ Decisions to Confirm

### Core implementation decisions

1. **Existing 3.8 tag — resolved:** do not retrofit or replace `v3.8.0.0`; begin the new process with
  Telemetry `v3.8.0.1`.
2. **First end-to-end version — resolved:** publish Telemetry `v3.8.0.1`, consume that exact version
  from SmartCache, and publish SmartCache `v3.8.0.1`.

The following implementation choices are fixed by this plan and are no longer open decisions:

- fail on every package output not declared by the versioned inventory manifest;
- require one `.snupkg` per declared package; and
- require PowerShell 7 (`pwsh`) for CI and local release/bootstrap scripts.

### Optional automation decisions

3. **Dependency-update PR creation:** use GitHub CLI only, or an approved pinned action?
4. **Release immutability:** enable GitHub immutable Releases if available under organization policy,
   in addition to workflow hash checks?

Workstream A is implemented locally and its release-tool tests pass. Its live `v3.8.0.1` run remains
pending. Workstreams B and C are not implemented. Decisions 3–4 do not block the required goal.
