using System;
using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    // The project's third Factory and the first that is *not* also an Object Pool, which is what
    // shows the two patterns were separable rather than one habit. EnemyFactory and
    // ProjectileFactory both wrap a pool because their instances churn; towers do not -- §6
    // declines pooling them outright ("a handful exist for the whole round"), so Create really
    // does Instantiate and Destroy really does destroy.
    //
    // It holds no live list either, which is the other asymmetry. Level owns that, because a
    // runtime-placed tower is the level's child and must not outlive a level swap -- the exact
    // opposite of the pooled enemies that must.
    //
    // What it does own is the wiring. Without it PlaceTowerCommand would take eight constructor
    // arguments, four of them collaborators it has no opinion about.
    public sealed class TowerFactory
    {
        readonly Tower prefab;
        readonly EnemyRegistry enemies;
        readonly ProjectileFactory projectiles;
        readonly float scanIntervalSeconds;

        public TowerFactory(
            Tower prefab, EnemyRegistry enemies, ProjectileFactory projectiles,
            float scanIntervalSeconds)
        {
            this.prefab = prefab != null ? prefab : throw new ArgumentNullException(nameof(prefab));
            this.enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
            this.scanIntervalSeconds = scanIntervalSeconds;
        }

        public Tower Create(TowerDefinition definition, Vector2 position, Transform parent)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            // Qualified: `using System` puts System.Object in scope too, so bare `Object` here is
            // ambiguous.
            Tower tower = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity, parent);
            tower.name = definition.name;
            tower.Configure(definition, enemies, projectiles, scanIntervalSeconds);
            return tower;
        }

        // For a tower authored inside a level prefab: it already knows which type it is, it only
        // needs its collaborators. Re-passing its own Definition is what lets Tower.Configure have
        // a single contract instead of a null-means-keep rule.
        public void Configure(Tower tower)
        {
            if (tower == null)
            {
                return;
            }

            tower.Configure(tower.Definition, enemies, projectiles, scanIntervalSeconds);
        }

        // The factory creates, so the factory destroys -- which keeps PlaceTowerCommand.Undo from
        // naming UnityEngine.Object at all, and puts the one lifecycle wrinkle below in the type
        // whose job is lifecycle.
        //
        // The wrinkle, and it is a test-shaped concession recorded as one (§14): outside play mode
        // `Destroy` logs an error and defers to a frame that never arrives, so an undone placement
        // would leak its GameObject into the next EditMode test. `DestroyImmediate` is the wrong
        // call at runtime -- it can tear an object down mid-frame while something still holds it --
        // so the branch is on which environment we are in, not on preference. The same EditMode
        // fact is why ObjectPoolTests tears its instances down by hand (§14).
        public void Destroy(Tower tower)
        {
            if (tower == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                UnityEngine.Object.Destroy(tower.gameObject);
            }
            else
            {
                UnityEngine.Object.DestroyImmediate(tower.gameObject);
            }
        }
    }
}
