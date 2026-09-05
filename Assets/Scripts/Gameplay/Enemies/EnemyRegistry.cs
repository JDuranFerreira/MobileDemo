using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileDemo.Gameplay.Enemies
{
    // The answer to "which enemies are alive right now", which three callers now need: Bootstrap
    // to tick and release them, Tower to scan for a target, and Projectile to splash at impact.
    // ObjectPool<T> deliberately does not expose its active set -- asking it to would make it a
    // registry -- so the list lives here instead. These are the same ten lines
    // systems/bootstrap.md says WaveRunner inherits; a plain class is what lets it inherit them
    // outright rather than reimplementing them.
    public sealed class EnemyRegistry
    {
        readonly List<Enemy> active = new List<Enemy>();
        readonly Func<Enemy, bool> release;

        public EnemyRegistry(Func<Enemy, bool> release)
        {
            this.release = release ?? throw new ArgumentNullException(nameof(release));
        }

        // The live list itself, not a copy: Tower scans it every 0.1 s per tower and Projectile
        // walks it on every splash, so a defensive copy would allocate on exactly the per-frame
        // path ARCHITECTURE.md §10 polices. Read-only is what makes handing it out safe.
        public IReadOnlyList<Enemy> Active => active;

        public void Add(Enemy enemy)
        {
            if (enemy == null)
            {
                throw new ArgumentNullException(nameof(enemy));
            }

            active.Add(enemy);
        }

        // Backwards, because a finished enemy is removed as we go.
        public void Tick(float dt)
        {
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Enemy enemy = active[i];
                enemy.Tick(dt);
                if (enemy.IsFinished)
                {
                    active.RemoveAt(i);
                    release(enemy);
                }
            }
        }

        // Nearest targetable enemy within range, or null. An index loop over squared distances:
        // no LINQ and no Sqrt on a path that runs once per tower per scan interval (§9, §10).
        public Enemy FindNearest(Vector2 from, float range)
        {
            float bestSqr = range * range;
            Enemy best = null;

            for (int i = 0; i < active.Count; i++)
            {
                Enemy candidate = active[i];
                if (!candidate.IsTargetable)
                {
                    continue;
                }

                float sqr = (candidate.Position - from).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        // Splash. Separate from FindNearest rather than a shared "query" helper taking a
        // predicate: two loops of six lines are cheaper to read than one indirection, and a
        // delegate per call would allocate on the impact path.
        public void DamageWithin(Vector2 centre, float radius, int damage)
        {
            float radiusSqr = radius * radius;

            // Backwards: TakeDamage can finish an enemy, and although the removal happens in
            // Tick rather than here, iterating backwards costs nothing and survives that
            // changing.
            for (int i = active.Count - 1; i >= 0; i--)
            {
                Enemy candidate = active[i];
                if (!candidate.IsTargetable)
                {
                    continue;
                }

                if ((candidate.Position - centre).sqrMagnitude <= radiusSqr)
                {
                    candidate.TakeDamage(damage);
                }
            }
        }
    }
}
