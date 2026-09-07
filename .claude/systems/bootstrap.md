# Bootstrap

The composition root — [ARCHITECTURE.md §5](../ARCHITECTURE.md)'s last row, and the one meant to
shrink. One job still has a named successor, listed under Status; as of §13.4 that is three fewer
than after §13.3, which was itself four fewer than before.

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
>
> **§13.4 is the second slice running to shrink it.** `LevelRunner` took the three rows that named
> it — configuring a level's towers, holding the level reference, constructing `PlacementRules` —
> and what arrived in exchange was one field (`levelPrefabs`, replacing `level`) and one line of
> composition. The Status table is down to a single row whose successor is an editor tool.
>
> **§13.3 disarmed it.** The Status table lost four rows and gained none, `Update` is one line, and
> the class shed two serialized fields — the first time any slice has made this file smaller. The
> rule at the top of this guide was right and the class did not need splitting, because the
> successors arrived instead. What is left is composition, validation and a `Level` reference, all
> three of which belong to a composition root.

## Responsibility

Assemble the round and drive it: read `GameConfig`, set the frame rate, build the pools, the
factories, the economy, the enemy registry, the input service, the level runner, the build
controller, the wave runner and the phase machine, and call one `Tick` per frame.

**"In the right order" left this sentence in §13.3**, along with the spawn timer. The tick order
is [`BuildState`](game-state-machine.md)'s and `WaveState`'s now; `Update` is `machine.Tick(dt)`.

It deliberately does **not**:

- **Become a Service Locator or a DI container.** §15 declines both by name. It is a `sealed`
  MonoBehaviour with serialized references and no static `Instance`, so nothing can reach into it.
- **Know that the UI exists.** It names no `MobileDemo.UI` type, because `HudPresenter`
  subscribes itself. That is §3's rule paying off concretely, and it is why the composition root
  does not need to sit in a fifth assembly above UI.
- **Model phases.** It constructs [`GameStateMachine`](game-state-machine.md) and ticks it, and
  names no state class beyond the four it registers. Which phase follows which is the states' own.
- **Touch the pools after construction.** [`EnemyFactory`](enemy-factory.md) and
  [`ProjectileFactory`](projectile.md) own them privately.
- **Own the live-enemy list any more.** [`EnemyRegistry`](enemy-registry.md) does, since three
  systems need it. `Bootstrap` constructs it and hands it out.
- **Place towers.** Two are authored inside the level prefab and the rest come from
  [`BuildController`](build-controller.md); `Bootstrap` only configures and ticks whatever
  [`Level.Towers`](level.md) exposes. It does not hold the live list either.
- **Decide anything about a tap.** It constructs the input service and the build controller and
  calls `Tick`. Where a tower may go is [`PlacementRules`](build-controller.md)'.
- **Stop at zero lives.** The defeat check is `GameStateMachine`'s, which subscribes `LivesChanged`.
- **Gate building on a phase.** `BuildState` does, by being the only state that ticks
  `BuildController`. This class no longer calls `build.Tick()` at all.
- **Spawn.** [`WaveRunner`](wave-runner.md) does, from a `WaveDefinition` on the level.

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
| Constructs | [`Economy`](economy.md), [`EnemyFactory`](enemy-factory.md), [`ObjectPool<Enemy>`](object-pool.md), [`EnemyRegistry`](enemy-registry.md), [`ProjectileFactory`](projectile.md), [`TowerFactory`](tower-factory.md), [`PointerInputService`](input-service.md), [`LevelRunner`](level-runner.md), [`BuildController`](build-controller.md), [`WaveRunner`](wave-runner.md), [`GameStateMachine`](game-state-machine.md) and its four states |
| Calls | `Economy.Subscribe`/`Unsubscribe`/`PublishCurrentState`, `BuildController.Subscribe`/`Unsubscribe`, `GameStateMachine.Add`/`Subscribe`/`Unsubscribe`/`Change`/`Tick`/`Shutdown`, `LevelRunner.Load` and the static `LevelRunner.CollectProjectileDefinitions` |
| Reads | `GameConfig`, `TowerCatalogue`, the level prefabs — and through [`LevelRunner`](level-runner.md) the live [`Level`](level.md), its [`EnemyPath`](enemy.md), its map bounds, its [`Tower` list](tower.md) and its wave sequence |
| Events | **one, and it is new.** It subscribes `RestartRequested` and reloads the scene. It still raises nothing, and still only makes `Economy`, `BuildController` and the machine subscribe at the right moments |

Depends on `MobileDemo.Core.Config`, `.Events`, `.Interfaces`, `.Pooling`,
`MobileDemo.Gameplay.Build`, `.Enemies`, `.Levels`, `.Phases`, `.Towers`, `.Waves`,
`System.Collections.Generic`, `UnityEngine` and `UnityEngine.SceneManagement`.

**`EnemyFactory.Create` and `EnemyRegistry.Add` left the Calls row**, which is the spawn timer
going. It still *constructs* both and hands them to `WaveRunner`.

## Data

**Seven** serialized scene references: `config`, `enemyPrefab`, `levelPrefabs`, `poolParent`,
`sceneCamera`, `towerPrefab`, `catalogue`.

**`level` became `levelPrefabs` in §13.4, and the scene lost its authored level entirely.** Every
map — including the first — is instantiated by [`LevelRunner`](level-runner.md), so map 1 is not a
special case of the swap that only the swap exercises. The count did not change: one reference to a
scene instance became one reference to an array of prefabs.

**That is two fewer than before, and the first time this list has shrunk.** `enemyDefinition` and
`spawnIntervalSeconds` both went to `WaveDefinition`, which is what the field comment on the second
of them predicted from the day it was written. The first was a single reference rather than a list
*precisely* so that growing it would not build `WaveRunner`'s job into its own placeholder — and
because it was never grown, `EnemyGreySoldier` had no spawner for three slices. It has one now.

The tower slice cost this component **no** new wiring, because towers and projectiles
arrived through `Level` and `TowerDefinition` — the claim [level.md](level.md) made when `Level`
was introduced, tested. **The build slice cost three, and each is a genuinely new kind of thing**
rather than a leak: a `Camera` (the input service needs one to convert a tap, and
`Camera.main` is a tag search), the `Tower` prefab (nothing else knew it — the authored towers are
prefab *instances*, not a prefab reference), and the catalogue (which types are buildable is
content that did not previously exist anywhere).

It holds levels rather than an `EnemyPath` because a level is what gets swapped (§1) — and that
prediction was tested twice and held both times: §13.3 added `WaveDefinition[]` to
[`Level`](level.md) and §13.4 added two more maps, and neither cost this component new wiring beyond
the array itself. `poolParent` stays a *separate* reference for the opposite reason: it must **not**
be inside a level, or a swap would destroy the pool — which §13.4 turned from a warning into
something the game does twice per run.

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
- **`Start` refuses to run against an unusable path.** It reads the live level from the runner and
  checks both `level.Path == null` (the
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
- **The tick order left this class in §13.3**, and the ordering argument went with it rather than
  being lost: `WaveState` ticks enemies, then towers, then projectiles, for the reason that was
  always given — towers must scan the positions enemies moved to, and ticking them first would aim
  every shot one frame stale. **"Building goes first of all" is gone and is not an omission.** It
  existed so a tower added or removed this frame was settled before anything iterated the list;
  build and combat now run in different phases and so never in the same frame, which is a stronger
  guarantee than the ordering was.
- **Building is gated now, and §10's allocation budget is repaired.** The violation this guide used
  to record — one `Instantiate` per player tap during what was nominally a wave — is gone, and it
  went exactly as §9 predicted: `BuildState` stops it by not calling `build.Tick()`, with no `bool
  buildingAllowed` and no change to `BuildController` at all. That is the driven tick's "pausing is
  free" argument paying its third and largest dividend.
- **`Update` is now a single call and `OnDisable` grew instead.** `machine?.Shutdown()` is there
  for a hazard nothing else covers: `BuildState` subscribes in `Enter` and unsubscribes in `Exit`,
  and a scene teardown — which a restart is — never reaches `Exit`. With domain reload off, the
  bus's statics survive a scene load, so a dead state would stay subscribed holding a destroyed
  `Level`. See [game-state-machine.md](game-state-machine.md).
- **`OnRestartRequested` reloads the scene rather than resetting each system.** Everything is
  rebuilt in `Awake`, so a `Reset` on `Economy`, the pools, the registry and the level's tower list
  would be five methods that exist only for this, each able to forget a field.
- **The enemy list is ticked backwards**, because a finished enemy is removed as we go. Index
  loop, no `foreach`, no LINQ (§10). That loop now lives in `EnemyRegistry`; the ten lines moved
  rather than multiplied.
- **Projectile definitions are collected *before* the factory is built**, so every pool is prewarmed at
  construction. A pool created lazily on the first shot would allocate its whole prewarm mid-wave.
- **That collection must union the level's towers with the *catalogue*, and forgetting the second
  half is silent.** `ProjectileFactory` refuses to build a pool after construction, and `Create` on
  an unknown prefab logs an error and returns null — so a tower the **player** places whose
  projectile was never collected fires nothing, with no error at the moment of placement. The
  level's own towers cover only what was authored; `TowerCatalogue.CollectProjectileDefinitions` covers
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
- **`Start` refuses a level with no waves**, the third member of the path/bounds family and new
  with the phase machine: without one the round would open in a build phase it could never leave.
- **Prewarm figures are still left disagreeing with their measurements, and half the trigger has
  now fired.** §13.3 measured `PeakActive=12` for the enemy pool against a prewarm of 30, and 1 for
  each projectile pool against 128 — the first numbers taken under a *real* wave rather than a
  fixed-interval stand-in. §10's guidance is to tune after a full run across all three maps, and
  there is still one map, so retuning now would trade one wrong measurement for another.
  `OnDestroy` logs all three.

## `GameStateMachine` arrived, and this section is kept rather than deleted

It used to read: *"There are no phases... a `GameStateMachine` with one state demonstrates nothing
and would have to be deleted and rewritten once real phases define real states. `Bootstrap.Update`
is the placeholder, and saying so here is what keeps this class from quietly becoming the god
object."*

That deferral was right and is now spent. The machine landed in §13.3 with four real states, and
`Bootstrap.Update` is one line forwarding to it. Kept as a record because the *reasoning* is the
part worth having: the type was not built early, so it was never written against imaginary phases
and never had to be rewritten. See [game-state-machine.md](game-state-machine.md).

## Status

**Implemented, wired, and no longer transitional in the way it was.** Five full runs are recorded —
two in §13, one in §13.1, one in §13.2, and §13.3's, which is the first to play a complete round:
four waves, victory, a restart, and a second identical round with a clean console.

`EnemyGreySoldier` is **spawned at last**, in waves 3 and 4. It was authored in §13 and unreferenced
for three slices because `enemyDefinition` was a single reference rather than a list — deliberately,
so that growing it would not build `WaveRunner`'s job into its own placeholder. The field is gone
and the definitions come from `WaveDefinition` now.

Each remaining temporary job has a named successor:

| Job here today | Successor |
|---|---|
| Logging `PeakActive` on shutdown | the editor Pool Overlay (§12, §15) |

**§13.4 emptied the three rows that named [`LevelRunner`](level-runner.md), exactly as the previous
version of this table predicted** — the tower configuring, the level reference and the
`PlacementRules` construction all moved there, per level rather than once at boot. The one row left
has an editor tool for a successor, not a runtime type, so this table is finished as far as the game
is concerned.

**The `Awake`/`Start` split shifted with them, and in the direction that removes a caveat.**
`PlacementRules` used to be built in `Start` because it reads baked waypoints and `EnemyPath` bakes
in *its* `Awake`, which Unity does not order. `LevelRunner.Load` bakes the path itself right after
instantiating, so that ordering worry left with the code. What is still built in `Start` is
`BuildController`, the `WaveRunner` and the phase machine — the things that must exist after every
`OnEnable` has run.

**Deliberately untested**, and now almost entirely wiring: composition, the reference checks —
including `RequireLevels`, which reports an empty array *and* an empty slot inside it, because the
second would otherwise only surface when a victory tried to swap into it — and a scene reload. `targetFrameRate`, the `Awake`/`OnEnable`/`Start` ordering and the restart are all
Play-Mode behaviour, and there is no PlayMode assembly (§12). It is certified by running the scene
with a clean console — which is what §10 already nominates as the certification for its allocation
budget, and what §13.3's session provides across two consecutive rounds.
