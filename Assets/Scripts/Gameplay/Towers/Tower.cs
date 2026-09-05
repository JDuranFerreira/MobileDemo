using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    // Not pooled, deliberately (ARCHITECTURE.md §6): a handful exist for the whole round and are
    // placed by hand, so pooling them would add lifecycle complexity for zero benefit.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class Tower : MonoBehaviour
    {
        [SerializeField] TowerDefinition definition;

        SpriteRenderer spriteRenderer;
        Transform cachedTransform;
        EnemyRegistry registry;
        ProjectileFactory projectiles;
        Enemy target;
        float scanInterval;
        float scanTimer;
        float reloadTimer;

        public TowerDefinition Definition => definition;

        void Awake() => Initialize();

        // Idempotent, and called from Configure too -- Awake is not sent outside play mode, so an
        // EditMode test's AddComponent never initializes. §14 records this as a test-shaped
        // concession, already paid for once by Enemy.
        void Initialize()
        {
            if (cachedTransform != null)
            {
                return;
            }

            cachedTransform = transform;
            spriteRenderer = GetComponent<SpriteRenderer>();
        }

        // The definition is an argument rather than only a serialized field, so a tower
        // instantiated at runtime can be told what it is -- Enemy.Configure's shape, for the same
        // reason. One contract and no overload: a tower authored inside a level prefab re-passes
        // its own Definition, which is what TowerFactory.Configure does. No setter on the
        // property, so this stays the only way in.
        public void Configure(
            TowerDefinition towerDefinition, EnemyRegistry enemies, ProjectileFactory factory,
            float scanIntervalSeconds)
        {
            Initialize();

            definition = towerDefinition;
            registry = enemies;
            projectiles = factory;

            // Clamped rather than trusted: a zero interval would turn §9's 10 Hz poll back into a
            // per-frame scan, silently undoing the decision the interval exists to make.
            scanInterval = Mathf.Max(0.01f, scanIntervalSeconds);

            if (definition != null)
            {
                // Same as Enemy: the sprite comes from the definition, which is what lets the two
                // tower types share one prefab and differ only as data.
                spriteRenderer.sprite = definition.Sprite;
            }

            // Staggered so towers placed in the same frame do not all scan on the same frame.
            // Cheap, and it spreads the one measurable cost this system has.
            scanTimer = Random.Range(0f, scanInterval);
        }

        public void Tick(float dt)
        {
            if (definition == null || registry == null || projectiles == null)
            {
                return;
            }

            scanTimer += dt;
            if (scanTimer >= scanInterval)
            {
                scanTimer -= scanInterval;
                target = registry.FindNearest(cachedTransform.position, definition.Range);
            }

            reloadTimer += dt;
            float shotInterval = definition.ShotsPerSecond > 0f ? 1f / definition.ShotsPerSecond : float.MaxValue;
            if (reloadTimer < shotInterval)
            {
                return;
            }

            // Re-checked at the moment of firing, not just at the moment of scanning: up to a
            // scan interval can pass between the two, which is plenty of time for the target to
            // die or walk out of range. Without this a tower shoots at corpses.
            if (!IsShootable(target))
            {
                target = null;

                // Capped rather than left to accumulate. A tower idle for ten seconds would
                // otherwise bank ten seconds of reload and empty it into the first enemy to walk
                // in range, one shot per frame. Held at exactly one shot: ready, not stockpiled.
                reloadTimer = shotInterval;
                return;
            }

            // Not zeroed: subtracting keeps the average rate honest when dt overshoots the
            // interval, the same reason Bootstrap's spawn timer subtracts.
            reloadTimer -= shotInterval;
            Fire();
        }

        bool IsShootable(Enemy candidate)
        {
            if (candidate == null || !candidate.IsTargetable)
            {
                return false;
            }

            float range = definition.Range;
            return (candidate.Position - (Vector2)cachedTransform.position).sqrMagnitude <= range * range;
        }

        void Fire()
        {
            if (definition.ProjectilePrefab == null)
            {
                return;
            }

            projectiles.Create(definition.ProjectilePrefab, cachedTransform.position, target, registry);
        }
    }
}
