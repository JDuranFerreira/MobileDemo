# PlacementGhost

The transparent tower a player sees between the two taps of a placement. Added in §13.6, and split
out of [build-controller.md](build-controller.md) rather than folded into it: that guide was already
at the ~150-line limit CLAUDE.md sets, and a renderer with no decisions in it reads better on its
own.

## Responsibility

Show where the tower being bought would stand. One `SpriteRenderer`: set its sprite, move it, turn
it on and off.

It deliberately does **not**:

- **Decide anything.** Not whether the spot is legal, not whether the player can afford it, not
  when the ghost goes away. Every one of those is settled before the event that reaches it.
- **Hold a reference to `BuildController`.** It could — they are in the same assembly — and the
  reason not to is what keeps the invoker a plain class that a fixture can drive with no scene
  (ARCHITECTURE.md §14).
- **Poll.** No `Update`. See Patterns.
- **Show an illegal state.** There is no red ghost, because there is no reachable state to paint
  red: a pending placement exists only where a tower could legally have been bought. Feedback on a
  *rejected* tap is still PrimeTween's job, and it is not installed (§15).
- **Draw a range circle.** Deliberately out of scope for the slice that added it — worth doing, and
  a different change. Named trigger: the first player question this ghost cannot answer, which is
  "what will it cover".

## Key types

| Type | File | Role |
|---|---|---|
| `PlacementGhost` | [PlacementGhost.cs](../../Assets/Scripts/Gameplay/Build/PlacementGhost.cs) | The renderer. Subscribes, moves, toggles. |
| `PlacementPreviewChanged` | [BuildEvents.cs](../../Assets/Scripts/Gameplay/Build/BuildEvents.cs) | `Active`, `Position`, `Tower`. §8's newest row. |

It is a `MonoBehaviour` in `MobileDemo.Gameplay`, not `MobileDemo.UI` — it is world-space and
positioned in world units, and §3 puts UI on the far side of a seam that may not reference Gameplay.

## Patterns used

**Observer**, and this one is the honest case rather than the convenient one. The alternative was a
serialized reference from `Bootstrap` into a ghost, or a `Pending` read from `Update`. The first
puts a scene object in the invoker's constructor; the second is a per-frame read of a value that
changes twice a round, which is exactly the polling-vs-events line §9 draws — and this is the side
of it events win.

§8 declined `TowerPlaced` and `TowerSold` for having a publisher and no subscriber. This row passes
that test: the subscriber is a renderer that cannot be reached any other way without giving a plain
class a scene reference.

## Collaborators

| Direction | With |
|---|---|
| Subscribes | `PlacementPreviewChanged` — raised by [`BuildController`](build-controller.md) |
| Publishes | nothing |
| Reads | `TowerDefinition.Sprite`, through the event's payload |
| Lives under | `PoolRoot` in `Gameplay.unity`, so it survives a level swap — the same reason `PoolRoot` is not a level child ([level.md](level.md)) |

## Data

One serialized field: `tint`, defaulting to white at 45% alpha. It is multiplied over the tower's
own sprite, so the alpha is the whole point and the colour is a nudge.

Not on `GameConfig`: it is a presentation value with one reader, where that asset is for numbers the
*rules* read (§7). The sorting order is authored on the scene object's `SpriteRenderer` — **6**, one
above `Tower.prefab`'s 5, so the ghost reads as being in front of what is already built.

## Gotchas

- **`spriteRenderer.enabled`, never `SetActive` on the GameObject.** The subscription lives on this
  component, on the same object; deactivating it would take the handler off the bus along with the
  sprite, and then nothing could turn it back on.
- **It starts hidden, in `Awake`.** The first event does not arrive until the player taps the board,
  which can be most of a build phase later — a ghost visible at the origin until then would be a
  tower nobody placed.
- **A `null` `Tower` on an active event is treated as inactive.** Cheap, and it means a wiring
  mistake reads as "no ghost" rather than as a `SpriteRenderer` with a null sprite.
- **The scene object was authored by a one-shot, not by hand.** `Assets/Scripts/Editor/`'s
  `GhostAuthoring.cs`, run through `Tools/unity.ps1 -Method`, then deleted — §11's rule for
  authoring scripts. If the object is missing from `Gameplay.unity`, nothing errors: the event goes
  nowhere and placement still works, silently, without a preview.

## Status

**Implemented.** Its own behaviour has no fixture — it is a `MonoBehaviour` whose whole job is a
`SpriteRenderer`, which is the half EditMode cannot see (§14). What *is* tested is everything that
decides for it: `BuildControllerTests` asserts the published sequence — one `Active: true` when a tap
arms a placement, one `Active: false` on the confirm, the cancel and the phase exit — so the only
untested link is the assignment of a sprite to a renderer.

| Missing | Trigger |
|---|---|
| A range circle | the first player question the ghost cannot answer |
| A rejected-tap flash | PrimeTween (§15) |
| A red/illegal ghost | a placement flow that can arm on an illegal spot — today none can |
