using System.Collections.Generic;
using MobileDemo.Core.Interfaces;
using MobileDemo.Core.Pooling;
using UnityEngine;

namespace MobileDemo.Gameplay.Enemies
{
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
            spriteRenderer.sprite = definition.Sprite;

            SetState(spawning);
        }

        // `?.` is safe on IGameState -- it is a plain interface, not a UnityEngine.Object.
        public void Tick(float dt) => current?.Tick(dt);

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
        }

        internal EnemyDefinition Definition { get; private set; }

        internal IReadOnlyList<Vector2> Waypoints { get; private set; }

        internal Vector2 Position => cachedTransform.position;

        internal void MoveTo(Vector2 position) => cachedTransform.position = position;

        internal void EnterMoving() => SetState(moving);

        internal void EnterDying() => SetState(dying);

        internal void MarkFinished() => IsFinished = true;

        void SetState(IGameState next)
        {
            current?.Exit();
            current = next;
            current?.Enter();
        }
    }
}
