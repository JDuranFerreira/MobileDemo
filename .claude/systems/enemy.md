# Enemy

The pooled enemy, its micro state machine, the path it walks and the data that drives it —
[ARCHITECTURE.md §5](../ARCHITECTURE.md)'s `Enemy` row plus §4's second state machine. This is
the system §13's vertical slice exists to prove.

> **This guide is over CLAUDE.md's ~150-line line, and the rule says to say so.** The overrun is
> the guide's *scope*, not the system's: it covers four types — `Enemy`, its three states,
> `EnemyPath` and `EnemyDefinition` — because §11 deliberately keeps them in one folder as one
> system. Splitting `EnemyPath` into its own guide would contradict §11's named trigger for
> splitting its *file*, which is the first non-enemy consumer of the path and has not happened.
> The trigger to split this document is that same one.

## Responsibility

Walk a path, take damage, and announce how it left the board — leaked or killed.

It deliberately does **not**:

- **Place itself, or decide when it dies.** `EnemyFactory` configures it; the driver releases it.
  See [enemy-factory.md](enemy-factory.md).
- **Decide what damaged it.** `TakeDamage(int)` takes an amount and nothing else. Which tower or
  projectile it came from is not modelled, because nothing needs it.
- **Subscribe to anything.** It publishes and listens to nothing, which is why it has no
  `OnEnable`/`OnDisable` at all.
- **Own its own `Update`.** It is ticked by the composition root. §9 records that decision and
  its cost.
- **Know about towers, waves or phases.** [`Tower`](tower.md) finds *it*, through
  [`EnemyRegistry`](enemy-registry.md); the dependency points one way only.

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
| Publishes | `EnemyLeaked` **or** `EnemyKilled` (from `EnemyDyingState.Enter`, one branch each) → [`Economy`](economy.md) |
| Subscribes | **nothing** |
| Created by | [`EnemyFactory`](enemy-factory.md), which calls `Configure` in the statement after `pool.Get()` |
| Tracked, ticked and released by | [`EnemyRegistry`](enemy-registry.md), on `IsFinished` |
| Damaged by | [`Projectile`](projectile.md), directly or through the registry's splash |
| Targeted by | [`Tower`](tower.md), via `IsTargetable` and `Position` |
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
version is a contract that one decorative child silently breaks, and `PathEditor` needs an explicit
array to edit anyway. **That second clause was written speculatively and turned out to be the
load-bearing one:** the tool renames children to match their index, so the names track the array
rather than the array tracking the names — see [path-editor.md](path-editor.md).

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
  already walking. Acceptable because waypoints are authored, not animated. (Baking is for the
  decoupling that keeps `Enemy` testable, *not* for a measured perf win — 64 `Transform` reads a
  frame would not have justified it alone.)

  **That named trigger has fired.** It said "`PathEditor` must call `EnemyPath.Bake()` after moving
  a handle", and [path-editor.md](path-editor.md) does exactly that — but the tool found something
  the trigger did not anticipate: the call has to be **guarded**, not unconditional. A path being
  briefly invalid is the normal state of one being authored, and `Bake` logs an error and empties
  itself under two waypoints, so an unguarded re-bake would spam the console during an edit and
  throw outright on an empty array slot. The stale cache is the *correct* state to leave behind
  mid-edit.
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
- **`maxHealth` and `currencyReward` now have readers**, so §7's authored-but-unread note is
  discharged: `Configure` seeds `CurrentHealth` from the first and `EnemyDyingState` pays out the
  second. `CurrentHealth` is reset in `Configure` and cleared in `OnDespawn` — the pooling reset
  that `OnDespawn_ThenOnSpawn_ThenConfigure_RestoresFullHealth` pins. A recycled enemy that kept
  its last life's health would die to a single hit, deep into a wave.
- **`TakeDamage` is gated on `IsTargetable`, and that gate is doing two jobs.** It is what makes
  the spawn window a gameplay rule rather than a cosmetic delay — a just-placed enemy cannot be
  shot. It is *also* what stops a double kill: two projectiles landing in the same frame would
  otherwise both drive health below zero, both transition to `Dying`, and pay the reward twice for
  one enemy.
- **`IsTargetable` is `current != null && current == moving`, and the null check is not
  redundant.** Before `Initialize` runs, both fields are null and `current == moving` is *true*,
  so an uninitialized enemy reported itself shootable. Play mode hides this because `Awake` always
  runs; a pooled instance between `Get()` and `Configure()`, and every EditMode test, does not.
  Found by a test, not by the run — §13.1 records it.
- **The death *cause* is stashed on the enemy, not passed to `Enter()`.** `IGameState.Enter` takes
  no argument, and widening it for one state would cost every other state a parameter it ignores.
  So `EnterDying(cause)` sets `DeathCause` and then swaps. One state with a branch, rather than
  two states — both exits are terminal and both end in `MarkFinished`, so only the announcement
  differs.
- **Still no `Collider2D` on the prefab**, and the tower slice did *not* add one: targeting is a
  distance query over `EnemyRegistry`, and splash is the same. Physics would have bought
  tunnelling bugs and a fixed-timestep dependency for nothing. object-pool.md's stale-transform
  gotcha therefore stays theoretical a while longer.
- **`current?.Tick(dt)` uses `?.` safely.** `IGameState` is a plain interface, not a
  `UnityEngine.Object`, so the destroyed-object trap `.editorconfig` holds at `suggestion` does
  not apply here. The surrounding codebase carries the opposite advice, hence the inline comment.

## Status

**Implemented, tested and run.** `Prefabs/EnemySoldier.prefab`, both definition assets and
`Level_01`'s ten waypoints are authored, and enemies have been seen spawning, walking the
switchback, and — since §13.1 — dying to tower fire before they reach the end. The EditMode tests
in [EnemyTests.cs](../../Assets/Tests/EditMode/EnemyTests.cs) cover placement, the spawn-delay
window, movement rate, the single `EnemyLeaked`, `IsFinished`, the inert `Get`-to-`Configure`
gap, and the pooling reset — `OnDespawn → OnSpawn → Configure` restarting from waypoint 0, which
is the one that proves the State pattern is paying for itself — plus the health half: damage,
the kill payout, the clamp at zero, the spawn-window immunity, `IsTargetable` across all four
states, and health restored across a pool cycle.

`EnemyPath` is deliberately untested: ~15 lines of bake and indexing, and reaching its
`[SerializeField] Transform[]` needs reflection or `SerializedObject` — a seam bought for trivial
code, when avoiding exactly that seam for the code that *matters* is why `Configure` takes
`IReadOnlyList<Vector2>`. Its one realistic failure has a `Debug.LogError` instead.

**That decision stands, and §13.5 changed what it costs rather than reversing it.** `Bake`'s
minimum-count guard checks the array's *length*, not its entries, so a filled array holding one
empty slot passes the guard and then throws a `NullReferenceException` — a second realistic failure
with neither a test nor a log behind it. It is now caught where it is actually created, in
[path-editor.md](path-editor.md)'s inspector, rather than by a fixture reaching into a private
field. Catching an authoring mistake at authoring time beats asserting it at test time.

The tower slice discharged everything this section used to list as pending — health,
`TakeDamage`, `EnemyKilled` on the kill branch — and it *was* the small change the single publish
site promised. §10's sprite atlas now exists too.

What is still pending: a second enemy **prefab** (the planned tanks, which cannot share the
soldier silhouette and therefore need a second pool — see [enemy-factory.md](enemy-factory.md)),
and a hit flash and death effect (PrimeTween, §15).

`EnemyGreySoldier` was the third item here and it has been spawned since §13.3. **§13.5 made it the
map-3 majority** — waves 9 to 12 are more grey than green, which is the first time this definition
carries a map rather than garnishing one. Its `damageOnLeak = 2` is why map 3 is the only map that
has ever cost the player a life in a winning run.
