# Tower

The project's first polling system — [ARCHITECTURE.md §9](../ARCHITECTURE.md)'s tick-interval scan,
which was a plan until this existed.

## Responsibility

Hold a `TowerDefinition`, find the nearest targetable enemy within range on a fixed interval, and
put a projectile in the air at its own fire rate.

It deliberately does **not**:

- **Damage anything.** The projectile carries the damage and applies it on impact. A tower that
  dealt damage directly would make `Projectile` decoration.
- **Get pooled.** §6 says so explicitly: a handful exist for a whole round, so pooling would add
  lifecycle complexity for zero benefit. Still true now that they are placed at runtime —
  [`TowerFactory`](tower-factory.md) genuinely instantiates, and an undone placement genuinely
  destroys.
- **Own its position.** Two towers are authored inside the level prefab and the rest are placed by
  [`BuildController`](build-controller.md); either way the instance is a child of the level and is
  listed by [`Level.Towers`](level.md). It never moves itself.
- **Decide whether it may be built or sold.** [`PlacementRules`](build-controller.md) answers where,
  and `BuildController` answers whether. A tower has no notion of its own cost.
- **Tear itself down when sold.** There is no `Sell` or `Dispose` here.
  `SellTowerCommand` removes it from the level's list — which is what stops it ticking — and
  deactivates the GameObject.
- **Scan every frame.** That is the entire point — see Gotchas.
- **Know about pools.** It asks [`ProjectileFactory`](projectile.md) for a projectile, the same
  way `WaveRunner` will ask `EnemyFactory` for an enemy.
- **Rotate to face its target, flash, or recoil.** Game feel is PrimeTween's job and PrimeTween is
  not installed (§15).

## Key types

| Type | File | Role |
|---|---|---|
| `Tower` | [Tower.cs](../../Assets/Scripts/Gameplay/Towers/Tower.cs) | The component. `Configure(definition, registry, factory, scanInterval)` then a driven `Tick(dt)`. |
| `TowerDefinition` | [TowerDefinition.cs](../../Assets/Scripts/Gameplay/Towers/TowerDefinition.cs) | `sprite`, `range`, `shotsPerSecond`, `projectilePrefab`, `cost`. |

Namespace `MobileDemo.Gameplay.Towers`, assembly `MobileDemo.Gameplay`.

## Patterns used

**Polling**, per [§9](../ARCHITECTURE.md). Not Observer: "enemy entered range" would mean every
enemy notifying every tower on every move — a many-to-many event storm strictly worse than a cheap
10 Hz poll. This is the case where events would be the naive answer.

**ScriptableObject-driven data**, per §7. The two tower types share one `Tower.prefab` and differ
only as `TowerDefinition` assets, exactly as the green and grey soldiers share
`EnemySoldier.prefab`. `Configure` applies the definition's sprite, which is what makes that work.

## Collaborators

| Direction | With |
|---|---|
| Configured by | [`TowerFactory`](tower-factory.md), which passes the definition, the registry, the projectile factory and `TOWER_SCAN_INTERVAL_SEC` |
| Ticked by | [`Bootstrap`](bootstrap.md), after enemies and before projectiles — see its note on order |
| Reads | [`EnemyRegistry.FindNearest`](enemy-registry.md), `TowerDefinition` |
| Calls | [`ProjectileFactory.Create`](projectile.md) |
| Listed by | [`Level.Towers`](level.md) — authored inside the prefab, or appended at runtime |
| Created / sold by | [`PlaceTowerCommand` and `SellTowerCommand`](build-controller.md) |
| Events | **none.** It raises nothing and subscribes to nothing |

## Data

`TowerGreen.asset` — range 2.6, 2 shots/s, fires `Projectile_Bullet`, cost 50.
`TowerRed.asset` — range 2.1, 0.8 shots/s, fires `Projectile_Fire` (splash), cost 75.
Both are listed in `Data/TowerCatalogue.asset`, which is what makes them buildable — see
[tower-factory.md](tower-factory.md) for why that asset exists for a pooling reason first.

**`cost` now has readers** — `BuildController`'s afford check and `SellTowerCommand`'s refund — so
`TowerDefinition` has no authored-but-unread fields left. That vindicates the call
`EnemyDefinition` made for `maxHealth`: author the asset once and completely, and the reader
arrives later.

There is still **no `upgradeTiers`**, and §7 records that the reason changed rather than persisting:
the shape question is answered (a `nextTier` reference, one asset per tier), and what blocks it now
is that upgrade and sell would compete for the same tap.

## Gotchas

- **Scan and reload are two separate timers.** They answer different questions at different rates;
  collapsing them would tie fire rate to scan rate.
- **The target is re-checked at the moment of firing, not only at the moment of scanning.** Up to
  a full scan interval passes between the two, which is plenty of time for the target to die or
  walk out of range. Without the re-check a tower shoots at corpses. This is the real cost of
  polling over an event, and it is cheaper than the storm would have been.
- **The reload timer is capped at one shot when there is no target.** Left accumulating, a tower
  idle for ten seconds banked ten seconds of reload and emptied it into the first enemy in range,
  one shot per frame. `Tick_AfterIdlingWithNoTarget_DoesNotBurstFire` pins this.
- **The first scan is staggered** by a random fraction of the interval in `Configure`, so towers
  placed in the same frame do not all scan on the same frame.
- **`scanInterval` is clamped to a floor of 0.01 s.** A zero would silently turn §9's 10 Hz poll
  back into a per-frame scan — undoing the decision without any error.
- **Where a tower is placed matters more than how it is tuned.** §13.1 records this at length: a
  green tower one segment from the spawn killed everything before it walked, and put the *other*
  tower permanently out of work. Ordering the splash tower first along the path and the fast one
  second is what made both fire, with no number changed.
- **`Configure` is idempotent-ish and must be called**; an unconfigured tower's `Tick` returns
  early rather than throwing, because `Awake` is not sent outside play mode (§14).
- **`Configure` takes the definition, and `Definition` has no setter.** That is the whole seam by
  which a tower instantiated from the shared prefab learns what it is — `Enemy.Configure`'s shape,
  for the same reason. There is deliberately no overload and no null-means-keep rule: an authored
  tower re-passes its own `Definition` (that is what `TowerFactory.Configure` does), so there is
  exactly one contract. A production setter would exist only for the build path.
- **An unconfigured tower renders no sprite**, because the sprite comes from the definition inside
  `Configure`. That is worth knowing as a *diagnostic*: a tower visible in the hierarchy but
  invisible on screen was never configured — which is exactly how §13.2 identified four orphan
  tower instances hiding inside `Level_01.prefab`.
- **A tower placed at runtime can silently never fire.** Its `TowerDefinition.ProjectilePrefab`
  must have been prewarmed into `ProjectileFactory` at boot, which is `TowerCatalogue`'s job. The
  failure is not raised here and not at placement — it arrives per shot, from `Create`.

## Status

**Implemented and seen running.** Both authored towers acquire and fire in §13.1's 30 s session,
and §13.2 reproduced it from the current repo state — currency climbing `$100 → $170` on kills over
26 s with `Lives 20` untouched.

**That reproduction was not free, and the reason belongs here.** Both `TowerDefinition` assets were
found holding pure field defaults — no sprite, no projectile prefab, identical range and cost — so
`Tower.Fire` returned early and neither tower fired, silently. §13.2 has the cause (an authoring
script calling `CreateAsset` over an existing path) and the restored values. It is worth knowing as
a diagnostic: **a tower with no sprite has no definition, and a tower with a definition but no
projectile prefab is disarmed with nothing logged.**

Placement is no longer hand-authored only: §13.2 added [`BuildController`](build-controller.md), so
towers can be placed and sold at runtime, and `TowerDefinition.cost` finally has readers. A tower
built by the factory is unit-tested to fire; a tower built by a *tap* has not been seen, because no
scripted session managed to deliver one (§13.2).

| Missing | Trigger |
|---|---|
| An upgrade path | a per-tower UI (§7) — not the data shape, which is decided |
| Rotation toward the target, a hit flash, a placement pop | PrimeTween (§15) |
| A `Sell`/teardown method on this component | nothing needs one; the level's list is the lifecycle |
