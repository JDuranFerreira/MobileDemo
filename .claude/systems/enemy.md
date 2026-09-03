# Enemy

The pooled enemy, its micro state machine, the path it walks and the data that drives it —
[ARCHITECTURE.md §5](../ARCHITECTURE.md)'s `Enemy` row plus §4's second state machine. This is
the system §13's vertical slice exists to prove.

## Responsibility

Walk a path, and announce that it reached the end.

It deliberately does **not**:

- **Place itself, or decide when it dies.** `EnemyFactory` configures it; the driver releases it.
  See [enemy-factory.md](enemy-factory.md).
- **Carry health.** Nothing can damage an enemy in this slice, so there is no `currentHealth`
  field. `EnemyDefinition` *does* carry `maxHealth`, and the asymmetry is deliberate — see
  Gotchas.
- **Subscribe to anything.** It publishes `EnemyLeaked` and listens to nothing, which is why it
  has no `OnEnable`/`OnDisable` at all.
- **Own its own `Update`.** It is ticked by the composition root. §9 records that decision and
  its cost.
- **Know about towers, waves or phases.** None exist yet.

## Key types

| Type | File | Role |
|---|---|---|
| `Enemy` | [Enemy.cs](../../Assets/Scripts/Gameplay/Enemies/Enemy.cs) | The pooled `MonoBehaviour`. `Configure`, `Tick`, `IsFinished`, `IPoolable`. |
| `EnemySpawningState`, `EnemyMovingState`, `EnemyDyingState` | [EnemyStates.cs](../../Assets/Scripts/Gameplay/Enemies/EnemyStates.cs) | The three `IGameState`s. All internal. |
| `EnemyPath` | [EnemyPath.cs](../../Assets/Scripts/Gameplay/Enemies/EnemyPath.cs) | Scene-authored waypoints, baked to a shared `Vector2[]`. |
| `EnemyDefinition` | [EnemyDefinition.cs](../../Assets/Scripts/Gameplay/Enemies/EnemyDefinition.cs) | The §7 tuning asset. |

Namespace `MobileDemo.Gameplay.Enemies`, assembly `MobileDemo.Gameplay`.

`EnemyPath.cs` and `EnemyStates.cs` both sit in `Enemies/` rather than earning folders of their
own; §11 records why, and the trigger that would move `EnemyPath`.

## Patterns used

**State**, reusing §4's `IGameState` verbatim rather than declaring a narrower interface. The
reuse is the point §4 is making ("reusing the shape shows the pattern generalises"), and the
price is three empty `Exit()` bodies — `Exit` earns its keep in the round's machine, where
`BuildState.Exit()` disables the build UI.

**Why classes and not an enum plus a `switch`,** which would be ~30 lines shorter: because
`EnemyMovingState` owns its own `waypointIndex` and re-zeroes it in `Enter()`, so **the pooling
reset for the path cursor is a consequence of the pattern rather than another line in
`OnSpawn`**. That is the concrete argument, not "State is a good pattern".

**Object Pool**, client side — `Enemy` is the project's first `IPoolable`. See Gotchas for what
that turned out to require.

**Not** an interface for the path: `Configure` takes `IReadOnlyList<Vector2>`, a BCL type, rather
than an `IEnemyPath` with one production implementation and one test fake — which §6 calls
architecture cosplay by name. The array-to-interface conversion buys the same testability with
zero new types, and makes "enemies must not mutate the shared path" structural.

## Collaborators

| Direction | What |
|---|---|
| Publishes | `EnemyLeaked` (from `EnemyDyingState.Enter`) → [`Economy`](economy.md) |
| Subscribes | **nothing** |
| Created by | [`EnemyFactory`](enemy-factory.md), which calls `Configure` in the statement after `pool.Get()` |
| Ticked and released by | [`Bootstrap`](bootstrap.md), on `IsFinished` |
| Pooled by | [`ObjectPool<Enemy>`](object-pool.md) |

Depends on `MobileDemo.Core.Events`, `.Interfaces` (`IGameState`), `.Pooling` (`IPoolable`),
`System.Collections.Generic` and `UnityEngine`. No package.

## Data

`EnemyDefinition` — `sprite`, `maxHealth`, `moveSpeed`, `currencyReward`, `damageOnLeak`,
`spawnDelaySeconds`. Two assets exist: `Assets/Data/EnemyGreenSoldier.asset` (the one `Bootstrap`
spawns) and `Assets/Data/EnemyGreySoldier.asset` (slower, tougher, worth more — authored and
pool-ready, but unspawned until `WaveRunner` can pick between definitions). Both drive the *same*
`Prefabs/EnemySoldier.prefab`, which is the point: a second enemy type cost an asset, not a type.

Field defaults (`moveSpeed = 1.5`, `damageOnLeak = 1`, `spawnDelaySeconds = 0.15`) are chosen so
`ScriptableObject.CreateInstance<EnemyDefinition>()` is a valid deterministic test fixture with
no seam. The tests assert **relatively** against those properties, so retuning the asset cannot
break them.

`EnemyPath` holds a serialized `Transform[]`, not "my children are my waypoints" — the implicit
version is a contract that one decorative child silently breaks, and §12's `PathEditor` needs an
explicit array to edit anyway.

**`EnemyPath` lives inside a level prefab, not loose in the scene.** §1's three maps each carry
their own path laid over their own art, so the path is per-level content and
[`Level`](level.md) is what exposes it. `Bootstrap` therefore holds a `Level`, not an
`EnemyPath` — see [bootstrap.md](bootstrap.md).

## Gotchas

- **`OnSpawn` cannot start the state machine.** The pool's order is `SetActive(true)` →
  `OnSpawn()` → `Get` returns, so the definition and the path arrive from the factory *after*
  `OnSpawn` has run. `EnemySpawningState.Enter()` reads `SpawnDelaySeconds`, so entering it from
  `OnSpawn` is a `NullReferenceException` on the first spawn and a stale timer on every later
  one. So: **`OnSpawn` resets pool-owned state only, and `Configure` starts the machine.** This
  is a genuine sharpening of [object-pool.md](object-pool.md)'s contract, recorded there too.
- **State instances are built once in `Initialize`, never in `OnSpawn`.** The prefab root is
  authored active, so prewarm's `Instantiate` runs `Awake` immediately and all 64 instances build
  their three states at load. Building them in `OnSpawn` — the tempting place, since it is the
  pool's "sole initializer" — would allocate 192 objects during a 64-enemy wave, which is the
  exact GC spike §2 calls pooling mandatory to avoid.
- **`Initialize()` is idempotent and called from `Configure` as well as `Awake`, and that is a
  test-shaped concession stated as one.** EditMode never sends `Awake`, so a test's
  `AddComponent<Enemy>()` would otherwise never initialize. It is one guard clause bought for
  §14's coverage — not defensive programming — and it also makes the code indifferent to whether
  `Instantiate` sends `Awake` at edit time, a version-dependent detail nothing should depend on.
- **`OnEnable`-before-`OnSpawn` does *not* bite here**, despite object-pool.md having predicted
  that it would. `Enemy` subscribes to nothing, so it has no `OnEnable`. The first real victim is
  still ahead: a pooled `Projectile` that subscribes, or an `Enemy` that ever needs to.
- **Never `Destroy` an enemy — `Release` it.** There is no `Destroy` call in this system at all;
  `IsFinished` plus the driver's release loop is the only exit path.
- **The path bake is a stale cache.** Moving a waypoint during Play does not affect enemies
  already walking. Acceptable because waypoints are authored, not animated. **Named trigger:**
  §12's `PathEditor` must call `EnemyPath.Bake()` after moving a handle. (Baking is for the
  decoupling that keeps `Enemy` testable, *not* for a measured perf win — 64 `Transform` reads a
  frame would not have justified it alone.)
- **A path needs at least two waypoints, and that is enforced at the edges, not re-checked in
  the tick.** `EnemyPath.Bake` logs a `Debug.LogError` and bakes empty; `Bootstrap.Start` then
  refuses to spawn, which is what stops one legible message becoming an
  `IndexOutOfRangeException` every two seconds. `EnemyMovingState.Tick` therefore assumes a valid
  cursor — arrival at the last waypoint transitions to `Dying` in the same tick, so it can never
  be re-entered out of range.
- **Movement uses plain `MoveTowards`, not a loop that consumes remaining distance across
  waypoints.** At 1.5 u/s and 60 FPS an enemy moves 0.025 u/frame, so it cannot skip a waypoint,
  and `MoveTowards` clamps exactly so `== target` needs no epsilon. The cost is losing one
  frame's movement at a corner, which is invisible. **Named trigger** for the loop version: high
  speeds, or a fast-forward feature.
- **`maxHealth` and `currencyReward` are authored but unread.** Deliberate, so the asset is
  authored once and completely and §7's list matches the code. The line *not* crossed is
  `Enemy.currentHealth`: a data knob a future system will read is a knob, but a field on a live
  object with no writer reads as working code.
- **No `Collider2D` on the prefab yet**, because nothing overlap-tests an enemy. When the tower
  slice adds one, object-pool.md's stale-transform gotcha goes live — which is exactly why
  "configure in the statement after `Get`" was established now, before it could bite.
- **`current?.Tick(dt)` uses `?.` safely.** `IGameState` is a plain interface, not a
  `UnityEngine.Object`, so the destroyed-object trap `.editorconfig` holds at `suggestion` does
  not apply here. The surrounding codebase carries the opposite advice, hence the inline comment.

## Status

**Implemented, tested and run.** `Prefabs/EnemySoldier.prefab`, both definition assets and
`Level_01`'s ten waypoints are authored, and enemies have been seen spawning, walking the
switchback and leaking — recycling through the pool with `InstanceCount` never leaving its
prewarm of 64 (§13 records the run). 10 EditMode tests in
[EnemyTests.cs](../../Assets/Tests/EditMode/EnemyTests.cs) cover placement, the spawn-delay
window, movement rate, the single `EnemyLeaked`, `IsFinished`, the inert `Get`-to-`Configure`
gap, and the pooling reset — `OnDespawn → OnSpawn → Configure` restarting from waypoint 0, which
is the one that proves the State pattern is paying for itself.

`EnemyPath` is deliberately untested: ~15 lines of bake and indexing, and reaching its
`[SerializeField] Transform[]` needs reflection or `SerializedObject` — a seam bought for trivial
code, when avoiding exactly that seam for the code that *matters* is why `Configure` takes
`IReadOnlyList<Vector2>`. Its one realistic failure has a `Debug.LogError` instead.

Pending the tower slice: health and `TakeDamage`, `EnemyKilled` in place of `EnemyLeaked` when
the cause is a kill (one publish site, so it is a small change), a collider, and a second enemy
type — which is also the trigger for §10's sprite atlas.
