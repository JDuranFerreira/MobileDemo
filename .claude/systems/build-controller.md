# BuildController

The build phase from [ARCHITECTURE.md §5](../ARCHITECTURE.md), and the home §6 promises the Command
pattern. Covers `BuildController`, `ICommand`, the two commands, and `PlacementRules` — the last
documented here rather than in its own guide, because it exists to serve this one system (§11's
rule about interfaces that serve one type, applied to a helper).

## Responsibility

Turn a tap into the right build action, and keep those actions undoable *while the phase that owns
undo is running*. Concretely: read one tap per frame, decide whether it confirms a pending
placement or arms a new one, validate, construct the command, execute it, and push it onto a LIFO
stack — or retire it on the spot, if the undo scope is closed.

A placement takes two taps. The first arms a **pending placement** — plain state, and announced on
the bus so [`PlacementGhost`](placement-ghost.md) can draw it — and the second buys it.

**A tap can no longer sell.** A placed tower is permanent, and the only way to remove one is
`Undo`, inside the build phase it was bought in. There is no branch in the dispatch for a tap on a
tower: `IsLegal` already rejects anything within `towerSpacing` of one, so such a tap is a silent
rejection like a tap on the road. `SellTowerCommand`, `Discard`, `Retire` and
`GameConfig.SellRefundFraction` are all still here and all currently without a producer — kept
rather than deleted because §6's Command reasoning is built on them and a sell affordance is a
plausible return; recorded as dead-by-decision rather than left to look like an oversight.
*Trigger to revive: any affordance that gives a tower back — a sell button on a per-tower UI, or a
salvage refund.*

It deliberately does **not**:

- **Subscribe to `CurrencyChanged`.** §8 used to list it as a consumer for its afford check. It
  holds the `Economy` by construction — it must, to build commands that spend — and `Currency` is a
  public getter, so the check is a comparison. Subscribing to mirror a readable number would be
  Observer where a plain reference is correct, plus a second source of truth for the balance. The
  afford check that *is* Observer's belongs to [`BuildMenu`](hud-presenter.md), which greys out a
  button it cannot afford.
- **Own the towers.** [`Level`](level.md) holds the live set;
  [`TowerFactory`](tower-factory.md) instantiates.
- **Know about phases.** It holds no `GamePhase` and asks nothing about one. Building happens in
  the phases that tick it — `Build` and, since §13.6, `Wave` — and undo happens while a phase has
  opened its undo scope. Both rules are told to it; neither is checked by it. See Gotchas.
- **Draw anything.** The ghost is a published fact
  (`PlacementPreviewChanged`) and [`PlacementGhost`](placement-ghost.md) renders it. That is what
  keeps this a plain class with a two-tap flow a fixture can drive.
- **Give feedback on a rejection.** Every rejected tap is silent. §15's PrimeTween owns the flash,
  and it is not installed.
- **Be a MonoBehaviour.** Which is what makes §14's "these pass without a scene" true for it.
- **Redo.** Cut permanently, not deferred — §6 records that "undo/redo for free" contradicted its
  own definition of a command four bullets later.

## Key types

| Type | File | Role |
|---|---|---|
| `ICommand` | [ICommand.cs](../../Assets/Scripts/Core/Interfaces/ICommand.cs) | `Execute` / `Undo`. Two methods, no `CanUndo` — see Gotchas. |
| `BuildController` | [BuildController.cs](../../Assets/Scripts/Gameplay/Build/BuildController.cs) | The invoker. `Tick()`, `Undo()`, `Selected`, `UndoDepth`, `Pending`, `CancelPending()`, `OpenUndoScope()`/`CloseUndoScope()`. |
| `PlacementRules` | [PlacementRules.cs](../../Assets/Scripts/Gameplay/Build/PlacementRules.cs) | `IsLegal`, `FindTowerAt` and `IsTheSameSpot` — three questions, one radius. |
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
| Publishes | `PlacementPreviewChanged`. Currency is still not its to announce: the commands move it and `Economy` says so |
| Ticked by | [`BuildState`](game-state-machine.md) **and `WaveState`** — the two phases you can build in |
| Constructed by | [`Bootstrap`](bootstrap.md), in `Start` — see Gotchas |

**§13.4 replaced two constructor arguments with one**, and the reason is the level swap: the
`Level` and the `PlacementRules` are both per map, where this object and its undo stack outlive
every map. Reading `Current` and `Rules` from the runner per tap is what makes a swap move one
field instead of rebuilding the invoker and re-subscribing it. Both are read together at the top of
`Tick`, so a rule from the outgoing map can never judge a tap on the incoming one.

## Data

No serialized fields — it is a plain class. Two numbers reach it, both indirectly, through the
[`LevelRunner`](level-runner.md) that builds the `PlacementRules`: `BuildRoadClearance` and
`BuildTowerSpacing`. `SellRefundFraction` used to be a sixth constructor argument and is gone with
the sell tap — nothing here constructs a `SellTowerCommand`, so the field had no reader.
`GameConfig` still carries the number (§7 records why it is a run rule rather than a per-tower
one), which is where it waits.

`Selected` opens on the catalogue's first entry, so the demo is playable before anything is tapped.

## Gotchas

- **`Tick()` takes no `dt`**, unlike every other `Tick` in the project, because nothing in it is
  time-based. Stated because the absence otherwise reads as an oversight and the next reader adds
  one.
- **There is no sell branch, and no check that a tap missed a tower either.** The spacing radius
  that used to decide *which* tower a tap hit is what now makes tapping one mean nothing: the tap
  fails `IsLegal` and falls through to the same silence the road gets. `FindTowerAt` survives as
  `PlacementRules`' own helper for `IsClearOfTowers` and has no caller in this class.
- **Undo is strictly LIFO, and `void Undo()` is sound because of that, not by luck.** Undoing a
  sell charges its refund back, and that refund can have been spent — which looks like a case for
  `bool Undo()`. It cannot happen: a spend made after a sell is a command *above* it on the stack,
  so it is undone and refunded first. §6 has the arithmetic.
  **Trigger for `bool Undo()`: the first spender that is not a `BuildController` command.**
- **A sold tower is deactivated, never destroyed.** With destroy-and-recreate, the stack
  `[Place, Sell]` breaks — undoing the sell yields a new instance, so undoing the place beneath it
  destroys a stale reference and leaves a free tower on the board. The cost is that a sold tower's
  GameObject stays alive, inactive, owned by the undo stack — until the phase ends.
- **`ClearHistory()` is what ends that ownership, and `BuildState.Exit()` reaches it through
  `CloseUndoScope()`.** Once
  the wave starts nothing can pop the stack, so every command on it is permanent; a sale that can
  no longer be undone has no owner left, and `SellTowerCommand.Discard()` destroys its tower. The
  interim rule this guide used to record — "nothing clears the stack" — is gone.
- **The discard is one `is SellTowerCommand` check in `Retire`, not an interface.** §6 stakes real weight on
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
- **Building is allowed during a wave, and this class still contains no code about phases.** Both
  `BuildState.Tick` and `WaveState.Tick` call `Tick()`; `VictoryState` and `DefeatState` do not, so
  the board is inert on the end screen for the same structural reason it was inert mid-wave before.
  §13.6 reopened §10's "no `Instantiate` during a wave" deliberately — one tower per confirmed
  placement, bounded by currency — and §10 records the amended invariant.
- **Undo is a scope the phase opens, and mid-wave commands are permanent the instant they run.**
  `BuildState.Enter()` calls `OpenUndoScope()`, `Exit()` calls `CloseUndoScope()`. With the scope
  closed, `Run` executes the command and *retires* it instead of recording it — the same
  `is SellTowerCommand → Discard()` check as `ClearHistory` — which has no producer now that a tap
  cannot sell, and stays because it is the half a returning sale would need. What a closed scope
  observably does today is refuse to record, so a mid-wave purchase cannot be rewound.
  The alternative was a `phase == Build` check in this class, which is the one thing it does not do.
- **A pending placement is not a command, and that is the rule rather than an omission.** §6 forbids
  a half-executed command reaching the stack, and an armed `PlaceTowerCommand` waiting for a second
  tap would be exactly that. So `pending` is a `TowerDefinition` plus a `Vector2`, and the command is
  constructed at the confirm.
- **The confirm re-validates legality and affordability.** Both can change between the taps: a
  refund or another purchase moves currency, and during a wave the board can change too. The
  refused confirm still clears the ghost.
- **The tap ladder is confirm, then place, and the order still matters.** A ghost sits on legal
  ground so it is a full spacing clear of every tower — but a tap between a ghost and a tower is
  inside both radii, and there the pending intent wins over "that spot is taken". A rejected tap
  leaves the ghost where it is; only a confirm, a `SelectTower` or a phase exit clears it.
- **Re-tapping a tower button is the dismiss gesture**, because there is no Cancel button.
  `SelectTower` cancels on every select, not only on a changed one.
- **`BuildAction.StartWave` reaches `OnBuildActionRequested` and is deliberately unhandled**, with
  no `default`. It travels this event because §8 chose one enum over three, but its receiver is
  `BuildState`. Two subscribers owning disjoint values of one enum is the shape that choice implies.

## Status

**Implemented and fully unit-tested; one link still unproven on screen.** `BuildControllerTests`
(30), `PlaceTowerCommandTests` (8), `SellTowerCommandTests` (10), `PlacementRulesTests` (14) and
`BuildStateTests` (11) pass as part of a 292-test suite, and the whole dispatch — tap, arm,
confirm, re-validate, place, record
or refuse, undo, clear — runs through `FakeInputService` with no scene.

**Removing the sell tap cost four tests that could not be rewritten, and that is worth naming.**
`Undo_AfterASell_RestoresTheTower`, the sell-in-the-middle solvency case,
`ClearHistory_DestroysTheTowersOfSalesItDiscards` and the mid-wave sale case all drove
`SellTowerCommand` *through the invoker*, and nothing can push one onto the stack any more.
`SellTowerCommandTests` still covers the command itself — refund, `Discard`, deactivate-not-destroy
— so what is lost is the integration, not the unit: §6's LIFO-solvency argument is now proved by
prose plus `SellTowerCommandTests`, where it used to have a fixture walking a mixed stack. If
selling returns, that fixture is the thing to restore first.

**What no run has shown is a real press arriving.** Six scripted play sessions failed to deliver a
synthesized tap: the press reaches the device at the right position and
`EventSystem.IsPointerOverGameObject()` correctly returns false, but a CLI-launched editor is
unfocused and the Input System discards the `wasPressedThisFrame` edge before the player loop reads
it. §13.2 records the full diagnosis. The unproven link is one line inside
[`PointerInputService`](input-service.md), not in this class — but it does mean nobody has yet seen
a tower appear under a finger.

**§13.5 exercised everything below that one line, at length, and for the first time as a player
rather than as a fixture.** The session driver swapped a tap queue in for `IInputService` and drove
this class through a whole run: two sells in the opening build phase of the defeat round, then up to
six placements per build phase across twelve waves and three maps in the winning round — every one
of them going through `FindTowerAt`, `IsLegal`, the afford check and a real `ICommand`. Three things
that produced which the unit tests could not:

- **A sale funded nothing and a refund arrived on screen**: selling both authored towers moved
  currency `100 → 162`, which is `SELL_REFUND_FRACTION` and the `floor` in `SellTowerCommand`
  observed on a live board rather than asserted.
- **Placement survives a level swap.** The player's towers die with their level (they are its
  children), so each map is rebuilt from carried-over currency — twelve build phases, four of them
  immediately after a swap, with no stale `PlacementRules` and no dangling tower.
- **`ClearHistory` ran twenty-four times with a clean console.** Every build phase ends by clearing
  a stack that may hold deactivated sold towers, and no `MissingReferenceException` appeared across
  two rounds and a restart — the failure this class is most likely to produce.

The gap is therefore narrower than "unproven on screen" suggests, and no smaller: everything from
the interface down is exercised, and the device read above it still is not.

| Missing | Trigger |
|---|---|
| `UpgradeTowerCommand` | a per-tower UI — and with selling gone from the tap, it no longer has to share one (§7) |
| Any way to remove a placed tower except `Undo` | an affordance that gives a tower back: a sell button on a per-tower UI, or a salvage refund |
| Any feedback on a rejected tap | PrimeTween (§15) |
| A `TowerRegistry` beside `Level` | a third owner of the live tower set that is not the level |
| Authored build plots instead of a distance rule | a map whose road art yields a legal spot that reads as unbuildable |
