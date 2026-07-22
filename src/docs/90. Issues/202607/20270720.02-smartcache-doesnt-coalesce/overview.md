# SmartCache — racing cache misses are not coalesced

**Date Reported:** 2026-07-22
**Reporter:** Dario Airoldi
**Status:** 🔶 Open — design proposed
**Severity:** Medium (efficiency / origin-load amplification; no correctness impact)
**Component:** `Diginsight.SmartCache` core (`SmartCache.GetAsync`)
**Framework:** .NET 10 · Diginsight.SmartCache 3.7.x

---

## 📑 Table of Contents

- [📝 Description](#-description)
- [🔍 Context Information](#-context-information)
- [🔬 Analysis](#-analysis)
- [💡 Proposed Feature: `CoalesceRacingCacheMisses`](#-proposed-feature-coalesceracingcachemisses)
- [🧩 Design — In-Memory Coalescing (v1)](#-design--in-memory-coalescing-v1)
- [🌐 Design — Cross-Node Coalescing (v2, future)](#-design--cross-node-coalescing-v2-future)
- [⚖️ Edge Cases & Semantics](#️-edge-cases--semantics)
- [📊 Observability](#-observability)
- [🧪 Testing Strategy](#-testing-strategy)
- [🚦 Risks & Trade-offs](#-risks--trade-offs)
- [📎 Appendix](#-appendix)

---

## 📝 Description

`SmartCache.GetAsync<T>` returns a cached value or, on a miss, invokes the caller's `fetchAsync`
delegate and stores the result. When **two or more calls for the same key miss concurrently**
(a *racing miss* / cache stampede), **each call independently runs `fetchAsync`** and therefore each
hits the origin. SmartCache has no *single-flight* (in-flight de-duplication) mechanism.

This is not a correctness bug — every caller receives a valid value — but it amplifies origin load
exactly when the origin is most vulnerable: the **cold window** (startup, post-invalidation, or just
after a key's freshness window elapses), when many requests for the same hot key arrive together.

**Goal of this issue:** introduce an opt-in
`SmartCacheOperationOptions.CoalesceRacingCacheMisses` flag that makes concurrent racing misses for
the same key share a **single** origin fetch — starting with an **in-memory (single-node)**
implementation, and later extending to **cross-node** coalescing over the existing companion.

---

## 🔍 Context Information

| Property | Value |
|----------|-------|
| **Primary file** | `src/Diginsight.SmartCache/SmartCache.cs` |
| **Public entry point** | `SmartCache.GetAsync<T>(object key, Func<CancellationToken, Task<T>> fetchAsync, SmartCacheOperationOptions?, Type? callerType, CancellationToken)` |
| **Options** | `src/Diginsight.SmartCache/SmartCacheOperationOptions.cs` |
| **Core options** | `ISmartCacheCoreOptions` / `SmartCacheCoreOptions` / `DynamicSmartCacheCoreOptions` |
| **Companion (cross-node)** | `Diginsight.SmartCache.Externalization.*` (Service Bus, Redis) |
| **Consumer that motivated this** | `Learn.Web` `CachedContentSource` (worked around it with a local `ConcurrentDictionary` single-flight) |

---

## 🔬 Analysis

### Current miss path (verbatim behaviour)

`SmartCache.GetAsync<T>` (public) computes freshness and delegates to the private
`GetAsync<T>(...)` overload. Inside it, the miss is resolved by a **local function**:

```csharp
async Task<T> FetchAndSetValueAsync(Activity? activity)
{
    SmartCacheObservability.Instruments.Sources.Add(1, SmartCacheObservability.Tags.Type.Miss);
    activity?.SetTag("cache.hit", 0);

    T value;
    using (SmartCacheObservability.Instruments.FetchDuration.StartLap(latencyMsecBox, ...Tags.Type.Miss))
    {
        value = await fetchAsync(cancellationToken);   // ← origin call, no single-flight guard
    }
    ...
    SetValue(keyHolder, value, timestamp, dynamicCoreOptions, absExpiration, sldExpiration);
    return value;
}
```

`FetchAndSetValueAsync` is invoked at **two** sites in the private `GetAsync<T>`:

1. **External-locations fallback** — after `TaskUtils.WhenAnyValid` fails to serve the key from a peer
   or passive (Redis) location.
2. **Final fallback** — when the local memory entry is missing or older than `minimumCreationDate`.

Both paths mean *"no acceptable cached value exists; go to the origin."* Neither consults an
in-flight registry. There is **no** `ConcurrentDictionary<key, Task>`, `Lazy<Task>`, or per-key
`SemaphoreSlim` guarding the fetch anywhere in `Diginsight.SmartCache`.

### Consequence — same node

Two overlapping calls for key `K`:

| | Call A | Call B |
|---|---|---|
| t0 | memory miss | memory miss |
| t1 | `fetchAsync` → **origin** | `fetchAsync` → **origin** |
| t2 | `SetValue` (store + notify) | `SetValue` (store + notify, overwrites) |

→ **2 origin fetches** for 1 logical key. With N racing callers, N fetches.

### Consequence — across nodes

SmartCache's cross-node coordination is **post-write and asynchronous**, not a pre-fetch lock:

- On a completed fetch, `SetValue` → `NotifyMiss` broadcasts a `CacheMissDescriptor` (via
  `companion.GetAllEventNotifiersAsync()`), which populates peers' `externalMissDictionary`
  ("node X has key K @ T"), optionally carrying a small value.
- A **later** miss on another node consults `externalMissDictionary` and may fetch from the peer /
  Redis (`TaskUtils.WhenAnyValid` races **locations** by latency) instead of the origin.

Because the "I have K" broadcast is emitted **after** the fetch completes, a genuinely simultaneous
multi-node miss has no signal to suppress peer fetches → each node fetches once. The distributed
layer de-duplicates **staggered** requests and propagates **invalidation**; it does not provide
**stampede protection** for a simultaneous race.

> ⚠️ `TaskUtils.WhenAnyValid` races cache **locations** for a *single* caller — it is not caller
> de-duplication.

### Why the fetch is the right choke point

Wrapping `FetchAndSetValueAsync` with a single-flight guard covers **both** call sites and leaves the
memory-hit and peer/Redis-hit fast paths untouched (those already avoid the origin). The leader runs
`fetchAsync` + `SetValue` (store + broadcast) exactly once; joiners receive the leader's value
without re-storing or re-notifying.

---

## 💡 Proposed Feature: `CoalesceRacingCacheMisses`

Add an **opt-in** switch that enables single-flight for a call.

### Surface — two independent opt-in flags

Cross-node coalescing costs more than in-memory (a broadcast per leader fetch + a bounded wait on
peers), and not every deployment wants it. So it is a **separate** flag layered on top of the
in-memory one — you can have in-memory coalescing alone, or add cross-node suppression on top.

**Flag 1 — in-memory single-flight (v1).** Collapses concurrent racing misses **on one node**.

```csharp
// SmartCacheOperationOptions.cs — per-call override (null = inherit)
public bool? CoalesceRacingCacheMisses { get; set; }
```

**Flag 2 — cross-node best-effort suppression (v2).** Adds companion-based suppression **across
nodes** (Strategy A). It **implies** Flag 1 — enabling it turns on in-memory single-flight
automatically (you cannot suppress peers without also coalescing local callers) — and only takes
cross-node effect when an event companion is configured. Because of its extra cost it is opt-in
separately:

```csharp
// SmartCacheOperationOptions.cs — per-call override (null = inherit)
public bool? CoalesceRacingCacheMissesAcrossNodes { get; set; }
```

> The request asked for `public bool CoalesceRacingCacheMisses { get; set; }`. A **nullable** `bool?`
> is recommended for both so a per-call value can *inherit* from configuration when unset; the
> non-nullable form is a valid simpler alternative if inheritance is not desired (see plan
> §"Option A vs B").

Both flags have matching global + class-aware defaults:

```csharp
// ISmartCacheCoreOptions.cs — global defaults (both default false; opt-in, preserve today's behaviour)
bool CoalesceRacingCacheMisses { get; }
bool CoalesceRacingCacheMissesAcrossNodes { get; }

// IDynamicSmartCacheCoreOptions.cs — class-aware defaults (per callerType)
bool? CoalesceRacingCacheMisses { get; }
bool? CoalesceRacingCacheMissesAcrossNodes { get; }
```

### Resolution order (mirrors `MaxAge`; cross-node implies in-memory)

```text
coalesceCrossNode = operationOptions.CoalesceRacingCacheMissesAcrossNodes
                 ?? dynamicCoreOptions.CoalesceRacingCacheMissesAcrossNodes
                 ?? coreOptions.CoalesceRacingCacheMissesAcrossNodes   // default false

coalesce          = ( operationOptions.CoalesceRacingCacheMisses
                   ?? dynamicCoreOptions.CoalesceRacingCacheMisses
                   ?? coreOptions.CoalesceRacingCacheMisses )          // default false
                 || coalesceCrossNode                                 // cross-node ⇒ in-memory
```

**Cross-node implies in-memory:** enabling `CoalesceRacingCacheMissesAcrossNodes` turns on
`CoalesceRacingCacheMisses` automatically — you cannot suppress peers without also coalescing local
callers. `coalesceCrossNode` then *additionally* requires a companion to be present; with no companion
it degrades silently to in-memory single-flight (Flag 1) — never an error, and **no Redis required**.
Enabling only Flag 1 gives in-memory coalescing with no cross-node cost. Example:
`Diginsight:SmartCache:CoalesceRacingCacheMissesAcrossNodes@CachedContentSource: true` (which implies
in-memory too).

---

## 🧩 Design — In-Memory Coalescing (v1)

### Data structure

A single-flight registry on the `SmartCache` instance, keyed by the **cache key object** (same
equality used by `memoryCache` and the `keys` dictionary — e.g. `MethodCallCacheKey` value equality):

```csharp
private readonly ConcurrentDictionary<object, TaskCompletionSource<object?>> inFlightFetches = new();
```

`TaskCompletionSource<object?>` (value boxed to `object?`, exactly as `ValueEntry` already stores it)
lets one type-erased registry serve all `T`.

### Leader / joiner protocol

Replace the direct `fetchAsync` call inside `FetchAndSetValueAsync` with:

```csharp
async Task<T> FetchAndSetValueAsync(Activity? activity)
{
    if (!coalesce)
    {
        return await CoreFetchAndSetAsync(activity, cancellationToken);   // existing behaviour
    }

    var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<object?> current = inFlightFetches.GetOrAdd(keyHolder.Payload, tcs);

    if (!ReferenceEquals(current, tcs))
    {
        // JOINER — await the leader's result under our own token.
        SmartCacheObservability.Instruments.Sources.Add(1, SmartCacheObservability.Tags.Type.Coalesced);
        activity?.SetTag("cache.coalesced", 1);
        object? boxed = await current.Task.WaitAsync(cancellationToken);
        return (T)boxed!;
    }

    // LEADER — run the real fetch+store once; publish to joiners.
    try
    {
        T value = await CoreFetchAndSetAsync(activity, CancellationToken.None);   // shared fetch
        tcs.SetResult(value);
        return value;
    }
    catch (Exception ex)
    {
        tcs.SetException(ex);
        throw;
    }
    finally
    {
        inFlightFetches.TryRemove(new KeyValuePair<object, TaskCompletionSource<object?>>(keyHolder.Payload, tcs));
    }
}
```

Where `CoreFetchAndSetAsync` is the extracted original body (`fetchAsync` + `SetValue` + metrics).

### Key properties

| Property | Design choice |
|----------|---------------|
| **One origin fetch** | Reference-equality on `GetOrAdd` elects a single leader per key |
| **Type erasure** | `object?` in the TCS; joiners cast `(T)` (value types box, as they already do in `ValueEntry`) |
| **Store/notify once** | Only the leader calls `SetValue` (→ `NotifyMiss` / Redis write); joiners never re-store |
| **Cancellation isolation** | Leader fetch runs under `CancellationToken.None`; each caller awaits `current.Task.WaitAsync(ct)`, so one caller cancelling never aborts the shared fetch |
| **Failure propagation** | Leader `SetException`; every joiner observes the same exception; entry removed in `finally` (no poisoned key) |
| **Registry hygiene** | Leader removes its TCS in `finally`; the window is bounded by the fetch duration |
| **Opt-in** | Guarded by the resolved `coalesce` flag; default off → zero behaviour change for existing users |

### Placement note

`coalesce` must be computed in the **public** `GetAsync<T>` (where `operationOptions` and
`dynamicCoreOptions` are available) and threaded into the private overload (new parameter), alongside
the existing `timestamp` / `minimumCreationDate` / expirations.

---

## 🌐 Design — Cross-Node Coalescing (v2, future)

In-memory v1 collapses **same-node** races. Cross-node races (N nodes, 1 key, simultaneous) still
produce up to N origin fetches. The chosen approach — **Strategy A** — rides the **existing** event
companion, requires **no Redis or other new infrastructure**, and is gated behind the separate
`CoalesceRacingCacheMissesAcrossNodes` flag (see §"Surface").

### Strategy A — Best-effort suppression via companion broadcast (chosen)

Reuse the existing event companion (Service Bus or any configured notifier):

1. When a leader begins a fetch (and `coalesceCrossNode` is on), broadcast a new
   `CacheFetchStartedDescriptor { Emitter, Key, StartedAt }`.
2. A peer that hits a racing miss for the same key and has already received the "fetch started" signal
   **suppresses** its own fetch and instead **awaits the value**, which arrives via the *existing*
   `CacheMiss` notification (the leader already broadcasts the value/pointer in `NotifyMiss` on
   completion, populating `externalMissDictionary`).
3. **Fallback / safety:** the peer waits at most a `CrossNodeFetchWaitBudget`; on timeout (or if the
   leader's completion never arrives, e.g. a lost message or a dead leader) it fetches itself. This
   bounds tail latency and makes the mechanism self-healing.

- ✅ **No extra infrastructure** — rides the companion already used for invalidation; works even with
  zero passive locations (no Redis).
- ✅ **Degrades safely** — no companion ⇒ silently reduces to in-memory (Flag 1); lost/late messages
  ⇒ budgeted fallback to a local fetch.
- ⚠️ **Best-effort by nature** — the "started" broadcast may arrive after a peer already began, so
  cross-node races **shrink** rather than vanish. That is the correct trade-off for load-shedding: it
  removes the bulk of duplicate origin fetches without a hard distributed lock.

### Rejected alternative — Redis distributed lock

A strong single-flight via a Redis lock (`SET lock:{key} NX PX {ttl}`: winner fetches + writes, losers
poll/subscribe) would give near-exactly-once cross-node fetches, but it is **rejected**:

- ❌ **Requires Redis** — many deployments (including the single-node `Learn.Web` case) have **no
  Redis at all**, so it is not universally applicable.
- ❌ Adds Redis round-trips on the cold path and a hard dependency on the passive store.
- ❌ Lock-TTL tuning is fiddly and a dead leader can stall peers until the TTL expires.

Strategy A achieves most of the benefit with none of these constraints, so no Redis-lock mode is
planned.

### Why a second flag (not a mode enum)

Rather than a `CoalesceScope` enum, cross-node is a **plain second boolean**
(`CoalesceRacingCacheMissesAcrossNodes`) layered on Flag 1. This keeps each capability independently
toggleable per call / per class / globally, matches the requested `SmartCacheOperationOptions` shape,
and avoids encoding the rejected Redis mode in the type system.

---

## ⚖️ Edge Cases & Semantics

| Case | Handling |
|------|----------|
| **Different `MaxAge` among racing callers** | The leader's fetch returns a value created "now" (`timestamp` truncated to the second). Since every racing caller's `minimumCreationDate = timestamp − MaxAge ≤ timestamp`, the fresh value satisfies all of them. Joiners accept the leader's value. |
| **`forceFetch` (`MaxAge = 0`) callers** | They demand a value created *now*. Within the sub-second racing window the leader's `timestamp` equals theirs (truncation), so coalescing is still correct. If stricter semantics are required, `forceFetch` calls may **opt out** of coalescing (skip the registry) — configurable. |
| **`Disabled` operation** | Short-circuits before any cache logic (unchanged); never coalesced. |
| **Leader faults** | All joiners observe the exception; the key is removed → the *next* call re-attempts (no cached failure). |
| **Leader cancelled by its caller** | Shared fetch runs under `CancellationToken.None`, so it completes for joiners; the cancelling caller still throws `OperationCanceledException` via `WaitAsync(ct)`. |
| **All callers cancel** | Fetch still completes and populates the cache (cache-warming side-effect). Acceptable; a future refinement could link a token that cancels only when *all* joiners have cancelled. |
| **Key equality** | Relies on the cache key's `Equals`/`GetHashCode` (e.g. `MethodCallCacheKey`), consistent with `memoryCache` and `keys`. |
| **Reentrancy** | `fetchAsync` that recursively calls `GetAsync` for the *same* key would deadlock on itself; document as unsupported (same constraint as any single-flight). |

---

## 📊 Observability

- **New source tag:** `Tags.Type.Coalesced = ("source_type", "coalesced")` — increment
  `Instruments.Sources` for each **joiner** so coalesced serves are distinguishable from `miss` /
  `memory` / `distributed`.
- **Activity tag:** `cache.coalesced = 1` on joiner activities.
- **Optional gauge:** an `UpDownCounter<long>` `cache.inflight.count` (increment on leader add,
  decrement on removal) to observe stampede width and registry occupancy.
- The **leader** keeps emitting the existing `miss` source + `FetchDuration` lap, so origin-fetch
  counts drop to one-per-stampede and the effect is directly visible in
  `cache.source.count{source_type=coalesced}` vs `{source_type=miss}`.

---

## 🧪 Testing Strategy

- **Single-flight:** N concurrent `GetAsync(sameKey)` against a fetch stub that blocks until released
  and counts invocations → assert **exactly one** invocation; all N receive the same value.
- **Opt-out:** with the flag off, assert N invocations (current behaviour preserved).
- **Failure fan-out:** leader throws → all joiners observe the exception; a subsequent call re-fetches
  (registry cleared).
- **Cancellation isolation:** leader's caller cancels → joiners still succeed; a joiner cancels → others
  unaffected.
- **Freshness:** joiner with a longer `MaxAge` accepts the leader value; `forceFetch` opt-out path (if
  enabled) bypasses the registry.
- **Config resolution:** operation ⟶ dynamic(class-aware) ⟶ core default precedence honoured.
- **Concurrency stress:** high-parallelism loop asserting registry returns to empty (no leaks).

---

## 🚦 Risks & Trade-offs

| Risk | Mitigation |
|------|------------|
| Behaviour change for existing users | **Opt-in** (both flags default `false`); no path changes when disabled |
| Type erasure via `object?` | Values already boxed in `ValueEntry`; cast is safe for the single key/type pair |
| Registry leak on pathological cancellation | Leader removes in `finally`; entry lifetime bounded by fetch duration; optional gauge to monitor |
| Joiner receiving a marginally older value | Bounded by the sub-second racing window + truncation; `forceFetch` opt-out available |
| Cross-node cost (broadcast + wait) | **Separate** opt-in (`CoalesceRacingCacheMissesAcrossNodes`); off by default; only active when a companion is present |
| Cross-node best-effort still races | Documented as load-shedding, not a hard lock; budgeted fallback to a local fetch; **no Redis dependency** |
| Reentrant same-key fetch deadlock | Documented constraint; standard for single-flight |

---

## 📎 Appendix

### A. Affected files (v1)

| File | Change |
|------|--------|
| `SmartCacheOperationOptions.cs` | add `bool? CoalesceRacingCacheMisses` (v1) — and `bool? CoalesceRacingCacheMissesAcrossNodes` (v2) |
| `ISmartCacheCoreOptions.cs` / `SmartCacheCoreOptions.cs` | add `bool CoalesceRacingCacheMisses` + `bool CoalesceRacingCacheMissesAcrossNodes` (both default `false`) |
| `IDynamicSmartCacheCoreOptions.cs` / `DynamicSmartCacheCoreOptions.cs` | add `bool?` variants of both flags (class-aware) |
| `SmartCache.cs` | resolve flags in public `GetAsync`; thread into private overload; single-flight registry + leader/joiner around the extracted `CoreFetchAndSetAsync`; (v2) companion `CacheFetchStarted` broadcast + budgeted suppression |
| `SmartCacheObservability.cs` | add `Tags.Type.Coalesced`; optional `cache.inflight.count` |

### B. Concept comparison

| Concept | Provided by | De-duplicates |
|---------|-------------|---------------|
| Single-flight (this proposal) | `SmartCache` (new, opt-in) | Concurrent **callers**, same key, same node (v1); cluster (v2) |
| Location race (`WhenAnyValid`) | `SmartCache` (existing) | Cache **locations** for one caller |
| Cross-node awareness | companion (Service Bus / Redis) | **Staggered** misses (post-write) + invalidation |

### C. Related

- Consumer-side workaround & motivating analysis: `Learn` repo →
  `src/docs/90. Issues/202607/20270720.02-smartcache-reload-redis/01.smartcache-doesnt-coalesce.md`
- Implementation plan: [`01-CoalesceRacingCacheMisses-plan.md`](01-CoalesceRacingCacheMisses-plan.md)
