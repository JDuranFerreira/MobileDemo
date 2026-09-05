# Projectile & ProjectileFactory

The second `ObjectPool<T>` client, and the one that proves the pool was generic for a reason —
see [ARCHITECTURE.md §6](../ARCHITECTURE.md).

## Responsibility

`Projectile` flies to where it was aimed and applies damage there, single-target or in a radius.
`ProjectileFactory` hands out configured, pooled projectiles — **one pool per prefab** — and ticks
the ones in flight.

They deliberately do **not**:

- **Use physics.** No `Rigidbody2D`, no collider, no trigger. Flight is `Vector2.MoveTowards`
  toward a point and impact is arrival at it. A physics projectile would buy tunnelling bugs and a
  fixed-timestep dependency for nothing this game needs.
- **Self-release.** `Projectile` sets `IsFinished`; the factory's `Tick` releases it. An object
  that returns itself to a pool it does not own is how double releases start.
- **Know their pool.** The factory maps instance → pool privately, so `Projectile` never learns it
  is pooled — the same property `EnemyFactory`'s private pool gives `Enemy`.
- **Track a target after impact.** `OnDespawn` clears both the target and the registry, or a
  recycled projectile could damage enemies from a level already swapped out.
- **Create pools lazily.** See Gotchas.

## Key types

| Type | File | Role |
|---|---|---|
| `Projectile` | [Projectile.cs](../../Assets/Scripts/Gameplay/Towers/Projectile.cs) | `MonoBehaviour, IPoolable`. `Configure`, driven `Tick(dt)`, `IsFinished`. |
| `ProjectileFactory` | [ProjectileFactory.cs](../../Assets/Scripts/Gameplay/Towers/ProjectileFactory.cs) | Pool per prefab, `Create`, `Tick`, `Release`, `IReadOnlyList<IPoolStats> Stats`. |

Namespace `MobileDemo.Gameplay.Towers`, assembly `MobileDemo.Gameplay`.

## Patterns used

**Object Pool** — the mobile constraint in §2, now with a second client.
**Factory** — the seam between a prefab and a live configured instance, mirroring `EnemyFactory`.

**Not ScriptableObject-driven**, and that is the interesting one. `speed`, `damage` and
`impactRadius` are `[SerializeField]`s on the prefab, not a `ProjectileDefinition` asset — §7
argues it in full. Short form: the prefab must exist anyway to carry the sprite, so a definition
beside it would be a second artifact to sync plus a real failure mode (a definition pointing at
the wrong prefab).

## Collaborators

| Direction | With |
|---|---|
| Constructed by | [`Bootstrap`](bootstrap.md), from the distinct prefabs the level's towers reference |
| Created by | [`Tower.Fire`](tower.md) |
| Ticked by | `Bootstrap`, last — after enemies have moved and towers have aimed |
| Reads | [`EnemyRegistry`](enemy-registry.md) for splash, `Enemy.Position` / `IsTargetable` for homing |
| Calls | `Enemy.TakeDamage`, `EnemyRegistry.DamageWithin` |
| Events | **none directly.** `EnemyKilled` is published by the enemy that dies, not by what killed it |

## Data

Authored on the prefabs, for the user to retune in the Inspector:

| Prefab | Sprite | speed | damage | impactRadius |
|---|---|---|---|---|
| `Projectile_Fire` | `fire_long` | 6 | 1 | **0.8** (splash) |
| `Projectile_Bullet` | `bullet_yellow` | 12 | 1 | 0 (single-target) |

`PROJECTILE_POOL_PREWARM` (128) sizes **each** pool, not all of them together. §2 records that
this figure is measurably oversized — both pools peaked at 1 — and why it is not simply edited
down yet.

## Gotchas

- **Splash and the direct hit are either/or, never both.** The aimed-at enemy sits at the centre
  of its own splash radius, so damaging it directly *and* splashing would hit it twice.
- **A target that dies mid-flight does not cancel the shot.** `aimPoint` stops updating and the
  projectile lands where the target last was. For a splash projectile that is the point; for a
  single-target one the impact simply finds nothing, which is honest rather than free.
- **`aimPoint` is seeded in `Configure`**, so a projectile whose target dies on its very first
  tick still has somewhere to fly instead of impacting on the tower.
- **Pools are prewarmed up front, from prefabs handed to the constructor.** A pool created lazily
  on the first shot would allocate its whole 128-instance prewarm in one frame — the exact spike
  §2 calls pooling mandatory to prevent. The cost is that `Bootstrap` must collect the distinct
  prefabs before constructing the factory.
- **An unregistered prefab logs an error and returns null rather than throwing.** A tower wired to
  a prefab the factory was never told about should cost that tower its shots, not the whole run.
- **Runtime tower placement made that pair sharper, and the defence lives elsewhere.** Once the
  player can *build* a tower, "the prefabs the level's towers use" is no longer the full set — a
  tower placed mid-round whose projectile was never collected gets null from `Create` and **fires
  nothing, with no error at the moment of placement**. The no-lazy invariant above is what makes
  that failure possible, and it is still the right call; what closes the hole is
  `TowerCatalogue.CollectProjectilePrefabs`, which `Bootstrap` unions with the level's own prefabs
  at boot. See [tower-factory.md](tower-factory.md).
- **Rotation assumes an upward-pointing sprite** (`degrees - 90`), which is right for `fire_long`
  and the missiles. A sideways sprite would need a different offset.
- **One dictionary entry per live projectile** is the price of keeping `Projectile` ignorant of
  pooling. It is paid on spawn and despawn, not per frame.

## Status

**Implemented and seen running.** Both prefabs fired in §13.1's session, neither pool grew, and
`ProjectileFactoryTests` + `ProjectileTests` cover flight, impact, splash radius, a target dying
mid-flight, and pool-per-prefab isolation. No trail, no impact effect, no arc — all PrimeTween
work that waits on §15.
