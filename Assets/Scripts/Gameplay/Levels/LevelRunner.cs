using System;
using System.Collections.Generic;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Gameplay.Levels
{
    // Swaps in the next map. §5's last name-only row, and the type §4 names as the reason
    // VictoryState is not terminal for maps 1 and 2.
    //
    // A plain class, not a MonoBehaviour, for §9's reason: it is driven by a state rather than by
    // Unity, and it is constructible in an EditMode test with two prefabs and no scene.
    //
    // It owns everything *derived* from which map is current -- the live instance, the placement
    // rules built from that instance's geometry, and its waypoints. That is what stops the swap
    // being a rebuild of half the composition root: BuildController, WaveState and VictoryState
    // hold this object rather than a Level, so a swap moves one field instead of re-wiring three
    // collaborators.
    public sealed class LevelRunner
    {
        readonly Level[] prefabs;
        readonly TowerFactory towers;
        readonly float roadClearance;
        readonly float towerSpacing;
        readonly Transform parent;

        public LevelRunner(
            Level[] prefabs, TowerFactory towers, float roadClearance, float towerSpacing,
            Transform parent)
        {
            this.prefabs = prefabs ?? throw new ArgumentNullException(nameof(prefabs));
            this.towers = towers ?? throw new ArgumentNullException(nameof(towers));
            this.roadClearance = roadClearance;
            this.towerSpacing = towerSpacing;
            this.parent = parent;

            if (prefabs.Length == 0)
            {
                throw new ArgumentException("A run needs at least one level.", nameof(prefabs));
            }
        }

        /// <summary>The live level, or null before the first <see cref="Load"/>.</summary>
        public Level Current { get; private set; }

        /// <summary>Built fresh for <see cref="Current"/>, because the geometry is per level.</summary>
        public PlacementRules Rules { get; private set; }

        /// <summary>Index of <see cref="Current"/> in the authored sequence, or -1 before the first load.</summary>
        public int Index { get; private set; } = -1;

        public int Count => prefabs.Length;

        public bool HasNext => Index + 1 < prefabs.Length;

        // Exposed here rather than left to callers chaining Current.Path.Waypoints, because a level
        // whose path reference is unassigned would make that chain a NullReferenceException at a
        // call site that has no business knowing a level has a Path component. Bootstrap.Start is
        // what refuses to run against an unusable path, with one legible error.
        public IReadOnlyList<Vector2> Waypoints =>
            Current != null && Current.Path != null
                ? Current.Path.Waypoints
                : Array.Empty<Vector2>();

        public bool Advance() => HasNext && Load(Index + 1);

        public bool Load(int index)
        {
            if (index < 0 || index >= prefabs.Length)
            {
                Debug.LogError(
                    $"LevelRunner has no level {index} to load: the run defines {prefabs.Length}.");
                return false;
            }

            if (prefabs[index] == null)
            {
                Debug.LogError($"LevelRunner's level {index} has no prefab assigned.");
                return false;
            }

            // Destroyed before the next is instantiated, so two maps are never on screen together
            // and the outgoing level's towers -- its children, authored and player-placed alike --
            // go with it. §6's reason for the live tower list living on Level rather than in a
            // registry: the lifetime question is answered by construction.
            DestroyCurrent();

            Current = UnityEngine.Object.Instantiate(prefabs[index], parent);

            // Named for the prefab, because "(Clone)" in an error message names nothing. The three
            // Bootstrap.Start validations quote this.
            Current.name = prefabs[index].name;
            Index = index;

            // Explicit, and load bearing. Instantiate does run the prefab's Awake -- and so its
            // bake -- but level.md's gotcha is that a swapper must not depend on that: the bake is
            // a cache, and the rules built two lines below read the result of it. One call here is
            // cheaper than a rule silently built from a stale or empty array.
            if (Current.Path != null)
            {
                Current.Path.Bake();
            }

            // Both jobs Bootstrap used to do once, at boot, for the one level that existed. They
            // are per level, which is the whole reason those two rows of bootstrap.md's successor
            // table named this type.
            ConfigureTowers();
            Rules = new PlacementRules(Waypoints, Current.Bounds, roadClearance, towerSpacing);
            return true;
        }

        // Every prefab's towers, not just the loaded level's -- and that is the difference between
        // this collecting and Bootstrap's old loop. ProjectileFactory is prewarmed once at boot
        // from exactly the definitions it is handed and returns null for any other, so a tower
        // authored on map 3 whose projectile was never collected would render, aim, and silently
        // never fire. §7 gives that failure as the reason TowerCatalogue exists; a second map makes
        // it reachable a second way.
        //
        // Reading Towers on a prefab *asset* seeds that component's runtime list from its own
        // serialized array and touches nothing serialized, so it neither dirties the asset nor
        // affects the instance a later Load makes.
        // Static, and called with the raw prefab array, because of a construction order this type
        // would otherwise sit in the middle of: the pools are prewarmed from these definitions, the
        // TowerFactory needs the pools, and this runner needs the TowerFactory to configure a
        // level's towers. Asking the question of the prefabs rather than of a constructed runner is
        // what unties that knot -- and the answer is a property of the authored levels, not of the
        // run in progress.
        public static void CollectProjectileDefinitions(
            IReadOnlyList<Level> prefabs, List<ProjectileDefinition> collected)
        {
            if (prefabs == null)
            {
                throw new ArgumentNullException(nameof(prefabs));
            }

            if (collected == null)
            {
                throw new ArgumentNullException(nameof(collected));
            }

            for (int i = 0; i < prefabs.Count; i++)
            {
                if (prefabs[i] == null)
                {
                    continue;
                }

                IReadOnlyList<Tower> authored = prefabs[i].Towers;
                for (int j = 0; j < authored.Count; j++)
                {
                    TowerDefinition tower = authored[j] != null ? authored[j].Definition : null;
                    ProjectileDefinition projectile = tower != null ? tower.Projectile : null;
                    if (projectile != null && !collected.Contains(projectile))
                    {
                        collected.Add(projectile);
                    }
                }
            }
        }

        void ConfigureTowers()
        {
            IReadOnlyList<Tower> authored = Current.Towers;
            for (int i = 0; i < authored.Count; i++)
            {
                towers.Configure(authored[i]);
            }
        }

        // TowerFactory.Destroy's branch, and the same test-shaped concession recorded there (§14):
        // outside play mode Destroy defers to a frame that never arrives, so an EditMode test would
        // leak a whole level per swap.
        void DestroyCurrent()
        {
            if (Current == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(Current.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(Current.gameObject);
            }

            Current = null;
        }
    }
}
