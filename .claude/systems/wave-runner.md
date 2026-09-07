# WaveRunner

Reads a `WaveDefinition` and schedules its spawns over time —
[`Bootstrap`](bootstrap.md)`.TickSpawner`'s successor, named for it since
[ARCHITECTURE.md §5](../ARCHITECTURE.md) and finally built in §13.3.

## Responsibility

Spawn a wave's groups in order on their own intervals, tick the live enemies, and answer whether
the wave is over.

It deliberately does **not**:

- **Own the round's phases.** It knows nothing about `Build` or `Victory`. `WaveState` starts it
  and polls it.
- **Decide which wave comes next.** `WaveState` holds `nextWaveIndex`; this type is told an index
  and echoes it back on `WaveCompleted`.
- **Construct the `EnemyRegistry`.** See the first gotcha — the guide it inherited that claim from
  was half wrong.
- **Tick towers or projectiles.** `WaveState` does, in §9's order, after this returns.
- **Stop at zero lives.** Defeat is [`GameStateMachine`](game-state-machine.md)'s.

## Key types

| Type | File | Role |
|---|---|---|
| `WaveRunner` | [WaveRunner.cs](../../Assets/Scripts/Gameplay/Waves/WaveRunner.cs) | The schedule |
| `WaveDefinition` | [WaveDefinition.cs](../../Assets/Scripts/Gameplay/Waves/WaveDefinition.cs) | The asset — an ordered `SpawnGroup[]` |
| `SpawnGroup` | same file | `{ EnemyDefinition, count, spawnInterval }`, exactly §7's shape |

A plain class, not a `MonoBehaviour`, for §9's reason: a driven `Tick(dt)` is callable from an
EditMode test with a deterministic `dt`, and there is no PlayMode assembly (§12).

## Patterns used

**Factory** (it asks [`EnemyFactory`](enemy-factory.md) for an enemy, and knows nothing about the
pool behind it) and **ScriptableObject-driven data** (§7). No pattern of its own — the fourth
member of the plain-class default §6 notices, after `EnemyRegistry`, `PlacementRules` and `Economy`.

## Collaborators

| Direction | With |
|---|---|
| Held by | `WaveState` |
| Holds | [`EnemyFactory`](enemy-factory.md), [`EnemyRegistry`](enemy-registry.md), the baked waypoints of the current map |
| Rebound by | `WaveState.OnLevelChanged`, after [`LevelRunner`](level-runner.md) swaps a map |
| Publishes | `WaveCompleted` — consumed by [`HudPresenter`](hud-presenter.md), **not** by the machine |
| Subscribes | nothing |

## Data

`WaveDefinition` assets, referenced from [`Level`](level.md)'s `waves` array. **Twelve are authored
as of §13.5 — `Data/Waves/Wave01..Wave12.asset`, four per map.**

`Wave01`–`Wave04` are map 1's original curve: green only, with `EnemyGreySoldier` entering in
wave 3. That was the **first time the grey soldier had ever been spawned** — it had been authored
since §13 and unreferenced ever since, because `Bootstrap`'s stand-in held one `EnemyDefinition`
rather than a list.

`Wave05`–`Wave08` (map 2) open with grey rather than introducing it, and `Wave09`–`Wave12`
(map 3) are grey-majority. Intervals tighten monotonically across all twelve, from 1.2s down to
0.5s. **Interval is the axis the ramp leans on, and that is a deliberate choice rather than an
aesthetic one:** a shorter interval raises how many enemies are on the board at once, where a
larger `count` raises the leak damage a single wave can do. One makes the map harder to hold; the
other makes it harder to survive a mistake on. This runner's own concurrency — and therefore the
enemy pool's `PeakActive` — moves with the first.

`spawnIntervalSeconds` is gone from `Bootstrap`. It was labelled "wave data, and `WaveDefinition`
absorbs it" from the day it was written, and it now has.

## Gotchas

- **It ticks the registry; it does not own it.** bootstrap.md's Status table said this type would
  "own the registry outright", and building it proved that half wrong: `Tower` and `Projectile`
  both hold the registry and are wired in `Bootstrap.Awake`, long before any wave, so the object
  must outlive every wave. What actually moved here is the ten-line tick-and-release loop, which is
  the half that was about waves. The table is corrected rather than quietly left.
- **`enemies.Tick(dt)` runs *before* the spawn schedule, and the order is load-bearing.** Enemies
  move first, then `WaveState` scans and fires at where they moved to (§9). Spawning after the move
  also means an enemy created this frame waits a frame before advancing — the reverse would step it
  before it was ever drawn at its origin.
- **The spawn loop is a `while`, not an `if`.** A frame long enough to owe two spawns must deliver
  both; dropping the surplus would silently shorten a wave on exactly the frames a mobile device is
  already struggling. `Tick_WithAFrameLongerThanTheInterval_SpawnsEveryEnemyItOwes` pins it.
- **`MinSpawnInterval = 0.01f` is what keeps that loop bounded.** A group authored at interval 0
  would not terminate. Clamped and warned rather than rejected — object-pool.md's stance that a bad
  tuning number should be recoverable where a missing dependency is not.
- **The opening enemy arrives on the tick that starts the wave, not one interval later.** Tapping
  Go has to have an immediate consequence, and an interval reads as the gap *between* spawns.
- **A wave with nothing to spawn logs an error.** It would otherwise be cleared on its first tick,
  which reads as a sequence that skipped a step rather than as the authoring mistake it is.
- **`IsCleared` stays true; only the announcement happens once.** `running` is the latch. Without
  it the bus would carry a `WaveCompleted` every frame until the next wave started, and a test
  asserting the final count one tick too early would pass anyway — which is why
  `Tick_WhenCleared_PublishesWaveCompletedExactlyOnce` asserts the *sequence*.
- **`WaveState` polls `IsCleared` rather than subscribing to `WaveCompleted`.** A state that holds
  this object by construction and subscribes to mirror a value it can read is the Observer misuse
  §6 warns about — the same call §13.2 made when it moved `CurrencyChanged` off `BuildController`.
  §8's `WaveCompleted` row therefore *moved* its consumer to the HUD rather than gaining one.
- **A one-waypoint path throws at construction.** `EnemyMovingState` indexes waypoint 1 on its
  first tick without re-checking, so it would be an `IndexOutOfRangeException` per spawn otherwise.
- **`Bind` is the one mutable dependency here, and it is refused mid-wave.** The level swap changes
  the road under this object, and an enemy holds the array it was configured with for the whole of
  its life — so a rebind during a wave would leave two roads live at once with one of them not on
  screen. It cannot happen from the game, because a level changes on victory and victory requires a
  cleared wave; the refusal is what keeps that ordering a fact rather than a convention. A path of
  fewer than two waypoints is refused the same way, keeping the one it had, which is the softer
  answer than the constructor's throw for the same mistake: by then a round is in progress.

## Status

**Implemented, authored and played.** §13.3 records four waves cleared twice in one scripted
session. The enemy pool peaked at 12 against a prewarm of 30 and never grew, which is the first
`PeakActive` ever measured under a real wave rather than a fixed-interval stand-in.

§13.4 played the same runner across three maps in one session — twelve waves, rebound twice — with
the enemy pool peaking at 16 against the same prewarm of 30.

**§13.5 played twelve *distinct* waves across the same three maps, and the concurrency barely
moved** — `PeakActive` read 16 and then 15 on two runs of harder data than §13.4's. That is worth
recording because the naive prediction was that a tighter ramp would raise the peak, and it did not:
map 3's waves spawn faster, but the player has more currency by then and buys more towers, so
enemies leave the board faster too. The peak is set by the *balance* between spawn rate and kill
rate, not by the spawn rate alone — which is why §2's prewarm figures were settled on a measurement
rather than on arithmetic over the wave assets.

Also observed there, and it is this runner's guarantee rather than luck: **twelve waves, four level
binds, no `MissingReferenceException`.** `Bind` refuses while a wave is running, so "a level changes
only between waves" held across two full runs including a scene reload between them.

`WaveRunnerTests` — 22 tests, its own pool and registry rather than `BuildScaffold`, because this
is the fixture that cares whether a finished enemy actually goes back to the pool. Four of them
cover `Bind`.
