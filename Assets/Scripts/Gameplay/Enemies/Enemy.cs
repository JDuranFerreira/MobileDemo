using System.Collections.Generic;
using MobileDemo.Core.Interfaces;
using MobileDemo.Core.Pooling;
using UnityEngine;

namespace MobileDemo.Gameplay.Enemies
{
    // Why the cause travels with the transition rather than being inferred: EnemyDyingState has
    // one Enter() and two very different announcements to make, and only the caller knows which.
    public enum EnemyDeathCause
    {
        Leaked,
        Killed
    }

    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class Enemy : MonoBehaviour, IPoolable
    {
        SpriteRenderer spriteRenderer;
        Transform cachedTransform;
        EnemySpawningState spawning;
        EnemyMovingState moving;
        EnemyDyingState dying;
        IGameState current;

        public bool IsFinished { get; private set; }

        // Only an enemy in the Moving state can be shot. Spawning is the placed-but-not-moving
        // window, and giving it the not-yet-targetable rule is what turns that ~0.15 s from a
        // cosmetic delay into a gameplay one. Dying and finished are self-evident.
        //
        // The null check is not redundant: before Initialize runs, `current` and `moving` are
        // both null and `current == moving` is true -- so an uninitialized enemy would report
        // itself shootable. Awake hides that in play mode; a pooled instance between Get() and
        // Configure(), and any EditMode test, does not.
        public bool IsTargetable => current != null && current == moving;

        void Awake() => Initialize();
        void Initialize()
        {
            if (spawning != null)
            {
                return;
            }

            cachedTransform = transform;
            spriteRenderer = GetComponent<SpriteRenderer>();

            // Built once at load, never in OnSpawn: a full-pool spawn burst would otherwise
            // allocate three of these per enemy mid-wave.
            spawning = new EnemySpawningState(this);
            moving = new EnemyMovingState(this);
            dying = new EnemyDyingState(this);
        }

        public void Configure(EnemyDefinition definition, IReadOnlyList<Vector2> waypoints)
        {
            Initialize();

            Definition = definition;
            Waypoints = waypoints;
            CurrentHealth = definition.MaxHealth;
            spriteRenderer.sprite = definition.Sprite;

            SetState(spawning);
        }

        // `?.` is safe on IGameState -- it is a plain interface, not a UnityEngine.Object.
        public void Tick(float dt) => current?.Tick(dt);

        // Ignored unless targetable, which also covers the double-kill a splash can cause: two
        // projectiles landing in the same frame would otherwise both push health below zero and
        // both transition to Dying, publishing EnemyKilled twice for one enemy.
        public void TakeDamage(int amount)
        {
            if (amount <= 0 || !IsTargetable)
            {
                return;
            }

            CurrentHealth -= amount;
            if (CurrentHealth <= 0)
            {
                CurrentHealth = 0;
                EnterDying(EnemyDeathCause.Killed);
            }
        }

        public void OnSpawn()
        {
            IsFinished = false;
            SetState(null);
        }

        public void OnDespawn()
        {
            SetState(null);
            Definition = null;
            Waypoints = null;
            CurrentHealth = 0;
        }

        public int CurrentHealth { get; private set; }

        internal EnemyDefinition Definition { get; private set; }

        internal IReadOnlyList<Vector2> Waypoints { get; private set; }

        internal Vector2 Position => cachedTransform.position;

        internal void MoveTo(Vector2 position) => cachedTransform.position = position;

        internal void EnterMoving() => SetState(moving);

        // The cause is stashed rather than passed, because IGameState.Enter() takes no argument
        // and widening it for one state would cost every other state a parameter it ignores.
        internal void EnterDying(EnemyDeathCause cause)
        {
            DeathCause = cause;
            SetState(dying);
        }

        internal EnemyDeathCause DeathCause { get; private set; }

        internal void MarkFinished() => IsFinished = true;

        void SetState(IGameState next)
        {
            current?.Exit();
            current = next;
            current?.Enter();
        }
    }
}
