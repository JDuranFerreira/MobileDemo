# Level

The unit that gets swapped — [ARCHITECTURE.md §5](../ARCHITECTURE.md)'s `Level` row. §1's three
maps are three prefabs, and this is the component on each prefab's root.

Short on purpose. It grew for the first time with the build slice — it now owns the *live* tower
set, not just the authored one — and Status says what still completes it.

> **This guide has crossed CLAUDE.md's ~150-line line, and the diagnosis that rule asks for is
> that the *component* has not.** `Level.cs` is under 100 lines and still just describes a prefab:
> three serialized references, a list, two mutators. What is long is the record — a reversed
> decision about the map art, and a scripted-authoring defect worth not repeating. If the next
> slice adds *behaviour* here rather than another paragraph, the rule is right and this splits.

## Responsibility

Be the prefab root's named contract for "this is a level", so a swapper can instantiate level *N*
and read its content without knowing the prefab's internal shape.

It deliberately does **not**:

- **Perform the swap.** That is `LevelRunner`'s job, and it waits on §4's machine — see Status.
- **Hold starting lives.** Lives carry over across all three maps (§1), so that is a *run*
  constant on `GameConfig`. §7 records why, because the wrong guess here is a plausible "fix".
- **Own the map art — and this bullet has been partly reversed.** It used to say nothing read the
  `SpriteRenderer` at runtime, noting only that slice 2's authoring script read its bounds to keep
  hand-placed towers on the board. §13.2 needed exactly that fact at runtime, for exactly that
  reason: [`PlacementRules`](build-controller.md) rejects a tap outside the board. So `Level` now
  carries a serialized `map` and exposes `Bounds`. It still does not *own* the art — it neither
  draws nor tunes it — but it is the only thing that knows the art's playable extent, and the
  prefab is the only place that knows it. Recorded as a reversal rather than edited away, because
  the reasoning is what makes the difference legible.
- **Instantiate or destroy towers.** It holds the live list and nothing more;
  [`TowerFactory`](tower-factory.md) creates and [`PlaceTowerCommand`](build-controller.md)
  destroys.
- **Configure or tick its towers.** It exposes the list; [`Bootstrap`](bootstrap.md) configures
  and drives them.
- **Tick anything.** It has no `Update` and no `Tick`. `Bootstrap` drives the enemies (§9).
- **Exist as a ScriptableObject.** §7 records why the prefab *is* the level.

## Key types

| Type | File | Role |
|---|---|---|
| `Level` | [Level.cs](../../Assets/Scripts/Gameplay/Levels/Level.cs) | Prefab-root component. `Path`, `Bounds`, `Towers`, `AddTower`, `RemoveTower`. |

Namespace `MobileDemo.Gameplay.Levels`, assembly `MobileDemo.Gameplay`.

No CS0118 hazard here, unlike [`Economy`](economy.md): the type is `Level` and the namespace is
`Levels`, so they are different identifiers and the namespace can match the folder normally.

## Patterns used

**None.** It is a serialized reference behind a property, and saying so is the point — §1's
three-map feature costs one component and three prefabs, not a level-management system.

## Collaborators

| Direction | With |
|---|---|
| Exposes | [`EnemyPath`](enemy.md) — read by [`Bootstrap`](bootstrap.md) for `Create` and its `Start` validation |
| Exposes | the live [`Tower`](tower.md) list — `Bootstrap` configures and ticks each, and collects their projectile prefabs |
| Mutated by | [`PlaceTowerCommand` and `SellTowerCommand`](build-controller.md), through `AddTower`/`RemoveTower` |
| Read by | [`PlacementRules`](build-controller.md) — `Bounds` for the board check, `Towers` for spacing and for hit-testing a tap |
| Will expose | `WaveDefinition[]`, when that type has code |
| Will be swapped by | `LevelRunner`, from `VictoryState` (§4) |

Depends on `MobileDemo.Gameplay.Enemies` and `UnityEngine`. Raises and subscribes to **no
events** — it is not in §8's catalogue.

## Data

Three serialized fields — `path`, `towers` and `map` — assigned **inside the prefab**. That is the
whole reason the component exists rather than `Bootstrap` referencing an `EnemyPath` directly: a
prefab reference resolved at author time beats a `GetComponentInChildren<EnemyPath>()` search at
swap time, and it means the wave slice can add `WaveDefinition[] waves` here without re-plumbing
any scene wiring. `towers` proved that claim rather than merely asserting it, and `map` proved it a
second time — both were added without touching a single scene reference.

**`towers` is the level's *opening* state, and the live set is separate.** The serialized array
seeds a runtime `List<Tower>` on first access and is never written to afterwards, so a player
selling an authored tower does not edit the prefab. `RemoveTower_LeavesTheSerializedArrayAlone`
pins that.

**Towers belong to the level, not to the scene**, because where a tower stands is map-specific:
each of the three prefabs will carry its own. It also means a swap brings its own towers and takes
the old ones with it, which is correct — unlike `PoolRoot`, below.

**That last sentence is also why the live list lives here rather than in a `TowerRegistry`.** The
lifetime requirement points the *opposite* way from the pooled enemies: a runtime-placed tower
should die with its level, where a pooled enemy must survive the swap. A registry owned by
`Bootstrap` would outlive the swap holding dangling references and need a `Clear` to compensate;
`Level` answers the question by construction, because the towers are its children.

## Gotchas

- **`PoolRoot` must not be a child of a level prefab.** Pooled enemies have to outlive a swap, and
  a level's destruction would take the whole pool with it — `ObjectPool<T>` never destroys its
  instances and has no way to notice they are gone (see [object-pool.md](object-pool.md)'s
  "instance destroyed behind the pool's back"). `Bootstrap` holds `poolParent` separately and its
  field carries a comment saying so. This is the single easiest thing to get wrong when authoring.
- **An unassigned `Path` fails at `Bootstrap.Start`, not here.** `Level` has no validation of its
  own: the realistic failure is forgetting the reference inside the prefab, and the honest place
  to catch that is where it is first needed. `Bootstrap.Start` checks `level.Path == null` and
  disables itself with one legible error naming the level.
- **The `EnemyPath` bake happens in the prefab's `Awake`**, so a freshly instantiated level's path
  is usable from the following frame's `Update` but not necessarily during the instantiating call.
  `Bootstrap` never reads it before `Start`, which holds today because the level instance is
  authored into the scene. **A swapper will not get that for free** — instantiate, then read the
  path no earlier than the next frame, or call `EnemyPath.Bake()` explicitly.
- **Waypoints are authored per level**, so each of the three prefabs carries its own path laid
  over its own map art. Nothing shares waypoints between levels.
- **`Towers` never returns null.** An unassigned array reads as a level with no towers, so callers
  need no null check — `Bootstrap` iterates it unconditionally.
- **The live list is seeded lazily and idempotently, never in `Awake`, and that is a real bug
  avoided rather than a style choice.** Unity does not order `Awake` between GameObjects, and
  `Bootstrap.Awake` reads `Towers` to collect projectile prefabs. A seed done in this component's
  `Awake` could therefore hand it an empty list — leaving every authored tower with no projectile
  pool and so **no shots, with nothing logged**. Being lazy also makes the component usable in an
  EditMode test, where `Awake` is never sent at all; it is the third instance of that pattern after
  `Enemy.Initialize` and `Tower.Initialize`.
  `Towers_WithoutAwake_SeedsFromTheSerializedArray` is what stops someone "simplifying" it back.
- **`Towers` hands out the live list, not a copy.** `Bootstrap` iterates it every frame and
  `PlacementRules` reads it on every tap, so a defensive copy would allocate on a path §10 polices.
  `IReadOnlyList<Tower>` is what makes that safe — `EnemyRegistry.Active`'s reasoning, reused.
- **`RemoveTower` is what actually stops a tower firing**, not `SetActive(false)`, because
  `Bootstrap` drives towers from this list. A pleasant accident of §9's driven-tick decision, and
  the reason `SellTowerCommand`'s deactivation is cosmetic.
- **A double `AddTower` is rejected**, because a tower in the list twice would be ticked twice and
  fire at double rate.
- **`Bounds` is zero-sized when `map` is unassigned**, and `Bootstrap.Start` refuses to run against
  that with one legible error — the same treatment an unusable path gets, for the same reason: a
  level you cannot build on is half a game.
- **Which path segment a tower is placed beside decides whether the level is playable.** §13.1
  records the full story: a tower one segment from the spawn killed every enemy before it walked,
  and starved the other tower of targets entirely. This is level design living in a prefab, and no
  test covers it.
- **A scripted prefab author that is not idempotent corrupts this prefab quietly, and it did.**
  Slice 2's authoring script re-instantiated its towers on every run while only ever resetting the
  serialized array's `arraySize` to 2 — so three runs left `Level_01` with **six** `Tower`
  instances against a two-entry array. The four orphans were never `Configure`d, so they rendered
  no sprite and never fired: invisible, and harmless enough that §13.1's recorded run numbers still
  stand. §13.2 repairs them. The reusable lesson is the one its own repair is built around: any
  script that authors prefab *children* must key off the serialized references and be a no-op on a
  second run, or the two drift apart with nothing to notice it.

## Status

**Partial — the level exists and runs, the swap does not.** `Level` has code, `Bootstrap` reads
it, and `Prefabs/Level_01.prefab` is now authored: root `Level`, a `Map` child holding the
`variant1_riverside_switchback` sprite at order 0, an `EnemyPath` child carrying ten waypoints
(`Waypoint00`–`Waypoint09`) traced along the road art, and two `Tower` instances — `TowerRed`
mid-path and `TowerGreen` near the end — with `path` and `towers` assigned inside the prefab. One instance sits in `Gameplay.unity` at the origin, and `PoolRoot` is a sibling at the
scene root rather than a child — the gotcha above, honoured. `Level_02` and `Level_03` are
deliberately not authored yet, because one prefab proves the shape.

**`LevelTests` is this component's first fixture**, added with the build slice, and §14 records why
it does not reverse the decision to leave `EnemyPath` untested: `Level` stopped being two
serialized getters the moment it grew a lazy-seeded runtime list and two mutators.

**Two things about the prefab are outstanding until §13.2's authoring script runs**: `map` is not
yet assigned inside it, and the four orphan `Tower` children are not yet removed. Both are that
script's first two jobs.

What completes this system, with triggers:

| Missing | Trigger |
|---|---|
| `LevelRunner` — instantiate the next prefab, destroy the current one | §4's `GameStateMachine` exists. A level change *is* a phase transition, so building it first would invent a second notion of "round over" |
| `WaveDefinition[] waves` on `Level` | `WaveDefinition` has code |
| `Level_02` / `Level_03` prefabs | the swap slice |
| Build plots, instead of a distance rule | a map whose road art yields a legal spot that reads as unbuildable |

Note that the swap also has a consequence §4 already records: `Victory` stops being terminal.
That, rather than the prefabs, is the real cost of §12's multiple-maps reversal.
