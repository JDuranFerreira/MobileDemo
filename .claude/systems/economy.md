# Economy

The currency-and-lives ledger from [ARCHITECTURE.md §5](../ARCHITECTURE.md). **Both halves exist
and both directions now work**: lives fall on a leak, currency rises on a kill and falls when a
tower is bought.

## Responsibility

Hold the round's lives and currency, move them when an enemy leaks or dies, and announce each
new total.

It deliberately does **not**:

- **Decide anything about the decrement.** Reaching zero is not defeat here; it is just zero.
  §8 hands `LivesChanged` to `GameStateMachine` for the defeat check, and that separation is what
  keeps this class arithmetic rather than policy.
- **Know who is listening.** No reference to `HudPresenter`, no reference to a UI type at all.
  That is the §3 seam the bus exists to provide.
- **Own its own subscription lifetime.** It has no `OnEnable`. See Gotchas.
- **Decide whether a purchase is allowed.** `TrySpend` refuses what the balance cannot cover, but
  the *affordability check* belongs to the caller: [`BuildController`](build-controller.md) tests
  `Currency >= cost` before it constructs a command, so `Execute` cannot fail. There is
  deliberately no `CanAfford` here — `Currency` is already a public getter, so it would be a
  wrapper over a property.
- **Offer an `Earn`.** Earning arrives through the bus as `EnemyKilled`. The imperative half is
  named `Refund`. See Gotchas.
- **Couple the two halves.** A kill must not touch lives and a leak must not touch currency. They
  share one subscriber, so `EnemyKilled_DoesNotTouchLives_AndEnemyLeaked_DoesNotTouchCurrency` is
  the only thing that says so.
- **Be a MonoBehaviour.** Which is precisely what makes §14's "these pass without a scene" true
  for it — its tests need no GameObject and no play mode.

## Key types

| Type | File | Role |
|---|---|---|
| `Economy` | [Economy.cs](../../Assets/Scripts/Gameplay/Economy/Economy.cs) | The ledger. `Lives`, `Currency`, `TrySpend`, `Refund`, `Subscribe`/`Unsubscribe`, `PublishCurrentState`. |

Namespace `MobileDemo.Gameplay`, assembly `MobileDemo.Gameplay`.

**The namespace does not match the folder, on purpose.** The file lives in `Economy/` per §11's
tree, but the namespace flattens to `MobileDemo.Gameplay`. With the class in
`MobileDemo.Gameplay.Economy`, any file inside `MobileDemo.Gameplay` — `Bootstrap.cs` — writing
`Economy economy;` resolves `Economy` against its enclosing namespace's members *before*
consulting `using` directives, finds the **namespace**, and fails `CS0118: 'Economy' is a
namespace but is used like a type`. Recorded here and at the top of the file so the
inconsistency is not "fixed" straight back into a compile error.

## Patterns used

**Observer**, subscriber side and publisher side both — and nothing else. Two events in, two
events out.

There is still no Command *here*, and that stayed true when the commands arrived: `PlaceTowerCommand`
and `SellTowerCommand` call `TrySpend` and `Refund` on this class, which makes it the commands'
**receiver** rather than a participant in the pattern. That is the one place §6 notes Command and
Observer can disagree, and the resolution lives in `Refund`: it restores the balance *and*
re-publishes, so an undo cannot leave the HUD showing the pre-undo number.

## Collaborators

| Direction | Event | With |
|---|---|---|
| Subscribes | `EnemyLeaked` | Raised by [`EnemyDyingState`](enemy.md) |
| Subscribes | `EnemyKilled` | Raised by the same state, on the other branch |
| Publishes | `LivesChanged` | Consumed by [`HudPresenter`](hud-presenter.md); `GameStateMachine` (defeat check) is still §4 work |
| Publishes | `CurrencyChanged` | Consumed by [`HudPresenter`](hud-presenter.md) and `BuildMenu` (its afford check) |
| Called by | — | [`PlaceTowerCommand` and `SellTowerCommand`](build-controller.md), which hold this object directly |

Constructed and owned by [`Bootstrap`](bootstrap.md), which also calls `Subscribe`/`Unsubscribe`
and `PublishCurrentState`. Depends on `MobileDemo.Core.Events` and `UnityEngine` (for `Mathf`)
and nothing else.

Every event this class is named against in §8 now has both ends wired. **One consumer moved rather
than arriving:** §8 used to hand `CurrencyChanged` to `BuildController` for an afford check, and it
is `BuildMenu`'s instead — the controller holds this object and asks it synchronously, where greying
out a button genuinely is a presentation reaction to a published fact.

## Data

None. `startingLives` and `startingCurrency` are **constructor arguments**, supplied by
`Bootstrap` from `GameConfig` — the same shape as `ObjectPool<T>`'s `prewarm`, and for the same
reason: the class stays constructible in a test with no asset and no scene.

## Gotchas

- **`Subscribe`/`Unsubscribe` are public, and are *not* called from the constructor.**
  [event-bus.md](event-bus.md)'s rule is subscribe in `OnEnable`, unsubscribe in `OnDisable`; a
  plain C# object has neither, so the *pairing* rule survives intact and the *caller* moves to
  the owning MonoBehaviour. Subscribing in the constructor would leave the bus holding a strong
  reference with no matching teardown — exactly the leak that guide warns about. It also keeps
  §8 honest: `EnemyLeaked` really is consumed by `Economy`, not by a `Bootstrap` that forwards.
- **The method group is load-bearing.** `Subscribe(OnEnemyLeaked)` and
  `Unsubscribe(OnEnemyLeaked)` create two *distinct* delegate instances, and removal still works
  because `Delegate` compares target + method, not reference. It is **lambdas** that silently
  remove nothing. `EnemyLeaked_AfterUnsubscribe_ChangesNothing` pins it, so switching these to
  lambdas fails a test rather than leaking in production.
- **Construct in `Awake`, announce in `Start`.** The constructor publishes nothing. If it did,
  whether the HUD saw the opening value would depend on undefined `Awake` order between two
  GameObjects. Unity guarantees every `OnEnable` completes before the first `Start`, so
  `Bootstrap.Start() => economy.PublishCurrentState()` makes it deterministic.
  `Constructor_RaisesNothing` pins the absence.
- **Publish only on change**, on both halves. A leak that does not move the number announces
  nothing, so the crossing to zero publishes **exactly once** — which is what the defeat check
  will need, and what `EnemyLeaked_CrossingZero_RaisesLivesChangedExactlyOnce` protects. A
  zero-reward kill likewise announces nothing.
- **`PublishCurrentState` announces both.** A currency label left blank until the first kill would
  look broken for the whole build phase.
- **Lives clamp at zero, they do not go negative**, and a negative `startingLives` clamps too
  rather than throwing — mirroring [object-pool.md](object-pool.md)'s stance that a bad tuning
  number is recoverable while a missing dependency is not.
- **Nothing stops the game at zero.** Enemies keep walking and the label sits at `0`. That is
  correct, not unfinished: the defeat transition is §4's.
- **`Refund`, not `Earn`, and the arithmetic being identical is the point.** A kill's reward and a
  sale's refund add the same integer, so one method would do. Two exist because they have different
  *divergence* triggers — a refund fee, or a "currency earned this wave" figure — and a shared name
  would hide the moment one arrives. It also keeps the vocabulary honest: earning is a past-tense
  fact that arrives on the bus; a refund is an imperative from a command that holds this object.
- **`TrySpend(0)` succeeds and announces nothing.** A `cost` of zero is legal authoring, so
  refusing it would make a free tower unplaceable — but announcing an unchanged total would break
  the publish-only-on-change invariant the two handlers keep. Negatives clamp the same way rather
  than minting currency.
- **A refused `TrySpend` publishes nothing**, which is the branch most likely to break that
  invariant: a rejected spend that re-announced the unchanged total would make every failed tap
  look like a transaction. `TrySpend_ForMoreThanCurrency_PublishesNothing` pins it.
- **Undo's correctness depends on the *stack*, not on this class.** (§13.6: with selling off the
  tap, every entry on that stack is a placement and so a debit, which makes the argument below
  trivially true rather than wrong. It is kept because a sale is one product decision away.)
  Undoing a sell charges its
  refund back, and that refund can have been spent — so `TrySpend` can fail here in principle.
  It cannot in practice, because `BuildController` is the only spender and pops strictly LIFO. §6
  has the argument; `SellTowerCommand` logs an error if the impossible happens rather than silently
  handing back a free tower.

## Status

**Implemented, and complete for the loop the demo has.** Its EditMode tests in
[EconomyTests.cs](../../Assets/Tests/EditMode/EconomyTests.cs) cover both halves: the
constructors and their clamps, the opening announcement of both totals, the subscribe/unsubscribe
pairing, the zero crossing, accumulation, that the two halves cannot touch each other, and — added
with the build slice — twelve spend/refund cases including the boundary at exactly the balance, the
zero and negative amounts, and `SpendThenRefund_PublishesCurrencyChangedBothTimes`, which asserts
the *sequence* of published totals rather than the final one. That last is the test §14 has
promised since the beginning, and the sequence matters because §6's rule is precisely that the
second publish happens.

The currency half arrived with the tower slice rather than with the first spender, which is one
step earlier than §2 predicted. §2 records why that prediction was wrong: the test is whether a
serialized knob has a *reader*, and earning is a reader.

**Both rows of this guide's old "what is still missing" table have now discharged**, and neither
arrived quite as predicted. `Spend` became `TrySpend` with the insufficient-funds path as a `bool`
rather than an exception, matching `ObjectPool.Release` and `ProjectileFactory.Create`. And the
predicted `BuildController` subscription never happened at all — it turned out to be the wrong
design, so the row was deleted rather than ticked off. See Collaborators.

Nothing is missing for the current loop. The next thing that will touch this class is §4's
`GameStateMachine`, which needs `LivesChanged` for the defeat check — the one subscriber §8 names
that still does not exist.
