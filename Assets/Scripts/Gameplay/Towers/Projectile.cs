using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    // Tuning lives here on the prefab rather than on a ProjectileDefinition asset, for the same
    // reason Level's path does (ARCHITECTURE.md §7): the prefab already has to exist to carry the
    // sprite, so an asset beside it would be a second artifact to keep in sync and a real failure
    // mode -- a definition pointing at the wrong prefab. A projectile type *is* its prefab.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class Projectile : MonoBehaviour, IPoolable
    {
        [SerializeField] float speed = 8f;
        [SerializeField] int damage = 1;

        [Tooltip("0 means single-target. Above 0, everything within this world-space radius of "
            + "the impact point is damaged -- including the enemy that was aimed at.")]
        [SerializeField] float impactRadius;

        Transform cachedTransform;
        EnemyRegistry registry;
        Enemy target;
        Vector2 aimPoint;

        public bool IsFinished { get; private set; }

        void Awake() => Initialize();

        // Idempotent and called from Configure as well as Awake, for the reason §14 records:
        // Awake is not sent outside play mode, so an EditMode test's AddComponent never
        // initializes.
        void Initialize()
        {
            if (cachedTransform == null)
            {
                cachedTransform = transform;
            }
        }

        public void Configure(Enemy enemy, EnemyRegistry enemies)
        {
            Initialize();

            registry = enemies;
            target = enemy;

            // Seeded now so a projectile whose target dies on the very first tick still has
            // somewhere to fly, rather than impacting on the tower.
            aimPoint = enemy != null ? enemy.Position : (Vector2)cachedTransform.position;
        }

        public void Tick(float dt)
        {
            if (IsFinished)
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
            Vector2 next = Vector2.MoveTowards(position, aimPoint, speed * dt);
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
            // recycled projectile damage enemies from a previous level.
            target = null;
            registry = null;
        }

        void Impact()
        {
            if (impactRadius > 0f)
            {
                // Splash only, never splash *plus* a direct hit: the aimed-at enemy sits at the
                // centre of the radius, so damaging it separately would hit it twice.
                registry?.DamageWithin(aimPoint, impactRadius, damage);
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
