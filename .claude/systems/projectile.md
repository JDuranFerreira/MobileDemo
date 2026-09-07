# Projectile & ProjectileFactory

The second `ObjectPool<T>` client, and the one that proves the pool was generic for a reason —
see [ARCHITECTURE.md §6](../ARCHITECTURE.md).

## Responsibility

`Projectile` flies to where it was aimed and applies damage there, single-target or in a radius.
`ProjectileDefinition` is what makes one projectile type differ from another.
`ProjectileFactory` hands out configured, pooled projectiles — **one pool per prefab** — and ticks
the ones in flight.

They deliberately do **not**:

- **Own their damage.** The firing tower supplies the figure (`TowerDefinition.damage`) and the
  definition scales it by `damageMultiplier`. §7 argues it: range, fire rate and damage are the
  three numbers a player compares when buying a tower, so all three are authored on the tower.
- **Use physics.** No `Rigidbody2D`, no collider, no trigger. Flight is `Vector2.MoveTowards`
  toward a point and impact is arrival at it. A physics projectile would buy tunnelling bugs and a
  fixed-timestep dependency for nothing this game needs.
- **Self-release.** `Projectile` sets `IsFinished`; the factory's `Tick` releases it. An object
  that returns itself to a pool it does not own is how double releases start.
- **Know their pool.** The factory maps instance → pool privately, so `Projectile` never learns it
  is pooled — the same property `EnemyFactory`'s private pool gives `Enemy`.
- **Carry tuning on the prefab.** One `Projectile.prefab` serves every type. See Patterns.
- **Track a target after impact.** `OnDespawn` clears the target, the registry *and* the
  definition, or a recycled projectile could damage enemies from a level already swapped out.
- **Create pools lazily.** See Gotchas.

## Key types

| Type | File | Role |
|---|---|---|
| `Projectile` | [Projectile.cs](../../Assets/Scripts/Gameplay/Towers/Projectile.cs) | `MonoBehaviour, IPoolable`. `Configure(definition, target, registry, towerDamage)`, driven `Tick(dt)`, `IsFinished`, `Definition`. |
| `ProjectileDefinition` | [ProjectileDefinition.cs](../../Assets/Scripts/Gameplay/Towers/ProjectileDefinition.cs) | `sprite`, `speed`, `damageMultiplier`, `impactRadius`, `prefab`. |
| `ProjectileFactory` | [ProjectileFactory.cs](../../Assets/Scripts/Gameplay/Towers/ProjectileFactory.cs) | Pool per prefab, `Create`, `Tick`, `Release`, `IReadOnlyList<IPoolStats> Stats`. |

Namespace `MobileDemo.Gameplay.Towers`, assembly `MobileDemo.Gameplay`.

## Patterns used

**Object Pool** — the mobile constraint in §2, now with a second client.
**Factory** — the seam between data and a live configured instance, mirroring `EnemyFactory`.

**ScriptableObject-driven, and that reversed an explicit earlier decision.** `speed`,
`damageMultiplier` and `impactRadius` used to be `[SerializeField]`s on two projectile *prefabs*;
§7 argued that a prefab is already an asset, so a definition beside it would be a second artifact
to sync. What that missed is that the two types differed only by values a component reads — the
GameObject underneath them was identical apart from its sprite — which is exactly the test §7
already applied to towers and enemies. §7 has the reversal in full and keeps the old argument.

**The prefab reference lives on the definition**, which is the one place this type is not shaped
like `TowerDefinition` (whose prefab is a `Bootstrap` field). The pools are keyed by prefab, so the
prefab has to be reachable from the data that names a type.

## Collaborators

| Direction | With |
|---|---|
| Constructed by | [`Bootstrap`](bootstrap.md), from the distinct definitions the level's towers and the catalogue reference |
| Created by | [`Tower.Fire`](tower.md), which passes `TowerDefinition.Damage` with the definition |
| Ticked by | `BuildState` and `WaveState`, last — after enemies have moved and towers have aimed |
| Reads | [`EnemyRegistry`](enemy-registry.md) for splash, `Enemy.Position` / `IsTargetable` for homing |
| Calls | `Enemy.TakeDamage`, `EnemyRegistry.DamageWithin` |
| Events | **none directly.** `EnemyKilled` is published by the enemy that dies, not by what killed it |

## Data

`Data/Projectiles/*.asset`, authored for the user to retune in the Inspector. Both share
`Prefabs/Projectile.prefab`:

| Asset | Sprite | speed | damageMultiplier | impactRadius |
|---|---|---|---|---|
| `ProjectileFire` | `fire_long` | 6 | 1 | **0.8** (splash) |
| `ProjectileBullet` | `bullet_yellow` | 12 | 1 | 0 (single-target) |

Both multipliers are `1` on purpose: the move to assets was a refactor, and it preserves §13.3's
balance exactly. The damage they scale is `TowerDefinition.damage` — 1 on both tower types.

`PROJECTILE_POOL_PREWARM` (128) sizes **each pool**, and there is now one pool rather than two,
because both definitions name the same prefab. §2 records that this figure is measurably oversized
— both pools peaked at 1 — and why it is not simply edited down yet.

## Gotchas

- **A projectile configured with no definition finishes immediately** and logs one error. Left in
  flight it would have no speed, never reach an aim point, and sit in the factory's live list
  forever. The pool gets it back on the next tick instead.
- **Damage is resolved once, in `Configure`.** A shot in the air carries the damage it was fired
  with; retuning an asset mid-flight cannot change it.
- **The product is floored at 1.** `Mathf.Max(1, Mathf.RoundToInt(towerDamage × multiplier))`, so a
  multiplier that rounds to nothing is a weak shot rather than a silently disarmed one.
- **Splash and the direct hit are either/or, never both.** The aimed-at enemy sits at the centre
  of its own splash radius, so damaging it directly *and* splashing would hit it twice.
- **A target that dies mid-flight does not cancel the shot.** `aimPoint` stops updating and the
  projectile lands where the target last was. For a splash projectile that is the point; for a
  single-target one the impact simply finds nothing, which is honest rather than free.
- **`aimPoint` is seeded in `Configure`**, so a projectile whose target dies on its very first
  tick still has somewhere to fly instead of impacting on the tower.
- **Pools are prewarmed up front, from the definitions handed to the constructor.** A pool created
  lazily on the first shot would allocate its whole 128-instance prewarm in one frame — the exact
  spike §2 calls pooling mandatory to prevent. The cost is that `Bootstrap` must collect the
  distinct definitions before constructing the factory.
- **An unregistered definition logs an error and returns null rather than throwing.** A tower wired
  to a definition the factory was never told about should cost that tower its shots, not the whole
  run. One shared prefab narrows that hole — every definition finds the same pool today — without
  closing it: the first type that needs its own prefab reopens it at full width.
- **The sprite comes from the definition**, applied in `Configure`. A projectile visible in the
  hierarchy but invisible on screen was never configured — the same diagnostic `Tower` has.
- **Rotation assumes an upward-pointing sprite** (`degrees - 90`), which is right for `fire_long`
  and the missiles. A sideways sprite would need a different offset.
- **One dictionary entry per live projectile** is the price of keeping `Projectile` ignorant of
  pooling. It is paid on spawn and despawn, not per frame.

## Status

**Implemented; the asset-driven rewrite is unit-tested but not yet seen running.** `ProjectileTests`
and `ProjectileFactoryTests` cover flight, impact, splash radius, a target dying mid-flight, the
damage arithmetic and its floor, a missing definition, and two definitions sharing one pool. What
has *not* happened since the rewrite is a play session — the previous evidence
(`Pool 'Projectile_Bullet': PeakActive=1`) names prefabs that no longer exist. No trail, no impact
effect, no arc — all PrimeTween work that waits on §15.
