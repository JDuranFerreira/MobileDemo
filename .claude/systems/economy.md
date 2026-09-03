# Economy

The currency-and-lives ledger from [ARCHITECTURE.md §5](../ARCHITECTURE.md). Today it keeps
**lives only** — see Status for why, and for what unblocks the other half.

## Responsibility

Hold the round's lives count, decrement it when an enemy leaks, and announce the new total.

It deliberately does **not**:

- **Decide anything about the decrement.** Reaching zero is not defeat here; it is just zero.
  §8 hands `LivesChanged` to `GameStateMachine` for the defeat check, and that separation is what
  keeps this class arithmetic rather than policy.
- **Know who is listening.** No reference to `HudPresenter`, no reference to a UI type at all.
  That is the §3 seam the bus exists to provide.
- **Own its own subscription lifetime.** It has no `OnEnable`. See Gotchas.
- **Touch currency.** Not yet. See Status.
- **Be a MonoBehaviour.** Which is precisely what makes §14's "these pass without a scene" true
  for it — its 11 tests need no GameObject and no play mode.

## Key types

| Type | File | Role |
|---|---|---|
| `Economy` | [Economy.cs](../../Assets/Scripts/Gameplay/Economy/Economy.cs) | The ledger. `Lives`, `Subscribe`/`Unsubscribe`, `PublishCurrentState`. |

Namespace `MobileDemo.Gameplay`, assembly `MobileDemo.Gameplay`.

**The namespace does not match the folder, on purpose.** The file lives in `Economy/` per §11's
tree, but the namespace flattens to `MobileDemo.Gameplay`. With the class in
`MobileDemo.Gameplay.Economy`, any file inside `MobileDemo.Gameplay` — `Bootstrap.cs` — writing
`Economy economy;` resolves `Economy` against its enclosing namespace's members *before*
consulting `using` directives, finds the **namespace**, and fails `CS0118: 'Economy' is a
namespace but is used like a type`. Recorded here and at the top of the file so the
inconsistency is not "fixed" straight back into a compile error.

## Patterns used

**Observer**, subscriber side and publisher side both — and nothing else. There is no Command
here yet, because nothing can spend; when `PlaceTowerCommand` lands it will call a `Spend` on
this class and `Undo` will have to refund **and re-publish**, which is the one place §6 notes
Command and Observer can disagree.

## Collaborators

| Direction | Event | With |
|---|---|---|
| Subscribes | `EnemyLeaked` | Raised by [`EnemyDyingState`](enemy.md) |
| Publishes | `LivesChanged` | Consumed by [`HudPresenter`](hud-presenter.md); `GameStateMachine` (defeat check) is still §4 work |

Constructed and owned by [`Bootstrap`](bootstrap.md), which also calls `Subscribe`/`Unsubscribe`
and `PublishCurrentState`. Depends on `MobileDemo.Core.Events` and `UnityEngine` (for `Mathf`)
and nothing else.

`CurrencyChanged` stays in §8's catalogue with no publisher. That is fine — §8 is a contract,
not an inventory.

## Data

None. `startingLives` is a **constructor argument**, supplied by `Bootstrap` from
`GameConfig.StartingLives` — the same shape as `ObjectPool<T>`'s `prewarm`, and for the same
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
- **Publish only on change.** A leak that does not move the number announces nothing, so the
  crossing to zero publishes **exactly once** — which is what the defeat check will need, and
  what `EnemyLeaked_CrossingZero_RaisesLivesChangedExactlyOnce` protects.
- **Lives clamp at zero, they do not go negative**, and a negative `startingLives` clamps too
  rather than throwing — mirroring [object-pool.md](object-pool.md)'s stance that a bad tuning
  number is recoverable while a missing dependency is not.
- **Nothing stops the game at zero.** Enemies keep walking and the label sits at `0`. That is
  correct, not unfinished: the defeat transition is §4's.

## Status

**Partial — lives only.** Implemented, and its 11 EditMode tests in
[EconomyTests.cs](../../Assets/Tests/EditMode/EconomyTests.cs) cover the constructor, the clamp,
the opening announcement, the subscribe/unsubscribe pairing, the zero crossing and the
publish-only-on-change decision.

The currency half arrives with the **first `ICommand` that can spend** — `PlaceTowerCommand`.
That is the named trigger, and it brings `STARTING_CURRENCY` onto `GameConfig` (§2), a
`CurrencyChanged` publisher (§8), and the refund-and-re-announce rule from §6's Command entry.
