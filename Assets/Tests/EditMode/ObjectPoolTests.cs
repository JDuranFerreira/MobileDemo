using System.Text.RegularExpressions;
using MobileDemo.Core.Pooling;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileDemo.Tests.EditMode
{
    // No `using System;` here on purpose: with UnityEngine imported it makes `Object` ambiguous
    // (CS0104), and this fixture needs UnityEngine.Object far more than it needs System.
    public class ObjectPoolTests
    {
        // Test-local poolable, same reasoning as EventBusTests' Ping/Pong: the pool's contract
        // should not be asserted through Enemy, which does not exist yet.
        class TestPoolable : MonoBehaviour, IPoolable
        {
            public int SpawnCalls;
            public int DespawnCalls;
            public bool ActiveDuringSpawn;
            public bool ActiveDuringDespawn;

            public void OnSpawn()
            {
                SpawnCalls++;
                ActiveDuringSpawn = gameObject.activeSelf;
            }

            public void OnDespawn()
            {
                DespawnCalls++;
                ActiveDuringDespawn = gameObject.activeSelf;
            }
        }

        GameObject root;
        TestPoolable prefab;
        Transform parent;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ObjectPoolTests");
            prefab = new GameObject("TestPrefab").AddComponent<TestPoolable>();
            prefab.transform.SetParent(root.transform);
            parent = new GameObject("PoolParent").transform;
            parent.SetParent(root.transform);
        }

        [TearDown]
        public void TearDown()
        {
            // DestroyImmediate, not Destroy: in EditMode Destroy defers to a frame that never
            // arrives, so every pooled instance would leak into the next test.
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        ObjectPool<TestPoolable> NewPool(int prewarm) =>
            new ObjectPool<TestPoolable>(prefab, prewarm, parent);

        [Test]
        public void Constructor_WithPrewarm_CreatesThatManyAvailableInstances()
        {
            ObjectPool<TestPoolable> pool = NewPool(4);

            Assert.AreEqual(4, pool.AvailableCount);
            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(4, pool.InstanceCount);
        }

        [Test]
        public void Constructor_WithPrewarm_LeavesInstancesInactive()
        {
            NewPool(3);

            foreach (Transform child in parent)
            {
                Assert.AreEqual(false, child.gameObject.activeSelf);
            }
        }

        /// <summary>
        /// Pins the prewarm decision: prewarm does not route through <c>Release</c>, so
        /// <c>OnDespawn</c> never fires on an instance that never spawned. An OnDespawn that
        /// plays a death effect would otherwise become a load-time bug.
        /// </summary>
        [Test]
        public void Constructor_WithPrewarm_DoesNotInvokeOnSpawnOrOnDespawn()
        {
            NewPool(3);

            foreach (Transform child in parent)
            {
                TestPoolable instance = child.GetComponent<TestPoolable>();
                Assert.AreEqual(0, instance.SpawnCalls);
                Assert.AreEqual(0, instance.DespawnCalls);
            }
        }

        [Test]
        public void Constructor_WithParent_ParentsInstancesUnderIt()
        {
            NewPool(2);

            Assert.AreEqual(2, parent.childCount);
        }

        [Test]
        public void Constructor_NullPrefab_Throws()
        {
            Assert.Throws<System.ArgumentNullException>(
                () => new ObjectPool<TestPoolable>(null, 1));
        }

        [Test]
        public void Constructor_NegativePrewarm_CreatesNoInstances()
        {
            ObjectPool<TestPoolable> pool = NewPool(-5);

            Assert.AreEqual(0, pool.InstanceCount);
            Assert.AreEqual(0, pool.Prewarm, "a negative prewarm clamps rather than throwing");
        }

        [Test]
        public void Name_ReportsThePrefabName()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);

            Assert.AreEqual("TestPrefab", pool.Name);
        }

        [Test]
        public void Get_ReturnsAnActiveInstance()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);

            TestPoolable item = pool.Get();

            Assert.AreEqual(true, item.gameObject.activeSelf);
        }

        [Test]
        public void Get_InvokesOnSpawn()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);

            TestPoolable item = pool.Get();

            Assert.AreEqual(1, item.SpawnCalls);
        }

        /// <summary>
        /// The pool activates before calling <c>OnSpawn</c>, so a coroutine or tween started
        /// there has a live GameObject. Reversing the order would throw on the first
        /// <c>StartCoroutine</c> in a pooled enemy.
        /// </summary>
        [Test]
        public void Get_ActivatesBeforeInvokingOnSpawn()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);

            TestPoolable item = pool.Get();

            Assert.AreEqual(true, item.ActiveDuringSpawn);
        }

        [Test]
        public void Get_MovesTheInstanceFromAvailableToActive()
        {
            ObjectPool<TestPoolable> pool = NewPool(2);

            pool.Get();

            Assert.AreEqual(1, pool.ActiveCount);
            Assert.AreEqual(1, pool.AvailableCount);
        }

        [Test]
        public void Get_Twice_ReturnsDifferentInstances()
        {
            ObjectPool<TestPoolable> pool = NewPool(2);

            TestPoolable first = pool.Get();
            TestPoolable second = pool.Get();

            Assert.AreNotSame(first, second);
        }

        [Test]
        public void Release_DeactivatesTheInstance()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            TestPoolable item = pool.Get();

            bool accepted = pool.Release(item);

            Assert.AreEqual(true, accepted);
            Assert.AreEqual(false, item.gameObject.activeSelf);
        }

        /// <summary>
        /// The mirror of <see cref="Get_ActivatesBeforeInvokingOnSpawn"/>: <c>OnDespawn</c> runs
        /// while the object is still active, so it can stop coroutines, kill tweens and spawn a
        /// death effect.
        /// </summary>
        [Test]
        public void Release_InvokesOnDespawnWhileStillActive()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            TestPoolable item = pool.Get();

            pool.Release(item);

            Assert.AreEqual(1, item.DespawnCalls);
            Assert.AreEqual(true, item.ActiveDuringDespawn);
        }

        [Test]
        public void Release_ReturnsTheInstanceToAvailable()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            TestPoolable item = pool.Get();

            pool.Release(item);

            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(1, pool.AvailableCount);
        }

        [Test]
        public void Release_ThenGet_ReusesTheSameInstance()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            TestPoolable first = pool.Get();
            pool.Release(first);

            TestPoolable second = pool.Get();

            Assert.AreSame(first, second);
            Assert.AreEqual(1, pool.InstanceCount);
        }

        [Test]
        public void GetAndReleaseRepeatedly_CreatesNoExtraInstances()
        {
            ObjectPool<TestPoolable> pool = NewPool(2);

            for (int i = 0; i < 100; i++)
            {
                TestPoolable a = pool.Get();
                TestPoolable b = pool.Get();
                pool.Release(a);
                pool.Release(b);
            }

            Assert.AreEqual(2, pool.InstanceCount, "the pool must recycle, not leak");
            Assert.AreEqual(0, pool.ActiveCount);
        }

        /// <summary>
        /// A double release would hand the same instance to two callers, which is the failure
        /// that actually corrupts a pool. It must be rejected rather than absorbed, and the
        /// second call must not fire <c>OnDespawn</c> again.
        /// </summary>
        [Test]
        public void Release_SameInstanceTwice_IsRejected()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            TestPoolable item = pool.Get();
            pool.Release(item);

            bool accepted = pool.Release(item);

            Assert.AreEqual(false, accepted);
            Assert.AreEqual(1, pool.AvailableCount);
            Assert.AreEqual(1, item.DespawnCalls);
        }

        [Test]
        public void Release_ForeignInstance_IsRejected()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            TestPoolable foreign = new GameObject("Foreign").AddComponent<TestPoolable>();
            foreign.transform.SetParent(root.transform);

            bool accepted = pool.Release(foreign);

            Assert.AreEqual(false, accepted);
            Assert.AreEqual(1, pool.AvailableCount);
            Assert.AreEqual(true, foreign.gameObject.activeSelf, "the pool must not touch what it does not own");
            Assert.AreEqual(0, foreign.DespawnCalls);
        }

        [Test]
        public void Release_Null_IsRejectedWithoutThrowing()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);

            Assert.AreEqual(false, pool.Release(null));
        }

        [Test]
        public void Release_DestroyedInstance_FreesTheSlotWithoutThrowing()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            TestPoolable item = pool.Get();
            Object.DestroyImmediate(item.gameObject);

            bool accepted = pool.Release(item);

            Assert.AreEqual(false, accepted);
            Assert.AreEqual(0, pool.ActiveCount, "the slot must free, or the pool grows forever");
            Assert.AreEqual(0, pool.InstanceCount);
        }

        /// <summary>
        /// The growth decision: an exhausted pool creates one more instance rather than returning
        /// null, so a mis-tuned prewarm costs a frame's allocation instead of a missing enemy.
        /// One per call, never a doubling — see ARCHITECTURE.md §6.
        /// </summary>
        [Test]
        public void Get_WhenExhausted_CreatesAnAdditionalInstance()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            pool.Get();

            TestPoolable grown = pool.Get();

            Assert.AreEqual(2, pool.InstanceCount, "grows by exactly one");
            Assert.AreEqual(2, pool.ActiveCount);
            Assert.AreEqual(true, grown.gameObject.activeSelf);
            Assert.AreEqual(1, grown.SpawnCalls);
        }

        [Test]
        public void Get_WhenExhausted_LogsAWarning()
        {
            ObjectPool<TestPoolable> pool = NewPool(0);

            LogAssert.Expect(LogType.Warning, new Regex("grew past its prewarm"));
            pool.Get();
        }

        [Test]
        public void Get_WhenExhausted_ParentsTheNewInstanceUnderTheSameParent()
        {
            ObjectPool<TestPoolable> pool = NewPool(0);

            TestPoolable grown = pool.Get();

            Assert.AreSame(parent, grown.transform.parent, "growth must reuse the create path");
        }

        [Test]
        public void Get_AfterGrowth_ThenRelease_ReusesTheGrownInstance()
        {
            ObjectPool<TestPoolable> pool = NewPool(0);
            TestPoolable grown = pool.Get();
            pool.Release(grown);

            TestPoolable reused = pool.Get();

            Assert.AreSame(grown, reused);
            Assert.AreEqual(1, pool.InstanceCount);
        }

        [Test]
        public void Get_WhenAvailableInstanceWasDestroyed_CreatesAReplacement()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);
            TestPoolable pooled = pool.Get();
            pool.Release(pooled);
            Object.DestroyImmediate(pooled.gameObject);

            TestPoolable replacement = pool.Get();

            Assert.AreEqual(true, replacement != null, "a destroyed instance must not be handed back");
            Assert.AreEqual(true, replacement.gameObject.activeSelf);
            Assert.AreEqual(1, pool.ActiveCount);
        }

        [Test]
        public void PeakActive_OnANewPool_IsZero()
        {
            ObjectPool<TestPoolable> pool = NewPool(4);

            Assert.AreEqual(0, pool.PeakActive);
        }

        [Test]
        public void PeakActive_HoldsTheHighWaterMarkAfterRelease()
        {
            ObjectPool<TestPoolable> pool = NewPool(3);
            TestPoolable a = pool.Get();
            TestPoolable b = pool.Get();

            pool.Release(a);
            pool.Release(b);

            Assert.AreEqual(0, pool.ActiveCount);
            Assert.AreEqual(2, pool.PeakActive, "PeakActive is a high-water mark, not a live count");
        }

        [Test]
        public void PeakActive_IncludesInstancesCreatedByGrowth()
        {
            ObjectPool<TestPoolable> pool = NewPool(1);

            pool.Get();
            pool.Get();

            Assert.AreEqual(2, pool.PeakActive);
        }

        [Test]
        public void InstanceCount_AlwaysEqualsActivePlusAvailable()
        {
            ObjectPool<TestPoolable> pool = NewPool(3);
            TestPoolable a = pool.Get();
            pool.Get();
            pool.Release(a);
            pool.Get();

            Assert.AreEqual(pool.ActiveCount + pool.AvailableCount, pool.InstanceCount);
            Assert.AreEqual(3, pool.InstanceCount);
        }
    }
}
