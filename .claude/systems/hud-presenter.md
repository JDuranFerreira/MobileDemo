# HudPresenter

The Observer subscriber from [ARCHITECTURE.md §5](../ARCHITECTURE.md) — and the project's first
`MobileDemo.UI` file, which makes it the first place §3's dependency rule is visible rather than
asserted.

## Responsibility

Render numbers the gameplay layer announces.

It deliberately does **not**:

- **Read gameplay state.** No reference to `Economy`, no `Update`, no polling. It knows a
  `LivesChanged` arrived and nothing else. **Its entire `using` block is
  `MobileDemo.Core.Events`, `TMPro`, `UnityEngine`** — that absence is the point, and it is worth
  reading the file just for it.
- **Hold the lives count.** The number lives in `Economy`. Giving the HUD its own copy — for
  instance by reading `GameConfig.StartingLives` to show an opening value — would work and would
  be wrong: a second source of truth that is right only by coincidence. §8's `LivesChanged` is
  how the opening value arrives.
- **Own the Canvas Scaler numbers.** Those are scene values; §10 is their home.
- **Do anything about zero.** Defeat is §4's.

## Key types

| Type | File | Role |
|---|---|---|
| `HudPresenter` | [HudPresenter.cs](../../Assets/Scripts/UI/HudPresenter.cs) | One subscription, one `SetText`. |

Namespace `MobileDemo.UI`, assembly `MobileDemo.UI`. It sits at the assembly root rather than in
a subfolder, per §11's tree.

## Patterns used

**Observer**, subscriber side only. That is the whole class, and it is the payoff §6 claims for
the bus: the economy does not know the HUD exists, and the two live in assemblies that cannot
reference each other.

`MobileDemo.UI.asmdef`'s reference to `MobileDemo.Gameplay` **stays** — §3 permits it and
`BuildMenu` will need it — and is unused by this file on purpose.

## Collaborators

| Direction | Event | With |
|---|---|---|
| Subscribes | `LivesChanged` | Raised by [`Economy`](economy.md) |

Depends on `MobileDemo.Core.Events`, TextMeshPro and `UnityEngine`. Nothing in
`MobileDemo.Gameplay`.

## Data

One serialized field, `livesLabel`, assigned in the scene. No ScriptableObject: there is nothing
to tune here.

The scene values it sits on, recorded here because this class deliberately does *not* own them
(see above) and because §2 is where they come from: the `HUD` root carries a Screen Space – Overlay
`Canvas` and a `CanvasScaler` set to *Scale With Screen Size*, `1080 × 1920`, match `0.5`. The
label is a `TextMeshProUGUI` child anchored top-left. There is **no `EventSystem`** in the scene
yet — nothing is tappable, and one serving nothing would be decoration.

## Gotchas

- **`OnEnable`/`OnDisable`, never `Awake`/`OnDestroy`** — [event-bus.md](event-bus.md)'s pairing
  rule. This object is not pooled, but the rule is the same one that made `IPoolable` necessary.
- **It sees the opening value because of an ordering guarantee, not luck.** Unity runs every
  `OnEnable` before the first `Start`, and `Bootstrap.Start` is what calls
  `Economy.PublishCurrentState()`. If that announcement ever moves into `Economy`'s constructor
  (called from `Bootstrap.Awake`), this label will silently start blank — the race is between two
  GameObjects' `Awake`, which Unity does not order.
- **`SetText`, not `text = value.ToString()`.** TMP formats into its own char buffer; the
  `ToString()` form allocates a string on every change, and §10's allocation budget applies to
  UI too. A non-zero GC Alloc while the HUD is updating points here first.
- **The format string is `"Lives {0:0}"`, with the `:0` deliberate.** TMP's `SetText` takes the
  argument as a `float`, and what a bare `{0}` renders for one is a TMP implementation detail;
  `:0` pins zero decimal places so the label cannot start showing `19.0`.
- **`TMP_Text`, not `TextMeshProUGUI`.** The abstract base, so the label can be canvas or world
  text without touching this class.
- **TMP Essential Resources must be imported**, once, via `Window > TextMeshPro`. Without them
  there is no default font asset and no shaders, so the label renders *nothing* — no error, just
  blank. The import writes `Assets/TextMesh Pro/` (3.9 MB) and it has to be committed, or the HUD
  is blank on every other clone. **Done, and committed.** The practical consequence for anyone
  verifying this class: reading `livesLabel.text` back is *not* proof it works, because this
  failure mode leaves the text perfectly correct and draws nothing. Check the resolved `font`, or
  take a screenshot.
- **An unassigned `livesLabel` disables the component with one error** rather than throwing on
  the first event, matching `Bootstrap`'s stance on its own references.

## Status

**Implemented, authored and run** — §13 step 4, and the step that closed that slice. A 32 s play
session showed the label reading **`Lives 14`** from a starting 20, drawn top-left over the map,
with `font = LiberationSans SDF` resolved. Both halves of that were checked deliberately: the
string proves the bus chain, the screenshot proves the glyphs, and the gotcha below is why
neither on its own would have been enough.

**Deliberately untested**, and the omission is now load-bearing rather than incidental: §14's
test assembly does not reference `MobileDemo.UI`, because none of its five targets is a UI class.
Testing this would need a `Canvas` and a TMP font asset to assert one `SetText`. It is verified by
running the scene and watching the number go down.

Pending: currency and wave counters (when there is currency and there are waves), the phase
readout from `PhaseChanged`, and `BuildMenu`/`EndScreen` as separate §11 files.
