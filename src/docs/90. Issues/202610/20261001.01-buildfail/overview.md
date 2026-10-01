# ISSUE: Debug solution build fails after telemetry `StartMethodActivity` overload removal

**Date:** 2026-10-01  
**Author:** Dario Airoldi  
**Status:** Resolved  
**Severity:** High (blocks local debugging of SmartCache together with telemetry source)  
**Component:** Diginsight.SmartCache (core, Http, Redis, ServiceBus externalization), dependency `Diginsight.Diagnostics`  
**Target Framework:** netstandard2.0; netstandard2.1; net8.0; net9.0; net10.0 (SDK `11.0.100-preview.7.26381.103`)  

---

## 📋 Table of Contents

1. [📝 Description](#-description)
2. [🔍 Context Information](#-context-information)
3. [🔬 Analysis](#-analysis)
4. [🔄 Reproduction Steps](#-reproduction-steps)
5. [✅ Solution Implemented](#-solution-implemented)
6. [📚 Additional Information](#-additional-information)
7. [✔️ Resolution Status](#️-resolution-status)
8. [🎓 Lessons Learned](#-lessons-learned)
9. [📎 Appendix](#-appendix)

---

## 📝 DESCRIPTION

Building `src/Diginsight.SmartCache.Debug.slnx` fails with compiler errors on every target framework. The debug solution references the sibling telemetry repository **as source** (`DiginsightCoreDirectImport=true`) so dependency code can be debugged. The build succeeds when the same projects reference the released `Diginsight.Diagnostics` **3.8.0.2 NuGet package** (`DiginsightCoreDirectImport=false`).

The failure is caused by **API drift** between the released package and the current telemetry source: the `StartMethodActivity(Type callerType, ILogger logger, ...)` overload used by SmartCache was removed in the telemetry source.

### Error Message
```
src\Diginsight.SmartCache\SmartCache.cs(79,103): error CS1503: Argument 3: cannot convert from
  'Microsoft.Extensions.Logging.ILogger' to 'System.Diagnostics.ActivityKind'
src\Diginsight.SmartCache\SmartCache.cs(79,114): error CS1660: Cannot convert lambda expression
  to type 'LogLevel?' because it is not a delegate type
```
The same pair of errors repeats for each `StartMethodActivity(TClass, logger, ...)` call site and each target framework.

### Impact
- `Diginsight.SmartCache.Debug.slnx` cannot be built, so SmartCache cannot be debugged together with telemetry source.
- All SmartCache projects that call `StartMethodActivity` are affected: core, Http, Redis and ServiceBus externalization.
- Builds that use packages (`Diginsight.SmartCache.slnx`, CI) are **not** affected while they stay on `Diginsight.Diagnostics` 3.8.0.2. They will fail once a telemetry package is published from current source.

---

## 🔍 CONTEXT INFORMATION

### Environment Details
- **Project:** `src/Diginsight.SmartCache.Debug.slnx` (SmartCache projects + `../../telemetry/src/*` projects)
- **Target Framework:** netstandard2.0, netstandard2.1, net8.0, net9.0, net10.0
- **SDK/Library Version:** .NET SDK `11.0.100-preview.7.26381.103` (from `src/global.json`); `DiginsightCoreVersion` = `3.8.0.2`
- **Database Name:** N/A
- **Operating System:** Windows

### Dependency switching configuration
`src/Directory.build.props.user` (local, imported by `src/Directory.Build.props`):

```xml
<DiginsightCoreSolutionDirectory>E:\dev.darioa\Diginsight\telemetry\src\</DiginsightCoreSolutionDirectory>
<DiginsightCoreDirectImport>true</DiginsightCoreDirectImport>
<DiginsightSmartCacheSolutionDirectory>E:\dev.darioa\Diginsight\smartcache\src\</DiginsightSmartCacheSolutionDirectory>
<DiginsightSmartCacheDirectImport>true</DiginsightSmartCacheDirectImport>
```

Each SmartCache `.csproj` switches on `DiginsightCoreDirectImport`:

| `DiginsightCoreDirectImport` | `Diginsight.Diagnostics` resolved as | Build result |
|------------------------------|--------------------------------------|--------------|
| `true` | `ProjectReference` → current telemetry source | ❌ CS1503 / CS1660 |
| `false` | `PackageReference` → NuGet 3.8.0.2 | ✅ Succeeds |

> `DiginsightSmartCacheDirectImport` is **not** involved: the failing symbols belong to `Diginsight.Diagnostics`.

### Exception Details
| Property | Value |
|----------|-------|
| **Exception Type** | Compile-time errors `CS1503`, `CS1660` |
| **Status Code** | N/A |
| **Activity ID** | N/A |

### Call Stack
```
SmartCache.GetAsync (public + private overloads) / SetValue / OnEvicted / TryGetDirectFromMemory / Invalidate
CachePreloader.PreloadAsync / NotifyAsync
HttpCacheLocation.GetAsync
RedisCacheLocation.GetAsync / TryWriteAsync
ServiceBusCacheCompanion.InstallAsync / ProcessAsync / UninstallAsync, ServiceBusCacheLocation.GetAsync
  └─ ActivitySource.StartMethodActivity(TClass, logger, () => new { ... })   ← overload no longer exists
```

### Overload sets compared
```csharp
// Diginsight.Diagnostics 3.8.0.2 (NuGet) — BOTH orders available
StartMethodActivity(Type callerType, ILogger logger, Func<object>? makeInputs = null, ActivityKind activityKind = ..., LogLevel? logLevel = null, ...)
StartMethodActivity(ILogger logger, Type callerType, Func<object>? makeInputs = null, ActivityKind activityKind = ..., LogLevel? logLevel = null, ...)

// Current telemetry source (ActivitySourceExtensions.Public.g.cs) — logger-first ONLY
StartMethodActivity(ILogger logger, Type callerType, Func<object>? makeInputs = null, ...)
StartMethodActivity(Type callerType, Func<object>? makeInputs = null, ActivityKind activityKind = ..., LogLevel? logLevel = null, ...)
```

---

## 🔬 ANALYSIS

### Root Cause Analysis

#### Primary cause: overload removed during a code-generation refactoring
In telemetry commit `89d4517fe7995da389199c79a8bad5792ef02032` (2026-09-02, Filippo Mineo, message `ActivitySourceExtensions.Public.tt`), about 600 hand-written overloads in `ActivitySourceExtensions.cs` were replaced by a T4 generator (`ActivitySourceExtensions.Public.tt`) and its output (`ActivitySourceExtensions.Public.g.cs`).

The generator always uses one parameter order:

```
[ILogger logger] [string activityName] [Type callerType] inputs/makeInputs activityKind logLevel [callerMemberName]
```

It does not generate the legacy **caller-type-first** variant `(Type callerType, ILogger logger, ...)`. The commit message gives no rationale. The diff suggests the removal was a **side effect of the refactoring** rather than a deliberate API decision *(inferred)*. The change was made after tag `v3.8.0.2`, so no published package contains it yet.

#### Why the build works with binaries and fails with source
The issue is **not** caused by compiling from source versus binaries. The NuGet package is a frozen snapshot of the API at `v3.8.0.2`, which still contains the legacy overload. The local telemetry checkout is newer and no longer contains it. If the telemetry checkout were at tag `v3.8.0.2`, the original SmartCache code would compile with direct import. A DLL built from current telemetry source would fail just like the source build.

#### Error Manifestation
```
1. SmartCache calls StartMethodActivity(TClass, logger, () => new { ... })
2. The (Type, ILogger, Func<object>?) overload does not exist in current source
3. Overload resolution falls back to StartMethodActivity(Type callerType, Func<object>? makeInputs, ActivityKind, LogLevel?, ...)
4. Argument 2 (logger) does not match Func<object>?; the compiler reports the closest candidate:
     - logger → ActivityKind  ⇒ CS1503
     - lambda → LogLevel?     ⇒ CS1660
```

### Impact Assessment

| Category | Impact | Severity |
|----------|--------|----------|
| **Functionality** | Debug solution does not compile; no runtime behavior change | High |
| **Data Integrity** | None | None |
| **User Experience** | Developers cannot step into telemetry code while debugging SmartCache | High |
| **Release pipeline** | Latent: the next telemetry package release would break SmartCache CI | Medium |

### Affected Workflows
1. ❌ **Local debugging with dependency source** (`Diginsight.SmartCache.Debug.slnx`, direct import `true`)
2. ❌ **Future upgrade** to a telemetry package built from post-`89d4517` source (latent)
3. ✅ **Package-based build** (`Diginsight.SmartCache.slnx`, direct import `false`, Diagnostics 3.8.0.2)

---

## 🔄 REPRODUCTION STEPS

### Step-by-Step Reproduction
1. **Enable dependency source import** in `src/Directory.build.props.user`:
   ```xml
   <DiginsightCoreDirectImport>true</DiginsightCoreDirectImport>
   <DiginsightCoreSolutionDirectory>E:\dev.darioa\Diginsight\telemetry\src\</DiginsightCoreSolutionDirectory>
   ```

2. **Check out telemetry** at a commit at or after `89d4517` (e.g. current `HEAD`).

3. **Build**:
   ```powershell
   dotnet restore .\src\Diginsight.SmartCache.Debug.slnx
   dotnet build   .\src\Diginsight.SmartCache.Debug.slnx --no-restore
   ```

4. **Errors occur**: CS1503 + CS1660 for every legacy `StartMethodActivity(TClass, logger, ...)` call, on each target framework.

### Affected Code Location
**File:** `src/Diginsight.SmartCache/SmartCache.cs` (and others, see appendix A)  
**Method:** `GetAsync`  
**Line:** 79
```csharp
// PROBLEMATIC CODE:
using Activity? activity = SmartCacheObservability.ActivitySource.StartMethodActivity(TClass, logger, () => new { key, operationOptions, callerType });
```

---

## ✅ SOLUTION IMPLEMENTED

### Fix Overview
All 15 SmartCache call sites were migrated to the **canonical logger-first positional overload**, which exists in both the released package 3.8.0.2 and the current telemetry source.

The legacy overload is **not** being reintroduced in telemetry as an `[Obsolete]` forwarder. Decision by the user: older consumers of the component have already been retired, so backward compatibility for the caller-type-first order is not needed.

### Code Changes

#### 1. Migrate calls with inputs
**Location:** `src/Diginsight.SmartCache/SmartCache.cs` (line 79, plus lines 186, 436, 592, 681, 714), and the Http, Redis and `CachePreloader` call sites

```csharp
// BEFORE:
StartMethodActivity(TClass, logger, () => new { key, operationOptions, callerType });

// AFTER:
StartMethodActivity(logger, TClass, () => new { key, operationOptions, callerType });
```

#### 2. Migrate calls without inputs
**Location:** `src/Diginsight.SmartCache.Externalization.ServiceBus/ServiceBusCacheCompanion.cs` (lines 301, 484, 710)

```csharp
// BEFORE:
StartMethodActivity(TClass, logger);

// AFTER:
StartMethodActivity(logger, TClass);
```

### Solution Features

#### ✅ Compatible with both dependency modes
- Compiles against telemetry source (direct import `true`) and NuGet 3.8.0.2 (direct import `false`).
- Ready for the next telemetry package built from current source.

#### ✅ No behavior change
- Both orders forward to the same `CoreCreateRichActivity(logger, makeInputs, callerMemberName, callerType, ...)` call. Activity names, inputs and logging are unchanged.

### Rejected alternative: named arguments
A first attempt used named arguments (`logger: logger, callerType: TClass, makeInputs: ...`). This compiles against the current source, but against package 3.8.0.2 it fails with:

```
error CS0121: The call is ambiguous between
  StartMethodActivity(Type, ILogger, Func<object>?, ActivityKind, LogLevel?, string) and
  StartMethodActivity(ILogger, Type, Func<object>?, ActivityKind, LogLevel?, string)
```

The two legacy overloads have the same parameter names and types, so named arguments cannot tell them apart. Positional logger-first calls bind uniquely in both versions.

### Transformation Examples

| Input | Output | Notes |
|-------|--------|-------|
| `StartMethodActivity(TClass, logger, () => new { key })` | `StartMethodActivity(logger, TClass, () => new { key })` | With inputs |
| `StartMethodActivity(TClass, logger)` | `StartMethodActivity(logger, TClass)` | Without inputs |
| `StartMethodActivity(logger: logger, callerType: TClass, ...)` | ❌ CS0121 on 3.8.0.2 | Rejected |

---

## 📚 ADDITIONAL INFORMATION

### Testing Recommendations

#### Unit Tests
No unit tests are needed: this is a compile-time API binding change and runtime behavior is unchanged. The compile-time check is the build in both dependency modes.

#### Integration Tests
1. **Debug solution with dependency source**
   - `dotnet build .\src\Diginsight.SmartCache.Debug.slnx` with `DiginsightCoreDirectImport=true`
   - Expected: 0 errors ✅ (verified)
2. **Package-based compile against Diagnostics 3.8.0.2**
   - A temporary console project referencing `Diginsight.Diagnostics` 3.8.0.2 and calling `StartMethodActivity(NullLogger.Instance, typeof(Program), () => new { ... })`
   - Expected: 0 errors ✅ (verified)
3. **Runtime trace sanity check** (recommended)
   - Run a sample (e.g. from `smartcache.samples`) and check that `GetAsync`/`SetValue` activities still show the correct caller type and inputs.

### Migration Considerations

#### ⚠️ Important: telemetry API is now logger-first only
Any other first-party consumer (samples, other Diginsight repos) that uses `StartMethodActivity(Type, ILogger, ...)` or `StartRichActivity(Type, ILogger, ...)` will break when it moves to a telemetry version after `89d4517`.

#### Migration Options

**Option 1: Migrate consumers to logger-first positional calls (chosen)**
- Swap the first two arguments: `(TClass, logger, …)` → `(logger, TClass, …)`.
- Works with both old and new telemetry.

**Option 2: Reintroduce an `[Obsolete]` forwarding overload in telemetry (not chosen)**
- Would preserve source compatibility for external consumers.
- Not needed: legacy consumers have been retired.

### Performance Impact

| Operation | Before Fix | After Fix | Delta |
|-----------|------------|-----------|-------|
| **Activity creation** | Forwarded to `CoreCreateRichActivity` | Same call | None |
| **Build (debug solution)** | Failed | ~10 s, 0 errors | Fixed |

### Security Considerations

- ✅ **No security impact**: argument order change only.
- ⚠️ **Unrelated pre-existing warning**: telemetry references `OpenTelemetry.Api` 1.9.0 (NU1902, GHSA-g94r-2vxg-569j). Track separately.

---

## REFERENCES

### Related Issues
- **Telemetry commit `89d4517`**: `ActivitySourceExtensions.Public.tt` introduced the generator and dropped the caller-type-first overloads.
- **Telemetry commit `239af87`**: `WIP Xmldoc` (later change to the same files).

### Code References

#### Modified Files
| File | Path | Changes |
|------|------|---------|
| **SmartCache.cs** | `src/Diginsight.SmartCache/SmartCache.cs` | 6 calls migrated to logger-first |
| **CachePreloader.cs** | `src/Diginsight.SmartCache/Externalization/CachePreloader.cs` | 2 calls migrated |
| **HttpCacheLocation.cs** | `src/Diginsight.SmartCache.Externalization.Http/HttpCacheLocation.cs` | 1 call migrated |
| **RedisCacheLocation.cs** | `src/Diginsight.SmartCache.Externalization.Redis/RedisCacheLocation.cs` | 2 calls migrated |
| **ServiceBusCacheCompanion.cs** | `src/Diginsight.SmartCache.Externalization.ServiceBus/ServiceBusCacheCompanion.cs` | 4 calls migrated |

#### Relevant dependency files (telemetry repo)
- `src/Diginsight.Diagnostics/ActivitySourceExtensions.Public.tt` — overload generator
- `src/Diginsight.Diagnostics/ActivitySourceExtensions.Public.g.cs` — generated overloads
- `src/Diginsight.Diagnostics/ActivitySourceExtensions.cs` — `CoreCreateRichActivity`

---

## ✔️ RESOLUTION STATUS

### 🎯 **STATUS: RESOLVED**

**Resolution Date:** 2026-10-01  
**Resolved By:** Dario Airoldi (with Copilot)  
**Resolution Type:** Code Fix (consumer migration to canonical API)

### Verification Checklist

- [x] **Code Changes Implemented**
  - [x] 15 `StartMethodActivity` calls migrated to `(logger, TClass[, makeInputs])`
  - [x] No legacy `(TClass, logger, …)` calls remain in `src/`
- [x] **Testing**
  - [x] `Diginsight.SmartCache.Debug.slnx` builds with direct import: 0 errors
  - [x] Logger-first call compiles against `Diginsight.Diagnostics` 3.8.0.2: 0 errors
  - [ ] Runtime trace sanity check with a sample application
- [ ] **Deployment**
  - [ ] Commit and push SmartCache changes
  - [ ] CI build (`.github/workflows/v3.yml`) green

### Follow-up Actions

#### Immediate (Priority 1)
- [ ] Commit the 5 modified `.cs` files.
- [ ] Review the `packages.lock.json` changes separately: they were produced by direct-import restores and replace package entries with `"type": "Project"` entries. Do not commit them if CI restores with packages and locked mode.

#### Short-term (Priority 2)
- [ ] Search other Diginsight repositories and `smartcache.samples` for `StartMethodActivity(typeof(...)/TClass, logger` and `StartRichActivity(... Type, ILogger` patterns, and migrate them.
- [ ] Bump `DiginsightCoreVersion` once telemetry publishes a package that includes `89d4517`, and confirm the package-mode build.

#### Long-term (Priority 3)
- [ ] Add a CI job (or a local script) that builds SmartCache against telemetry `main` source to catch API drift early.
- [ ] Record public API removals in telemetry release notes/changelog.

### Success Criteria

✅ **Achieved:**
- Debug solution builds with dependency source.
- SmartCache code compiles against both the released and the current telemetry API.

📋 **Pending Verification:**
- CI build after commit.
- Runtime traces unchanged in a sample application.

---

## 🎓 LESSONS LEARNED

### What Went Wrong
1. **Silent public API removal**: converting hand-written overloads to a T4 generator dropped a public overload without notice in the commit message or changelog.
2. **Dependency mode hides drift**: package-mode builds stay green on an old snapshot, so the break only appears in the debug (source) mode.
3. **Duplicate overloads with identical parameter names**: the old API offered two parameter orders with the same names, so named arguments became ambiguous (CS0121). A compile-time fix had to be checked against both API versions.

### What Went Right
1. **Debug solution caught the break early**, before a telemetry package release would have broken CI.
2. **Comparing git history** (`v3.8.0.2` vs `HEAD`) found the exact commit and mechanism quickly.
3. **Checking against both dependency versions** caught the named-argument ambiguity before it was committed.

### Improvements for Future
1. **API diff on generator refactors**: compare the public API surface before and after (e.g. with `Microsoft.DotNet.ApiCompat` / package validation) when replacing hand-written code with generated code.
2. **Single canonical parameter order**: keep `ILogger` first in all telemetry extension overloads and avoid permutation overloads.
3. **Cross-repo source build in CI**: periodically build consumers against dependency source to catch drift before release.

---

## 📎 APPENDIX

### A. Migrated call sites (current line numbers)

| File | Line | Method context |
|------|------|----------------|
| `Diginsight.SmartCache/SmartCache.cs` | 79 | `GetAsync` |
| `Diginsight.SmartCache/SmartCache.cs` | 186 | `GetAsync` (private core overload) |
| `Diginsight.SmartCache/SmartCache.cs` | 436 | `SetValue` |
| `Diginsight.SmartCache/SmartCache.cs` | 592 | `OnEvicted` |
| `Diginsight.SmartCache/SmartCache.cs` | 681 | `TryGetDirectFromMemory` |
| `Diginsight.SmartCache/SmartCache.cs` | 714 | `Invalidate` |
| `Diginsight.SmartCache/Externalization/CachePreloader.cs` | 38 | `PreloadAsync` |
| `Diginsight.SmartCache/Externalization/CachePreloader.cs` | 60 | `NotifyAsync` |
| `Diginsight.SmartCache.Externalization.Http/HttpCacheLocation.cs` | 35 | `GetAsync` |
| `Diginsight.SmartCache.Externalization.Redis/RedisCacheLocation.cs` | 36 | `GetAsync` |
| `Diginsight.SmartCache.Externalization.Redis/RedisCacheLocation.cs` | 94 | `TryWriteAsync` |
| `Diginsight.SmartCache.Externalization.ServiceBus/ServiceBusCacheCompanion.cs` | 301 | `InstallAsync` |
| `Diginsight.SmartCache.Externalization.ServiceBus/ServiceBusCacheCompanion.cs` | 484 | `ProcessAsync` |
| `Diginsight.SmartCache.Externalization.ServiceBus/ServiceBusCacheCompanion.cs` | 710 | `UninstallAsync` |
| `Diginsight.SmartCache.Externalization.ServiceBus/ServiceBusCacheCompanion.cs` | 794 | `ServiceBusCacheLocation.GetAsync` |

### B. Telemetry generator parameter order (from `ActivitySourceExtensions.Public.tt`)

```csharp
if (hasLogger)        parameters.Add("ILogger logger");
if (isStandalone)     parameters.Add("string activityName");
if (callerMode == 1)  parameters.Add("Type callerType");
parameters.Add(hasInputsFunc ? "Func<object>? makeInputs = null" : "object inputs");
parameters.Add("ActivityKind activityKind = ActivityKind.Internal");
parameters.Add("LogLevel? logLevel = null");
if (!isStandalone)    parameters.Add("[CallerMemberName] string callerMemberName = \"\"");
```

The order is fixed: `logger` always comes before `callerType`, so the legacy `(Type, ILogger, …)` order is never generated.

### C. Verification commands

```powershell
# Debug solution (dependency source)
dotnet restore .\src\Diginsight.SmartCache.Debug.slnx
dotnet build   .\src\Diginsight.SmartCache.Debug.slnx --no-restore
#   → 34 Warning(s), 0 Error(s)

# Find any remaining legacy calls
git grep -nE "StartMethodActivity\(\s*(TClass|typeof\()[^,]*,\s*logger" -- src
#   → no matches
```

---

**Document Version:** 1.0  
**Last Updated:** 2026-10-01  
**Next Review:** When `DiginsightCoreVersion` is bumped past telemetry `89d4517`
