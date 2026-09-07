# LevelRunner

The map swapper — [ARCHITECTURE.md §5](../ARCHITECTURE.md)'s last name-only row, filled in by
§13.4. §1's three maps are three prefabs played in sequence, and this is the object that decides
which one is live.

## Responsibility

Own everything *derived from which map is current*: the live [`Level`](level.md) instance, the
[`PlacementRules`](build-controller.md) built from that instance's geometry, and its baked
waypoints. Load the first map at boot, and swap to the next one when a level is won.

It deliberately does **not**:

- **Decide when to swap.** [`VictoryState`](game-state-machine.md) does, because §4 names victory as
  where a level change belongs. This type answers "is there a next one" and performs it.
- **Own the live tower list.** That is [`Level`](level.md)'s, and the swap is exactly why: a
  runtime-placed tower is the level's child and dies with it (§6).
- **Own the pools.** Pooled enemies and projectiles must *outlive* a swap, which is why
  [`Bootstrap`](bootstrap.md) holds `poolParent` as a scene-root sibling rather than a level child.
- **Track wave progress.** [`WaveState`](game-state-machine.md) owns the wave index and is told to
  restart it; this type does not know a wave exists.
- **Be a MonoBehaviour.** It is driven by a state, not by Unity, and is constructible in an EditMode
  test with two templates and no scene (§9, §14).

## Key types

| Type | File | Role |
|---|---|---|
| `LevelRunner` | [LevelRunner.cs](../../Assets/Scripts/Gameplay/Levels/LevelRunner.cs) | `Current`, `Rules`, `Waypoints`, `Index`, `HasNext`, `Load`, `Advance`, and the static `CollectProjectileDefinitions` |

Namespace `MobileDemo.Gameplay.Levels`, assembly `MobileDemo.Gameplay`.

## Patterns used

**None**, and that is now this project's third such answer after
[`EnemyRegistry`](enemy-registry.md) and `PlacementRules` (§6). The tempting ones were a
`LevelService` singleton, an `ILevelProvider` interface, or scene-per-level with additive loading.
There is one implementation, one owner and no lifetime question, so each would have been a type
declaration wrapped around a field.

**What it is instead is an indirection with a reason**: `BuildController` and `WaveState` hold the
runner rather than a `Level`, so a swap moves one field instead of rebuilding the invoker, its undo
stack and its bus subscription. The alternative — rebinding a `Level` into each consumer — is the
mirrored state §6 keeps rejecting, one copy per consumer that can go stale.

`WaveRunner` is the one consumer that *is* rebound, with `Bind(waypoints)`, and the asymmetry is
the reason: an enemy is configured with the waypoint array at spawn and walks it for the rest of its
life, so the runner has to hand out one array per wave rather than resolve it per frame.

## Collaborators

| Direction | With |
|---|---|
| Constructed by | [`Bootstrap`](bootstrap.md), from the serialized `levelPrefabs` array |
| Uses | [`TowerFactory`](tower-factory.md) — `Configure` on every authored tower of a loaded level |
| Produces | [`PlacementRules`](build-controller.md), fresh per level |
| Read by | [`BuildController`](build-controller.md) (`Current`, `Rules`), [`WaveState`](game-state-machine.md) (`Current`, `Waypoints`) |
| Driven by | [`VictoryState`](game-state-machine.md) — `HasNext`, `Advance` |

Raises and subscribes to **no events** — it is not in §8's catalogue. A `LevelChanged` event was
considered and declined: every participant holds the runner by construction, and the swap has an
order (advance → tell `WaveState` → change phase) that a bus round trip would make look incidental.

## Data

No assets of its own. It is constructed from `Level[]` prefabs plus `BUILD_ROAD_CLEARANCE` and
`BUILD_TOWER_SPACING` off [`GameConfig`](../ARCHITECTURE.md) — the two numbers `PlacementRules`
needs, passed through rather than stored anywhere new.

`Prefabs/Level_01`, `Level_02` and `Level_03` are the three maps: riverside switchback, coastal
hook, central lake. All three carry the same `Wave01`–`Wave04` sequence, which is a tuning decision
recorded in §13.4 rather than a constraint of this type.

## Gotchas

- **The path is baked explicitly after `Instantiate`.** `Instantiate` does run the prefab's `Awake`,
  but [level.md](level.md)'s gotcha says a swapper must not depend on that ordering, and the rules
  built two lines later read the bake. One call is cheaper than a rule silently built from an empty
  array.
- **`CollectProjectileDefinitions` is static and takes the prefab array**, because of a construction
  knot: the pools are prewarmed from those definitions, `TowerFactory` needs the pools, and the
  runner needs the `TowerFactory`. Asking the prefabs directly unties it.
- **It collects from *every* map, not the loaded one.** `ProjectileFactory` is prewarmed once and
  returns `null` for a definition it was never given, so a tower authored on map 3 with an
  uncollected projectile would render, aim and never fire, silently (§7).
- **Reading `Towers` on a prefab asset is safe.** It seeds that component's runtime list from its own
  serialized array and touches nothing serialized, so it neither dirties the asset nor affects the
  instance a later `Load` makes.
- **`Destroy` branches on `Application.isPlaying`**, the same test-shaped concession
  [tower-factory.md](tower-factory.md) records: outside play mode `Destroy` defers to a frame that
  never arrives, so an EditMode test would leak a whole level per swap.
- **In play mode the outgoing level survives to the end of the frame.** Unity's `Destroy` is
  deferred, so for the rest of the swapping frame two maps exist. Nothing renders between, so it is
  invisible — but a `FindAnyObjectByType<Level>()` during that frame can return either, which is a
  trap for instrumentation rather than for the game.
- **A swap mid-wave is not possible and is not defended against here.** `WaveRunner.Bind` is what
  refuses it, because it is the object that would be left holding two paths.

## Status

**Implemented, tested and played.** `LevelRunnerTests` covers loading, advancing, the terminal last
level, the rules and bounds being rebuilt per map, and the cross-map definition collection;
`VictoryStateTests` covers the swap as the round performs it. §13.4's session played all three maps
twice in a row with no exceptions.

What is *not* here, with triggers:

| Missing | Trigger |
|---|---|
| A fade or slide across the swap | PrimeTween (§15), which is still not installed |
| Per-level wave sequences | the difficulty pass §13.3's finding names — the data exists per level already (`Level.waves`), so this is authoring, not code |
| Restarting at the *current* level rather than the run | a meta-progression decision §1 excludes |
