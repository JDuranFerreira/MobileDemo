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

Nothing subscribes yet — the bus landed first, ahead of §13's vertical slice. The
[§8 catalogue](../ARCHITECTURE.md) is the contract for who will:

| Event | Raised by | Consumed by |
|---|---|---|
| `EnemyKilled` | `Enemy` | `Economy`, `WaveRunner` |
| `EnemyLeaked` | `Enemy` | `Economy`, `WaveRunner` |
| `CurrencyChanged` | `Economy` | `HudPresenter`, `BuildController` |
| `LivesChanged` | `Economy` | `HudPresenter`, `GameStateMachine` |
| `PhaseChanged` | `GameStateMachine` | `HudPresenter`, build UI |
| `WaveCompleted` | `WaveRunner` | `GameStateMachine` |

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
  Every `Subscribe` needs a matching `Unsubscribe` on the same delegate instance.
- **Unsubscribe needs the *same* delegate.** `Unsubscribe(e => Foo(e))` after
  `Subscribe(e => Foo(e))` removes nothing — the two lambdas are different objects. Subscribe a
  method group (`Subscribe(OnFoo)`) or store the `Action<T>` in a field.
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
unsubscribe, no de-duplication). No production subscriber exists yet — the first arrives with
§13's vertical slice, when `Enemy` raises `EnemyLeaked` and a `HudPresenter` label listens.
