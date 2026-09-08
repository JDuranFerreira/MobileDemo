# GameStateMachine

The round's phases — [ARCHITECTURE.md §4](../ARCHITECTURE.md)'s State pattern, which that section
ranks highest of the patterns here for a job signal. It was the last of the three §4 diagram boxes
to become code, deliberately: the enemy's micro machine shipped first because a one-state round
machine would have demonstrated nothing and been rewritten once real phases existed.

## Responsibility

Own the current `IGameState`, swap between them, forward `Tick`, and announce the change.

It deliberately does **not**:

- **Decide transitions.** It is a swapper, not a rules table. `BuildState` decides that a Go tap
  means Wave; `WaveState` decides that a cleared last wave means Victory. The machine's only
  self-initiated transition is defeat, which it owns because it is the one rule that is true from
  *any* phase.
- **Know what a phase does.** It holds `IGameState`, never `BuildState`.
- **Validate a transition table.** There is no "legal transitions" matrix. §14 promised one before
  the machine existed; what it turned out to want was four states that each name their own
  successor, which is a table distributed across four files rather than one file's `switch`.
- **Tick anything but the current state.** Pausing the board *is* not calling a state (§9).

## Key types

| Type | File | Role |
|---|---|---|
| `GameStateMachine` | [GameStateMachine.cs](../../Assets/Scripts/Gameplay/Phases/GameStateMachine.cs) | Holds the states, swaps, publishes, forwards `Tick` |
| `BuildState` | [BuildState.cs](../../Assets/Scripts/Gameplay/Phases/BuildState.cs) | Placing and undo; listens for Go; opens and closes the undo scope |
| `WaveState` | [WaveState.cs](../../Assets/Scripts/Gameplay/Phases/WaveState.cs) | Drives `WaveRunner` and the combat tick order — which starts with building again |
| `VictoryState` | [VictoryState.cs](../../Assets/Scripts/Gameplay/Phases/VictoryState.cs) | Swaps in the next map, or freezes the board when the run is won |
| `DefeatState` | [DefeatState.cs](../../Assets/Scripts/Gameplay/Phases/DefeatState.cs) | Empty, and stays empty (see Gotchas) |

`IGameState` is `MobileDemo.Core.Interfaces` and is unchanged by this slice — see the first gotcha.

## Patterns used

**State**, and this is the instance §4 stakes the pattern's reputation on. What justifies classes
over an enum plus a `switch` here is the same thing that justified it for the enemy: `BuildState`
owns a *subscription* whose lifetime is exactly the phase, and `WaveState` owns `nextWaveIndex`.
Those are per-state fields with per-state setup and teardown, which a `switch` would have to keep
in the machine and reset by hand.

## Collaborators

| Direction | With |
|---|---|
| Held by | [`Bootstrap`](bootstrap.md), which constructs it in `Start` and calls `Tick` in `Update` |
| Holds | four `IGameState`s, keyed by `GamePhase` |
| Publishes | `PhaseChanged` — the §8 row that waited three slices for a publisher |
| Subscribes | `LivesChanged`, for the defeat check |

`BuildState` holds [`BuildController`](build-controller.md) and `ProjectileFactory`; `WaveState`
holds [`WaveRunner`](wave-runner.md), [`LevelRunner`](level-runner.md), `ProjectileFactory` **and
the same `BuildController`**;
`VictoryState` holds the machine, the [`LevelRunner`](level-runner.md) and `WaveState`.

**`WaveState` holds the runner rather than a [`Level`](level.md), as of §13.4**, and the difference
is the swap: the level under it changes, and reading `levels.Current` per use is what stops it
holding a map the round has left.

## Data

**None.** No serialized fields, no asset. The wave *sequence* is `Level`'s (§7); the phases are
code. `GamePhase` lives in Core's `GameEvents.cs` because `PhaseChanged` carries it.

## Gotchas

- **`IGameState` did not gain a `GamePhase` member, and that is why states are keyed by a
  dictionary.** The interface is shared with `EnemySpawningState`/`EnemyMovingState`/
  `EnemyDyingState`, which have no phase, so the member would have to be answered three times with
  a lie. §4's claim is that reusing the shape shows the pattern generalises; a member only one
  implementer can answer would have quietly withdrawn it.
- **`PhaseChanged` is published *before* `Enter()`, which is the opposite of the obvious order.**
  A state can transition again from inside its own `Enter` — `WaveState` does, when the sequence it
  was asked for is already exhausted — and with the publish after `Enter` the inner change announces
  first and the outer one announces the *abandoned* phase last, leaving every subscriber holding a
  phase the round has already left. `Change_FromInsideEnter_PublishesInTheOrderTheTransitionsHappened`
  pins it. The cost is that a subscriber hears a phase a fraction before its state has set itself
  up, which nothing can observe: `PhaseChanged` carries the enum and nothing else.
- **`Change` to the phase already current is ignored, not re-entered.** The defeat check can ask
  for it, and re-entering would run `Exit` then `Enter` on the same instance — for `BuildState`
  that means unsubscribing and resubscribing its Go button and clearing an undo stack twice.
- **`Shutdown()` exists for one reason and it is not tidiness.** `BuildState` subscribes in `Enter`
  and unsubscribes in `Exit`, and **scene teardown never calls `Exit`**. With *Enter Play Mode
  Options* skipping domain reload, `EventBus`'s statics survive a scene load — `ClearAll` runs at
  `SubsystemRegistration`, which a scene load does not reach — so without this a dead `BuildState`
  stays on the bus holding a destroyed `Level`, and the restart's second round throws
  `MissingReferenceException` from code that reads as correct. `Bootstrap.OnDisable` calls it.
- **Which states tick `BuildController` is the whole of the build gate, and §13.6 widened it
  rather than replacing it.** `BuildState.Tick` and `WaveState.Tick` both call it, so a tower can be
  bought mid-wave; `VictoryState` and `DefeatState` do not, so the board is inert behind the end
  screen. There is still no `if (phase == Build)` anywhere. What could not be answered by the same
  mechanism is undo, because a *tick* is per frame and undo is per phase: `BuildState.Enter` opens
  `BuildController`'s undo scope and `Exit` closes it, which is why a mid-wave purchase is permanent
  the instant it happens. `BuildStateTests` pins both halves.
- **Both `Exit` bodies cancel a pending placement, and between them they cover every route out.**
  Build→Wave, Wave→Build, Wave→Victory, and a defeat from either — the machine calls the current
  state's `Exit` on every `Change`, so no ghost survives a phase it was offered in.
- **Defeat is checked from any phase, with no "am I in a wave" guard.** Lives only fall during a
  wave today, so the guard would be a condition that is always true — and one that would silently
  swallow the loss if a cost ever charged lives outside one.
- **`DefeatState` is still empty and `VictoryState` is not, which is the asymmetry this guide
  predicted.** Victory was empty *for now*; §13.4 gave it the level swap §4 always named for it, so
  clearing map 1 or 2 advances [`LevelRunner`](level-runner.md), restarts `WaveState`'s sequence and
  changes to `Build`. Defeat stays empty permanently, because §1's lives carry across all three
  maps, so hitting zero is terminal for the run rather than for the map.
- **The swap re-enters `Change` from inside `Enter`, on purpose.** That is the same re-entrancy the
  publish-before-`Enter` rule two bullets up exists for, now with a second user: `PhaseChanged`
  carries `Victory` and then `Build` in the order they happened. `EndScreen` is therefore shown and
  hidden within one frame on a mid-run victory — §13.4 checked the frame after each one and found
  the panel hidden, so nothing renders it.
- **`VictoryState` tells `WaveState` about the swap by calling it, not by publishing.** It holds
  both by construction, and the call has to land between the advance and the next `Enter`; an event
  would make that order look incidental. The third time §6's "hold it, do not subscribe to it" call
  has been made.

## Status

**Implemented, wired and played.** §13.3 records a scripted session: four waves, `Build`↔`Wave`
alternating, `Victory`, a restart, then a second identical round with a clean console. §13.4 ran the
same shape across three maps: twelve waves, two mid-run victories that swap and return to `Build`,
and one terminal victory.

`GameStateMachineTests` covers it with no scene, no GameObject and no ScriptableObject — its
collaborators are an interface and a static bus, which only `EconomyTests` can also say.
`BuildStateTests` covers the subscription-lifetime claim, and `VictoryStateTests` the swap.

**The defeat transition has now been seen at runtime, and §13.5 is where that finally happened.**
It had been asserted by tests since §13.3 and never executed. What made it cheap was not tuning the
waves up: the player already has an action that guarantees defeat, which is selling the map's
authored towers and building nothing back. (§13.6 removed both halves of that — the authored towers
and the sell tap — so the cheap route to `Defeat` today is simply to build nothing, which is the
opening state. The recorded run below still stands as evidence for the transition.) The recorded
session sold both, started waves, and
watched lives go 20 → 14 → 6 → 0, entering `Defeat` during wave 3 of map 1.

Two things that transition proved which no test could:

- **It fires from `Wave`, mid-wave, on an event.** `OnLivesChanged` has no phase guard (see the
  Gotchas above), and this is the first observation of it firing from a live phase rather than a
  fixture.
- **It is genuinely terminal.** Three further seconds of game time with **zero** phase changes,
  with the end screen up throughout — §4's Victory/Defeat asymmetry observed rather than asserted.

For the record, this did not come from a difficulty change. A clean win still ends at `lives=18`,
exactly as in §13.4, because the harder waves buy the player more currency and therefore more
towers. §13.5 records that as an open design question with the on-device playtest as its trigger.
