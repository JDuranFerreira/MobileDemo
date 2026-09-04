# Level

The unit that gets swapped — [ARCHITECTURE.md §5](../ARCHITECTURE.md)'s `Level` row. §1's three
maps are three prefabs, and this is the component on each prefab's root.

Short on purpose: today it exposes one reference. See Status for what completes it.

## Responsibility

Be the prefab root's named contract for "this is a level", so a swapper can instantiate level *N*
and read its content without knowing the prefab's internal shape.

It deliberately does **not**:

- **Perform the swap.** That is `LevelRunner`'s job, and it waits on §4's machine — see Status.
- **Hold starting lives.** Lives carry over across all three maps (§1), so that is a *run*
  constant on `GameConfig`. §7 records why, because the wrong guess here is a plausible "fix".
- **Own the map art.** The `SpriteRenderer` is just a child of the prefab; nothing reads it.
- **Tick anything.** It has no `Update` and no `Tick`. `Bootstrap` drives the enemies (§9).
- **Exist as a ScriptableObject.** §7 records why the prefab *is* the level.

## Key types

| Type | File | Role |
|---|---|---|
| `Level` | [Level.cs](../../Assets/Scripts/Gameplay/Levels/Level.cs) | Prefab-root component. `Path`. |

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
| Will expose | `WaveDefinition[]`, when that type has code |
| Will be swapped by | `LevelRunner`, from `VictoryState` (§4) |

Depends on `MobileDemo.Gameplay.Enemies` and `UnityEngine`. Raises and subscribes to **no
events** — it is not in §8's catalogue.

## Data

One serialized field, `path`, assigned **inside the prefab**. That is the whole reason the
component exists rather than `Bootstrap` referencing an `EnemyPath` directly: a prefab reference
resolved at author time beats a `GetComponentInChildren<EnemyPath>()` search at swap time, and it
means the wave slice can add `WaveDefinition[] waves` here without re-plumbing any scene wiring.

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

## Status

**Partial — the level exists and runs, the swap does not.** `Level` has code, `Bootstrap` reads
it, and `Prefabs/Level_01.prefab` is now authored: root `Level`, a `Map` child holding the
`variant1_riverside_switchback` sprite at order 0, and an `EnemyPath` child carrying ten
waypoints (`Waypoint00`–`Waypoint09`) traced along the road art, with `path` assigned inside the
prefab. One instance sits in `Gameplay.unity` at the origin, and `PoolRoot` is a sibling at the
scene root rather than a child — the gotcha above, honoured. `Level_02` and `Level_03` are
deliberately not authored yet, because one prefab proves the shape.

What completes this system, with triggers:

| Missing | Trigger |
|---|---|
| `LevelRunner` — instantiate the next prefab, destroy the current one | §4's `GameStateMachine` exists. A level change *is* a phase transition, so building it first would invent a second notion of "round over" |
| `WaveDefinition[] waves` on `Level` | `WaveDefinition` has code |
| `Level_02` / `Level_03` prefabs | the swap slice |

Note that the swap also has a consequence §4 already records: `Victory` stops being terminal.
That, rather than the prefabs, is the real cost of §12's multiple-maps reversal.
