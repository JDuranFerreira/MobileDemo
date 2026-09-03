using System;
using System.Collections.Generic;
using MobileDemo.Core.Pooling;
using UnityEngine;

namespace MobileDemo.Gameplay.Enemies
{
    public sealed class EnemyFactory
    {
        readonly ObjectPool<Enemy> pool;

        // The pool is injected and kept private, so this is the only type that knows an Enemy is
        // pooled.
        public EnemyFactory(ObjectPool<Enemy> pool)
        {
            if (pool == null)
            {
                throw new ArgumentNullException(nameof(pool));
            }

            this.pool = pool;
        }

        public IPoolStats Stats => pool;

        public Enemy Create(EnemyDefinition definition, IReadOnlyList<Vector2> waypoints)
        {
            // Unlike inside ObjectPool<T>, this `== null` does bind UnityEngine.Object's
            // overloaded operator, because the type is concrete rather than a type parameter.
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            // Get() returns an active instance still at last life's transform and does not
            // configure it, so Configure has to be the very next statement.
            Enemy enemy = pool.Get();
            enemy.Configure(definition, waypoints);
            return enemy;
        }

        public bool Release(Enemy enemy) => pool.Release(enemy);
    }
}
