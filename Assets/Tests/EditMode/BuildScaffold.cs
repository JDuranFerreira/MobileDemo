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

        // Sprites and textures are assets, not GameObjects, so destroying Root does not take them
        // with it -- one leaked per level template for the rest of the editor session otherwise
        // (§14).
        readonly List<Object> mapAssets = new List<Object>();

        public BuildScaffold()
        {
            Root = new GameObject("BuildScaffold");

            ProjectilePrefab = new GameObject("TestProjectile").AddComponent<Projectile>();
            ProjectilePrefab.transform.SetParent(Root.transform);

            ProjectileDefinition = ScriptableObject.CreateInstance<ProjectileDefinition>();
            ProjectileDefinition.name = "TestProjectileDefinition";
            SerializedFields.Set(ProjectileDefinition, "speed", 20f);
            SerializedFields.Set(ProjectileDefinition, "prefab", ProjectilePrefab);

            Green = NewDefinition("Green", GreenCost);
            Red = NewDefinition("Red", RedCost);

            Catalogue = ScriptableObject.CreateInstance<TowerCatalogue>();
            SerializedFields.Set(Catalogue, "buildable", new[] { Green, Red });

            TowerPrefab = new GameObject("TowerPrefab").AddComponent<Tower>();
            TowerPrefab.transform.SetParent(Root.transform);

            Economy = new Economy(20, 100);
            Economy.Subscribe();

            Registry = new EnemyRegistry(_ => true);
            Projectiles = new ProjectileFactory(new[] { ProjectileDefinition }, 4, Root.transform);
            Towers = new TowerFactory(TowerPrefab, Registry, Projectiles, ScanInterval);

            // The level is loaded through a LevelRunner rather than used directly, because that is
            // how the game reaches one: BuildController reads Current and Rules per tap so a swap
            // moves one field instead of rebuilding the invoker. The template below is authored to
            // the same geometry the literals above describe -- a 16x16 board and a road along
            // y = 0 -- so the rules the runner builds are the rules these fixtures used to hand it.
            Levels = new LevelRunner(
                new[] { NewLevelTemplate("LevelTemplate", Road) },
                Towers,
                RoadClearance,
                TowerSpacing,
                Root.transform);
            Levels.Load(0);
        }

        public GameObject Root { get; }

        public LevelRunner Levels { get; }

        /// <summary>The live level — the runner's instance, not the template it was cloned from.</summary>
        public Level Level => Levels.Current;

        public PlacementRules Rules => Levels.Rules;

        public Economy Economy { get; }

        public EnemyRegistry Registry { get; }

        public ProjectileFactory Projectiles { get; }

        public TowerFactory Towers { get; }

        public TowerCatalogue Catalogue { get; }

        public TowerDefinition Green { get; }

        public TowerDefinition Red { get; }

        public Tower TowerPrefab { get; }

        public Projectile ProjectilePrefab { get; }

        public ProjectileDefinition ProjectileDefinition { get; }

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
            for (int i = 0; i < mapAssets.Count; i++)
            {
                DestroyAsset(mapAssets[i]);
            }

            mapAssets.Clear();

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
            SerializedFields.Set(definition, "damage", 1);
            SerializedFields.Set(definition, "projectile", ProjectileDefinition);
            return definition;
        }

        /// <summary>
        /// A level for a <see cref="LevelRunner"/> to clone: a map sprite that gives
        /// <see cref="Level.Bounds"/> a real extent, and an <see cref="EnemyPath"/> carrying
        /// <paramref name="road"/> as waypoint transforms so the runner's bake reproduces that
        /// array exactly.
        /// </summary>
        /// <remarks>
        /// The sprite is one pixel per unit, which is what makes the bounds arithmetic literal
        /// rather than a scale factor to remember: a size of 16 is a 16-unit board centred on the
        /// level's origin.
        /// </remarks>
        public Level NewLevelTemplate(string name, Vector2[] road, float size = 16f)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(Root.transform);

            int pixels = Mathf.RoundToInt(size);
            Texture2D texture = new Texture2D(pixels, pixels);
            Sprite sprite = Sprite.Create(
                texture, new Rect(0f, 0f, pixels, pixels), new Vector2(0.5f, 0.5f), 1f);
            mapAssets.Add(texture);
            mapAssets.Add(sprite);

            SpriteRenderer map = new GameObject("Map").AddComponent<SpriteRenderer>();
            map.transform.SetParent(go.transform);
            map.sprite = sprite;

            EnemyPath path = new GameObject("Path").AddComponent<EnemyPath>();
            path.transform.SetParent(go.transform);

            Transform[] waypoints = new Transform[road.Length];
            for (int i = 0; i < road.Length; i++)
            {
                GameObject waypoint = new GameObject($"Waypoint{i:00}");
                waypoint.transform.SetParent(path.transform);
                waypoint.transform.position = road[i];
                waypoints[i] = waypoint.transform;
            }

            SerializedFields.Set(path, "waypoints", waypoints);

            Level created = go.AddComponent<Level>();
            SerializedFields.Set(created, "map", map);
            SerializedFields.Set(created, "path", path);
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
