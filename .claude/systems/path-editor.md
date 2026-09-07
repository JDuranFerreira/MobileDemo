# PathEditor

A custom inspector and scene-view tool for authoring a level's road. See
[ARCHITECTURE.md §13.5](../ARCHITECTURE.md) for the slice that built it, §11 for why it can exist at
all, and §15 for the dependency it justifies declining.

**The first file that ships from `MobileDemo.Editor`** — an assembly created five slices earlier and
deliberately held empty until something belonged in it.

## Responsibility

Editing the `Transform[]` on an [`EnemyPath`](enemy.md): moving waypoints with scene handles,
numbering them on screen, inserting and removing them as one action, and warning about the two
authoring mistakes the runtime is silent about.

It owns **no runtime behaviour**. It is excluded from player builds by the `Editor/` folder and the
Editor-platform asmdef, and nothing in `Core`, `Gameplay` or `UI` may reference it (§3's dependency
direction, and §11's one-way editor→runtime rule).

What it deliberately does *not* own:

- **The array's authoritative editing surface.** `DrawDefaultInspector()` still runs, so the plain
  `Transform[]` field is fully editable. This tool adds to hand-authoring rather than replacing it —
  retargeting one slot at an existing `Transform` is still quickest by hand.
- **Reordering.** No move-up/move-down. Insert-at-index covers the edit that actually comes up
  (a corner added mid-road), and a reorder UI would be a second way to express the same change.
- **Geometry validation beyond two cases.** No self-intersection check, no clamping a waypoint into
  `Level.Bounds`. Bounds come from the map `SpriteRenderer`, not from the path
  ([level.md](level.md)), and nothing at runtime cares where a waypoint sits relative to the art.
- **Path creation.** Adding an `EnemyPath` to a level is `AddComponent`, and a wizard for that
  would be tooling for a once-per-map action.

## Key types

| Type | File | Note |
|---|---|---|
| `PathEditor` | [PathEditor.cs](../../Assets/Scripts/Editor/PathEditor.cs) | `[CustomEditor(typeof(EnemyPath))]`. The whole system. |
| `EnemyPath` | [EnemyPath.cs](../../Assets/Scripts/Gameplay/Enemies/EnemyPath.cs) | The target. Its `public Bake()` and its `OnDrawGizmos` both predate this tool and were written for it. |

## Patterns used

**None, and that is the point.** A custom inspector is a Unity extension point, not a design
pattern, and there is nothing here worth abstracting: one editor, one target type, no lifetime
question. It sits in the same category as `EnemyRegistry` and `PlacementRules` — the plain-class
answer ARCHITECTURE.md §6 calls this project's default.

The pattern-shaped decision it *does* make is about `Undo`: every insert and remove is wrapped in
one undo group (`Undo.SetCurrentGroupName` / `CollapseUndoOperations`), because the child object and
the array slot are two operations and a half-undone edit leaves an orphan `Transform` parented under
the path with no slot pointing at it.

## Collaborators

- **Reads and writes** `EnemyPath`'s serialized `waypoints` array, through `SerializedObject` rather
  than reflection — `ApplyModifiedProperties` is what marks the asset or prefab dirty, which a
  reflected field write would not do.
- **Calls** `EnemyPath.Bake()` after every edit that leaves a valid path. That method has been
  `public` since it was written, with a comment saying a future `PathEditor` would need it.
- **Reads** nothing from `PlacementRules`, though it warns about a case that only matters *because*
  of it — see the coincident-waypoint gotcha below. Importing the rule to check it would make an
  editor tool depend on a build-phase system for a warning string.
- **Raises and subscribes to no events.** The EventBus is a runtime concern; there is no phase, no
  economy and no wave here.

## Data

None of its own. No ScriptableObject, no `GameConfig` field, no `EditorPrefs`. The one piece of
state it holds is the `int` index in the insert/remove row, which lives on the editor instance and
is deliberately not persisted — a remembered index would be a stale index after the next selection.

## Gotchas

- **The re-bake must be guarded, not unconditional.** `EnemyPath.Bake` logs a `Debug.LogError` and
  empties itself under two waypoints, and throws a `NullReferenceException` on a null entry (its
  guard checks the array's *length* only). A path being briefly invalid is the **normal** state of
  one being authored, so `Rebake` returns without calling `Bake` unless the array is valid — leaving
  the last good road baked. [enemy.md](enemy.md) records that this refines the named trigger it set.
- **Handles write world space; the prefabs author local.** `EnemyPath.Bake` reads
  `Transform.position`, so the tool writes through the `Transform` rather than assuming the two
  agree. All three level prefabs sit at local zero and `Bootstrap` instantiates with `parent: null`,
  so local == world *today* — that is a property of the authored offsets, not of the design.
- **`z` is held across a drag.** The handle returns a `Vector3`; the board is 2D and the bake
  truncates `z` anyway, so taking the handle's `z` would let a drag in a rotated scene view push a
  waypoint off the plane where nothing at runtime would ever show it.
- **Deleting an array element that holds an object reference takes two calls.** The first assigns
  null, the second removes the slot — Unity's long-standing `DeleteArrayElementAtIndex` behaviour,
  and getting it wrong leaves a null entry, which is the exact failure this tool warns about.
- **Only a child of *this* path is destroyed on remove.** A slot retargeted at some other
  `Transform` loses its slot and keeps its object; deleting a GameObject the tool did not create is
  not its call to make.
- **Children are renamed to match their index.** The array is authoritative for the bake, so the
  names exist for the human — which is exactly why they must not be allowed to lie after a mid-array
  insert.
- **`UnityEditor.Editor`, spelled out.** An unqualified `Editor` base class inside a namespace called
  `MobileDemo.Editor` binds to the namespace and fails to compile.

### The two warnings, and why they are the tool's real justification

Both are cases the runtime is deliberately silent about, so the inspector is the only place they can
be caught before they cost a play session:

1. **A null entry in the array.** Passes `Bake`'s length guard, then throws on the first bake.
   `OnDrawGizmos` skips nulls, so the scene view looks correct right up to Play. Raised as an error.
2. **Coincident consecutive waypoints.** `PlacementRules.DistanceToSegmentSqr` has a degenerate
   branch for a zero-length segment that collapses it to a point check — so a duplicated node
   quietly shrinks the road's no-build clearance, with nothing logged anywhere and no visible
   difference in the gizmo. Raised as a warning.

A tool that only drew handles and labels would have been decoration. These two are why it is a job.

## Status

**Implemented.** Shipped in §13.5, and the reason it was built then rather than earlier is that
twelve authored waves across three maps made path work recurring rather than one-off.

**Not tested, deliberately, and not silently.** Testing it from `MobileDemo.Tests.EditMode` would
mean adding a `MobileDemo.Editor` reference to an assembly kept minimal on purpose, in order to
exercise `UnityEditor` GUI code — the least valuable half of the tool. Instead a **throwaway** check
(`SliceSixPathCheck`, deleted after running, per §11) drove the insert and remove paths headless and
asserted that the array, the child objects and the bake still agreed: append renumbers and re-bakes,
a mid-array insert does not disturb its neighbours, a remove destroys the child rather than only the
slot, an out-of-range index is a no-op, and an invalid path is *not* re-baked over the last good one.
That last one is where the check found a mistake — in its own expectation, not in the tool.

**What is not certified: the scene view.** Handles, labels and dragging are `UnityEditor` GUI, which
no headless run can drive. Dragging a handle and watching the next wave walk the new road needs a
focused editor and a human hand, and it has not been done — recorded as uncertified in §13.5
alongside the `PointerInputService` link, which has the same shape of gap for the same reason.

What would complete this system, with triggers:

| Missing | Trigger |
|---|---|
| Waypoint reordering | an authored road that needs a leg moved rather than replaced. Inserting and removing has covered every edit so far |
| Snap-to-grid | grid-authored maps, which would also pull in 2D Tilemap (§15 lists it as conditional) |
| A path-length readout | balancing that needs it. Enemy travel time is path length over `moveSpeed`, and no tuning pass has asked for the first number yet |
| `PoolOverlay` | §12. Named here only because it is the other file §11 says will live in this folder, and this one arriving first does not bring it closer |
