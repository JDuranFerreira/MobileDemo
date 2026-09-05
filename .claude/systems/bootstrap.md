# Bootstrap

The composition root for [ARCHITECTURE.md §13](../ARCHITECTURE.md)'s vertical slice. **Explicitly
transitional** — most of what it does has a named successor, listed under Status.

> **Over CLAUDE.md's ~150-line line, and here the rule is diagnosing correctly rather than
> misfiring.** This class really is doing too much: it composes, validates, spawns, orders five
> tick calls and reports pool statistics. The difference from an accidental god object is that
> every one of those jobs already has a named owner waiting in the Status table, and the tower
> slice moved one out (the live-enemy loop, now [`EnemyRegistry`](enemy-registry.md)) rather than
> adding to the pile. If this guide grows again without that table shrinking, the rule is right
> and the class needs splitting.
>
> **That has now happened, so the trigger is armed rather than hypothetical.** The build slice
> added three serialized references, a fifth tick call and a second composition point in `Start`,
> while the Status table lost **nothing** — `BuildController` took the build phase's jobs, but the
> tick order, the spawn timer and the single level reference are all still here. The next slice
> (phases) is the one that has to take the tick order out, and if it does not, this class should be
> split whether or not the successors have arrived.

## Responsibility

Assemble the slice and drive it: read `GameConfig`, set the frame rate, build the pools, the
factories, the economy, the enemy registry, the input service and the build controller, configure
the level's towers, spawn on a timer, and drive one tick per frame in the right order.

It deliberately does **not**:

- **Become a Service Locator or a DI container.** §15 declines both by name. It is a `sealed`
  MonoBehaviour with serialized references and no static `Instance`, so nothing can reach into it.
- **Know that the UI exists.** It names no `MobileDemo.UI` type, because `HudPresenter`
  subscribes itself. That is §3's rule paying off concretely, and it is why the composition root
  does not need to sit in a fifth assembly above UI.
- **Model phases.** There is no `GameStateMachine` here. See Gotchas.
- **Touch the pools after construction.** [`EnemyFactory`](enemy-factory.md) and
  [`ProjectileFactory`](projectile.md) own them privately.
- **Own the live-enemy list any more.** [`EnemyRegistry`](enemy-registry.md) does, since three
  systems need it. `Bootstrap` constructs it and hands it out.
- **Place towers.** Two are authored inside the level prefab and the rest come from
  [`BuildController`](build-controller.md); `Bootstrap` only configures and ticks whatever
  [`Level.Towers`](level.md) exposes. It does not hold the live list either.
- **Decide anything about a tap.** It constructs the input service and the build controller and
  calls `Tick`. Where a tower may go is [`PlacementRules`](build-controller.md)'.
- **Stop at zero lives.** The defeat check is §4's job.
- **Gate building on a phase.** There are no phases, so building is always allowed. See Gotchas.

## Key types

| Type | File | Role |
|---|---|---|
| `Bootstrap` | [Bootstrap.cs](../../Assets/Scripts/Gameplay/Bootstrap.cs) | The root. `Awake` composes, `Start` announces, `Update` drives. |

Namespace `MobileDemo.Gameplay`, assembly `MobileDemo.Gameplay`. At the assembly root rather than
in a subfolder — it is not a system in the `Enemies/`/`Phases/` sense, it is the seam that
assembles them, which is the same reason `HudPresenter.cs` sits at the UI root.

## Patterns used

**None, deliberately.** It is plain construction in `Awake`. That is worth stating because a
composition root is exactly where a demo is tempted to show off a container, and §15 has already
argued that a DI framework at this scale "reads as cargo-culting, not competence".

The one design decision it does carry is the **driven tick** — see Gotchas and §9.

## Collaborators

| Direction | With |
|---|---|
| Constructs | [`Economy`](economy.md), [`EnemyFactory`](enemy-factory.md), [`ObjectPool<Enemy>`](object-pool.md), [`EnemyRegistry`](enemy-registry.md), [`ProjectileFactory`](projectile.md), [`TowerFactory`](tower-factory.md), [`PointerInputService`](input-service.md), [`PlacementRules`](build-controller.md), [`BuildController`](build-controller.md) |
| Calls | `Economy.Subscribe`/`Unsubscribe`/`PublishCurrentState`, `BuildController.Subscribe`/`Unsubscribe`/`Tick`, `EnemyFactory.Create`, `EnemyRegistry.Add`/`Tick`, `TowerFactory.Configure`, `Tower.Tick`, `ProjectileFactory.Tick` |
| Reads | `GameConfig`, `EnemyDefinition`, `TowerCatalogue`, [`Level`](level.md) — and through it the [`EnemyPath`](enemy.md), its map bounds and its [`Tower` list](tower.md) |
| Events | **none of its own.** It raises nothing and subscribes to nothing; it only makes `Economy` and `BuildController` do so at the right moments |

Depends on `MobileDemo.Core.Config`, `.Interfaces`, `.Pooling`, `MobileDemo.Gameplay.Build`,
`.Enemies`, `.Levels`, `.Towers`, `System.Collections.Generic` and `UnityEngine`.

## Data

**Nine** serialized scene references. Six from earlier slices — `config`, `enemyPrefab`,
`enemyDefinition`, `level`, `poolParent`, `spawnIntervalSeconds` — and three from the build slice:
`sceneCamera`, `towerPrefab` and `catalogue`.

The tower slice cost this component **no** new wiring, because towers and projectile prefabs
arrived through `Level` and `TowerDefinition` — the claim [level.md](level.md) made when `Level`
was introduced, tested. **The build slice cost three, and each is a genuinely new kind of thing**
rather than a leak: a `Camera` (the input service needs one to convert a tap, and
`Camera.main` is a tag search), the `Tower` prefab (nothing else knew it — the authored towers are
prefab *instances*, not a prefab reference), and the catalogue (which types are buildable is
content that did not previously exist anywhere).

It holds a `Level` rather than an `EnemyPath` because a level is what gets swapped (§1) — so the
wave slice can add `WaveDefinition[]` to [`Level`](level.md) without touching any wiring here.
`poolParent` stays a *separate* reference for the opposite reason: it must **not** be inside the
level, or a swap would destroy the pool.

`spawnIntervalSeconds` is a **scene** value and deliberately *not* on `GameConfig`: it is wave
data, not a global constant, and §2's list does not contain it. It is the field
`WaveDefinition`'s `{ EnemyDefinition, count, spawnInterval }` group absorbs.

## Gotchas

- **`Awake` composes, `Start` announces.** Unity guarantees every `Awake` and `OnEnable`
  completes before the first `Start`, so publishing the opening lives value from `Start` is what
  makes the HUD deterministic instead of racing two GameObjects' `Awake`. Moving that publish
  into `Economy`'s constructor silently blanks the label. **There is now a label on the other end
  to blank**, so this stopped being a prediction: §13's HUD run watched it count down from the
  opening value, which is the ordering working.
- **The path is never read before `Start`.** `EnemyPath` bakes in its own `Awake`, and Unity does
  not order `Awake` between GameObjects. One line of discipline in one file, chosen over a
  lazy-bake guard on `EnemyPath`.
- **`Start` refuses to run against an unusable path.** It checks both `level.Path == null` (the
  reference forgotten *inside* the prefab — a failure `Awake`'s field checks cannot see) and a
  waypoint array with fewer than two entries, which `EnemyPath.Awake` has already logged the
  reason for. The check is what stops that one message becoming an `IndexOutOfRangeException`
  every two seconds. Same for the five unassigned-reference checks in `Awake`: five
  `Debug.LogError`s and `enabled = false`, instead of a per-frame `NullReferenceException`
  forever. The checks do not short-circuit, so all missing references are reported in one run.
- **`Application.targetFrameRate` is silently ignored when
  `QualitySettings.vSyncCount != 0`.** This project runs quality level 0 (`vSyncCount: 0`) so
  §10's table row holds today — but Medium and above ship `vSyncCount: 1`, so changing quality
  level voids it with no error. Not worth defensive code; worth knowing.
- **`poolParent`'s scale must be exactly `(1,1,1)`, and it must not live inside a level
  prefab.** The scale half is object-pool.md's gotcha: `Instantiate(prefab, parent)` is not
  world-space, so a scaled parent silently scales every enemy. The parentage half is new with §1's
  three maps: pooled enemies outlive a level swap, and a pool whose instances were destroyed with
  the old level has no way to notice. Between them these are the two scene values most likely to
  produce a baffling bug if got wrong.
- **`Awake` composes, `Start` composes again — and the split is load-bearing.** Most of the graph
  is built in `Awake`, but `PlacementRules` and `BuildController` are built in `Start`, because
  `PlacementRules` reads the baked waypoints and `EnemyPath` bakes them in *its* `Awake`, which
  Unity does not order against this one. Same one-line discipline as the path gotcha above, second
  beneficiary.
  - **The consequence that is easy to get wrong:** `OnEnable` runs *before* the first `Start`, so
    `build?.Subscribe()` there is a no-op on the first cycle. `Start` therefore subscribes
    explicitly after constructing, and `OnEnable` covers every *later* enable. Removing either
    leaves the build menu's buttons silently doing nothing, or double-subscribed.
- **Tick order is build → spawner → enemies → towers → projectiles, and it is not arbitrary.**
  Enemies move first, towers then scan the positions they moved to, and projectiles fly at those
  same positions; ticking towers first would aim every shot one frame stale. Building leads so a
  tower added or removed this frame is settled before anything iterates the list.
  `build.Tick()` takes no `dt` — nothing in it is time-based, and a parameter with no reader is
  what §2's discipline rejects.
- **`TickTowers` runs backwards now**, because the list can change during a round. Same cheap
  insurance the enemy loop already took.
- **Building is never gated, so §10's allocation budget is broken on purpose.** One `Instantiate`
  per player tap during what is nominally a wave. It is bounded, recorded in §10, and it repairs
  itself with no code change when `BuildState` lands and stops calling `build.Tick()` — which is
  §9's "pausing is free" argument paying off a second time. A `bool buildingAllowed` that nothing
  sets would have been worse than the honest violation.
- **The enemy list is ticked backwards**, because a finished enemy is removed as we go. Index
  loop, no `foreach`, no LINQ (§10). That loop now lives in `EnemyRegistry`; the ten lines moved
  rather than multiplied.
- **Projectile prefabs are collected *before* the factory is built**, so every pool is prewarmed at
  construction. A pool created lazily on the first shot would allocate its whole prewarm mid-wave.
- **That collection must union the level's towers with the *catalogue*, and forgetting the second
  half is silent.** `ProjectileFactory` refuses to build a pool after construction, and `Create` on
  an unknown prefab logs an error and returns null — so a tower the **player** places whose
  projectile was never collected fires nothing, with no error at the moment of placement. The
  level's own towers cover only what was authored; `TowerCatalogue.CollectProjectilePrefabs` covers
  what can be built. See [tower-factory.md](tower-factory.md).
- **`Start` refuses to run against a level with no map bounds**, the same shape as the path check
  and for the same reason: with runtime placement, a level you cannot build on is half a game, and
  one legible error beats a board that silently rejects every tap.
- **`OnDestroy` logs every pool, not just the enemy one** — three lines now, and the projectile
  figures are the ones §2 uses to record that `PROJECTILE_POOL_PREWARM` is oversized.
- **`Bootstrap` ticks the enemies rather than each `Enemy` owning an `Update`** — recorded as a
  decision in §9. Short form: §9's own vocabulary is `Tick`; a driven tick is callable from an
  EditMode test with an explicit `dt` where `Update` is not callable at all (and there is no
  PlayMode assembly, §12); pausing is free, which `BuildState` will need; and the order is
  deterministic. The honest cost is the `List<Enemy>` and ~10 lines of bookkeeping that `Update`
  would have given for free — the pool cannot supply that list without becoming a registry.
- **Spawning repeats forever rather than spawning literally one enemy**, though §13 step 2 says
  "gets one enemy". A repeating spawn is what actually *proves* recycling: `PoolRoot`'s child
  count holds at the prewarm figure while one child toggles active.
- **Prewarm figures are left disagreeing with their measurements on purpose.** The enemy pool
  runs at 30 against §2's stated 64 and peaks at 5; each projectile pool prewarms 128 and peaks at
  1. §10's guidance is to tune to `PeakActive` after a *full wave* across all three maps, and
  there is neither a wave nor a second map yet, so retuning now would be tuning to the wrong
  measurement. `OnDestroy` logs all three so they can be read after a run.

## `GameStateMachine` is deliberately absent

There are no phases: no build phase, no wave, no victory or defeat. So `PhaseChanged` has no
publisher and `GamePhase` sits unused in Core. A `GameStateMachine` with one state demonstrates
nothing and would have to be deleted and rewritten once real phases define real states.
`Bootstrap.Update` is the placeholder, and saying so here is what keeps this class from quietly
becoming the god object.

## Status

**Implemented, wired, and transitional by design.** Three full runs are recorded — two in §13 and
one in §13.1, the last with towers firing, currency climbing and all three pools holding at their
prewarm.

**The build slice's three new references are authored**, along with the `EventSystem` and the build
menu, and the scene has been played since: currency climbed on kills and no pool grew. The one
thing that session could not do was deliver a tap — see [build-controller.md](build-controller.md).

`enemyDefinition` is wired to `EnemyGreenSoldier`. `EnemyGreySoldier` is authored but **unspawned**,
because this field is a single reference rather than a list — deliberately, since `WaveRunner`'s
`{ EnemyDefinition, count, spawnInterval }` group is what will choose between definitions, and
growing an array here would be building that type's job into its placeholder.

Each temporary job has a named successor:

| Job here today | Successor |
|---|---|
| Driving the per-frame tick | `GameStateMachine` (§4) |
| The spawn timer and `spawnIntervalSeconds` | `WaveRunner` + `WaveDefinition` (§5, §7) |
| Constructing and ticking `EnemyRegistry` | `WaveRunner` — it owns the registry outright, which is why the registry is a plain class |
| Configuring the level's towers | `LevelRunner`, which will do it on every swap |
| Holding the single `level` reference | `LevelRunner`, which instantiates and swaps the three (§4) |
| Calling `build.Tick()` unconditionally | `BuildState`, which pauses it by not calling it (§9) |
| Constructing `PlacementRules` | `LevelRunner`, which will build a fresh one per level |
| Logging `PeakActive` on shutdown | the editor Pool Overlay (§12, §15) |

**This table gained two rows and lost none**, which is the diagnosis the note at the top of this
guide asks for. Every job still has a named owner, so the class is not yet an accident — but the
next slice must actually move some, not add more.

**Deliberately untested**, and more so than before: extracting the tick loop into
`EnemyRegistry` moved the one piece of real logic it had somewhere testable, leaving wiring and
ordering. Its whole job is now assembling an asset, a prefab, a level and a Canvas; a test would
have to build all four, at which point it is testing Unity's serialization. `targetFrameRate`, the spawn cadence, the release loop and the
`Awake`/`OnEnable`/`Start` ordering are all Play-Mode behaviour, and there is no PlayMode
assembly (§12). It is certified by running the scene with a clean console — which is what §10
already nominates as the certification for its allocation budget.
