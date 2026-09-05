# ObjectPool

The Object Pool implementation from [ARCHITECTURE.md §6](../ARCHITECTURE.md) — the system §2 calls
"mandatory, not decorative", because `Instantiate`/`Destroy` at wave rates is the classic mobile
GC-spike source.

> **Over CLAUDE.md's ~150-line line, and this is the case where the rule misfires.** `ObjectPool<T>`
> is 99 lines of code with one responsibility. The guide is long because it is where the project
> records what the pool deliberately does *not* do — no registry, no overlay, no `Clear`, no sweep
> — each with a named trigger. That inventory of declined work is the most useful thing here, and
> shortening the document would mean deleting reasoning rather than simplifying a system.

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

§13's slice gave it its first real consumers:

| Consumer | Uses | For | State |
|---|---|---|---|
| [`EnemyFactory`](enemy-factory.md) | `ObjectPool<Enemy>` | Gets a pooled enemy and applies an `EnemyDefinition`. Constructed by [`Bootstrap`](bootstrap.md) and kept private | **does** |
| [`Enemy`](enemy.md) | implements `IPoolable` | Resetting `IsFinished` and tearing down its state machine | **does** |
| `Bootstrap.OnDestroy` | `IPoolStats` | Logs `PeakActive` after a run — §10's number to tune prewarm to | **does** |
| `Tower` | `ObjectPool<Projectile>` | Firing at `TOWER_SCAN_INTERVAL_SEC`-driven cadence (§9) | planned |
| `Projectile` | implements `IPoolable` | Resetting velocity on spawn | planned |
| Pool Overlay | `IPoolStats` | Live counts in the editor — still §12 work | planned |

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
- **But `OnSpawn` cannot initialize from data that arrives *after* `Get()`** — discovered by
  `Enemy`, and a genuine sharpening of the "`Get` does not place the object" bullet above. The
  pool's order is `SetActive(true)` → `OnSpawn()` → `Get` returns, so anything the *caller* passes
  in (an `EnemyDefinition`, a path) does not exist yet: reading it from `OnSpawn` is a
  `NullReferenceException` on the first spawn and, worse, a **stale value from the previous life**
  on every spawn after. The working split is `OnSpawn` resets pool-owned state only, and a
  `Configure` call from the factory starts the object's life. See [enemy.md](enemy.md).
- **A pooled type builds its per-instance helper objects in `Awake`, not `OnSpawn`.** The
  corollary of the two bullets above, and the one with a performance cost rather than a
  correctness one: `OnSpawn` runs once per spawn, so allocating there means a full-pool burst
  allocates the whole set mid-wave. `Enemy` builds its three state objects in `Awake`, where
  prewarm pays for all 64 at load. This is why the prefab-root-must-be-active rule matters twice.
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

## Second client

`ObjectPool<Projectile>` arrived with the tower slice and is the first evidence that the generic
was worth having — until then "one generic pool serves both clients" (§5) was a claim with one
client. Three things it exercised that `Enemy` never did:

- **`IPoolStats` as a collection.** [`ProjectileFactory`](projectile.md) holds a
  `IReadOnlyList<IPoolStats>` across several pools, which is the exact use case this interface's
  own comment says it exists for. Before this, `PeakActive` had one reader and the non-generic
  face was justified structurally rather than by a caller.
- **Several pools of the same closed type.** A projectile's tuning lives on its prefab (§7), so
  each prefab needs its own pool — and pools are therefore keyed by prefab, not by type. Nothing
  in `ObjectPool<T>` had to change for that; the factory above it does the keying.
- **A measured prewarm that is far too large.** Both projectile pools peaked at 1 active against
  a prewarm of 128. §2 records why that is not simply edited down yet.

Still no second `Enemy` pool: the planned tanks cannot share the soldier silhouette and so will
need one, which is the trigger [enemy-factory.md](enemy-factory.md) already names.

## Status

**Implemented.** `IPoolable`, `IPoolStats` and `ObjectPool<T>` exist and compile against Unity 6.3
at `-langversion:9.0`. 30 EditMode tests in
[ObjectPoolTests.cs](../../Assets/Tests/EditMode/ObjectPoolTests.cs) cover §14's get/release/reuse
and no-leak, plus growth on exhaustion, the rejected double release, the destroyed-instance paths,
and the two ordering guarantees §6's reasoning leans on (`SetActive`→`OnSpawn`, `OnDespawn`→
`SetActive`). "Warns *once*" is documented but not tested — Unity's test framework does not track
unexpected warnings, so there is nothing to assert against.

**It now has a production consumer, and has run.** §13's slice wired `EnemyFactory` to an
`ObjectPool<Enemy>`, with `Enemy` as the first `IPoolable` and `Bootstrap.OnDestroy` as the first
reader of `IPoolStats`. `EnemyFactoryTests` covers the seam; the pool's own 30 tests keep sole
ownership of growth, the rejected double release and the ordering guarantees, so nothing
re-asserts them.

Measured in play mode over ~40 spawns in 81 s: `InstanceCount` never left its prewarm of 64 and
`PeakActive` settled at 9, so recycling is observed rather than merely tested, and §2's prewarm
figure is confirmed as *far* larger than this slice needs. It is deliberately **not** retuned
down — §10's guidance is to tune to `PeakActive` after a full *wave*, and `WaveRunner` does not
exist, so 9 is a measurement of `Bootstrap`'s placeholder cadence rather than of the game.

A second run, with the HUD attached, reproduced `PeakActive=9` exactly — against a prewarm that
had meanwhile been retuned in the asset to **30**, giving
`PeakActive=9, InstanceCount=30, Prewarm=30` and again no growth. Two things follow. The pool's
recycling is now observed at two different prewarms, which is a stronger claim than one run at
one size. And the prewarm the asset carries no longer matches the 64 §2 names — that divergence
is recorded there, unresolved on purpose, because 9 is still map 1's peak under a placeholder
spawner and §2's figure has to cover the worst of three maps under a real `WaveRunner`.

**One prediction this guide made was wrong, and is corrected rather than quietly dropped.** It
used to close by saying `Enemy` would be "the first place the `OnEnable`-before-`OnSpawn` gotcha
will actually bite." It isn't: `Enemy` *publishes* `EnemyLeaked` and subscribes to nothing, so it
has no `OnEnable`/`OnDisable` at all. That gotcha's first real victim is still ahead — a pooled
`Projectile` that subscribes, or an `Enemy` that one day needs to. What `Enemy` did surface
instead is the `OnSpawn`-cannot-initialize rule now recorded under Gotchas, which is a different
and more useful lesson.
