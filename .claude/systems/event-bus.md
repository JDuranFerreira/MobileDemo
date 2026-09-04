# EventBus

The Observer implementation from [ARCHITECTURE.md §6](../ARCHITECTURE.md) — the seam that lets
`MobileDemo.UI` and `MobileDemo.Gameplay` stay in assemblies that cannot reference each other (§3).

## Responsibility

Deliver a typed value to every subscriber of that type, synchronously, without allocating.

It deliberately does **not**:

- **Queue or defer.** `Publish` runs handlers inline on the caller's stack. No frame delay, no
  ordering machinery — a deferred bus is a scheduler, and nothing in this demo needs one.
- **Guarantee handler order.** Subscribers run in subscription order today, but nothing should
  depend on that; two handlers that must run in sequence are one handler.
- **Own lifetimes.** It holds strong references to whatever subscribes and never unsubscribes
  anything on your behalf. See Gotchas.
- **Cross threads.** Main thread only (§9's world is `Update`-driven).
- **Carry continuous state.** Tower→target and enemy→path are plain references, not
  subscriptions — §6 spells out why.

## Key types

| Type | File | Role |
|---|---|---|
| `IEvent` | [IEvent.cs](../../Assets/Scripts/Core/Events/IEvent.cs) | Marker. Constrains the bus and makes the catalogue enumerable by "find implementations". |
| `EventBus<TEvent>` | [EventBus.cs](../../Assets/Scripts/Core/Events/EventBus.cs) | The bus. `Subscribe` / `Unsubscribe` / `Publish` / `Clear`, constrained `where TEvent : struct, IEvent`. |
| `EventBus` | [EventBus.cs](../../Assets/Scripts/Core/Events/EventBus.cs) | Non-generic. Holds only the reset registry and `ClearAll()`. |
| `GamePhase` | [GameEvents.cs](../../Assets/Scripts/Core/Events/GameEvents.cs) | The §4 phases. In Core because `PhaseChanged` carries it and UI must read it. |
| The six event structs | [GameEvents.cs](../../Assets/Scripts/Core/Events/GameEvents.cs) | The §8 catalogue, one `readonly struct` each. |

Namespace `MobileDemo.Core.Events`, assembly `MobileDemo.Core`.

## Patterns used

**Observer**, in the one shape that earns it: discrete, one-to-many, low-frequency facts. §6's
"where I did *not* use it" is the other half of that decision and should be read with it.

The implementation choices, and what each buys — the reasoning is recorded in full in §6, so this
is the short form:

- **Static generic class over a `Dictionary<Type, List<Delegate>>` singleton.** Closing a generic
  over a type gives each event its own statics — the type-keyed lookup happens once at JIT/AOT
  time, not per publish. A mismatched handler becomes a compile error instead of a runtime cast.
- **Multicast `Action<TEvent>` over a `List<Action<TEvent>>`.** Delegates are immutable, so the
  invocation list is already a snapshot: mutating subscriptions from inside a handler is safe with
  no defensive copy, and a copy per publish would allocate on the exact path §10 forbids it on.
- **`readonly struct` payloads, enforced.** The `struct` half of the constraint is what makes the
  no-boxing property a compiler guarantee rather than a habit — a `class Foo : IEvent` would
  otherwise compile and allocate at every publish site.

## Collaborators

§13's slice wired the first two events end to end. The
[§8 catalogue](../ARCHITECTURE.md) remains the contract for the rest:

| Event | Raised by | Consumed by | Live? |
|---|---|---|---|
| `EnemyLeaked` | [`Enemy`](enemy.md) (`EnemyDyingState.Enter`) | [`Economy`](economy.md); `WaveRunner` planned | **yes** |
| `LivesChanged` | [`Economy`](economy.md) | [`HudPresenter`](hud-presenter.md); `GameStateMachine` planned | **yes** |
| `EnemyKilled` | `Enemy` | `Economy`, `WaveRunner` | no publisher — needs towers |
| `CurrencyChanged` | `Economy` | `HudPresenter`, `BuildController` | no publisher — needs spending |
| `PhaseChanged` | `GameStateMachine` | `HudPresenter`, build UI | no publisher — needs phases |
| `WaveCompleted` | `WaveRunner` | `GameStateMachine` | no publisher — needs waves |

Two events with two subscribers is also the first live test of the §3 claim: `Enemy` (Gameplay)
and `HudPresenter` (UI) now communicate across assemblies that cannot reference each other, and
neither names the other.

Depends on: `System`, `System.Collections.Generic`, and `UnityEngine` for the play-mode reset
attribute. `GameEvents.cs` additionally uses `UnityEngine.Vector2`. Nothing else — no package, no
asset, no container. Unity-only by choice; see §6's dependency-footprint bullet.

`BuildController`'s commands are the one collaborator that both *raises* and must *re-raise*:
`Undo()` on a build command has to re-announce `CurrencyChanged`, or the HUD keeps the pre-undo
balance. See §6's Command entry.

## Data

None. No ScriptableObject, no asset, no serialized field — which is precisely §6's argument
against SO event channels: the wiring is code, so an asset per event would be an editor step
buying nothing.

## Gotchas

- **Subscribe in `OnEnable`, unsubscribe in `OnDisable`.** Not `Awake`/`OnDestroy` — a disabled or
  pooled object left subscribed still receives events and acts on them. This is the live trap now
  that §6's pooling has landed, because a pooled enemy is *disabled, not destroyed*.
  [`ObjectPool<T>`](object-pool.md) deliberately leaves `OnEnable`/`OnDisable` alone, calling
  `IPoolable.OnSpawn`/`OnDespawn` instead, precisely so this pairing stays free for subscribers.
  The ordering consequence is the pool's to document: `OnEnable` runs *before* `OnSpawn`, so a
  handler wired up in `OnEnable` must not assume `OnSpawn` has reset anything yet.
- **The bus holds strong references.** A subscriber that never unsubscribes is never collected.
  Every `Subscribe` needs a matching `Unsubscribe`.
- **Unsubscribe needs an *equal* delegate — which is not the same as the same instance, and the
  difference matters.** `Unsubscribe(e => Foo(e))` after `Subscribe(e => Foo(e))` removes nothing,
  because the two lambdas are distinct objects that are also not equal. But a **method group
  works**: `Subscribe(OnFoo)` and `Unsubscribe(OnFoo)` create two distinct delegate instances and
  removal still succeeds, because `Delegate` equality compares *target + method*. So the rule to
  follow is "subscribe a method group, or store the `Action<T>` in a field" — and the thing to
  avoid is specifically a **lambda**, not a repeated conversion. [`Economy`](economy.md) depends
  on this, and `EnemyLeaked_AfterUnsubscribe_ChangesNothing` pins it.
- **A non-MonoBehaviour subscriber has no `OnEnable`, so the pairing rule keeps its shape and the
  *caller* moves.** `Economy` is a plain class; it exposes `Subscribe`/`Unsubscribe` and its
  owning MonoBehaviour (`Bootstrap`) calls them from `OnEnable`/`OnDisable`. Subscribing from the
  constructor instead would hand the bus a strong reference with no matching teardown — this
  guide's own leak, one layer down.
- **Construct in `Awake`, announce in `Start`.** Unity guarantees every `OnEnable` completes
  before the first `Start`, so a publisher built in `Awake` can safely announce its opening state
  from `Start` and know that a UI subscriber wired up in `OnEnable` will hear it. Publishing from
  the constructor instead makes the delivery depend on undefined `Awake` order between two
  GameObjects — the HUD silently starts blank. See [economy.md](economy.md).
- **Duplicate subscriptions fire twice.** There is no de-duplication, by decision; a double
  subscription always means a missing unsubscribe upstream. Pinned by a test so removing the
  behaviour has to be deliberate.
- **An exception in one handler skips the rest.** No per-handler `try`/`catch` — isolating them
  costs a `GetInvocationList()` array per publish, and a swallowed handler exception hides for
  weeks. A handler that can legitimately fail catches its own failure.
- **Statics survive domain reload being off.** `EventBus.ClearAll()` runs at
  `SubsystemRegistration` to stop last session's subscribers reaching this one. If a closed
  `EventBus<T>` is ever created by reflection at runtime, it registers its resetter on first
  touch like any other — but it will only be cleared if it has been touched at least once.
- **Call `EventBus.ClearAll()` in test teardown.** The bus is global state; without it one test's
  subscribers leak into the next.
- **Re-entrancy is fine, recursion isn't guarded.** Publishing from inside a handler works (tested)
  — but a cycle where A raises B and B raises A will overflow the stack. Nothing prevents it.
- **Class events don't compile.** `where TEvent : struct, IEvent` rejects reference-type payloads
  by design. If you ever need a payload too large to copy, that is a signal the event is carrying
  state it shouldn't — pass an id and let the handler look it up.

## Status

**Implemented.** Bus, marker and the full §8 catalogue exist and compile against Unity 6.3 at
`-langversion:9.0` (which is what rules out the one-line `record struct` form — see §6).
12 EditMode tests in
[EventBusTests.cs](../../Assets/Tests/EditMode/EventBusTests.cs) cover delivery, unsubscribe,
per-type isolation, `ClearAll`, and the two behaviours §6's reasoning leans on (mid-publish
unsubscribe, no de-duplication).

**It now has production subscribers, and the chain has been run.** §13's slice wrote exactly the
chain this guide predicted — `Enemy` raises `EnemyLeaked`, `Economy` consumes it and raises
`LivesChanged`, and a `HudPresenter` label listens — and that chain has since been watched
end to end: the label counted down to `Lives 14` from a starting 20 in a 32 s session.

`ClearAll()` is therefore no longer theoretical either. The project has *Enter Play Mode Options*
set to skip domain reload, so a missing `ClearAll` would surface as a `MissingReferenceException`
on the **second** Play; repeated play sessions have been clean, which is that guard doing its job
rather than an absence of evidence.

*(An earlier revision of this section said the chain had not been run and that `ClearAll` was
"unexercised in anger". That was already stale when it was written — `bootstrap.md` and
`object-pool.md` both recorded a completed run — and it is corrected here rather than quietly
deleted, because a guide disagreeing with two others is the failure mode this file format exists
to avoid.)*
