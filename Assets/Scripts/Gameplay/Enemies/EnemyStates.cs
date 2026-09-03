using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using UnityEngine;

namespace MobileDemo.Gameplay.Enemies
{
    // Internal by default, so MobileDemo.UI and MobileDemo.Editor cannot see them. Exit() is
    // empty in all three: the cost of sharing IGameState with the round's machine, where it does
    // have work to do.

    sealed class EnemySpawningState : IGameState
    {
        readonly Enemy enemy;
        float elapsed;

        internal EnemySpawningState(Enemy enemy) => this.enemy = enemy;

        public void Enter()
        {
            elapsed = 0f;

            // The placement ObjectPool<T>.Get deliberately does not do.
            enemy.MoveTo(enemy.Waypoints[0]);
        }

        public void Tick(float dt)
        {
            elapsed += dt;
            if (elapsed >= enemy.Definition.SpawnDelaySeconds)
            {
                enemy.EnterMoving();
            }
        }

        public void Exit()
        {
        }
    }

    sealed class EnemyMovingState : IGameState
    {
        readonly Enemy enemy;
        int waypointIndex;

        internal EnemyMovingState(Enemy enemy) => this.enemy = enemy;

        // Waypoint 0 is where Spawning placed it. Re-zeroing here is the pooling reset, and the
        // reason these are classes rather than an enum.
        public void Enter() => waypointIndex = 1;

        // Assumes at least two waypoints, so index 1 is valid on the first tick. Not re-checked:
        // arrival at the last waypoint transitions away in the same tick, so this cannot be
        // re-entered out of range. EnemyPath and Bootstrap enforce the minimum.
        public void Tick(float dt)
        {
            // MoveTowards clamps exactly, so `== target` needs no epsilon. It cannot overshoot a
            // waypoint at these speeds; the cost is losing one frame's movement at a corner.
            Vector2 target = enemy.Waypoints[waypointIndex];
            Vector2 next = Vector2.MoveTowards(enemy.Position, target, enemy.Definition.MoveSpeed * dt);
            enemy.MoveTo(next);

            if (next == target)
            {
                waypointIndex++;
                if (waypointIndex >= enemy.Waypoints.Count)
                {
                    enemy.EnterDying();
                }
            }
        }

        public void Exit()
        {
        }
    }

    // Named for what the enemy becomes, not the cause. One publish site, so taking a cause and
    // raising EnemyKilled instead is a small change once towers exist.
    sealed class EnemyDyingState : IGameState
    {
        readonly Enemy enemy;

        internal EnemyDyingState(Enemy enemy) => this.enemy = enemy;

        public void Enter()
        {
            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(enemy.Definition.DamageOnLeak));
            enemy.MarkFinished();
        }

        public void Tick(float dt)
        {
        }

        public void Exit()
        {
        }
    }
}
