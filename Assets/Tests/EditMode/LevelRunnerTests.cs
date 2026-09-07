using System.Collections.Generic;
using System.Text.RegularExpressions;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileDemo.Tests.EditMode
{
    // The swap, tested with three level templates and no scene. BuildScaffold supplies the towers,
    // the projectile pool and the template factory; what is asserted here is only what the runner
    // itself decides -- which level is live, what it destroyed on the way, and the geometry it
    // derived from the one that arrived.
    public class LevelRunnerTests
    {
        static readonly Vector2[] FirstRoad = { new Vector2(-4f, 0f), new Vector2(4f, 0f) };
        static readonly Vector2[] SecondRoad = { new Vector2(0f, -3f), new Vector2(0f, 3f) };

        BuildScaffold scaffold;
        Level[] templates;

        [SetUp]
        public void SetUp()
        {
            scaffold = new BuildScaffold();
            templates = new[]
            {
                scaffold.NewLevelTemplate("First", FirstRoad),
                scaffold.NewLevelTemplate("Second", SecondRoad, 8f),
                scaffold.NewLevelTemplate("Third", FirstRoad),
            };
        }

        [TearDown]
        public void TearDown() => scaffold.Dispose();

        LevelRunner NewRunner(params Level[] levels) => new LevelRunner(
            levels,
            scaffold.Towers,
            BuildScaffold.RoadClearance,
            BuildScaffold.TowerSpacing,
            scaffold.Root.transform);

        [Test]
        public void NewRunner_HasNothingLoadedYet()
        {
            LevelRunner runner = NewRunner(templates);

            Assert.IsNull(runner.Current);
            Assert.IsNull(runner.Rules);
            Assert.AreEqual(-1, runner.Index);
            Assert.AreEqual(3, runner.Count);
        }

        [Test]
        public void NewRunner_WithNoLevels_Throws()
        {
            Assert.Throws<System.ArgumentException>(() => NewRunner());
        }

        [Test]
        public void Load_InstantiatesTheLevelRatherThanUsingThePrefab()
        {
            LevelRunner runner = NewRunner(templates);

            Assert.IsTrue(runner.Load(0));
            Assert.AreNotSame(templates[0], runner.Current, "the template must stay untouched");
            Assert.AreEqual("First", runner.Current.name, "named for the prefab, not '(Clone)'");
            Assert.AreEqual(0, runner.Index);
        }

        [Test]
        public void Load_BakesThePathSoTheWaypointsAreReadable()
        {
            LevelRunner runner = NewRunner(templates);
            runner.Load(0);

            CollectionAssert.AreEqual(FirstRoad, runner.Waypoints);
        }

        // The rules are the reason the runner exists rather than a level reference passed around:
        // they are derived from the live level's geometry, so they have to be rebuilt per map.
        [Test]
        public void Load_BuildsPlacementRulesFromTheLoadedLevel()
        {
            LevelRunner runner = NewRunner(templates);
            runner.Load(0);

            Vector2 clearOfTheFirstRoad = new Vector2(0f, 3f);
            Assert.IsTrue(runner.Rules.IsLegal(clearOfTheFirstRoad, runner.Current.Towers));

            runner.Load(1);

            Assert.IsFalse(
                runner.Rules.IsLegal(clearOfTheFirstRoad, runner.Current.Towers),
                "the second map's road runs up x = 0, so the same spot is now on it");
            Assert.IsTrue(runner.Rules.IsLegal(new Vector2(3f, 0f), runner.Current.Towers));
        }

        // The second map is half the size, so a spot legal on the first is off the board here.
        // Bounds come from the level's own map art, which is why a per-level rule and not a run
        // constant.
        [Test]
        public void Load_RebuildsTheBoardBoundsFromTheNewMap()
        {
            LevelRunner runner = NewRunner(templates);
            runner.Load(1);

            Assert.IsFalse(runner.Rules.IsLegal(new Vector2(7f, 2f), runner.Current.Towers));
        }

        [Test]
        public void Load_DestroysThePreviousLevelAndItsTowers()
        {
            LevelRunner runner = NewRunner(templates);
            runner.Load(0);

            Level first = runner.Current;
            Tower placed = scaffold.Towers.Create(scaffold.Green, Vector2.zero, first.transform);
            first.AddTower(placed);

            runner.Load(1);

            Assert.IsTrue(first == null, "the outgoing level is destroyed, not merely detached");
            Assert.IsTrue(placed == null, "its towers are its children and go with it");
        }

        [Test]
        public void Load_OutOfRange_KeepsTheCurrentLevelAndSaysSo()
        {
            LevelRunner runner = NewRunner(templates);
            runner.Load(0);
            Level loaded = runner.Current;

            LogAssert.Expect(LogType.Error, new Regex("no level 3"));

            Assert.IsFalse(runner.Load(3));
            Assert.AreSame(loaded, runner.Current);
            Assert.AreEqual(0, runner.Index);
        }

        [Test]
        public void Advance_WalksTheSequenceAndStopsAtTheLast()
        {
            LevelRunner runner = NewRunner(templates);
            runner.Load(0);

            Assert.IsTrue(runner.HasNext);
            Assert.IsTrue(runner.Advance());
            Assert.AreEqual(1, runner.Index);

            Assert.IsTrue(runner.Advance());
            Assert.AreEqual(2, runner.Index);

            Assert.IsFalse(runner.HasNext);
            Assert.IsFalse(runner.Advance(), "the last map's victory ends the run");
            Assert.AreEqual(2, runner.Index, "and a refused advance changes nothing");
        }

        // The pools are prewarmed once, at boot, from what this returns -- so a projectile named
        // only by the third map's towers has to be in it before the first map is played. §7's
        // silently-never-fires failure, reachable a second way once there is more than one map.
        [Test]
        public void CollectProjectileDefinitions_ReadsEveryLevelRatherThanTheLoadedOne()
        {
            ProjectileDefinition lateMapOnly =
                ScriptableObject.CreateInstance<ProjectileDefinition>();
            lateMapOnly.name = "LateMapOnly";

            TowerDefinition lateTower = ScriptableObject.CreateInstance<TowerDefinition>();
            lateTower.name = "LateTower";
            SerializedFields.Set(lateTower, "projectile", lateMapOnly);

            Tower authored = scaffold.NewBareTower("AuthoredOnTheThirdMap");
            authored.transform.SetParent(templates[2].transform);
            SerializedFields.Set(authored, "definition", lateTower);
            SerializedFields.Set(templates[2], "towers", new[] { authored });

            List<ProjectileDefinition> collected = new List<ProjectileDefinition>();
            LevelRunner.CollectProjectileDefinitions(templates, collected);

            Assert.Contains(lateMapOnly, collected);

            Object.DestroyImmediate(lateTower);
            Object.DestroyImmediate(lateMapOnly);
        }

        [Test]
        public void CollectProjectileDefinitions_DoesNotRepeatOne()
        {
            List<ProjectileDefinition> collected = new List<ProjectileDefinition>
            {
                scaffold.ProjectileDefinition,
            };

            LevelRunner.CollectProjectileDefinitions(templates, collected);

            Assert.AreEqual(1, collected.Count);
        }
    }
}
