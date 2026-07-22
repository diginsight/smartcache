# Plan — `CoalesceRacingCacheMisses` for SmartCache

**Status:** Draft → Actionable
**Owner:** Dario Airoldi
**Related:** [overview.md](overview.md) (analysis)
**Target:** `Diginsight.SmartCache` 3.7.x
**Goal:** Add an opt-in single-flight so concurrent racing misses for the same key share one origin
fetch — **v1 in-memory (single node)**, **v2 cross-node (future)**.

---

## Guiding principles

- **Opt-in, zero-impact-by-default.** Default `false`; when disabled the code path is byte-identical
  to today.
- **One choke point.** Guard the origin fetch (`FetchAndSetValueAsync`), covering both call sites.
- **Reuse existing patterns.** Config resolution mirrors `MaxAge`; observability mirrors existing
  `source_type` tags.
- **Ship v1 fully before v2.** v2 (cross-node) builds on v1 and must not regress it.

---

## Phase 0 — Baseline & tests (safety net) `[ ]`

- [ ] 0.1 Add a failing xUnit test proving the stampede: N concurrent `GetAsync(sameKey)` with a
  blocking, invocation-counting fetch stub → currently asserts **N** fetches. Locks in current
  behaviour and becomes the v1 regression guard.
- [ ] 0.2 Capture a micro-benchmark (optional) of cold-key concurrent gets for before/after numbers.

**Acceptance:** test builds and reproduces N-fetch behaviour on `main`.

---

## Phase 1 — Configuration surface `[ ]`

**Flag 1 — in-memory single-flight (v1):**

- [ ] 1.1 `SmartCacheOperationOptions.cs` — add `public bool? CoalesceRacingCacheMisses { get; set; }`
  (nullable = inherit). `Clone()` (MemberwiseClone) carries it automatically.
- [ ] 1.2 `ISmartCacheCoreOptions.cs` — add `bool CoalesceRacingCacheMisses { get; }`.
- [ ] 1.3 `SmartCacheCoreOptions.cs` — implement with default **`false`**; bind from
  `Diginsight:SmartCache`.
- [ ] 1.4 `IDynamicSmartCacheCoreOptions.cs` — add `bool? CoalesceRacingCacheMisses { get; }`.
- [ ] 1.5 `DynamicSmartCacheCoreOptions.cs` — class-aware `bool?` (enables
  `CoalesceRacingCacheMisses@<CallerType>`).

**Flag 2 — cross-node best-effort suppression (v2 surface, wired in Phase 7):**

- [ ] 1.6 `SmartCacheOperationOptions.cs` — add
  `public bool? CoalesceRacingCacheMissesAcrossNodes { get; set; }`.
- [ ] 1.7 `ISmartCacheCoreOptions.cs` / `SmartCacheCoreOptions.cs` — add
  `bool CoalesceRacingCacheMissesAcrossNodes` (default **`false`**).
- [ ] 1.8 `IDynamicSmartCacheCoreOptions.cs` / `DynamicSmartCacheCoreOptions.cs` — add the class-aware
  `bool?` variant.

> The two flags are **independent** and resolved the same way (see Phase 2). Flag 2 only has effect
> when Flag 1 is also on **and** a companion is configured; there is **no Redis dependency**.

**Decision — Option A vs B (record in PR):**
- **A (recommended):** `bool?` on operation options → supports inherit chain
  `operation ?? dynamic ?? core`.
- **B (simpler):** `bool` on operation options as literally requested → no inherit; per-call value
  always wins. If chosen, drop the dynamic variants or treat `false` as "not set".

**Acceptance:** solution compiles; new keys bind from configuration; existing configs unaffected.

---

## Phase 2 — Resolve the effective flags `[ ]`

- [ ] 2.1 In **public** `SmartCache.GetAsync<T>`, resolve both flags (reuse the already-fetched
  `dynamicCoreOptions` / `coreOptions`), with **cross-node implying in-memory**:
  ```csharp
  bool coalesceCrossNode =
      finalOperationOptions.CoalesceRacingCacheMissesAcrossNodes
      ?? dynamicCoreOptions.CoalesceRacingCacheMissesAcrossNodes
      ?? coreOptions.CoalesceRacingCacheMissesAcrossNodes;

  bool coalesce =
      ( finalOperationOptions.CoalesceRacingCacheMisses
        ?? dynamicCoreOptions.CoalesceRacingCacheMisses
        ?? coreOptions.CoalesceRacingCacheMisses )
      || coalesceCrossNode;   // cross-node ⇒ in-memory
  ```
- [ ] 2.2 Thread `coalesce` (v1) and `coalesceCrossNode` (v2) into the **private** `GetAsync<T>(...)`
  as new parameters (alongside `timestamp`, `maybeMinimumCreationDate`, expirations). v1 consumes
  `coalesce`; `coalesceCrossNode` stays inert until Phase 7.

**Acceptance:** flags flow to the private overload; enabling only `AcrossNodes` also enables in-memory;
no behavioural change yet (v1 registry added in Phase 3).

---

## Phase 3 — In-memory single-flight (v1 core) `[ ]`

- [ ] 3.1 Add the registry field to `SmartCache`:
  ```csharp
  private readonly ConcurrentDictionary<object, TaskCompletionSource<object?>> inFlightFetches = new();
  ```
- [ ] 3.2 Extract the current body of `FetchAndSetValueAsync` into
  `CoreFetchAndSetAsync(Activity?, CancellationToken)` (fetch + `SetValue` + metrics), unchanged.
- [ ] 3.3 Reimplement `FetchAndSetValueAsync` with the leader/joiner protocol (see overview
  §"Design — In-Memory"):
  - `if (!coalesce) return await CoreFetchAndSetAsync(activity, cancellationToken);`
  - `GetOrAdd` a `TaskCompletionSource<object?>`; reference-equality elects the leader.
  - **Leader:** `await CoreFetchAndSetAsync(activity, CancellationToken.None)`; `tcs.SetResult`;
    `SetException` on error; **remove in `finally`**.
  - **Joiner:** `await current.Task.WaitAsync(cancellationToken)`; cast `(T)`.
- [ ] 3.4 Confirm both `FetchAndSetValueAsync` call sites (external-locations fallback + final
  fallback) route through the new guard.

**Acceptance:** Phase 0 test now asserts **exactly one** fetch when the flag is on; **N** when off.

---

## Phase 4 — Observability `[ ]`

- [ ] 4.1 `SmartCacheObservability.cs` — add
  `Tags.Type.Coalesced = new("source_type", "coalesced")`.
- [ ] 4.2 Joiner increments `Instruments.Sources.Add(1, Tags.Type.Coalesced)` and sets activity tag
  `cache.coalesced = 1`.
- [ ] 4.3 (Optional) add `UpDownCounter<long> cache.inflight.count`; +1 on leader add, −1 on removal.

**Acceptance:** metrics distinguish coalesced serves from misses; leader still emits the `miss` source.

---

## Phase 5 — Edge-case hardening & tests `[ ]`

- [ ] 5.1 Failure fan-out test — leader throws → all joiners observe it; next call re-fetches.
- [ ] 5.2 Cancellation isolation tests — leader's caller cancels (joiners still succeed); joiner
  cancels (others unaffected).
- [ ] 5.3 Freshness test — joiner with larger `MaxAge` accepts leader value.
- [ ] 5.4 Decide & implement `forceFetch` (`MaxAge=0`) policy: **coalesce** (default) or **opt-out**;
  add a test for the chosen behaviour.
- [ ] 5.5 Registry-leak stress test — high parallelism; assert `inFlightFetches` returns to empty.
- [ ] 5.6 Config-precedence test — operation ⟶ dynamic ⟶ core.

**Acceptance:** all edge-case tests green; registry provably drains.

---

## Phase 6 — Docs & changelog `[ ]`

- [ ] 6.1 Update SmartCache README / concepts with the opt-in flag, resolution order, and semantics.
- [ ] 6.2 Add a changelog entry under `docs/10. ChangeLog`.
- [ ] 6.3 Cross-link this issue's [overview.md](overview.md) and the `Learn` consumer note.

**Acceptance:** documented feature with configuration examples
(`CoalesceRacingCacheMisses@CachedContentSource: true`).

---

## Phase 7 (future) — Cross-node coalescing (v2, Strategy A only) `[ ]`

> Design in overview §"Cross-Node Coalescing". Do **not** start until v1 ships and stabilises.
> **Strategy A only** (companion broadcast). The Redis-lock strategy is **rejected** (not every
> deployment has Redis); do not implement it.

- [ ] 7.1 Consume the `coalesceCrossNode` flag (resolved in Phase 2, and which already forces
  `coalesce` on via the cross-node ⇒ in-memory implication) inside the private `GetAsync` / leader
  path.
- [ ] 7.2 Add `CacheFetchStartedDescriptor { Emitter, Key, StartedAt }` + a companion notifier method;
  the leader broadcasts on fetch start **only when** `coalesce && coalesceCrossNode` and a companion
  is present.
- [ ] 7.3 Peer suppression: on a racing miss where a matching `CacheFetchStarted` was seen, await the
  value via the existing `CacheMiss`/`externalMissDictionary` completion instead of fetching.
- [ ] 7.4 Add `CrossNodeFetchWaitBudget` (config) + budgeted fallback: on timeout / lost message /
  dead leader, the peer fetches locally. Must be self-healing.
- [ ] 7.5 Graceful degradation: no companion ⇒ silently behave as in-memory (Flag 1); never error.
- [ ] 7.6 Multi-node test harness (in-proc fakes for the companion): suppression, timeout fallback,
  lost-message tolerance, dead-leader recovery.

**Acceptance:** simultaneous multi-node miss triggers measurably fewer origin fetches (best-effort),
with safe budgeted fallback and **no new infrastructure dependency**.

---

## Rollout

1. Merge Phases 1–6 behind the default-`false` flag (no user impact).
2. `Learn.Web` enables it via `CoalesceRacingCacheMisses@CachedContentSource: true` and **removes** its
   local `ConcurrentDictionary` single-flight workaround.
3. Gather production metrics (`source_type=coalesced` vs `miss`) before scheduling v2.

---

## Open questions

- [ ] Default for `forceFetch` callers: coalesce or opt-out?
- [ ] Ship the optional `cache.inflight.count` gauge in v1 or defer?
- [ ] Option A (`bool?`) vs Option B (`bool`) on `SmartCacheOperationOptions` — confirm with maintainers.
- [ ] v2 default `CrossNodeFetchWaitBudget` value, and whether Flag 2's surface ships in v1 (inert) or
  only alongside the Phase 7 implementation.
