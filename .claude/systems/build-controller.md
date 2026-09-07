# BuildController

The build phase from [ARCHITECTURE.md §5](../ARCHITECTURE.md), and the home §6 promises the Command
pattern. Covers `BuildController`, `ICommand`, the two commands, and `PlacementRules` — the last
documented here rather than in its own guide, because it exists to serve this one system (§11's
rule about interfaces that serve one type, applied to a helper).

## Responsibility

Turn a tap into the right build action, and keep those actions undoable. Concretely: read one tap
per frame, decide whether it sells an existing tower or places a new one, validate, construct the
command, execute it, and push it onto a LIFO stack.

It deliberately does **not**:

- **Subscribe to `CurrencyChanged`.** §8 used to list it as a consumer for its afford check. It
  holds the `Economy` by construction — it must, to build commands that spend — and `Currency` is a
  public getter, so the check is a comparison. Subscribing to mirror a readable number would be
  Observer where a plain reference is correct, plus a second source of truth for the balance. The
  afford check that *is* Observer's belongs to [`BuildMenu`](hud-presenter.md), which greys out a
  button it cannot afford.
- **Own the towers.** [`Level`](level.md) holds the live set;
  [`TowerFactory`](tower-factory.md) instantiates.
- **Know about phases.** Building is allowed at all times. See Gotchas.
- **Give feedback on a rejection.** Every rejected tap is silent. §15's PrimeTween owns the flash,
  and it is not installed.
- **Be a MonoBehaviour.** Which is what makes §14's "these pass without a scene" true for it.
- **Redo.** Cut permanently, not deferred — §6 records that "undo/redo for free" contradicted its
  own definition of a command four bullets later.

## Key types

| Type | File | Role |
|---|---|---|
| `ICommand` | [ICommand.cs](../../Assets/Scripts/Core/Interfaces/ICommand.cs) | `Execute` / `Undo`. Two methods, no `CanUndo` — see Gotchas. |
| `BuildController` | [BuildController.cs](../../Assets/Scripts/Gameplay/Build/BuildController.cs) | The invoker. `Tick()`, `Undo()`, `Selected`, `UndoDepth`. |
| `PlacementRules` | [PlacementRules.cs](../../Assets/Scripts/Gameplay/Build/PlacementRules.cs) | `IsLegal` and `FindTowerAt`. |
| `PlaceTowerCommand` | [PlaceTowerCommand.cs](../../Assets/Scripts/Gameplay/Build/PlaceTowerCommand.cs) | Spends, instantiates, adds. Undo destroys and refunds in full. |
| `SellTowerCommand` | [SellTowerCommand.cs](../../Assets/Scripts/Gameplay/Build/SellTowerCommand.cs) | Removes, deactivates, refunds a fraction. |
| `BuildActionRequested` | [BuildEvents.cs](../../Assets/Scripts/Gameplay/Build/BuildEvents.cs) | The UI's one route in. |

`ICommand` is in `MobileDemo.Core`; everything else is `MobileDemo.Gameplay.Build`. The namespace
does **not** flatten here, unlike `Economy` and `Input`, because `Build` is not the name of any type
in it.

## Patterns used

**Command**, and this is the slice that decides whether §6's claim about it was honest. The
right-hand column of §6 argues Command "feels *forced* in a pure action game" and that a build
phase earns it. Two things make that true rather than asserted here: the undo stack has a button
wired to it, and validation sits in the invoker so that no half-executed command can be recorded.

**`PlacementRules` uses no pattern at all**, in the shape [`EnemyRegistry`](enemy-registry.md)
established: one implementation, one owner, no lifetime question. An `IPlacementValidator` with a
rule per class would be three classes to express three `if`s.

## Collaborators

| Direction | With |
|---|---|
| Reads | [`IInputService`](input-service.md), [`Economy`](economy.md), [`LevelRunner`](level-runner.md) — and through it the live [`Level`](level.md) and its `PlacementRules` — `TowerCatalogue`, `TowerDefinition.Cost` |
| Calls | `Economy.TrySpend`/`Refund`, `Level.AddTower`/`RemoveTower`, [`TowerFactory.Create`](tower-factory.md) |
| Subscribes | `BuildActionRequested` — raised by `BuildMenu` |
| Publishes | **nothing.** The commands move currency; `Economy` announces it |
| Constructed by | [`Bootstrap`](bootstrap.md), in `Start` — see Gotchas |

**§13.4 replaced two constructor arguments with one**, and the reason is the level swap: the
`Level` and the `PlacementRules` are both per map, where this object and its undo stack outlive
every map. Reading `Current` and `Rules` from the runner per tap is what makes a swap move one
field instead of rebuilding the invoker and re-subscribing it. Both are read together at the top of
`Tick`, so a rule from the outgoing map can never judge a tap on the incoming one.

## Data

No serialized fields — it is a plain class. Three numbers arrive as constructor arguments from
`GameConfig`: `SellRefundFraction` directly, and `BuildRoadClearance` and `BuildTowerSpacing`
through the [`LevelRunner`](level-runner.md) that now builds the `PlacementRules`. §7 records why
the refund fraction is a run rule rather than a per-tower one.

`Selected` opens on the catalogue's first entry, so the demo is playable before anything is tapped.

## Gotchas

- **`Tick()` takes no `dt`**, unlike every other `Tick` in the project, because nothing in it is
  time-based. Stated because the absence otherwise reads as an oversight and the next reader adds
  one.
- **Sell is checked before legality.** A tap on an existing tower can never be misread as an
  illegal placement, and `PlacementRules` shares one radius between "is this spot free" and "which
  tower did I hit", so exactly one branch can be true.
- **Undo is strictly LIFO, and `void Undo()` is sound because of that, not by luck.** Undoing a
  sell charges its refund back, and that refund can have been spent — which looks like a case for
  `bool Undo()`. It cannot happen: a spend made after a sell is a command *above* it on the stack,
  so it is undone and refunded first. §6 has the arithmetic.
  **Trigger for `bool Undo()`: the first spender that is not a `BuildController` command.**
- **A sold tower is deactivated, never destroyed.** With destroy-and-recreate, the stack
  `[Place, Sell]` breaks — undoing the sell yields a new instance, so undoing the place beneath it
  destroys a stale reference and leaves a free tower on the board. The cost is that a sold tower's
  GameObject stays alive, inactive, owned by the undo stack — until the phase ends.
- **`ClearHistory()` is what ends that ownership, and `BuildState.Exit()` is what calls it.** Once
  the wave starts nothing can pop the stack, so every command on it is permanent; a sale that can
  no longer be undone has no owner left, and `SellTowerCommand.Discard()` destroys its tower. The
  interim rule this guide used to record — "nothing clears the stack" — is gone.
- **The discard is one `is SellTowerCommand` check, not an interface.** §6 stakes real weight on
  `ICommand` being exactly `Execute`/`Undo`, and a third member would have to be answered by
  `PlaceTowerCommand`, for which it means nothing: its own `Undo` already destroys what it made, and
  a placement still *on* the stack is a tower the player owns and is looking at. An `IDiscardable`
  with a single implementer would be the same check with a type declaration around it.
- **`Level.RemoveTower` is what stops a tower firing**, not `SetActive(false)` — `Bootstrap` drives
  towers from that list. The deactivation is cosmetic.
- **The refund is floored, not rounded**, and cached on the command at construction.
  `floor(75 × 0.5) = 37`; rounding up would let an odd-cost sell-and-rebuy loop mint a coin.
  `SellThenRebuy_CostsTheDifference` guards it.
- **`PlaceTowerCommand.Execute` logs an error if `TrySpend` refuses.** The invoker checks first, so
  reaching that branch is a broken invariant — and saying so out loud beats silently placing a free
  tower, which is `ProjectileFactory.Create`'s stance on an unknown prefab.
- **A tower placed at runtime can silently never fire.** `ProjectileFactory` is prewarmed once, so
  its projectile definition must have been collected at boot — which is what `TowerCatalogue` is for
  (§7). Nothing errors at the moment of placement; the error comes from `Create`, later, per shot.
- **The road check projects onto each *segment*, clamped**, not onto the nearer waypoint. The
  failure mode of the waypoint version is not at corners — where the nearest point on both
  adjoining segments *is* the corner — but mid-segment, beside the middle of a long straight run.
- **Building is blocked during a wave, and this class contains no code that does it.** `BuildState`
  ticks it; `WaveState` does not. §10's "no `Instantiate` during a wave" violation is repaired, and
  §9's prediction that it would repair itself "with no change to any of this code" held exactly —
  the diff to this file is `ClearHistory` and one constructor argument, neither about gating.
- **`BuildAction.StartWave` reaches `OnBuildActionRequested` and is deliberately unhandled**, with
  no `default`. It travels this event because §8 chose one enum over three, but its receiver is
  `BuildState`. Two subscribers owning disjoint values of one enum is the shape that choice implies.

## Status

**Implemented and fully unit-tested; one link still unproven on screen.** `BuildControllerTests`
(20), `PlaceTowerCommandTests` (8), `SellTowerCommandTests` (10), `PlacementRulesTests` (13) and
`BuildStateTests` (9) pass as part of a 256-test suite, and the whole dispatch — tap, hit-test,
validate, choose a command, push, undo, clear — runs through `FakeInputService` with no scene.

**What no run has shown is a real press arriving.** Six scripted play sessions failed to deliver a
synthesized tap: the press reaches the device at the right position and
`EventSystem.IsPointerOverGameObject()` correctly returns false, but a CLI-launched editor is
unfocused and the Input System discards the `wasPressedThisFrame` edge before the player loop reads
it. §13.2 records the full diagnosis. The unproven link is one line inside
[`PointerInputService`](input-service.md), not in this class — but it does mean nobody has yet seen
a tower appear under a finger.

| Missing | Trigger |
|---|---|
| `UpgradeTowerCommand` | a per-tower UI, so upgrade and sell stop competing for one tap (§7) |
| Any feedback on a rejected tap | PrimeTween (§15) |
| A `TowerRegistry` beside `Level` | a third owner of the live tower set that is not the level |
| Authored build plots instead of a distance rule | a map whose road art yields a legal spot that reads as unbuildable |
