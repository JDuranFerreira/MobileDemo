using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    // Tuning lives on a ProjectileDefinition asset, not on this prefab (ARCHITECTURE.md §7). One
    // Projectile.prefab serves every projectile type and the definition supplies the sprite, the
    // same way one Tower.prefab serves both tower types and one EnemySoldier.prefab serves both
    // soldiers.
    //
    // The damage it applies is not its own: the firing tower supplies the figure and the
    // definition's multiplier scales it. So "how hard does this tower hit" has exactly one
    // authored answer, on the tower.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class Projectile : MonoBehaviour, IPoolable
    {
        Transform cachedTransform;
        SpriteRenderer spriteRenderer;
        ProjectileDefinition definition;
        EnemyRegistry registry;
        Enemy target;
        Vector2 aimPoint;

        // Resolved once in Configure rather than recomputed at impact: the tower's damage is a
        // property of the shot that was fired, so a mid-flight retune of the asset must not change
        // what a projectile already in the air does.
        int damage;

        public bool IsFinished { get; private set; }

        public ProjectileDefinition Definition => definition;

        void Awake() => Initialize();

        // Idempotent and called from Configure as well as Awake, for the reason §14 records:
        // Awake is not sent outside play mode, so an EditMode test's AddComponent never
        // initializes.
        void Initialize()
        {
            if (cachedTransform != null)
            {
                return;
            }

            cachedTransform = transform;
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        public void Configure(
            ProjectileDefinition projectileDefinition, Enemy enemy, EnemyRegistry enemies,
            int towerDamage)
        {
            Initialize();

            definition = projectileDefinition;
            registry = enemies;
            target = enemy;

            // Seeded now so a projectile whose target dies on the very first tick still has
            // somewhere to fly, rather than impacting on the tower.
            aimPoint = enemy != null ? enemy.Position : (Vector2)cachedTransform.position;

            if (definition == null)
            {
                // Finished rather than left in flight: with no definition there is no speed, so
                // Tick would never reach its aim point and ProjectileFactory would hold the
                // instance in its live list forever. One error, then the pool gets it back.
                Debug.LogError($"Projectile '{name}' was configured with no definition.", this);
                damage = 0;
                IsFinished = true;
                return;
            }

            // Same as Tower and Enemy: the sprite comes from the definition, which is what lets
            // every projectile type share one prefab.
            spriteRenderer.sprite = definition.Sprite;

            // Floored at 1 so a multiplier that rounds to nothing is a weak shot rather than a
            // silently disarmed one -- object-pool.md's stance that a bad tuning number should be
            // recoverable, applied to arithmetic.
            damage = Mathf.Max(1, Mathf.RoundToInt(towerDamage * definition.DamageMultiplier));
        }

        public void Tick(float dt)
        {
            if (IsFinished || definition == null)
            {
                return;
            }

            // Re-aim only while the target is worth aiming at. A target that dies mid-flight
            // leaves aimPoint at its last known position, so the shot lands where it was -- which
            // is what makes a splash projectile still worth firing into a cluster.
            if (target != null && target.IsTargetable)
            {
                aimPoint = target.Position;
            }

            Vector2 position = cachedTransform.position;
            Vector2 next = Vector2.MoveTowards(position, aimPoint, definition.Speed * dt);
            MoveTo(next, aimPoint - position);

            // MoveTowards clamps exactly, so this needs no epsilon -- the same idiom
            // EnemyMovingState uses for waypoint arrival.
            if (next == aimPoint)
            {
                Impact();
            }
        }

        public void OnSpawn() => IsFinished = false;

        public void OnDespawn()
        {
            // Cleared or the pool holds the last target alive, and a stale registry would let a
            // recycled projectile damage enemies from a previous level. The definition goes with
            // them: a recycled projectile is a different shot, and Configure supplies all three.
            target = null;
            registry = null;
            definition = null;
            damage = 0;
        }

        void Impact()
        {
            if (definition.ImpactRadius > 0f)
            {
                // Splash only, never splash *plus* a direct hit: the aimed-at enemy sits at the
                // centre of the radius, so damaging it separately would hit it twice.
                registry?.DamageWithin(aimPoint, definition.ImpactRadius, damage);
            }
            else if (target != null && target.IsTargetable)
            {
                target.TakeDamage(damage);
            }

            // Never releases itself. The owner's loop does, exactly as it does for Enemy -- an
            // object that returns itself to a pool it does not own is how double releases start.
            IsFinished = true;
        }

        void MoveTo(Vector2 position, Vector2 heading)
        {
            cachedTransform.position = position;

            // Elongated sprites (fire_long, the missiles) read as backwards without this. Guarded
            // because atan2 of a zero vector is meaningless, which happens on the final step into
            // the aim point.
            if (heading.sqrMagnitude > 0.000001f)
            {
                float degrees = Mathf.Atan2(heading.y, heading.x) * Mathf.Rad2Deg;
                cachedTransform.rotation = Quaternion.Euler(0f, 0f, degrees - 90f);
            }
        }
    }
}
