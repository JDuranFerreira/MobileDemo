using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // Builds its prefab from a runtime GameObject under a throwaway root and tears it down with
    // DestroyImmediate -- the technique ObjectPoolTests established and §14 already explains.
    //
    // Growth on exhaustion, the rejected double release and the SetActive/OnSpawn ordering are
    // deliberately not re-tested here: ObjectPoolTests owns them, and asserting them through the
    // factory would be testing the pool twice.
    public class EnemyFactoryTests
    {
        const float Dt = 0.1f;

        GameObject root;
        Enemy prefab;
        Transform parent;
        EnemyDefinition definition;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("EnemyFactoryTests");
            prefab = new GameObject("EnemyPrefab").AddComponent<Enemy>();
            prefab.transform.SetParent(root.transform);
            parent = new GameObject("PoolParent").transform;
            parent.SetParent(root.transform);
            definition = ScriptableObject.CreateInstance<EnemyDefinition>();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            if (definition != null)
            {
                Object.DestroyImmediate(definition);
            }
        }

        EnemyFactory NewFactory(int prewarm) =>
            new EnemyFactory(new ObjectPool<Enemy>(prefab, prewarm, parent));

        static Vector2[] StraightPath() => new[] { new Vector2(0f, 0f), new Vector2(0f, 1f) };

        [Test]
        public void Constructor_NullPool_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(() => new EnemyFactory(null));
        }

        [Test]
        public void Create_ReturnsAnActiveInstance()
        {
            EnemyFactory factory = NewFactory(1);

            Enemy enemy = factory.Create(definition, StraightPath());

            Assert.IsTrue(enemy.gameObject.activeSelf);
        }

        /// <summary>
        /// The seam this type exists for: <c>Get()</c> returns an instance still at last life's
        /// transform, and the factory places it in the statement right after.
        /// </summary>
        [Test]
        public void Create_PlacesTheInstanceAtTheFirstWaypoint()
        {
            EnemyFactory factory = NewFactory(1);
            Vector2[] path = StraightPath();

            Enemy enemy = factory.Create(definition, path);

            Assert.AreEqual(path[0], (Vector2)enemy.transform.position);
        }

        /// <summary>
        /// Proves the definition was actually applied, not merely stored, by driving the enemy at
        /// the speed the asset specifies. Asserting the sprite instead would need a seam onto
        /// <c>EnemyDefinition</c>'s serialized field, which buys less.
        /// </summary>
        [Test]
        public void Create_AppliesTheDefinition()
        {
            EnemyFactory factory = NewFactory(1);
            Enemy enemy = factory.Create(definition, StraightPath());

            enemy.Tick(definition.SpawnDelaySeconds);
            enemy.Tick(Dt);

            Assert.AreEqual(definition.MoveSpeed * Dt, enemy.transform.position.y, 1e-4f);
        }

        [Test]
        public void Create_NullDefinition_Throws()
        {
            EnemyFactory factory = NewFactory(1);

            Assert.Throws<System.ArgumentNullException>(() => factory.Create(null, StraightPath()));
        }

        [Test]
        public void Create_ThenRelease_ThenCreate_ReusesTheSameInstanceRestartedFromTheStart()
        {
            EnemyFactory factory = NewFactory(1);
            Vector2[] path = StraightPath();

            Enemy first = factory.Create(definition, path);
            first.Tick(definition.SpawnDelaySeconds);
            first.Tick(Dt);
            Assert.AreNotEqual(path[0], (Vector2)first.transform.position, "precondition: it moved");

            Assert.IsTrue(factory.Release(first));
            Enemy second = factory.Create(definition, path);

            Assert.AreSame(first, second, "one prewarmed instance means the pool must recycle it");
            Assert.AreEqual(path[0], (Vector2)second.transform.position);
            Assert.IsFalse(second.IsFinished);
        }

        /// <summary>
        /// §10 nominates <c>PeakActive</c> as the number prewarm gets tuned to, so the factory
        /// has to expose it -- this is the reader that makes the claim collectable.
        /// </summary>
        [Test]
        public void Stats_ExposesThePoolsCounts()
        {
            EnemyFactory factory = NewFactory(2);
            Enemy enemy = factory.Create(definition, StraightPath());
            factory.Release(enemy);

            IPoolStats stats = factory.Stats;

            Assert.AreEqual(2, stats.Prewarm);
            Assert.AreEqual(2, stats.InstanceCount);
            Assert.AreEqual(0, stats.ActiveCount);
            Assert.AreEqual(1, stats.PeakActive, "PeakActive is a high-water mark, not a live count");
        }
    }
}
