using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // No `using System;`, for the reason ObjectPoolTests gives.
    //
    // Firing is asserted through the projectile pool's ActiveCount rather than by reading a
    // private target field: what matters about a tower is that it puts projectiles in the air at
    // the right rate, at the right enemy, and not otherwise.
    public class TowerTests
    {
        const float Range = 5f;
        const int Damage = 2;
        const float ShotsPerSecond = 2f;
        const float ScanInterval = 0.1f;
        const float ShotInterval = 1f / ShotsPerSecond;

        GameObject root;
        Transform poolParent;
        EnemyDefinition enemyDefinition;
        TowerDefinition towerDefinition;
        ProjectileDefinition projectileDefinition;
        Projectile projectilePrefab;
        ProjectileFactory projectiles;
        EnemyRegistry registry;
        Tower tower;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("TowerTests");
            poolParent = new GameObject("PoolParent").transform;
            poolParent.SetParent(root.transform);

            enemyDefinition = ScriptableObject.CreateInstance<EnemyDefinition>();

            projectilePrefab = new GameObject("TestProjectile").AddComponent<Projectile>();
            projectilePrefab.transform.SetParent(root.transform);

            projectileDefinition = ScriptableObject.CreateInstance<ProjectileDefinition>();
            SerializedFields.Set(projectileDefinition, "speed", 20f);
            SerializedFields.Set(projectileDefinition, "prefab", projectilePrefab);

            towerDefinition = ScriptableObject.CreateInstance<TowerDefinition>();
            SerializedFields.Set(towerDefinition, "range", Range);
            SerializedFields.Set(towerDefinition, "shotsPerSecond", ShotsPerSecond);
            SerializedFields.Set(towerDefinition, "damage", Damage);
            SerializedFields.Set(towerDefinition, "projectile", projectileDefinition);

            projectiles = new ProjectileFactory(new[] { projectileDefinition }, 4, poolParent);
            registry = new EnemyRegistry(_ => true);

            tower = new GameObject("Tower").AddComponent<Tower>();
            tower.transform.SetParent(root.transform);
            tower.Configure(towerDefinition, registry, projectiles, ScanInterval);

            // Configure staggers the first scan by a random fraction of the interval, so tests
            // that must fire on the first opportunity zero it rather than depending on the draw.
            SerializedFields.Set(tower, "scanTimer", ScanInterval);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();

            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            if (enemyDefinition != null)
            {
                Object.DestroyImmediate(enemyDefinition);
            }

            if (towerDefinition != null)
            {
                Object.DestroyImmediate(towerDefinition);
            }

            if (projectileDefinition != null)
            {
                Object.DestroyImmediate(projectileDefinition);
            }
        }

        int ShotsFired => projectiles.Stats[0].PeakActive;

        /// <summary>A registered enemy, already past its spawn delay so it is targetable.</summary>
        Enemy NewTargetableEnemyAt(Vector2 position)
        {
            Enemy enemy = new GameObject("Enemy").AddComponent<Enemy>();
            enemy.transform.SetParent(root.transform);
            enemy.Configure(enemyDefinition, new[] { position, position + new Vector2(0f, 1000f) });
            enemy.Tick(enemyDefinition.SpawnDelaySeconds);
            registry.Add(enemy);
            return enemy;
        }

        /// <summary>Ticks the tower for <paramref name="seconds"/> in scan-interval steps.</summary>
        void TickFor(float seconds)
        {
            int steps = Mathf.RoundToInt(seconds / ScanInterval);
            for (int i = 0; i < steps; i++)
            {
                tower.Tick(ScanInterval);
            }
        }

        [Test]
        public void Tick_WithNoEnemies_FiresNothing()
        {
            TickFor(2f);

            Assert.AreEqual(0, ShotsFired);
        }

        [Test]
        public void Tick_WithAnEnemyInRange_Fires()
        {
            NewTargetableEnemyAt(new Vector2(1f, 0f));

            tower.Tick(ShotInterval);

            Assert.AreEqual(1, ShotsFired);
        }

        [Test]
        public void Tick_WithAnEnemyBeyondRange_FiresNothing()
        {
            NewTargetableEnemyAt(new Vector2(Range + 1f, 0f));

            TickFor(2f);

            Assert.AreEqual(0, ShotsFired);
        }

        /// <summary>
        /// The spawn window's not-yet-targetable rule, seen from the tower's side.
        /// </summary>
        [Test]
        public void Tick_WithAnEnemyStillSpawning_FiresNothing()
        {
            Enemy enemy = new GameObject("Enemy").AddComponent<Enemy>();
            enemy.transform.SetParent(root.transform);
            enemy.Configure(enemyDefinition, new[] { Vector2.one, new Vector2(1f, 1000f) });
            registry.Add(enemy);
            Assert.IsFalse(enemy.IsTargetable, "precondition: it is still spawning");

            TickFor(2f);

            Assert.AreEqual(0, ShotsFired);
        }

        /// <summary>
        /// §9's decision made observable, and the reason this tower fires 100×/s: with the reload
        /// ready on every tick, the scan interval is the only thing that can hold fire. Nine
        /// ticks inside one interval produce nothing; the tenth crosses it and fires.
        /// </summary>
        [Test]
        public void Tick_AcquiresATargetOnlyOnTheScanInterval()
        {
            SerializedFields.Set(towerDefinition, "shotsPerSecond", 100f);
            SerializedFields.Set(tower, "scanTimer", 0f);
            NewTargetableEnemyAt(new Vector2(1f, 0f));

            for (int i = 0; i < 9; i++)
            {
                tower.Tick(ScanInterval * 0.1f);
            }

            Assert.AreEqual(0, ShotsFired, "no scan has happened yet");

            tower.Tick(ScanInterval * 0.1f);

            Assert.AreEqual(1, ShotsFired, "the tick that completes the interval scans and fires");
        }

        [Test]
        public void Tick_OverTime_FiresAtTheDefinitionsRate()
        {
            NewTargetableEnemyAt(new Vector2(1f, 0f));

            TickFor(2f);

            // Two shots a second for two seconds, and the reload cap is what keeps it to exactly
            // that rather than to however many intervals elapsed.
            Assert.AreEqual(4, ShotsFired);
        }

        /// <summary>
        /// The regression this fixture exists for. A tower idle for a long stretch used to bank
        /// one shot per elapsed interval and then empty the whole stockpile into the first enemy
        /// to walk into range, one shot per frame. The reload is capped at a single shot instead.
        /// </summary>
        [Test]
        public void Tick_AfterIdlingWithNoTarget_DoesNotBurstFire()
        {
            TickFor(10f);
            Assert.AreEqual(0, ShotsFired, "precondition: ten idle seconds fired nothing");

            NewTargetableEnemyAt(new Vector2(1f, 0f));
            TickFor(ScanInterval * 2f);

            Assert.AreEqual(1, ShotsFired, "one banked shot, not ten seconds' worth");
        }

        /// <summary>
        /// Nearest, not first-found. Asserted through damage rather than a private field: the
        /// near enemy is the one that must lose health — and it loses the *tower's* damage, which
        /// is the other half of what this pins since §7 moved that figure here.
        /// </summary>
        [Test]
        public void Tick_WithTwoEnemiesInRange_TargetsTheNearest()
        {
            Enemy far = NewTargetableEnemyAt(new Vector2(4f, 0f));
            Enemy near = NewTargetableEnemyAt(new Vector2(1f, 0f));

            tower.Tick(ShotInterval);
            for (int i = 0; i < 100; i++)
            {
                projectiles.Tick(0.02f);
            }

            Assert.AreEqual(enemyDefinition.MaxHealth - Damage, near.CurrentHealth);
            Assert.AreEqual(enemyDefinition.MaxHealth, far.CurrentHealth);
        }

        /// <summary>
        /// Isolates the re-check at the moment of firing, which needs the tower to reload faster
        /// than it scans: the target it is holding is stale and dead, and no rescan has cleared
        /// it. Without that check a tower spends its shots on corpses.
        /// </summary>
        [Test]
        public void Tick_WhenTheHeldTargetDiesBetweenScans_FiresNothingFurther()
        {
            SerializedFields.Set(towerDefinition, "shotsPerSecond", 100f);
            Enemy enemy = NewTargetableEnemyAt(new Vector2(1f, 0f));

            tower.Tick(ScanInterval);
            Assert.AreEqual(1, ShotsFired, "precondition: it acquired and fired");

            enemy.TakeDamage(enemyDefinition.MaxHealth);
            for (int i = 0; i < 9; i++)
            {
                // Reloaded on every one of these, and none of them reaches the next scan.
                tower.Tick(ScanInterval * 0.1f);
            }

            Assert.AreEqual(1, ShotsFired);
        }

        [Test]
        public void Tick_BeforeConfigure_DoesNotThrow()
        {
            Tower unwired = new GameObject("Unwired").AddComponent<Tower>();
            unwired.transform.SetParent(root.transform);

            Assert.DoesNotThrow(() => unwired.Tick(1f));
        }

        [Test]
        public void Configure_AppliesTheDefinitionsSpriteSoBothTowersCanShareOnePrefab()
        {
            Texture2D texture = new Texture2D(4, 4);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, 4f, 4f), new Vector2(0.5f, 0.5f));
            SerializedFields.Set(towerDefinition, "sprite", sprite);

            tower.Configure(towerDefinition, registry, projectiles, ScanInterval);

            Assert.AreSame(sprite, tower.GetComponent<SpriteRenderer>().sprite);

            // Both, or the fixture leaks a texture for the whole editor session.
            Object.DestroyImmediate(sprite);
            Object.DestroyImmediate(texture);
        }

        // The build path's requirement: a tower instantiated from the shared prefab arrives with
        // whatever definition the prefab carried, and Configure is the only way to tell it what it
        // actually is. Definition has no setter, so this is the whole seam.
        [Test]
        public void Configure_WithADifferentDefinition_ReplacesTheSerializedOne()
        {
            TowerDefinition other = ScriptableObject.CreateInstance<TowerDefinition>();
            SerializedFields.Set(other, "range", Range * 2f);

            tower.Configure(other, registry, projectiles, ScanInterval);

            Assert.AreSame(other, tower.Definition);
            Object.DestroyImmediate(other);
        }
    }
}
