using System.Collections.Generic;
using MobileDemo.Core.Config;
using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Levels;
using UnityEngine;

namespace MobileDemo.Gameplay
{
    public sealed class Bootstrap : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] Enemy enemyPrefab;
        [SerializeField] EnemyDefinition enemyDefinition;
        [SerializeField] Level level;

        // Deliberately not inside the level prefab: pooled enemies have to outlive a level swap.
        [SerializeField] Transform poolParent;

        // Scene value rather than a GameConfig constant: this is wave data, and WaveDefinition
        // absorbs it.
        [SerializeField] float spawnIntervalSeconds = 2f;

        readonly List<Enemy> live = new List<Enemy>();
        Economy economy;
        EnemyFactory factory;
        float spawnTimer;

        void Awake()
        {
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }

            Application.targetFrameRate = config.TargetFrameRate;

            economy = new Economy(config.StartingLives);
            factory = new EnemyFactory(
                new ObjectPool<Enemy>(enemyPrefab, config.EnemyPoolPrewarm, poolParent));
        }

        void OnEnable() => economy?.Subscribe();

        void OnDisable() => economy?.Unsubscribe();

        // Awake composes, Start announces: every OnEnable has run by the first Start, so the HUD
        // sees the opening lives value without racing another GameObject's Awake. The path is
        // never read before Start either, because it bakes in its own Awake.
        void Start()
        {
            if (level.Path == null || level.Path.Waypoints.Count < 2)
            {
                // EnemyPath has already logged an unusable array. Refusing to spawn is what stops
                // that one message becoming an exception every couple of seconds.
                Debug.LogError(
                    $"Bootstrap on '{name}' is disabled: level '{level.name}' has no usable path.",
                    this);
                enabled = false;
                return;
            }

            economy.PublishCurrentState();
        }

        void Update()
        {
            float dt = Time.deltaTime;
            TickSpawner(dt);
            TickEnemies(dt);
        }

        void OnDestroy()
        {
            if (factory == null)
            {
                return;
            }

            // PeakActive after a run is the number prewarm should be tuned to, so it has to be
            // readable after a run.
            IPoolStats stats = factory.Stats;
            Debug.Log(
                $"Pool '{stats.Name}': PeakActive={stats.PeakActive}, "
                + $"InstanceCount={stats.InstanceCount}, Prewarm={stats.Prewarm}");
        }

        void TickSpawner(float dt)
        {
            spawnTimer += dt;
            if (spawnTimer < spawnIntervalSeconds)
            {
                return;
            }

            // Repeating rather than one enemy: a repeating spawn is what proves the pool
            // recycles. Nothing stops at zero lives -- the defeat check is the phase machine's.
            spawnTimer -= spawnIntervalSeconds;
            live.Add(factory.Create(enemyDefinition, level.Path.Waypoints));
        }

        void TickEnemies(float dt)
        {
            // Backwards, because a finished enemy is removed as we go.
            for (int i = live.Count - 1; i >= 0; i--)
            {
                Enemy enemy = live[i];
                enemy.Tick(dt);
                if (enemy.IsFinished)
                {
                    live.RemoveAt(i);
                    factory.Release(enemy);
                }
            }
        }

        // Deliberately does not short-circuit, so one run reports every missing reference.
        bool HasRequiredReferences()
        {
            bool ok = true;
            ok &= Require(config, nameof(config));
            ok &= Require(enemyPrefab, nameof(enemyPrefab));
            ok &= Require(enemyDefinition, nameof(enemyDefinition));
            ok &= Require(level, nameof(level));
            ok &= Require(poolParent, nameof(poolParent));
            return ok;
        }

        bool Require(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"Bootstrap on '{name}' has no {fieldName} assigned.", this);
            return false;
        }
    }
}
