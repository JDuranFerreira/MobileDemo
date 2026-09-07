using System;
using System.Collections.Generic;
using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    // One pool per prefab, not one pool per projectile *type*. A type is a ProjectileDefinition
    // asset and every type shares one Projectile.prefab, so today that means one pool -- but the
    // keying is still by prefab, because that is what a pool actually hands back: an instance of
    // the prefab it was built from. systems/enemy-factory.md records the same lesson for enemies.
    // Two definitions naming one prefab share its pool, which is now the normal case rather than
    // the exception.
    public sealed class ProjectileFactory
    {
        readonly Dictionary<Projectile, ObjectPool<Projectile>> poolsByPrefab;
        readonly List<IPoolStats> stats = new List<IPoolStats>();

        // Which pool an instance came from. The alternative -- a field on Projectile pointing at
        // its own pool -- would make the projectile know it is pooled, which is exactly what
        // EnemyFactory's private pool avoids. One dictionary entry per live projectile is the
        // price, and it is paid on spawn/despawn rather than per frame.
        readonly Dictionary<Projectile, ObjectPool<Projectile>> poolsByInstance =
            new Dictionary<Projectile, ObjectPool<Projectile>>();

        // The live set lives here rather than in a ProjectileRegistry beside EnemyRegistry,
        // because the two halves of that type would find no second owner: nothing ever asks
        // "which projectiles are in flight" the way towers ask it of enemies. What is left is a
        // tick-and-release loop over instances this class already maps to their pools, so
        // splitting it out would be a type built to hold one loop.
        readonly List<Projectile> live = new List<Projectile>();

        // Definitions are supplied up front and their prefabs prewarmed here, deliberately not
        // created lazily on the first shot: a pool built mid-wave would allocate its whole prewarm
        // in one frame -- the exact spike ARCHITECTURE.md §2 calls pooling mandatory to prevent.
        public ProjectileFactory(
            IReadOnlyList<ProjectileDefinition> definitions, int prewarm, Transform parent = null)
        {
            if (definitions == null)
            {
                throw new ArgumentNullException(nameof(definitions));
            }

            poolsByPrefab = new Dictionary<Projectile, ObjectPool<Projectile>>(definitions.Count);

            for (int i = 0; i < definitions.Count; i++)
            {
                ProjectileDefinition definition = definitions[i];
                Projectile prefab = definition != null ? definition.Prefab : null;
                if (prefab == null || poolsByPrefab.ContainsKey(prefab))
                {
                    // Two definitions sharing a prefab is the normal case now, not an error --
                    // they share the pool. A null is the caller's problem and Create reports it.
                    continue;
                }

                ObjectPool<Projectile> pool = new ObjectPool<Projectile>(prefab, prewarm, parent);
                poolsByPrefab.Add(prefab, pool);
                stats.Add(pool);
            }
        }

        // Plural, unlike EnemyFactory.Stats -- this is the collection of unrelated closed pool
        // types that IPoolStats exists to make holdable at all.
        public IReadOnlyList<IPoolStats> Stats => stats;

        // damage is the firing tower's figure, passed through rather than looked up: the factory
        // has no opinion about it, and the projectile scales it by its definition's multiplier.
        public Projectile Create(
            ProjectileDefinition definition, Vector2 origin, Enemy target, EnemyRegistry enemies,
            int damage)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            Projectile prefab = definition.Prefab;
            if (prefab == null || !poolsByPrefab.TryGetValue(prefab, out ObjectPool<Projectile> pool))
            {
                // Not thrown: a tower wired to a definition the factory was never told about
                // should cost that tower its shots, not the whole run.
                Debug.LogError(
                    $"ProjectileFactory has no pool for '{definition.name}'. Its prefab must be "
                    + "reachable from a definition passed to the constructor so it can be "
                    + "prewarmed.");
                return null;
            }

            Projectile projectile = pool.Get();
            projectile.transform.position = origin;
            projectile.Configure(definition, target, enemies, damage);
            poolsByInstance[projectile] = pool;
            live.Add(projectile);
            return projectile;
        }

        // Backwards, because a finished projectile is removed as we go -- the same shape
        // EnemyRegistry.Tick uses.
        public void Tick(float dt)
        {
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Projectile projectile = live[i];
                projectile.Tick(dt);
                if (projectile.IsFinished)
                {
                    live.RemoveAt(i);
                    Release(projectile);
                }
            }
        }

        public bool Release(Projectile projectile)
        {
            if (projectile == null || !poolsByInstance.TryGetValue(projectile, out ObjectPool<Projectile> pool))
            {
                return false;
            }

            poolsByInstance.Remove(projectile);
            return pool.Release(projectile);
        }
    }
}
