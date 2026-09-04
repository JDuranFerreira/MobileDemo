# Bootstrap

The composition root for [ARCHITECTURE.md §13](../ARCHITECTURE.md)'s vertical slice. **Explicitly
transitional** — most of what it does has a named successor, listed under Status.

## Responsibility

Assemble the slice and drive it: read `GameConfig`, set the frame rate, build the pool, the
factory and the economy, spawn on a timer, tick the live enemies, release the finished ones.

It deliberately does **not**:

- **Become a Service Locator or a DI container.** §15 declines both by name. It is a `sealed`
  MonoBehaviour with serialized references and no static `Instance`, so nothing can reach into it.
- **Know that the UI exists.** It names no `MobileDemo.UI` type, because `HudPresenter`
  subscribes itself. That is §3's rule paying off concretely, and it is why the composition root
  does not need to sit in a fifth assembly above UI.
- **Model phases.** There is no `GameStateMachine` here. See Gotchas.
- **Touch the pool after construction.** [`EnemyFactory`](enemy-factory.md) owns it privately.
- **Stop at zero lives.** The defeat check is §4's job.

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
| Constructs | [`Economy`](economy.md), [`EnemyFactory`](enemy-factory.md), [`ObjectPool<Enemy>`](object-pool.md) |
| Calls | `Economy.Subscribe`/`Unsubscribe`/`PublishCurrentState`, `EnemyFactory.Create`/`Release`, `Enemy.Tick` |
| Reads | `GameConfig`, `EnemyDefinition`, [`Level`](level.md) — and through it the [`EnemyPath`](enemy.md) |
| Events | **none of its own.** It raises nothing and subscribes to nothing; it only makes `Economy` do so at the right moments |

Depends on `MobileDemo.Core.Config`, `.Pooling`, `MobileDemo.Gameplay.Enemies`,
`System.Collections.Generic` and `UnityEngine`.

## Data

Six serialized scene references: `config`, `enemyPrefab`, `enemyDefinition`, `level`,
`poolParent`, and `spawnIntervalSeconds`.

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
- **The enemy list is ticked backwards**, because a finished enemy is removed as we go. Index
  loop, no `foreach`, no LINQ (§10).
- **`Bootstrap` ticks the enemies rather than each `Enemy` owning an `Update`** — recorded as a
  decision in §9. Short form: §9's own vocabulary is `Tick`; a driven tick is callable from an
  EditMode test with an explicit `dt` where `Update` is not callable at all (and there is no
  PlayMode assembly, §12); pausing is free, which `BuildState` will need; and the order is
  deterministic. The honest cost is the `List<Enemy>` and ~10 lines of bookkeeping that `Update`
  would have given for free — the pool cannot supply that list without becoming a registry.
- **Spawning repeats forever rather than spawning literally one enemy**, though §13 step 2 says
  "gets one enemy". A repeating spawn is what actually *proves* recycling: `PoolRoot`'s child
  count holds at the prewarm figure while one child toggles active.
- **Prewarm stays at §2's shipped 64** even though this slice has a handful of enemies alive.
  §10's guidance is to tune to `PeakActive` after a *full wave*, and there is no wave yet, so
  retuning now would be tuning to the wrong measurement. `OnDestroy` logs the figure so it can be
  read after a run.

## `GameStateMachine` is deliberately absent

There are no phases: no build phase, no wave, no victory or defeat. So `PhaseChanged` has no
publisher and `GamePhase` sits unused in Core. A `GameStateMachine` with one state demonstrates
nothing and would have to be deleted and rewritten once real phases define real states.
`Bootstrap.Update` is the placeholder, and saying so here is what keeps this class from quietly
becoming the god object.

## Status

**Implemented, wired, and transitional by design.** It has now run: the `GameConfig` asset, the
`EnemySoldier` and `Level_01` prefabs and all six serialized references are authored on
`Gameplay.unity` (renamed from `SampleScene.unity` when §13 closed), and two full runs are
recorded in §13 — enemies spawning every 2 s, recycling through a pool that never grew past its
prewarm, and a HUD label counting down from the value `Start` announces.

`enemyDefinition` is wired to `EnemyGreenSoldier`. `EnemyGreySoldier` is authored but **unspawned**,
because this field is a single reference rather than a list — deliberately, since `WaveRunner`'s
`{ EnemyDefinition, count, spawnInterval }` group is what will choose between definitions, and
growing an array here would be building that type's job into its placeholder.

Each temporary job has a named successor:

| Job here today | Successor |
|---|---|
| Driving the per-frame tick | `GameStateMachine` (§4) |
| The spawn timer and `spawnIntervalSeconds` | `WaveRunner` + `WaveDefinition` (§5, §7) |
| Owning `List<Enemy> live` | `WaveRunner` — the same ten lines, inherited |
| Holding the single `level` reference | `LevelRunner`, which instantiates and swaps the three (§4) |
| Logging `PeakActive` on shutdown | the editor Pool Overlay (§12, §15) |

**Deliberately untested.** Its whole job is wiring an asset, a prefab, two scene components and a
Canvas together; a test would have to build all four, at which point it is testing Unity's
serialization. `targetFrameRate`, the spawn cadence, the release loop and the
`Awake`/`OnEnable`/`Start` ordering are all Play-Mode behaviour, and there is no PlayMode
assembly (§12). It is certified by running the scene with a clean console — which is what §10
already nominates as the certification for its allocation budget.
