using System.Text.RegularExpressions;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileDemo.Tests.EditMode
{
    // No `using System;`, for the reason ObjectPoolTests gives. The runtime-built-prefab
    // technique is the one EnemyFactoryTests borrowed from ObjectPoolTests.
    //
    // What this fixture is really pinning is the pool-per-prefab decision: a projectile's damage
    // and speed live on its prefab, so one shared pool would hand a fire tower a bullet.
    public class ProjectileFactoryTests
    {
        const int Prewarm = 3;

        GameObject root;
        Transform poolParent;
        Projectile fire;
        Projectile bullet;
        EnemyRegistry registry;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ProjectileFactoryTests");
            poolParent = new GameObject("PoolParent").transform;
            poolParent.SetParent(root.transform);

            fire = NewPrefab("Fire", 12);
            bullet = NewPrefab("Bullet", 1);
            registry = new EnemyRegistry(_ => true);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        Projectile NewPrefab(string name, int damage)
        {
            Projectile prefab = new GameObject(name).AddComponent<Projectile>();
            prefab.transform.SetParent(root.transform);
            SerializedFields.Set(prefab, "speed", 10f);
            SerializedFields.Set(prefab, "damage", damage);
            return prefab;
        }

        ProjectileFactory NewFactory(params Projectile[] prefabs) =>
            new ProjectileFactory(prefabs, Prewarm, poolParent);

        [Test]
        public void Constructor_CreatesOnePoolPerPrefab()
        {
            ProjectileFactory factory = NewFactory(fire, bullet);

            Assert.AreEqual(2, factory.Stats.Count);
            Assert.AreEqual(Prewarm, factory.Stats[0].Prewarm);
        }

        /// <summary>
        /// Two towers of the same type share a pool. That is normal, not a configuration error.
        /// </summary>
        [Test]
        public void Constructor_WithTheSamePrefabTwice_CreatesOnePool()
        {
            ProjectileFactory factory = NewFactory(fire, fire);

            Assert.AreEqual(1, factory.Stats.Count);
        }

        [Test]
        public void Constructor_PrewarmsEveryPoolUpFront()
        {
            ProjectileFactory factory = NewFactory(fire, bullet);

            // The whole point of taking the prefabs up front rather than creating pools lazily:
            // a pool built on the first shot would allocate its prewarm mid-wave.
            Assert.AreEqual(2 * Prewarm, poolParent.childCount);
            Assert.AreEqual(0, factory.Stats[0].ActiveCount);
        }

        [Test]
        public void Constructor_NullPrefabs_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new ProjectileFactory(null, Prewarm));
        }

        [Test]
        public void Create_ReturnsAnInstanceOfTheRequestedPrefab()
        {
            ProjectileFactory factory = NewFactory(fire, bullet);

            Projectile spawned = factory.Create(bullet, Vector2.zero, null, registry);

            Assert.IsNotNull(spawned);
            Assert.IsTrue(spawned.name.StartsWith("Bullet"), $"got '{spawned.name}'");
        }

        [Test]
        public void Create_DrawsFromTheMatchingPoolOnly()
        {
            ProjectileFactory factory = NewFactory(fire, bullet);

            factory.Create(fire, Vector2.zero, null, registry);

            Assert.AreEqual(1, factory.Stats[0].ActiveCount, "the fire pool");
            Assert.AreEqual(0, factory.Stats[1].ActiveCount, "the bullet pool is untouched");
        }

        [Test]
        public void Create_PlacesTheProjectileAtTheOrigin()
        {
            ProjectileFactory factory = NewFactory(fire);

            Projectile spawned = factory.Create(fire, new Vector2(3f, 4f), null, registry);

            Assert.AreEqual(new Vector2(3f, 4f), (Vector2)spawned.transform.position);
        }

        /// <summary>
        /// A tower wired to a prefab the factory was never told about costs that tower its shots,
        /// not the whole run — so this logs and returns null rather than throwing.
        /// </summary>
        [Test]
        public void Create_WithAnUnregisteredPrefab_LogsAndReturnsNull()
        {
            ProjectileFactory factory = NewFactory(fire);

            LogAssert.Expect(LogType.Error, new Regex("has no pool for"));
            Projectile spawned = factory.Create(bullet, Vector2.zero, null, registry);

            Assert.IsNull(spawned);
        }

        [Test]
        public void Create_NullPrefab_Throws()
        {
            ProjectileFactory factory = NewFactory(fire);

            Assert.Throws<System.ArgumentNullException>(
                () => factory.Create(null, Vector2.zero, null, registry));
        }

        [Test]
        public void Tick_ReleasesFinishedProjectilesBackToTheirPool()
        {
            ProjectileFactory factory = NewFactory(fire);

            // No target and an aim point it is already standing on, so it lands on its first tick.
            factory.Create(fire, Vector2.zero, null, registry);
            Assert.AreEqual(1, factory.Stats[0].ActiveCount, "precondition: it is in flight");

            factory.Tick(0.1f);

            Assert.AreEqual(0, factory.Stats[0].ActiveCount);
            Assert.AreEqual(Prewarm, factory.Stats[0].InstanceCount, "it recycled, it did not leak");
        }

        [Test]
        public void Tick_LeavesProjectilesStillInFlightAlone()
        {
            ProjectileFactory factory = NewFactory(fire);
            Enemy enemy = new GameObject("Enemy").AddComponent<Enemy>();
            enemy.transform.SetParent(root.transform);
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            enemy.Configure(definition, new[] { new Vector2(50f, 0f), new Vector2(50f, 1000f) });
            enemy.Tick(definition.SpawnDelaySeconds);

            factory.Create(fire, Vector2.zero, enemy, registry);
            factory.Tick(0.1f);

            Assert.AreEqual(1, factory.Stats[0].ActiveCount);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void Release_AProjectileItNeverCreated_ReturnsFalse()
        {
            ProjectileFactory factory = NewFactory(fire);
            Projectile foreign = NewPrefab("Foreign", 1);

            Assert.IsFalse(factory.Release(foreign));
        }

        [Test]
        public void Release_Null_ReturnsFalseWithoutThrowing()
        {
            ProjectileFactory factory = NewFactory(fire);

            Assert.IsFalse(factory.Release(null));
        }

        [Test]
        public void CreateThenTickRepeatedly_CreatesNoExtraInstances()
        {
            ProjectileFactory factory = NewFactory(fire);

            for (int i = 0; i < 50; i++)
            {
                factory.Create(fire, Vector2.zero, null, registry);
                factory.Tick(0.1f);
            }

            Assert.AreEqual(Prewarm, factory.Stats[0].InstanceCount, "the pool must recycle, not leak");
        }
    }
}
