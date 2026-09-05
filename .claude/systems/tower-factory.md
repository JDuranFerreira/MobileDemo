# TowerFactory

The seam between a `TowerDefinition` and a live tower, from [ARCHITECTURE.md §5](../ARCHITECTURE.md).
Also covers `TowerCatalogue`, which is the data it and [`Bootstrap`](bootstrap.md) read.

**The project's third Factory and the first that is not also an Object Pool** — which is the reason
it earns a guide rather than a paragraph in [tower.md](tower.md).

## Responsibility

Instantiate a tower of a given definition at a given position, wired to the collaborators it needs;
and wire an already-authored tower to the same collaborators.

It deliberately does **not**:

- **Pool.** §6 declines pooling towers outright: a handful exist for the whole round, so pooling
  would add lifecycle complexity for zero benefit. `Create` really allocates.
- **Own the live tower set.** [`Level`](level.md) does. See Patterns.
- **Add the tower to the level.** [`PlaceTowerCommand`](build-controller.md) decides ownership; this
  class only builds.
- **Destroy anything.** `PlaceTowerCommand.Undo` destroys what it placed.
- **Know which types are buildable.** `TowerCatalogue` holds that, and `BuildController` reads it.

## Key types

| Type | File | Role |
|---|---|---|
| `TowerFactory` | [TowerFactory.cs](../../Assets/Scripts/Gameplay/Towers/TowerFactory.cs) | `Create(definition, position, parent)` and `Configure(tower)`. |
| `TowerCatalogue` | [TowerCatalogue.cs](../../Assets/Scripts/Gameplay/Towers/TowerCatalogue.cs) | `Buildable`, and `CollectProjectilePrefabs`. |

Namespace `MobileDemo.Gameplay.Towers`. The catalogue is filed with the data it lists rather than in
`Build/`, which holds behaviour.

## Patterns used

**Factory, and nothing else — which is the point.** Until this slice both factories here wrapped a
pool ([`EnemyFactory`](enemy-factory.md), [`ProjectileFactory`](projectile.md)), so a reader could
fairly conclude that "factory" in this project meant "pool with a configure step". Strip the pool
away and what remains is exactly the seam §6 claims Factory is: data on one side, a configured live
instance on the other.

**Why it exists at all, given it owns no pool.** Without it `PlaceTowerCommand` takes eight
constructor arguments — four of them collaborators (`EnemyRegistry`, `ProjectileFactory`, the tower
prefab, the scan interval) that neither the command nor `BuildController` has any opinion about. It
also gives authored and placed towers **one** wiring path: `Bootstrap.ConfigureTowers` delegates
here, so `TOWER_SCAN_INTERVAL_SEC` is named once instead of twice.

**Why it holds no live list.** Lifetime, and it points the opposite way from enemies. Pooled enemies
must *outlive* a level swap, which is why `PoolRoot` is a scene-root sibling; a runtime-placed tower
must not — it is the level's child and should leave with it. A `TowerRegistry` owned by `Bootstrap`
would survive the swap holding dangling references and need a `Clear` to compensate, where `Level`
answers the question by construction. *Trigger to split one out: a third owner of the live set that
is not the level.*

## Collaborators

| Direction | With |
|---|---|
| Constructs | [`Tower`](tower.md) instances, from one shared `Tower.prefab` |
| Calls | `Tower.Configure(definition, registry, projectiles, scanInterval)` |
| Holds | [`EnemyRegistry`](enemy-registry.md), [`ProjectileFactory`](projectile.md), the prefab, the scan interval |
| Read by | [`PlaceTowerCommand`](build-controller.md), [`Bootstrap`](bootstrap.md) |
| Events | **none** |

`TowerCatalogue` is read by `BuildController` (which types can be built), `BuildMenu` (one button
each) and `Bootstrap` (the projectile-prefab closure).

## Data

`Data/TowerCatalogue.asset`, holding `TowerRed` then `TowerGreen` — the order they stand in along
the path, so the menu reads in the same order the board does.

Both tower types share one `Tower.prefab` and differ only as `TowerDefinition` data, exactly as the
green and grey soldiers share `EnemySoldier.prefab`. §7 records the dividing line: a tower's range
and fire rate are numbers a component reads, so they belong in an asset.

## Gotchas

- **The catalogue exists for a *pooling* reason before a UI one, and this is the gotcha most likely
  to bite.** `ProjectileFactory` is prewarmed once with the prefabs `Bootstrap` collected and
  refuses to build a pool later; `Create` on an unknown prefab logs an error and returns null. So a
  tower the player *can build* whose projectile was never collected **fires nothing, and nothing
  errors at the moment of placement** — the error arrives later, per shot, from a different class.
  `Bootstrap.CollectProjectilePrefabs` unions the level's authored towers with
  `TowerCatalogue.CollectProjectilePrefabs` precisely to close that, and
  `PlaceTowerCommandTests.Execute_ThenTick_PutsAProjectileInTheAir` is what catches a regression.
- **`Configure(tower)` re-passes the tower's own `Definition`.** That is what lets
  `Tower.Configure` have a single contract instead of a null-means-keep rule, and it is why
  `Tower.Definition` needs no setter.
- **`Create` names the instance after the definition**, so `TowerRed` and `TowerGreen` are
  distinguishable in the hierarchy during a play session. Cosmetic, but §13.1's lesson was that
  invisible state costs a verification run.
- **`Object.Instantiate` must be qualified in this file.** It has `using System`, which puts
  `System.Object` in scope alongside `UnityEngine`'s, so a bare `Object` is ambiguous — a real
  compile error, not a hypothetical.
- **The catalogue never returns null** from `Buildable`; an unassigned array reads as "nothing
  buildable", which `BuildController` treats as a `Selected` of null and a tap that places nothing.
  [`Level.Towers`](level.md)' precedent.
- **`CollectProjectilePrefabs` appends rather than replacing**, and de-duplicates, because two tower
  types sharing a projectile is normal — they share the pool.

## Status

**Implemented and tested.** `TowerFactoryTests` (8) and `TowerCatalogueTests` (7) pass, including
`Create_ReturnsATowerThatFiresOnTick` — so a factory-built tower demonstrably acquires and fires.
What no play session has shown is a tower built by a *tap*, because no synthesized tap ever reached
the game; §13.2 has the diagnosis, and the gap is in input rather than here.

**`Destroy` branches on `Application.isPlaying`, and that is a test-shaped concession** recorded as
one (§14): outside play mode `Destroy` defers to a frame that never arrives, so an undone placement
would leak its GameObject into the next EditMode test, while `DestroyImmediate` is the wrong call at
runtime. It lives here rather than in `PlaceTowerCommand.Undo` because lifecycle is this type's job.

| Missing | Trigger |
|---|---|
| A `TowerRegistry` holding the live set | a third owner that is not the level |
| Lazy projectile-pool creation | never — §6 declines it, and the catalogue exists so it is not needed |
| Tower rotation toward its target | PrimeTween (§15) |
