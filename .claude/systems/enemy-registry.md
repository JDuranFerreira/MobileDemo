# EnemyRegistry

The answer to "which enemies are alive right now" — and, per
[ARCHITECTURE.md §6](../ARCHITECTURE.md), the place where a pattern was deliberately *not* reached
for.

## Responsibility

Own the list of live enemies: tick them, release the finished ones, and answer the two spatial
questions the tower system asks of it.

It deliberately does **not**:

- **Create enemies.** [`EnemyFactory`](enemy-factory.md) does. The registry is handed the result.
- **Own the pool.** It releases through a `Func<Enemy, bool>` supplied at construction —
  `EnemyFactory.Release` — so the pool still has exactly one owner.
- **Become a service.** No singleton, no static, no `IEnemyProvider`. It is constructed by
  [`Bootstrap`](bootstrap.md) and passed to whoever needs it.
- **Know about towers or projectiles.** It exposes positions and takes damage requests; who is
  asking is not its business.
- **Copy its list for callers.** See Gotchas.
- **Have a projectile counterpart.** Nothing ever asks which projectiles are in flight, so that
  loop lives on [`ProjectileFactory`](projectile.md) instead. The asymmetry is reasoned, not an
  oversight.

## Key types

| Type | File | Role |
|---|---|---|
| `EnemyRegistry` | [EnemyRegistry.cs](../../Assets/Scripts/Gameplay/Enemies/EnemyRegistry.cs) | `Active`, `Add`, `Tick`, `FindNearest`, `DamageWithin`. |

Namespace `MobileDemo.Gameplay.Enemies`, assembly `MobileDemo.Gameplay`.

## Patterns used

**None, deliberately** — which is the whole reason this guide exists. The obvious candidates were
a singleton service, an `IEnemyProvider` interface, or a `TargetingSystem`. Each was declined for
the same reason: there is one implementation, one owner and no lifetime question, so an
abstraction would add a layer without removing a decision. §6 records the full argument.

What *did* justify a type rather than passing a list around: three callers need the live set, and
one of them ([`Projectile`](projectile.md)) needs it mid-flight, so it would have to hold the
reference regardless.

## Collaborators

| Direction | With |
|---|---|
| Constructed by | [`Bootstrap`](bootstrap.md), with `EnemyFactory.Release` as the release callback |
| Fed by | `Bootstrap`'s spawn timer — `WaveRunner`'s job later |
| Ticked by | `Bootstrap`, first each frame, before towers aim |
| Read by | [`Tower`](tower.md) (`FindNearest`), [`Projectile`](projectile.md) (`DamageWithin`) |
| Calls | `Enemy.Tick`, `Enemy.TakeDamage`, and the release callback |
| Events | **none.** It raises nothing and subscribes to nothing |

## Data

None. Range and damage arrive as arguments from whoever is asking.

## Gotchas

- **`Active` hands out the live list, not a copy.** A tower scans it every 0.1 s and a projectile
  walks it on every splash, so a defensive copy would allocate on exactly the per-frame path §10
  polices. `IReadOnlyList<Enemy>` is what makes that safe.
- **`FindNearest` and `DamageWithin` are two loops, not one query helper taking a predicate.** Six
  lines twice is cheaper to read than one indirection, and a delegate per call would allocate on
  the impact path.
- **Squared distances throughout.** No `Sqrt`, no LINQ — §10.
- **Both loops skip non-targetable enemies**, which is what keeps spawning and dying enemies out
  of tower target selection and out of splash.
- **`Tick` iterates backwards**, because a finished enemy is removed as it goes. These are
  literally the ten lines [bootstrap.md](bootstrap.md) said `WaveRunner` would inherit; keeping it
  a plain class is what lets `WaveRunner` own it outright rather than reimplement it.
- **Nothing removes an enemy except `Tick`.** `TakeDamage` can finish one, but it stays in the
  list until the next tick sweeps it. Callers must therefore tolerate a finished enemy appearing
  in `Active` for up to one frame — which is why both loops re-check `IsTargetable` rather than
  trusting membership.

## Status

**Implemented.** Covered indirectly by `TowerTests` and `ProjectileTests`, which drive it as the
fixture for every targeting and splash assertion. It has no fixture of its own, deliberately: its
behaviour is only meaningful through a caller, and testing `Add` then `Active.Count` would assert
`List<T>`.
