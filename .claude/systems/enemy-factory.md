# EnemyFactory

> A second factory now exists — [`ProjectileFactory`](projectile.md) — and it is the case this
> guide's "a second prefab means a second pool" note predicted. It keeps a pool per prefab because
> projectile tuning lives on the prefab (§7). Read the two together.

The Factory from [ARCHITECTURE.md §6](../ARCHITECTURE.md) — the seam between *data* (which
enemy) and *instance* (a live pooled object), and the only type in the project that knows an
`Enemy` is pooled.

## Responsibility

Hand back a configured, placed, live enemy for a given `EnemyDefinition` — and take it back.

It deliberately does **not**:

- **Construct its own pool.** The pool is injected. [`Bootstrap`](bootstrap.md) is the
  composition root, and injection is what keeps this class constructible in a test.
- **Own the set of live enemies.** §5 defines this type as turning a definition into an enemy.
  Owning the live set is a different responsibility, and it belongs to whoever drives the tick —
  `Bootstrap` today, `WaveRunner` later. [object-pool.md](object-pool.md) declines to expose its
  active set for the same reason: a pool that hands out its registry is a registry.
- **Decide when an enemy dies.** `Release` is called by the driver on `IsFinished`.
- **Abstract itself.** No `AbstractFactory` hierarchy, no `IEnemyFactory`. §6 rules out both by
  name: one concrete factory is enough for two enemy types.

## Key types

| Type | File | Role |
|---|---|---|
| `EnemyFactory` | [EnemyFactory.cs](../../Assets/Scripts/Gameplay/Enemies/EnemyFactory.cs) | `Create` / `Release` / `Stats`. |

Namespace `MobileDemo.Gameplay.Enemies`, assembly `MobileDemo.Gameplay`.

## Patterns used

**Factory**, in the one shape §6 justifies: it is the single place that knows how to wire an
asset to a pooled instance. Two lines of body is the honest size of that job —

```
Enemy enemy = pool.Get();
enemy.Configure(definition, waypoints);
```

— and the value is not the lines, it is that **exactly one place** in the codebase performs that
pairing, so the `Get`-then-configure ordering rule cannot drift.

**Object Pool**, as the client that owns the pool privately. `Release` keeps its name from §13
step 2's own wording: "`Release` is the opposite direction, the return to the pool."

## Collaborators

| Direction | With |
|---|---|
| Depends on | [`ObjectPool<Enemy>`](object-pool.md) (injected), [`Enemy`](enemy.md), `EnemyDefinition` |
| Used by | [`Bootstrap`](bootstrap.md) — and by `WaveRunner`, when §5's row has code |
| Exposes | `IPoolStats` via `Stats`, read by `Bootstrap.OnDestroy` |

It raises and subscribes to **no events**. It is not in §8's catalogue, and neither is the pool.

## Data

None of its own. It reads whatever `EnemyDefinition` it is handed and passes the waypoints
straight through.

## Gotchas

- **`Create` configures in the statement immediately after `Get()`, and that ordering is the
  point.** `Get()` returns an instance that is already active but still sitting at last life's
  transform, and object-pool.md's contract is explicit that placing it is the caller's job
  ("position it in the same frame, before anything reads it"). Nothing can observe the gap today
  because the enemy prefab has no collider — but the moment the tower slice adds one, a physics
  step in that gap would fire triggers at the stale position. Establishing the rule before it can
  bite is the reason this method exists rather than callers doing both steps themselves.
- **A null `definition` throws; nothing else does.** Same split as the pool: a missing dependency
  is unrecoverable, a bad number clamps.
- **`definition == null` here really does see a destroyed asset — unlike inside
  `ObjectPool<T>`.** The type is concrete, so it binds `UnityEngine.Object`'s overloaded `==`.
  The pool needed its `IsAlive` cast precisely because a generic `T` never binds it.
  Same-shaped code, opposite semantics, one file apart — which is why both carry a comment.
- **`Stats` is a read-only face, not the pool.** Handing out `IPoolStats` rather than
  `ObjectPool<Enemy>` is what keeps "the factory is the only type that knows an `Enemy` is
  pooled" true while still letting §10's `PeakActive` be read. This is the property's whole job
  today; §15's editor overlay that would also read it is still §12 work.
- **A rejected `Release` returns `false` and warns from inside the pool**, every time — the
  factory adds no handling of its own, because there is nothing sensible to do about a caller
  defect at this level.

## Status

**Implemented**, one enemy type. 7 EditMode tests in
[EnemyFactoryTests.cs](../../Assets/Tests/EditMode/EnemyFactoryTests.cs) cover the null guards,
the active-and-placed instance, that the definition is genuinely *applied* (asserted by driving
the enemy at the asset's speed, rather than through a seam onto the sprite field), the
create/release/create recycle restarting from the path's start, and `Stats`.

Growth on exhaustion, the rejected double release and the `SetActive`↔`OnSpawn` ordering are
deliberately **not** re-tested here — `ObjectPoolTests` owns them, and asserting them through the
factory would be testing the pool twice.

**The second `EnemyDefinition` now exists** (`EnemyGreySoldier`), and it cost no change here —
still one concrete factory, one pool, one prefab. That is the §6 claim discharged: a second enemy
type is data.

Pending: `WaveRunner` replacing `Bootstrap` as the caller, and — the one that *will* change this
type — **a second enemy prefab**. Green and grey share a body shape so they share
`EnemySoldier.prefab`; the planned tanks do not, and `ObjectPool<T>` is per-prefab by
construction, so a tank needs a second pool. This constructor takes a single
`ObjectPool<Enemy>`, so that slice reshapes the factory (pool-per-prefab, keyed by definition)
rather than only adding assets. Recorded here because today's one-prefab authoring is exactly
what defers it.
