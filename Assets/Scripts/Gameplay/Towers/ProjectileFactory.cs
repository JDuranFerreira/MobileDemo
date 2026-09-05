using System;
using System.Collections.Generic;
using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    // One pool per prefab, not one pool for all projectiles. A pool hands back an instance of the
    // prefab it was built from, and a projectile's speed, damage and impact radius are serialized
    // on that prefab -- so two projectile types cannot share a pool without handing a fire tower
    // a bullet. systems/enemy-factory.md already records the same lesson for enemies: a second
    // prefab means a second pool.
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

        // Prefabs are supplied up front and prewarmed here, deliberately not created lazily on
        // the first shot: a pool built mid-wave would allocate its whole prewarm in one frame --
        // the exact spike ARCHITECTURE.md §2 calls pooling mandatory to prevent.
        public ProjectileFactory(IReadOnlyList<Projectile> prefabs, int prewarm, Transform parent = null)
        {
            if (prefabs == null)
            {
                throw new ArgumentNullException(nameof(prefabs));
            }

            poolsByPrefab = new Dictionary<Projectile, ObjectPool<Projectile>>(prefabs.Count);

            for (int i = 0; i < prefabs.Count; i++)
            {
                Projectile prefab = prefabs[i];
                if (prefab == null || poolsByPrefab.ContainsKey(prefab))
                {
                    // Two towers sharing a projectile type is normal, not an error -- they share
                    // the pool. A null is the caller's problem and Create reports it.
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

        public Projectile Create(Projectile prefab, Vector2 origin, Enemy target, EnemyRegistry enemies)
        {
            if (prefab == null)
            {
                throw new ArgumentNullException(nameof(prefab));
            }

            if (!poolsByPrefab.TryGetValue(prefab, out ObjectPool<Projectile> pool))
            {
                // Not thrown: a tower wired to a prefab the factory was never told about should
                // cost that tower its shots, not the whole run.
                Debug.LogError(
                    $"ProjectileFactory has no pool for '{prefab.name}'. It must be passed to the "
                    + "constructor so it can be prewarmed.");
                return null;
            }

            Projectile projectile = pool.Get();
            projectile.transform.position = origin;
            projectile.Configure(target, enemies);
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
