using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using MobileDemo.Core.Pooling;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Phases;
using MobileDemo.Gameplay.Waves;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // Victory is per level and defeat is per run (§1, §4), and this fixture is where that
    // asymmetry becomes testable: clearing the last wave of a map that is not the last one swaps
    // and returns to Build, and only the final map's victory is terminal.
    public class VictoryStateTests
    {
        // Stands in for BuildState, which subscribes to the bus and clears an undo stack -- neither
        // of which this fixture is asking about. GameStateMachineTests' RecordingState, kept local
        // because it records only what these tests read.
        sealed class NullState : IGameState
        {
            public int Entered { get; private set; }

            public void Enter() => Entered++;

            public void Tick(float dt)
            {
            }

            public void Exit()
            {
            }
        }

        static readonly Vector2[] FirstRoad = { new Vector2(-4f, 0f), new Vector2(4f, 0f) };
        static readonly Vector2[] SecondRoad = { new Vector2(6f, 0f), new Vector2(6f, 3f) };

        readonly List<Object> assets = new List<Object>();

        BuildScaffold scaffold;
        LevelRunner levels;
        EnemyRegistry registry;
        WaveRunner waves;
        WaveState waveState;
        GameStateMachine machine;
        NullState build;
        List<GamePhase> published;

        [SetUp]
        public void SetUp()
        {
            scaffold = new BuildScaffold();

            Level[] templates =
            {
                NewLevelWithWaves("First", FirstRoad),
                NewLevelWithWaves("Second", SecondRoad),
            };

            levels = new LevelRunner(
                templates,
                scaffold.Towers,
                BuildScaffold.RoadClearance,
                BuildScaffold.TowerSpacing,
                scaffold.Root.transform);
            levels.Load(0);

            Enemy prefab = new GameObject("EnemyPrefab").AddComponent<Enemy>();
            prefab.transform.SetParent(scaffold.Root.transform);

            EnemyFactory enemies =
                new EnemyFactory(new ObjectPool<Enemy>(prefab, 4, scaffold.Root.transform));
            registry = new EnemyRegistry(enemies.Release);
            waves = new WaveRunner(enemies, registry, levels.Waypoints);

            machine = new GameStateMachine();

            // WaveState ticks the build controller, because a tower can be bought mid-wave. Nothing
            // here taps, so it is a no-op -- but it is a real one rather than a null, which is what
            // keeps these tests honest about the wave tick's actual shape.
            BuildController builder = new BuildController(
                new FakeInputService(),
                scaffold.Economy,
                levels,
                scaffold.Towers,
                scaffold.Catalogue);

            waveState = new WaveState(machine, waves, levels, scaffold.Projectiles, builder);
            build = new NullState();

            machine.Add(GamePhase.Build, build);
            machine.Add(GamePhase.Wave, waveState);
            machine.Add(GamePhase.Victory, new VictoryState(machine, levels, waveState));

            published = new List<GamePhase>();
            EventBus<PhaseChanged>.Subscribe(OnPhaseChanged);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<PhaseChanged>.Unsubscribe(OnPhaseChanged);
            scaffold.Dispose();

            for (int i = 0; i < assets.Count; i++)
            {
                if (assets[i] != null)
                {
                    Object.DestroyImmediate(assets[i]);
                }
            }

            assets.Clear();
        }

        void OnPhaseChanged(PhaseChanged evt) => published.Add(evt.Phase);

        Level NewLevelWithWaves(string name, Vector2[] road)
        {
            Level level = scaffold.NewLevelTemplate(name, road);

            EnemyDefinition enemy = ScriptableObject.CreateInstance<EnemyDefinition>();
            assets.Add(enemy);

            WaveDefinition wave = ScriptableObject.CreateInstance<WaveDefinition>();
            wave.name = $"{name}Wave";
            SerializedFields.Set(wave, "groups", new[] { new SpawnGroup(enemy, 1, 0.5f) });
            assets.Add(wave);

            SerializedFields.Set(level, "waves", new[] { wave });
            return level;
        }

        [Test]
        public void Victory_WithALevelRemaining_SwapsAndReturnsToBuild()
        {
            machine.Change(GamePhase.Victory);

            Assert.AreEqual(1, levels.Index, "the next map is live");
            Assert.AreEqual("Second", levels.Current.name);
            Assert.AreEqual(GamePhase.Build, machine.Current);
            Assert.AreEqual(1, build.Entered);
        }

        // The publish order the machine's before-Enter rule exists for: the victory that caused the
        // swap is announced before the build phase it led to, rather than after it.
        [Test]
        public void Victory_WithALevelRemaining_AnnouncesVictoryThenBuild()
        {
            machine.Change(GamePhase.Victory);

            CollectionAssert.AreEqual(
                new[] { GamePhase.Victory, GamePhase.Build }, published);
        }

        [Test]
        public void Victory_OnTheLastLevel_IsTerminal()
        {
            levels.Load(1);

            machine.Change(GamePhase.Victory);

            Assert.AreEqual(GamePhase.Victory, machine.Current);
            Assert.AreEqual(0, build.Entered);
            CollectionAssert.AreEqual(new[] { GamePhase.Victory }, published);
        }

        [Test]
        public void Victory_RestartsTheWaveSequenceOnTheNewLevel()
        {
            // Clear the first map's only wave, which is what a real run arrives at victory through.
            machine.Change(GamePhase.Wave);
            TickUntilTheWaveIsCleared();

            Assert.AreEqual(GamePhase.Victory, published[published.Count - 2]);
            Assert.AreEqual(GamePhase.Build, machine.Current);

            machine.Change(GamePhase.Wave);

            Assert.AreEqual(0, waves.WaveIndex, "the second map opens on its own first wave");
        }

        // The other half of the swap: enemies spawned after it walk the new map's road.
        [Test]
        public void Victory_PointsTheWaveRunnerAtTheNewLevelsPath()
        {
            machine.Change(GamePhase.Victory);
            machine.Change(GamePhase.Wave);
            waves.Tick(0f);

            Assert.AreEqual(1, registry.Active.Count);
            Assert.AreEqual(SecondRoad[0], (Vector2)registry.Active[0].transform.position);
        }

        // Bounded rather than a while-loop, so a sequence that never clears fails as an assertion
        // instead of hanging the run -- WaveRunnerTests' technique.
        void TickUntilTheWaveIsCleared()
        {
            for (int i = 0; i < 32 && machine.Current == GamePhase.Wave; i++)
            {
                machine.Tick(0.5f);
            }

            Assert.AreNotEqual(GamePhase.Wave, machine.Current, "the wave should have cleared");
        }
    }
}
