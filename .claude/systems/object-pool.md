# ObjectPool

The Object Pool implementation from [ARCHITECTURE.md §6](../ARCHITECTURE.md) — the system §2 calls
"mandatory, not decorative", because `Instantiate`/`Destroy` at wave rates is the classic mobile
GC-spike source.

## Responsibility

Hand out a configured, active pooled `Component` and take it back, without allocating and without
destroying anything.

It deliberately does **not**:

- **Place or configure the instance.** `Get()` returns it at the transform where it died. Applying
  an `EnemyDefinition` and positioning it is `EnemyFactory`'s job — that seam is §6's Factory entry.
- **Own the prefab's lifetime.** It never destroys an instance, not even on growth. Instances die
  with their scene.
- **Grow silently.** It grows rather than failing a spawn, but says so — see Gotchas.
- **Track what it did not hand out.** An instance destroyed while active and never released is
  undetectable without a sweep, and a sweep is declined. See Gotchas.
- **Raise events.** Growth is a log, not a `PoolGrew` event, because nothing in-game should react
  to it. The pool is not in the [§8 catalogue](../ARCHITECTURE.md) at all.
- **Cross threads or scenes.** Main thread only, one scene, no `Clear`/`Dispose`.

## Key types

| Type | File | Role |
|---|---|---|
| `IPoolable` | [IPoolable.cs](../../Assets/Scripts/Core/Pooling/IPoolable.cs) | `OnSpawn` / `OnDespawn`. The reset contract a pooled component implements. |
| `IPoolStats` | [IPoolStats.cs](../../Assets/Scripts/Core/Pooling/IPoolStats.cs) | Read-only counts: `Name`, `Prewarm`, `InstanceCount`, `ActiveCount`, `AvailableCount`, `PeakActive`. |
| `ObjectPool<T>` | [ObjectPool.cs](../../Assets/Scripts/Core/Pooling/ObjectPool.cs) | The pool. `Get()` / `Release()`, constrained `where T : Component, IPoolable`. |

Namespace `MobileDemo.Core.Pooling`, assembly `MobileDemo.Core`.

## Patterns used

**Object Pool**, in the one shape §2's mobile constraint demands. §6 records why it is here and why
towers are *not* pooled; this is the short form of the implementation choices.

- **Prefab + `Component` constrained, not a delegate factory.** `where T : Component, IPoolable`
  means the pool owns `Instantiate`, parenting and `SetActive` — which is most of what a Unity pool
  *is*. A `Func<T>` version would be a re-implementation of `UnityEngine.Pool.ObjectPool<T>`, which
  §15 explicitly declines, and it would push `SetActive` into every `IPoolable`.
- **`Stack<T>` available + `HashSet<T>` active.** The stack gives O(1) get/release and reuses the
  most-recently-released instance, whose transform is warmest in cache. The set is what lets
  `Release` *reject* a double release or a foreign instance, and it makes `ActiveCount` free. A
  bool flag on `IPoolable` would be cheaper but cannot spot an instance belonging to another pool
  — the failure that actually corrupts a pool — and it would move the pool's bookkeeping into the
  contract the pool exists to guard.
- **`Prewarm`/`InstanceCount`, not `Capacity`.** In .NET `Capacity` means "slots before growth",
  which is precisely what growth destroys. `InstanceCount` is derived from the two collections, so
  it stays true when a destroyed instance is discarded, and `InstanceCount > Prewarm` is the durable
  record that a pool grew — better evidence than a console line that scrolls away.
- **Two callbacks rather than `OnEnable`/`OnDisable`.** Those are spoken for: a pooled subscriber is
  disabled, not destroyed, so `OnEnable`/`OnDisable` is the only correct home for bus subscription
  (see [event-bus.md](event-bus.md)). `OnSpawn` resets gameplay state.

## Collaborators

Nothing uses it yet — the pool landed second, after the bus and ahead of §13's vertical slice.
§5 and §13 name who will:

| Consumer | Uses | For |
|---|---|---|
| `EnemyFactory` | `ObjectPool<Enemy>` | §13 step 2 — gets a pooled enemy and applies an `EnemyDefinition` |
| `Tower` | `ObjectPool<Projectile>` | Firing at `TOWER_SCAN_INTERVAL_SEC`-driven cadence (§9) |
| `Enemy` / `Projectile` | implement `IPoolable` | Resetting health, waypoint index, velocity on spawn |
| Pool Overlay (planned) | `IPoolStats` | Live counts in the editor — the tool is still §12 work |

Depends on `System`, `System.Collections.Generic` and `UnityEngine`. No package, no asset, no base
class a pooled type must inherit beyond `Component`. `MobileDemo.Core` references nothing, which is
what keeps this usable from both Gameplay and UI.

It raises and subscribes to **no events**. That is deliberate — see Responsibility.

## Data

None. Prewarm is a **constructor argument**, not a serialized field: the pool is constructed in
code, so it stays testable with no asset and no scene. §2's `ENEMY_POOL_PREWARM` and
`PROJECTILE_POOL_PREWARM` move onto `GameConfig` when §13's slice creates it — a ScriptableObject
holding one field today would be an asset for its own sake.

## Gotchas

- **Never `Destroy` a pooled instance — `Release` it.** `Get` skips a destroyed available instance
  and `Release` frees the slot for one destroyed while active, so neither throws. But an instance
  destroyed while active and *never released* is invisible: `ActiveCount` never drops,
  `AvailableCount` stays put, and the pool grows forever. Detecting it needs a sweep over `active`,
  which is per-frame work to catch a caller bug, so it is declined rather than hidden.
- **`==` on a generic `T` does not see a destroyed object.** C# does not bind a constraint type's
  overloaded operators on a type parameter, so `item == null` inside `ObjectPool<T>` is plain
  reference equality and a destroyed instance reads as live. The pool casts to `UnityEngine.Object`
  in one `IsAlive` helper for exactly this reason. Anything generic added here must go through it.
- **The pool parent must be an unscaled empty.** `Instantiate(prefab, parent)` is
  `instantiateInWorldSpace: false`, so instances inherit the parent's transform — a parent scaled
  to anything but 1 silently scales every enemy.
- **The prefab root must be authored *active*.** Instantiating an inactive prefab skips
  `Awake`/`OnEnable`, moving that cost to the first mid-wave `SetActive(true)` — the exact hitch
  prewarm exists to prevent.
- **`OnSpawn` is the sole initializer.** Prewarm instantiates, deactivates and pushes without
  calling `OnDespawn`, so nothing may depend on `OnDespawn` having run. Routing prewarm through
  `Release` would fire it 64× at load and turn any death effect into a load-time bug.
- **`OnEnable` runs *before* `OnSpawn`.** The pool activates first so a coroutine or tween started
  in `OnSpawn` has a live GameObject. The cost: `OnEnable` must not read state that `OnSpawn`
  resets, or it reads the previous life's values. Subscribe in `OnEnable`, reset in `OnSpawn`, in
  that order, never the reverse.
- **`Get` does not place the object.** Position it in the same frame, before anything reads it — a
  physics step with an enabled collider at the stale position will fire triggers there.
- **Growth warns once per pool.** A burst can exhaust a pool many times in one frame and a flooded
  console is a console nobody reads. It grows by **one** per exhausted `Get`, never doubling —
  doubling a 64-deep pool mid-wave trades the hitch you were avoiding for a bigger one. It is
  `LogWarning`, never `LogError`, because an error log fails a Unity test.
- **A rejected `Release` warns *every* time.** The asymmetry with growth is the point: growth is a
  tuning miss with a bounded cost, a double release is a caller defect that would hand one instance
  to two owners. A rejected instance is left untouched — deactivating something the pool does not
  own is a bug of our own making.
- **`PeakActive` never resets.** It is a whole-session high-water mark, not per-wave. That is what
  makes it the number to tune §2's prewarm figures to after a full run (§10); a per-wave reading
  would need the overlay that is still out of scope.
- **No `Clear`/`Dispose`, and no play-mode static reset.** Unlike `EventBus`, the pool holds no
  statics, so nothing survives a domain-reload skip and there is no `ClearAll()` analogue to write.
  Instances die with their scene, so a `Clear` would have no caller, and a `Dispose` would have to
  choose `Destroy` or `DestroyImmediate` and be wrong in one of edit/play mode. Add one only when a
  pool must outlive its scene, or on additive-scene unload.
- **A negative prewarm clamps; a null prefab throws.** A pool with no prefab cannot exist, so that
  is unrecoverable. A bad tuning number is recoverable, and dying over it would contradict the
  grow-and-warn stance `Get` takes on the same class of mistake.

## Status

**Implemented.** `IPoolable`, `IPoolStats` and `ObjectPool<T>` exist and compile against Unity 6.3
at `-langversion:9.0`. 30 EditMode tests in
[ObjectPoolTests.cs](../../Assets/Tests/EditMode/ObjectPoolTests.cs) cover §14's get/release/reuse
and no-leak, plus growth on exhaustion, the rejected double release, the destroyed-instance paths,
and the two ordering guarantees §6's reasoning leans on (`SetActive`→`OnSpawn`, `OnDespawn`→
`SetActive`). "Warns *once*" is documented but not tested — Unity's test framework does not track
unexpected warnings, so there is nothing to assert against.

No production consumer exists yet. The first is §13 step 2, when `EnemyFactory` gets a pooled
`Enemy` from an `ObjectPool<Enemy>`; `Enemy` is also the first `IPoolable`, and the first place the
`OnEnable`-before-`OnSpawn` gotcha above will actually bite.
