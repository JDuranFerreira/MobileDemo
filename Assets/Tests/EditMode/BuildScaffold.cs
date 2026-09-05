using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // The scaffolding the four build fixtures share: a Level with a map and a road, two tower
    // types, a projectile pool and an Economy. Everything below is buildable with no scene and no
    // play mode, which is what makes the build path testable at all.
    //
    // Held by composition rather than inherited as a base fixture. A base class would have to
    // expose its state as protected fields, and the project's naming rules reserve camelCase for
    // *private* fields -- so the fields would have to be PascalCase, where `Level Level` and
    // `Economy Economy` are exactly the shadowing this codebase already went out of its way to
    // avoid once (see Economy.cs's namespace note). Reached through a `scaffold.` prefix, the
    // names stay unambiguous. It also makes the lifetime explicit instead of depending on NUnit's
    // base-then-derived SetUp order.
    //
    // Awake is not sent outside play mode, so nothing here relies on it: Level seeds its tower
    // list lazily and Tower.Configure initializes explicitly, both by design (§14).
    sealed class BuildScaffold
    {
        public const int GreenCost = 50;
        public const int RedCost = 75;
        public const float RefundFraction = 0.5f;
        public const float RoadClearance = 0.9f;
        public const float TowerSpacing = 0.7f;
        public const float ScanInterval = 0.1f;
        public const float ShotsPerSecond = 2f;
        public const float ShotInterval = 1f / ShotsPerSecond;

        /// <summary>A road along y = 0, so anything a unit above it is clear.</summary>
        public static readonly Vector2[] Road = { new Vector2(-4f, 0f), new Vector2(4f, 0f) };

        /// <summary>Clear of the road and inside the board.</summary>
        public static readonly Vector2 LegalSpot = new Vector2(-2f, 3f);

        /// <summary>A second spot, far enough from <see cref="LegalSpot"/> not to collide.</summary>
        public static readonly Vector2 OtherLegalSpot = new Vector2(2f, 3f);

        readonly List<EnemyDefinition> enemyDefinitions = new List<EnemyDefinition>();

        public BuildScaffold()
        {
            Root = new GameObject("BuildScaffold");

            ProjectilePrefab = new GameObject("TestProjectile").AddComponent<Projectile>();
            ProjectilePrefab.transform.SetParent(Root.transform);
            SerializedFields.Set(ProjectilePrefab, "speed", 20f);
            SerializedFields.Set(ProjectilePrefab, "damage", 1);

            Green = NewDefinition("Green", GreenCost);
            Red = NewDefinition("Red", RedCost);

            Catalogue = ScriptableObject.CreateInstance<TowerCatalogue>();
            SerializedFields.Set(Catalogue, "buildable", new[] { Green, Red });

            TowerPrefab = new GameObject("TowerPrefab").AddComponent<Tower>();
            TowerPrefab.transform.SetParent(Root.transform);

            Level = NewLevel();

            Economy = new Economy(20, 100);
            Economy.Subscribe();

            Registry = new EnemyRegistry(_ => true);
            Projectiles = new ProjectileFactory(new[] { ProjectilePrefab }, 4, Root.transform);
            Towers = new TowerFactory(TowerPrefab, Registry, Projectiles, ScanInterval);
            Rules = new PlacementRules(
                Road,
                new Bounds(Vector3.zero, new Vector3(16f, 16f, 0f)),
                RoadClearance,
                TowerSpacing);
        }

        public GameObject Root { get; }

        public Level Level { get; }

        public Economy Economy { get; }

        public EnemyRegistry Registry { get; }

        public ProjectileFactory Projectiles { get; }

        public TowerFactory Towers { get; }

        public PlacementRules Rules { get; }

        public TowerCatalogue Catalogue { get; }

        public TowerDefinition Green { get; }

        public TowerDefinition Red { get; }

        public Tower TowerPrefab { get; }

        public Projectile ProjectilePrefab { get; }

        /// <summary>Projectiles put in the air, read from the pool rather than a private field.</summary>
        public int ShotsFired => Projectiles.Stats[0].PeakActive;

        public void Dispose()
        {
            Economy?.Unsubscribe();
            EventBus.ClearAll();

            if (Root != null)
            {
                Object.DestroyImmediate(Root);
            }

            DestroyAsset(Green);
            DestroyAsset(Red);
            DestroyAsset(Catalogue);

            // An unparented ScriptableObject otherwise leaks for the whole editor session (§14).
            for (int i = 0; i < enemyDefinitions.Count; i++)
            {
                DestroyAsset(enemyDefinitions[i]);
            }

            enemyDefinitions.Clear();
        }

        public Tower Place(TowerDefinition definition, Vector2 position) =>
            Towers.Create(definition, position, Level.transform);

        /// <summary>
        /// A registered enemy, already past its spawn delay so it is targetable — the shape
        /// TowerTests uses. Its path runs far away so it stays put for the length of a test.
        /// </summary>
        public Enemy AddTargetableEnemyAt(Vector2 position)
        {
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            enemyDefinitions.Add(definition);

            Enemy enemy = new GameObject("Enemy").AddComponent<Enemy>();
            enemy.transform.SetParent(Root.transform);
            enemy.Configure(definition, new[] { position, position + new Vector2(0f, 1000f) });
            enemy.Tick(definition.SpawnDelaySeconds);
            Registry.Add(enemy);
            return enemy;
        }

        /// <summary>
        /// Ticks a tower far enough to acquire and fire once.
        /// </summary>
        /// <remarks>
        /// Two timers have to cross, not one, and ticking only the scan interval is the mistake
        /// this helper exists to stop repeating: <c>Configure</c> staggers the first scan by a
        /// random fraction of the interval, so the scan timer is forced rather than waited out —
        /// but the reload timer starts at zero and the fixture's towers fire twice a second, so a
        /// 0.1 s tick acquires a target and then holds its shot.
        /// </remarks>
        public static void TickUntilItFires(Tower tower)
        {
            SerializedFields.Set(tower, "scanTimer", ScanInterval);
            tower.Tick(ShotInterval);
        }

        public Tower NewBareTower(string name)
        {
            Tower tower = new GameObject(name).AddComponent<Tower>();
            tower.transform.SetParent(Root.transform);
            return tower;
        }

        TowerDefinition NewDefinition(string name, int cost)
        {
            TowerDefinition definition = ScriptableObject.CreateInstance<TowerDefinition>();
            definition.name = name;
            SerializedFields.Set(definition, "cost", cost);
            SerializedFields.Set(definition, "range", 5f);
            SerializedFields.Set(definition, "shotsPerSecond", ShotsPerSecond);
            SerializedFields.Set(definition, "projectilePrefab", ProjectilePrefab);
            return definition;
        }

        /// <summary>
        /// A Level with a real map SpriteRenderer, because Level.Bounds reads it — though the
        /// placement rule here is built from an explicit Bounds so the geometry stays literal.
        /// </summary>
        Level NewLevel()
        {
            GameObject go = new GameObject("Level");
            go.transform.SetParent(Root.transform);

            SpriteRenderer map = new GameObject("Map").AddComponent<SpriteRenderer>();
            map.transform.SetParent(go.transform);

            Level created = go.AddComponent<Level>();
            SerializedFields.Set(created, "map", map);
            SerializedFields.Set(created, "towers", new Tower[0]);
            return created;
        }

        static void DestroyAsset(Object asset)
        {
            if (asset != null)
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
