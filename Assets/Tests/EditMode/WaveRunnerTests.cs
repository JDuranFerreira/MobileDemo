using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MobileDemo.Core.Events;
using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Waves;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileDemo.Tests.EditMode
{
    // Builds its own pool and registry rather than reaching for BuildScaffold, which deliberately
    // releases through a no-op: this fixture is the one that cares whether a finished enemy
    // actually goes back to the pool, because that is half of what "cleared" means.
    //
    // The prefab is built from a runtime GameObject under a throwaway root -- ObjectPoolTests'
    // technique, which §14 explains.
    public class WaveRunnerTests
    {
        const float SpawnDelay = 0.15f;
        const float Interval = 0.5f;

        // One unit long, which at the default move speed of 1.5 is about two thirds of a second
        // of walking. Long enough that a spawn assertion is not silently offset by a leak on the
        // same tick — the first draft used a tenth of a unit and every "two enemies" assertion
        // read one, because the first had already reached the end and been released.
        static readonly Vector2[] Path = { new Vector2(0f, 0f), new Vector2(0f, 1f) };

        GameObject root;
        Transform poolParent;
        EnemyFactory factory;
        EnemyRegistry registry;
        WaveRunner runner;
        List<int> completed;

        // Both are unparented ScriptableObjects, which otherwise leak for the whole editor
        // session (§14) -- BuildScaffold keeps its enemy definitions for the same reason.
        readonly List<EnemyDefinition> definitions = new List<EnemyDefinition>();
        readonly List<WaveDefinition> waves = new List<WaveDefinition>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("WaveRunnerTests");

            Enemy prefab = new GameObject("EnemyPrefab").AddComponent<Enemy>();
            prefab.transform.SetParent(root.transform);

            poolParent = new GameObject("PoolParent").transform;
            poolParent.SetParent(root.transform);

            factory = new EnemyFactory(new ObjectPool<Enemy>(prefab, 16, poolParent));
            registry = new EnemyRegistry(factory.Release);
            runner = new WaveRunner(factory, registry, Path);

            completed = new List<int>();
            EventBus<WaveCompleted>.Subscribe(OnWaveCompleted);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<WaveCompleted>.Unsubscribe(OnWaveCompleted);
            EventBus.ClearAll();

            if (root != null)
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            DestroyAll(definitions);
            DestroyAll(waves);
        }

        // Qualified: `using System` puts System.Object in scope too, so a bare `Object` constraint
        // is ambiguous -- TowerFactory has the same note at its Instantiate call.
        static void DestroyAll<T>(List<T> assets) where T : UnityEngine.Object
        {
            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] != null)
                {
                    UnityEngine.Object.DestroyImmediate(assets[i]);
                }
            }

            assets.Clear();
        }

        void OnWaveCompleted(WaveCompleted evt) => completed.Add(evt.WaveIndex);

        /// <summary>
        /// Health is the readable fingerprint of which definition a spawn used: it is public and
        /// seeded straight from the asset, where <c>Enemy.Definition</c> is internal and reaching
        /// it would need a seam this fixture does not otherwise want.
        /// </summary>
        EnemyDefinition NewDefinition(int maxHealth)
        {
            EnemyDefinition definition = ScriptableObject.CreateInstance<EnemyDefinition>();
            SerializedFields.Set(definition, "maxHealth", maxHealth);
            SerializedFields.Set(definition, "spawnDelaySeconds", SpawnDelay);
            definitions.Add(definition);
            return definition;
        }

        WaveDefinition NewWave(params SpawnGroup[] groups)
        {
            WaveDefinition wave = ScriptableObject.CreateInstance<WaveDefinition>();
            wave.name = "TestWave";
            SerializedFields.Set(wave, "groups", groups);
            waves.Add(wave);
            return wave;
        }

        /// <summary>
        /// Walks every spawned enemy to the end of the path, where it leaks, finishes and is
        /// released. Bounded rather than a while-loop, so a runner that never clears fails as a
        /// readable assertion instead of hanging the test run.
        /// </summary>
        void TickUntilTheBoardIsEmpty()
        {
            for (int i = 0; i < 16 && registry.Active.Count > 0; i++)
            {
                runner.Tick(Interval);
            }

            Assert.AreEqual(0, registry.Active.Count, "the board should have emptied by now");
        }

        [Test]
        public void Constructor_NullFactory_Throws() =>
            Assert.Throws<ArgumentNullException>(
                () => new WaveRunner(null, registry, Path));

        [Test]
        public void Constructor_NullRegistry_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new WaveRunner(factory, null, Path));

        [Test]
        public void Constructor_NullWaypoints_Throws() =>
            Assert.Throws<ArgumentNullException>(() => new WaveRunner(factory, registry, null));

        /// <summary>
        /// EnemyMovingState indexes waypoint 1 on its first tick without re-checking, so a
        /// one-waypoint path is an IndexOutOfRangeException per spawn rather than a bad-looking
        /// wave. Bootstrap already refuses such a level; this is the same refusal one layer in.
        /// </summary>
        [Test]
        public void Constructor_WithASingleWaypoint_Throws() =>
            Assert.Throws<ArgumentException>(
                () => new WaveRunner(factory, registry, new[] { Vector2.zero }));

        [Test]
        public void Bind_NullPath_Throws() =>
            Assert.Throws<ArgumentNullException>(() => runner.Bind(null));

        /// <summary>
        /// The level swap's half of this class: between waves the runner is pointed at the next
        /// map's road, and the enemies spawned after that walk it.
        /// </summary>
        [Test]
        public void Bind_BetweenWaves_SendsLaterEnemiesDownTheNewPath()
        {
            Vector2[] elsewhere = { new Vector2(10f, 0f), new Vector2(10f, 1f) };

            Assert.IsTrue(runner.Bind(elsewhere));

            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(1), 1, Interval)), 0);
            runner.Tick(0f);

            Assert.AreEqual(1, registry.Active.Count);
            Assert.AreEqual(
                elsewhere[0], (Vector2)registry.Active[0].transform.position);
        }

        /// <summary>
        /// Enemies hold the array they were configured with, so a mid-wave rebind would leave two
        /// roads live at once with one of them invisible. It cannot happen from the game — a level
        /// changes on victory, which requires the wave to be cleared — and the refusal is what
        /// keeps that ordering a fact rather than a convention.
        /// </summary>
        [Test]
        public void Bind_DuringAWave_IsRefused()
        {
            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(1), 2, Interval)), 3);
            runner.Tick(0f);

            LogAssert.Expect(LogType.Error, new Regex("during wave 3"));

            Assert.IsFalse(runner.Bind(new[] { new Vector2(10f, 0f), new Vector2(10f, 1f) }));
        }

        [Test]
        public void Bind_WithASingleWaypoint_IsRefusedAndKeepsThePathItHad()
        {
            LogAssert.Expect(LogType.Error, new Regex("fewer than two waypoints"));

            Assert.IsFalse(runner.Bind(new[] { Vector2.zero }));

            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(1), 1, Interval)), 0);
            runner.Tick(0f);

            Assert.AreEqual(Path[0], (Vector2)registry.Active[0].transform.position);
        }

        [Test]
        public void WaveIndex_BeforeAnyWave_IsMinusOne()
        {
            Assert.AreEqual(-1, runner.WaveIndex);
            Assert.IsFalse(runner.IsRunning);
            Assert.IsFalse(runner.IsCleared);
        }

        [Test]
        public void StartWave_NullDefinition_Throws() =>
            Assert.Throws<ArgumentNullException>(() => runner.StartWave(null, 0));

        /// <summary>
        /// The opening enemy arrives on the tick that starts the wave, not one interval later:
        /// tapping Go has to have an immediate consequence, and the interval reads as the gap
        /// *between* spawns.
        /// </summary>
        [Test]
        public void Tick_AfterStartWave_SpawnsTheFirstEnemyImmediately()
        {
            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(3), 3, Interval)), 0);

            runner.Tick(0f);

            Assert.AreEqual(1, registry.Active.Count);
        }

        [Test]
        public void Tick_SpawnsOnePerInterval()
        {
            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(3), 3, Interval)), 0);

            runner.Tick(0f);
            Assert.AreEqual(1, registry.Active.Count, "the opening spawn");

            runner.Tick(Interval * 0.5f);
            Assert.AreEqual(1, registry.Active.Count, "half an interval is not an interval");

            runner.Tick(Interval * 0.5f);
            Assert.AreEqual(2, registry.Active.Count);
        }

        /// <summary>
        /// A frame long enough to owe two spawns must deliver both. Dropping the surplus would
        /// silently shorten a wave on exactly the frames a mobile device is already struggling.
        /// </summary>
        [Test]
        public void Tick_WithAFrameLongerThanTheInterval_SpawnsEveryEnemyItOwes()
        {
            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(3), 4, Interval)), 0);

            runner.Tick(Interval * 2f);

            Assert.AreEqual(3, registry.Active.Count, "the opening spawn plus the two owed");
        }

        [Test]
        public void Tick_SpawnsGroupsInOrder()
        {
            runner.StartWave(
                NewWave(
                    new SpawnGroup(NewDefinition(3), 1, Interval),
                    new SpawnGroup(NewDefinition(7), 1, Interval)),
                0);

            runner.Tick(0f);
            runner.Tick(Interval);

            Assert.AreEqual(2, registry.Active.Count);
            Assert.AreEqual(3, registry.Active[0].CurrentHealth, "the first group spawns first");
            Assert.AreEqual(7, registry.Active[1].CurrentHealth);
        }

        /// <summary>
        /// A group authored with no enemy or a count of zero is noise, not a pause. Giving it
        /// meaning would make an empty group a hidden delay knob — a field with two jobs.
        /// </summary>
        [Test]
        public void StartWave_SkipsGroupsWithNothingToSpawn()
        {
            runner.StartWave(
                NewWave(
                    new SpawnGroup(null, 5, Interval),
                    new SpawnGroup(NewDefinition(3), 0, Interval),
                    new SpawnGroup(NewDefinition(7), 1, Interval)),
                0);

            runner.Tick(0f);

            Assert.AreEqual(1, registry.Active.Count);
            Assert.AreEqual(7, registry.Active[0].CurrentHealth);
        }

        [Test]
        public void IsCleared_WhileAnEnemyIsStillAlive_IsFalse()
        {
            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(3), 1, Interval)), 0);

            runner.Tick(0f);

            Assert.IsFalse(runner.IsCleared, "everything is spawned, but it is still walking");
            Assert.IsTrue(runner.IsRunning);
        }

        [Test]
        public void IsCleared_WhenTheLastEnemyHasGone_IsTrue()
        {
            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(3), 1, Interval)), 0);
            runner.Tick(0f);

            TickUntilTheBoardIsEmpty();

            Assert.AreEqual(0, registry.Active.Count);
            Assert.IsTrue(runner.IsCleared);
        }

        /// <summary>
        /// The sequence, not just the flag. IsCleared stays true for every later tick, so without
        /// the running latch the bus would carry a WaveCompleted per frame until the next wave —
        /// and a final-count assertion taken one tick too early would pass anyway.
        /// </summary>
        [Test]
        public void Tick_WhenCleared_PublishesWaveCompletedExactlyOnce()
        {
            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(3), 1, Interval)), 3);
            runner.Tick(0f);

            TickUntilTheBoardIsEmpty();
            runner.Tick(Interval);
            runner.Tick(Interval);

            Assert.AreEqual(new[] { 3 }, completed, "the index it was started with, announced once");
            Assert.IsFalse(runner.IsRunning);
            Assert.IsTrue(runner.IsCleared, "cleared stays true; only the announcement is once");
        }

        [Test]
        public void StartWave_AfterAWaveWasCleared_RunsAgain()
        {
            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(3), 1, Interval)), 0);
            runner.Tick(0f);
            TickUntilTheBoardIsEmpty();

            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(7), 1, Interval)), 1);
            runner.Tick(0f);

            Assert.AreEqual(1, runner.WaveIndex);
            Assert.IsTrue(runner.IsRunning);
            Assert.IsFalse(runner.IsCleared);
            Assert.AreEqual(7, registry.Active[0].CurrentHealth);
        }

        /// <summary>
        /// A wave with nothing to spawn completes on its first tick, which reads as a sequence
        /// that skipped a step rather than as the authoring mistake it is. It says so out loud.
        /// </summary>
        [Test]
        public void StartWave_WithNothingToSpawn_LogsAnError()
        {
            LogAssert.Expect(LogType.Error, new Regex("nothing to"));

            runner.StartWave(NewWave(new SpawnGroup(null, 0, Interval)), 0);
            runner.Tick(0f);

            Assert.IsTrue(runner.IsCleared);
        }

        /// <summary>
        /// A zero interval would make the spawn loop unbounded. Clamped rather than rejected —
        /// object-pool.md's stance that a bad tuning number should be recoverable — and warned
        /// about, so it stays fixable.
        /// </summary>
        [Test]
        public void StartWave_WithAZeroInterval_WarnsAndStillTerminates()
        {
            LogAssert.Expect(LogType.Warning, new Regex("clamped"));

            runner.StartWave(NewWave(new SpawnGroup(NewDefinition(3), 3, 0f)), 0);
            runner.Tick(0f);

            Assert.AreEqual(1, registry.Active.Count, "clamped, not collapsed into one frame");
        }

        /// <summary>
        /// The registry is ticked whether or not a wave is running, which is what lets the build
        /// phase exist at all: enemies from a wave that has just ended still have to finish and go
        /// back to the pool.
        /// </summary>
        [Test]
        public void Tick_BeforeAnyWave_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => runner.Tick(0.1f));
            Assert.AreEqual(0, registry.Active.Count);
        }
    }
}
