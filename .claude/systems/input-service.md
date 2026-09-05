# InputService

The touch layer from [ARCHITECTURE.md §10](../ARCHITECTURE.md). One interface, one runtime
implementation — which is **not** what §10 originally planned, and the difference is most of what
this guide is for.

## Responsibility

Answer one question once per frame: was there a tap this frame, and where is it in world space?

It deliberately does **not**:

- **Queue or consume taps.** It is a query. See Gotchas.
- **Distinguish a touch from a mouse click** to its caller. That is the whole point.
- **Know what a tap means.** [`BuildController`](build-controller.md) decides that.
- **Handle press-and-hold, drag, or multi-touch.** Each has a trigger below.
- **Live in Core.** The interface does; the implementation cannot, because it needs
  `Unity.InputSystem` and Core references nothing project-specific.

## Key types

| Type | File | Role |
|---|---|---|
| `IInputService` | [IInputService.cs](../../Assets/Scripts/Core/Interfaces/IInputService.cs) | `bool TryGetTap(out Vector2 world)`. One member. |
| `PointerInputService` | [PointerInputService.cs](../../Assets/Scripts/Gameplay/Input/PointerInputService.cs) | Reads `Touchscreen.current` or `Mouse.current`, converts to world. |
| `FakeInputService` | [FakeInputService.cs](../../Assets/Tests/EditMode/FakeInputService.cs) | The second implementation, and the interface's real justification. |

`PointerInputService` sits in `Input/` but its namespace is **`MobileDemo.Gameplay`**, flattened —
the second instance of the rule [economy.md](economy.md) records, and the more dangerous one:
`Input` is a live identifier in Unity code, so a namespace by that name would shadow it for every
file in the assembly. The folder keeps its name.

## Patterns used

**None — and §5's table used to claim otherwise.** It said "Strategy-ish (one interface, editor vs
device)", and §10 promised a `TouchInputService` plus an `EditorInputService`. That plan was wrong,
not merely superseded:

With the project on `activeInputHandler: 1` (Input System only), `Touchscreen.current` and
`Mouse.current` are read through one API on every platform. Two classes would have been the same
six lines with a different device line, and **nothing would ever have selected between them at
runtime** — the choice is a `#if UNITY_EDITOR`. That is a Strategy with no varying strategy, which
is what §6 calls architecture cosplay when it declines `IFactory<T>`. The package had already
absorbed the abstraction §10 credited to the interface.

**So why keep the interface?** Substitution in tests, and that is a smaller and more honest reason
than the one it replaced. `BuildController`'s tap dispatch is the logic the build slice actually
adds; a ten-line fake drives it with no device, no camera and no play mode, where the alternative is
`InputTestFixture` and a synthetic device in every fixture. It is a **test-shaped justification for
a production interface**, labelled as one — the same honesty §14 applies to `Enemy.Initialize`'s
idempotence. It is not the seam §14 declined for `internal` setters: that is a back door into a
production type, where this is a whole implementation of a published contract.
[`IPoolStats`](object-pool.md) is the precedent for a one-implementation interface with a written
reason.

## Collaborators

| Direction | With |
|---|---|
| Implements | `IInputService` (Core) |
| Reads | `Touchscreen.current`, `Mouse.current`, `EventSystem.current`, and a `Camera` |
| Read by | [`BuildController`](build-controller.md) — and it must stay the only caller |
| Constructed by | [`Bootstrap`](bootstrap.md), from a serialized `sceneCamera` |
| Events | **none.** §9 records why input is polled rather than evented |

## Data

None serialized. The `Camera` is a constructor argument — not `Camera.main`, which is a tag search,
and `Bootstrap` already holds a serialized reference for everything else it composes.

## Gotchas

- **It is a query, not a queue.** `wasPressedThisFrame` stays true for the whole frame and nothing
  here consumes it, so two callers in one frame would both see one tap. `BuildController` must
  remain the sole caller. `FakeInputService` deliberately *does* consume, so a test's one `Tick`
  sees exactly one tap.
- **Press, not release.** One frame, no held state, and both device APIs offer it directly. The
  misfire it risks is precisely the affordance §6 says undo models, so the responsive choice is the
  one the undo stack already covers. *Trigger for release-with-slide-off-to-cancel: on-device
  playtesting.*
- **`ScreenToWorldPoint` is correct only because the camera is orthographic** (size 7, at z = −10)
  against a board on the z = 0 plane. §2 pins that by having no camera controller and no scrolling,
  so it is safe by decision rather than by luck. Under a perspective camera both x and y would be
  wrong — the classic 2D tap bug.
- **`IsPointerOverGameObject` is what stops a menu tap also placing a tower**, and it is why
  `MobileDemo.Gameplay` lists `UnityEngine.UI`. That reference is documentation rather than
  plumbing (the assembly is `autoReferenced`), and it does not breach §3: that rule is about
  `MobileDemo.UI`, this project's UI layer, where `UnityEngine.EventSystems` is Unity's input
  plumbing. Solving it with screen geometry instead would have required Gameplay to know HUD
  layout, which *would* have breached it.
- **Known caveat, accepted:** with `InputSystemUIInputModule` the pointer-over state can be one
  frame stale on the press frame. Not worth defensive code; worth knowing before debugging a tap
  that got through.
- **Both HUD labels must have `raycastTarget = false`.** `TMP_Text` inherits `true` from `Graphic`,
  so once an `EventSystem` exists the "Lives" and "$" labels silently swallow taps under them.
  Nothing errors — the taps just stop arriving in one corner.
- **`Touchscreen.current` and `Mouse.current` are both null-checked** because either can be absent:
  no mouse on a phone, no touchscreen on a desktop. Neither is an error.

## Status

**Implemented, wired, and deliberately untested.** `PointerInputService` joins `Bootstrap` and the
UI classes in §14's "not tested, and why" list: `Mouse.current` is null in EditMode,
`InputTestFixture` is declined, and what would remain to assert is `camera.ScreenToWorldPoint` —
which is Unity's. The interface exists so that everything *downstream* of it is tested, and
`BuildControllerTests` is that coverage.

Not yet seen in a play session; §13.2 records what is outstanding.

| Missing | Trigger |
|---|---|
| A second implementation | the first device-only concern — a pinch, a platform gesture |
| Press/hold/drag, or a placement ghost | on-device playtesting says two-tap feels wrong |
| An `.inputactions` asset | a rebindable control, which this demo has no use for |
