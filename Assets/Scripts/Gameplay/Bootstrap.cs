using System.Collections.Generic;
using MobileDemo.Core.Config;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Phases;
using MobileDemo.Gameplay.Towers;
using MobileDemo.Gameplay.Waves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace MobileDemo.Gameplay
{
    public sealed class Bootstrap : MonoBehaviour
    {
        [SerializeField] GameConfig config;
        [SerializeField] Enemy enemyPrefab;

        [Tooltip("Played in order. Every one is instantiated by LevelRunner, including the first, "
            + "so no level is authored into the scene.")]
        [SerializeField] Level[] levelPrefabs;

        // The build phase's three. towerPrefab is the one prefab both tower types share, and
        // catalogue is what makes a runtime placement's projectile pool exist -- see
        // CollectProjectileDefinitions.
        [SerializeField] Camera sceneCamera;
        [SerializeField] Tower towerPrefab;
        [SerializeField] TowerCatalogue catalogue;

        // Deliberately not inside the level prefab: pooled enemies and projectiles have to
        // outlive a level swap.
        [SerializeField] Transform poolParent;

        // Enemies get a registry and projectiles do not, which is deliberate: a registry exists
        // so towers can ask "who is alive and in range", and nothing ever asks that of a
        // projectile. ProjectileFactory keeps its own live set instead -- see its note.
        Economy economy;
        EnemyFactory factory;
        EnemyRegistry enemies;
        ProjectileFactory projectiles;
        TowerFactory towerFactory;
        LevelRunner levels;
        IInputService input;
        BuildController build;
        GameStateMachine machine;

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
                CollectProjectileDefinitions(), config.ProjectilePoolPrewarm, poolParent);

            towerFactory = new TowerFactory(
                towerPrefab, enemies, projectiles, config.TowerScanIntervalSec);
            input = new PointerInputService(sceneCamera);

            // The runner is built last of the collaborators because it needs the tower factory,
            // and it loads immediately: the first map is instantiated here rather than authored
            // into the scene, so map 1 is not a special case of the swap.
            levels = new LevelRunner(
                levelPrefabs, towerFactory, config.BuildRoadClearance, config.BuildTowerSpacing,
                null);
            levels.Load(0);
        }

        void OnEnable()
        {
            economy?.Subscribe();
            build?.Subscribe();
            machine?.Subscribe();
            EventBus<RestartRequested>.Subscribe(OnRestartRequested);
        }

        void OnDisable()
        {
            economy?.Unsubscribe();
            build?.Unsubscribe();
            machine?.Unsubscribe();
            EventBus<RestartRequested>.Unsubscribe(OnRestartRequested);

            // The current phase's Exit() has no other caller on the way out. BuildState subscribes
            // in Enter and unsubscribes in Exit, and a scene teardown -- which a restart is -- never
            // reaches Exit on its own, so without this a dead state stays on the bus holding a
            // destroyed Level. EventBus.ClearAll covers the same hazard between play *sessions*;
            // it runs at SubsystemRegistration, which a scene load does not reach.
            machine?.Shutdown();
        }

        // Awake composes, Start announces: every OnEnable has run by the first Start, so the HUD
        // sees the opening lives and currency values without racing another GameObject's Awake.
        // The level itself no longer needs that ordering -- LevelRunner bakes its path as it loads
        // -- so what is left here is validation and the phases that read it.
        void Start()
        {
            Level level = levels.Current;
            if (level == null)
            {
                // LevelRunner has already said which prefab it could not load. Refusing to run is
                // the same answer the three checks below give, for the same reason.
                enabled = false;
                return;
            }

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

            // A level with no waves is the third member of this family, and it arrived with the
            // phase machine: without one there is nothing to tap Go for, so the round would open
            // in a build phase it could never leave. Same answer as the two above -- one legible
            // error rather than a board that looks alive and does nothing.
            if (level.Waves.Count == 0)
            {
                Debug.LogError(
                    $"Bootstrap on '{name}' is disabled: level '{level.name}' defines no waves, "
                    + "so the build phase would have nothing to start.",
                    this);
                enabled = false;
                return;
            }

            // The placement rules that used to be constructed here are LevelRunner's now, built per
            // level -- and the ordering worry that kept this in Start rather than Awake went with
            // them: the runner bakes the path explicitly after instantiating, so the rules never
            // read an array Unity had not got to yet.
            build = new BuildController(
                input, economy, levels, towerFactory, catalogue, config.SellRefundFraction);

            // Subscribed here and not only in OnEnable, because OnEnable has already run by now --
            // it fires before the first Start, when this object did not yet exist. OnEnable still
            // covers every *later* enable, so the pairing survives and nothing subscribes twice.
            build.Subscribe();

            // The wave sequence and the phases, built here for the same reason as the two above:
            // WaveRunner needs the baked waypoints. The states are constructed after the machine
            // because each holds it to transition, and the machine holds none of them until Add --
            // which is why there is no circular-construction problem to solve.
            WaveRunner waves = new WaveRunner(factory, enemies, levels.Waypoints);

            machine = new GameStateMachine();
            WaveState waveState = new WaveState(machine, waves, levels, projectiles);

            machine.Add(GamePhase.Build, new BuildState(machine, build, projectiles));
            machine.Add(GamePhase.Wave, waveState);

            // Victory holds the wave state as well as the runner, because a swap is two facts: a
            // new map, and a wave sequence that starts again at zero. Naming both here is what
            // keeps that ordering explicit rather than a rule two states have to agree on.
            machine.Add(GamePhase.Victory, new VictoryState(machine, levels, waveState));
            machine.Add(GamePhase.Defeat, new DefeatState());

            // Subscribed here rather than only in OnEnable for BuildController's reason: OnEnable
            // has already run by now, when this object did not yet exist. OnEnable still covers
            // every later enable, so nothing subscribes twice.
            machine.Subscribe();

            economy.PublishCurrentState();

            // Last, and after PublishCurrentState: entering Build publishes PhaseChanged, and a
            // HUD that learns the phase before it has any numbers would render a build phase with
            // a blank purse for one frame.
            machine.Change(GamePhase.Build);
        }

        // One call. The tick order that used to live here is now BuildState's and WaveState's,
        // which is the whole point of the slice -- systems/bootstrap.md's Status table said this
        // class had to lose jobs rather than gain them, or be split.
        void Update() => machine.Tick(Time.deltaTime);

        // Reloading the scene rather than resetting each system in place. Everything is rebuilt in
        // Awake, so a Reset on Economy, the pools, the registry and the level's tower list would
        // be five methods that exist only for this, each able to forget a field. The cost is a
        // frame's hitch on a screen where the player has already stopped playing.
        //
        // The one thing that does not rebuild itself is the bus: with domain reload off, statics
        // survive a scene load, so every subscriber has to come off on the way out. OnDisable
        // above is where that happens, and machine.Shutdown() is the part that is easy to miss.
        static void OnRestartRequested(RestartRequested evt) =>
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);

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

        // Distinct definitions only -- two towers of the same type name one projectile type, and
        // the factory de-duplicates again by prefab behind this.
        //
        // Neither half is optional garnish. ProjectileFactory is prewarmed once from exactly these
        // definitions and refuses to build a pool later, so a tower whose projectile is not in this
        // list gets null from Create and silently never fires. LevelRunner's half covers every
        // *authored* tower on every map -- all three, not just the one loaded, because the pools
        // outlive the swap and are built before the third map is ever instantiated. The catalogue's
        // half covers what the *player* can build.
        List<ProjectileDefinition> CollectProjectileDefinitions()
        {
            List<ProjectileDefinition> collected = new List<ProjectileDefinition>();
            LevelRunner.CollectProjectileDefinitions(levelPrefabs, collected);
            catalogue.CollectProjectileDefinitions(collected);
            return collected;
        }

        // Deliberately does not short-circuit, so one run reports every missing reference.
        bool HasRequiredReferences()
        {
            bool ok = true;
            ok &= Require(config, nameof(config));
            ok &= Require(enemyPrefab, nameof(enemyPrefab));
            ok &= RequireLevels();
            ok &= Require(poolParent, nameof(poolParent));
            ok &= Require(sceneCamera, nameof(sceneCamera));
            ok &= Require(towerPrefab, nameof(towerPrefab));
            ok &= Require(catalogue, nameof(catalogue));
            return ok;
        }

        // A run with no maps, and a run whose second map is an empty slot, are the same authoring
        // slip caught one frame apart otherwise -- the second only when victory tried to swap into
        // it. Both are reported here, before anything is built.
        bool RequireLevels()
        {
            if (levelPrefabs == null || levelPrefabs.Length == 0)
            {
                Debug.LogError($"Bootstrap on '{name}' has no levelPrefabs assigned.", this);
                return false;
            }

            bool ok = true;
            for (int i = 0; i < levelPrefabs.Length; i++)
            {
                if (levelPrefabs[i] == null)
                {
                    Debug.LogError($"Bootstrap on '{name}' has no prefab in levelPrefabs[{i}].", this);
                    ok = false;
                }
            }

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
