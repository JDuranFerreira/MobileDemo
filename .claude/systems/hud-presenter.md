# HudPresenter

The Observer subscriber from [ARCHITECTURE.md §5](../ARCHITECTURE.md) — and the project's first
`MobileDemo.UI` file, which makes it the first place §3's dependency rule is visible rather than
asserted.

## Responsibility

Render the two numbers the gameplay layer announces: lives and currency.

It deliberately does **not**:

- **Read gameplay state.** No reference to `Economy`, no `Update`, no polling. It knows a
  `LivesChanged` or a `CurrencyChanged` arrived and nothing else. **Its entire `using` block is
  `MobileDemo.Core.Events`, `TMPro`, `UnityEngine`** — that absence is the point, and it is worth
  reading the file just for it. Adding towers, projectiles and an economy half changed nothing
  about it.
- **Hold either count.** The numbers live in `Economy`. Giving the HUD its own copy — for
  instance by reading `GameConfig.StartingLives` to show an opening value — would work and would
  be wrong: a second source of truth that is right only by coincidence. §8's `LivesChanged` and
  `CurrencyChanged` are how the opening values arrive.
- **Own the Canvas Scaler numbers.** Those are scene values; §10 is their home.
- **Do anything about zero.** Defeat is §4's.

## Key types

| Type | File | Role |
|---|---|---|
| `HudPresenter` | [HudPresenter.cs](../../Assets/Scripts/UI/HudPresenter.cs) | Two subscriptions, two `SetText`s. |

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
| Subscribes | `CurrencyChanged` | Raised by the same `Economy` |

Depends on `MobileDemo.Core.Events`, TextMeshPro and `UnityEngine`. Nothing in
`MobileDemo.Gameplay`.

## Data

Two serialized fields, `livesLabel` and `currencyLabel`, assigned in the scene. No
ScriptableObject: there is nothing to tune here.

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
- **Both labels are required, and both are reported in one run.** The check does not
  short-circuit, so a scene missing both says so once rather than twice over two sessions —
  the same stance `Bootstrap` takes on its five references. A missing label disables the component
  rather than throwing on the first event.
- **The currency label's format is `"${0:0}"`.** Same `:0` reasoning as the lives label: `SetText`
  takes a `float`, so without it the label can start rendering `$105.0`.

## Status

**Implemented, authored and run.** §13 step 4 closed the first slice with the label reading
**`Lives 14`** from a starting 20, drawn top-left over the map with `font = LiberationSans SDF`
resolved — the string proves the bus chain, the screenshot proves the glyphs, and the TMP gotcha
above is why neither on its own would have been enough.

§13.1 added the currency label, duplicated from the lives label inside the authoring script rather
than built fresh, so it inherits the font asset, material and anchoring and the two cannot drift
apart. That session ended on **`Lives 20`** and **`$150`** from a starting `$100` — the first time
both events in this class have been seen firing in the same run, and the first time the lives
label held still because the towers were working rather than because nothing was happening.

**Deliberately untested**, and the omission is now load-bearing rather than incidental: §14's
test assembly does not reference `MobileDemo.UI`, because none of its five targets is a UI class.
Testing this would need a `Canvas` and a TMP font asset to assert one `SetText`. It is verified by
running the scene and watching the number go down.

**`BuildMenu` joins it on the Canvas with §13.2**, and takes one of this class's expected jobs with
it: the afford check. §8 used to hand `CurrencyChanged`'s second consumption to `BuildController`;
it is `BuildMenu` that subscribes, greying out a button whose tower the player cannot afford, while
`BuildController` asks `Economy` directly. Both UI classes are excluded from tests by the same
assembly-reference decision.

**One gotcha this class inherited without changing:** with an `EventSystem` in the scene — which
the build menu requires — `TMP_Text` inherits `Graphic.raycastTarget = true`, so the "Lives" and
"$" labels would start swallowing world taps that land under them. Both are set to `false`. Nothing
errors when this is wrong; taps simply stop arriving in one corner of the screen, and the HUD's
`GraphicRaycaster` had been inert until now precisely because there was no `EventSystem`.

Pending: a wave counter (when there are waves), the phase readout from `PhaseChanged`, and
`EndScreen` as a separate §11 file.
