# HudPresenter

The Observer subscriber from [ARCHITECTURE.md §5](../ARCHITECTURE.md) — and the project's first
`MobileDemo.UI` file, which makes it the first place §3's dependency rule is visible rather than
asserted.

## Responsibility

Render what the gameplay layer announces: lives, currency, the phase and the waves cleared.

It deliberately does **not**:

- **Read gameplay state.** No reference to `Economy`, no `Update`, no polling. It knows a
  `LivesChanged` or a `CurrencyChanged` arrived and nothing else. **Its entire `using` block is
  `MobileDemo.Core.Events`, `TMPro`, `UnityEngine`** — that absence is the point, and it is worth
  reading the file just for it. Adding towers, projectiles, an economy half, a phase machine and a
  wave runner has changed nothing about it.
- **Hold either count.** The numbers live in `Economy`. Giving the HUD its own copy — for
  instance by reading `GameConfig.StartingLives` to show an opening value — would work and would
  be wrong: a second source of truth that is right only by coincidence. §8's `LivesChanged` and
  `CurrencyChanged` are how the opening values arrive.
- **Own the Canvas Scaler numbers.** Those are scene values; §10 is their home.
- **Do anything about zero.** Defeat is `GameStateMachine`'s, which subscribes the same
  `LivesChanged` this class does — two subscribers to one event, neither aware of the other, which
  is the one-to-many §6 says the bus is for.
- **Show the end screen.** [`EndScreen`](../../Assets/Scripts/UI/EndScreen.cs) is a separate
  component subscribing the same `PhaseChanged`, for the same reason.
- **Count anything.** `WaveCompleted` carries the index, so the label reads it rather than keeping
  a tally — a counter here would be a second source of truth for a number the event already has.

## Key types

| Type | File | Role |
|---|---|---|
| `HudPresenter` | [HudPresenter.cs](../../Assets/Scripts/UI/HudPresenter.cs) | Four subscriptions, four `SetText`s. |

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
| Subscribes | `PhaseChanged` | Raised by [`GameStateMachine`](game-state-machine.md) |
| Subscribes | `WaveCompleted` | Raised by [`WaveRunner`](wave-runner.md) |

**`WaveCompleted`'s consumer moved here rather than being added.** §8's table gave it to
`GameStateMachine`; the machine turned out to hold its runner by construction and poll it, so
subscribing would have been Observer used to mirror a readable value — the call §13.2 already made
about `CurrencyChanged` and `BuildController`. A HUD is what one-to-many broadcast was for.

Depends on `MobileDemo.Core.Events`, TextMeshPro and `UnityEngine`. Nothing in
`MobileDemo.Gameplay`.

## Data

Four serialized fields — `livesLabel`, `currencyLabel`, `phaseLabel` and `waveLabel` — assigned in
the scene. No ScriptableObject: there is nothing to tune here.

`phaseLabel` and `waveLabel` sit top-right, mirroring the two existing labels top-left. Both are
authored with `raycastTarget = false`, which is not cosmetic — see Gotchas.

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
- **All four labels are required, and all four are reported in one run.** The check does not
  short-circuit, so a scene missing several says so once rather than over four sessions — the same
  stance `Bootstrap` takes on its references. A missing label disables the component rather than
  throwing on the first event.
- **The currency label's format is `"${0:0}"`.** Same `:0` reasoning as the lives label: `SetText`
  takes a `float`, so without it the label can start rendering `$105.0`.
- **`raycastTarget = false` on every label, and it is not cosmetic.** `TMP_Text` inherits
  `Graphic.raycastTarget = true`, so the moment an `EventSystem` exists a label silently swallows
  the board taps that land under it. §13.2 paid for this once when the build menu brought the
  `EventSystem` in and the two existing labels started eating placements; the two new labels are
  authored with it off for the same reason. Nothing errors when this is wrong — taps just stop
  arriving in one corner of the screen.
- **The phase label uses a `switch` over `GamePhase`, not `ToString()`.** An enum's `ToString()`
  allocates a string on every transition, and the enum's names are code identifiers — four
  literals cost nothing and leave the wording free to differ from the type.
- **The wave label is seeded in `OnEnable`, unlike the other three.** Lives, currency and phase are
  all announced from `Bootstrap.Start`; no wave has completed when the round opens, so there is no
  opening `WaveCompleted` to catch and the label would otherwise render its authored placeholder.

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

**§13.6 gave `BuildMenu` two phase gates where it had one, and a colour where `interactable` was
already spoken for.** Towers can be bought during a wave now, so the tower buttons are live in
`Build` *and* `Wave` while Undo and Go stay `Build`-only — the first is a design change, the second
two are the undo scope and the Go subscription respectively (see
[build-controller.md](build-controller.md) and [game-state-machine.md](game-state-machine.md)). The
armed type is shown by writing `selectedTint` to the button's own `image.color`, not to its
`ColorBlock`: the `Button`'s own `ColorTint` transition keeps multiplying its pressed and disabled
states over the top, so "armed" and "cannot afford" are both legible at once. `selectedIndex` opens
at 0 because `BuildController`'s constructor seeds `Selected` from the catalogue's first entry — the
same fact stated on both sides of §3's seam, since nothing publishes the selection and neither
assembly may reference the other. *Trigger for making it an event: a second thing that can change
the selection.* The menu still enforces nothing; the phase and the scope do.

**One gotcha this class inherited without changing:** with an `EventSystem` in the scene — which
the build menu requires — `TMP_Text` inherits `Graphic.raycastTarget = true`, so the "Lives" and
"$" labels would start swallowing world taps that land under them. Both are set to `false`. Nothing
errors when this is wrong; taps simply stop arriving in one corner of the screen, and the HUD's
`GraphicRaycaster` had been inert until now precisely because there was no `EventSystem`.

**Both pending items landed in §13.3.** The wave counter and the phase readout are code, wired and
seen: the §13.3 session watched the label track `Build`/`Wave` across four waves and two rounds.
`EndScreen` also landed, as the separate §11 file it was always going to be rather than as fields
here — it toggles a panel and asks for a restart, which is a different job from rendering numbers.

Nothing on this class is pending.
