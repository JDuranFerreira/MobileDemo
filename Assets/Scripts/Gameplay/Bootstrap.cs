using System.Collections.Generic;
using MobileDemo.Core.Config;
using MobileDemo.Core.Interfaces;
using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Gameplay
{
    public sealed class Bootstrap : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] Enemy enemyPrefab;
        [SerializeField] EnemyDefinition enemyDefinition;
        [SerializeField] Level level;

        // The build phase's three. towerPrefab is the one prefab both tower types share, and
        // catalogue is what makes a runtime placement's projectile pool exist -- see
        // CollectProjectilePrefabs.
        [SerializeField] Camera sceneCamera;
        [SerializeField] Tower towerPrefab;
        [SerializeField] TowerCatalogue catalogue;

        // Deliberately not inside the level prefab: pooled enemies and projectiles have to
        // outlive a level swap.
        [SerializeField] Transform poolParent;

        // Scene value rather than a GameConfig constant: this is wave data, and WaveDefinition
        // absorbs it.
        [SerializeField] float spawnIntervalSeconds = 2f;

        // Enemies get a registry and projectiles do not, which is deliberate: a registry exists
        // so towers can ask "who is alive and in range", and nothing ever asks that of a
        // projectile. ProjectileFactory keeps its own live set instead -- see its note.
        Economy economy;
        EnemyFactory factory;
        EnemyRegistry enemies;
        ProjectileFactory projectiles;
        TowerFactory towerFactory;
        IInputService input;
        BuildController build;
        float spawnTimer;

        void Awake()
        {
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }

            Application.targetFrameRate = config.TargetFrameRate;

            economy = new Economy(config.StartingLives, config.StartingCurrency);
            factory = new EnemyFactory(
                new ObjectPool<Enemy>(enemyPrefab, config.EnemyPoolPrewarm, poolParent));

            // The registry releases through the factory, so the pool still has exactly one owner.
            enemies = new EnemyRegistry(factory.Release);
            projectiles = new ProjectileFactory(
                CollectProjectilePrefabs(), config.ProjectilePoolPrewarm, poolParent);

            towerFactory = new TowerFactory(
                towerPrefab, enemies, projectiles, config.TowerScanIntervalSec);
            input = new PointerInputService(sceneCamera);

            ConfigureTowers();
        }

        void OnEnable()
        {
            economy?.Subscribe();
            build?.Subscribe();
        }

        void OnDisable()
        {
            economy?.Unsubscribe();
            build?.Unsubscribe();
        }

        // Awake composes, Start announces: every OnEnable has run by the first Start, so the HUD
        // sees the opening lives and currency values without racing another GameObject's Awake.
        // The path is never read before Start either, because it bakes in its own Awake.
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

            // A level you cannot build on is now half a game, so an unreadable map is worth the
            // same one legible error the path already gets.
            if (level.Bounds.size == Vector3.zero)
            {
                Debug.LogError(
                    $"Bootstrap on '{name}' is disabled: level '{level.name}' has no map bounds, "
                    + "so nothing could be placed on it.",
                    this);
                enabled = false;
                return;
            }

            // Composed here rather than in Awake, because PlacementRules reads the baked waypoints
            // and EnemyPath bakes them in *its* Awake -- which Unity does not order against this
            // one. The second beneficiary of the same one-line discipline that already keeps the
            // path from being read before Start.
            build = new BuildController(
                input,
                economy,
                level,
                new PlacementRules(
                    level.Path.Waypoints,
                    level.Bounds,
                    config.BuildRoadClearance,
                    config.BuildTowerSpacing),
                towerFactory,
                catalogue,
                config.SellRefundFraction);

            // Subscribed here and not only in OnEnable, because OnEnable has already run by now --
            // it fires before the first Start, when this object did not yet exist. OnEnable still
            // covers every *later* enable, so the pairing survives and nothing subscribes twice.
            build.Subscribe();

            economy.PublishCurrentState();
        }

        // Order matters: enemies move first, then towers scan the positions they moved to, then
        // projectiles fly at those same positions. Ticking towers first would aim every shot one
        // frame stale.
        //
        // Building goes first of all, so a tower added or removed this frame is in the list before
        // anything iterates it. It also takes no dt -- nothing in it is time-based.
        void Update()
        {
            float dt = Time.deltaTime;
            build.Tick();
            TickSpawner(dt);
            enemies.Tick(dt);
            TickTowers(dt);
            projectiles.Tick(dt);
        }

        void OnDestroy()
        {
            // PeakActive after a run is the number prewarm should be tuned to, so it has to be
            // readable after a run.
            if (factory != null)
            {
                LogPoolStats(factory.Stats);
            }

            if (projectiles == null)
            {
                return;
            }

            IReadOnlyList<IPoolStats> projectileStats = projectiles.Stats;
            for (int i = 0; i < projectileStats.Count; i++)
            {
                LogPoolStats(projectileStats[i]);
            }
        }

        static void LogPoolStats(IPoolStats stats) =>
            Debug.Log(
                $"Pool '{stats.Name}': PeakActive={stats.PeakActive}, "
                + $"InstanceCount={stats.InstanceCount}, Prewarm={stats.Prewarm}");

        // Distinct prefabs only -- two towers of the same type share one pool. Built here rather
        // than inside ProjectileFactory because the level is what knows which towers exist.
        //
        // The catalogue's half is not optional garnish. ProjectileFactory is prewarmed once with
        // exactly these prefabs and refuses to build a pool later, so a tower the *player* places
        // whose projectile is not in this list gets null from Create and silently never fires. The
        // level's own towers cover only what was authored; the catalogue covers what can be built.
        List<Projectile> CollectProjectilePrefabs()
        {
            List<Projectile> prefabs = new List<Projectile>();
            IReadOnlyList<Tower> towers = level.Towers;

            for (int i = 0; i < towers.Count; i++)
            {
                TowerDefinition towerDefinition = towers[i] != null ? towers[i].Definition : null;
                Projectile prefab = towerDefinition != null ? towerDefinition.ProjectilePrefab : null;
                if (prefab != null && !prefabs.Contains(prefab))
                {
                    prefabs.Add(prefab);
                }
            }

            catalogue.CollectProjectilePrefabs(prefabs);
            return prefabs;
        }

        // Delegated, so the scan interval and the two collaborators a tower needs are named in one
        // place rather than here as well as in TowerFactory.Create.
        void ConfigureTowers()
        {
            IReadOnlyList<Tower> towers = level.Towers;
            for (int i = 0; i < towers.Count; i++)
            {
                towerFactory.Configure(towers[i]);
            }
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
            enemies.Add(factory.Create(enemyDefinition, level.Path.Waypoints));
        }

        // Backwards, now that the list can change during a round -- the same cheap insurance the
        // enemy loop takes, and it survives a tower being sold from inside a tick.
        void TickTowers(float dt)
        {
            IReadOnlyList<Tower> towers = level.Towers;
            for (int i = towers.Count - 1; i >= 0; i--)
            {
                if (towers[i] != null)
                {
                    towers[i].Tick(dt);
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
            ok &= Require(sceneCamera, nameof(sceneCamera));
            ok &= Require(towerPrefab, nameof(towerPrefab));
            ok &= Require(catalogue, nameof(catalogue));
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
