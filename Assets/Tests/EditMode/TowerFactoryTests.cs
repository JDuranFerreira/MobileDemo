using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // The project's first Factory that is not also an Object Pool, so the thing worth asserting is
    // the opposite of EnemyFactory's: Create really does produce a fresh instance, because towers
    // are deliberately not recycled.
    public class TowerFactoryTests
    {
        static readonly Vector2 Spot = new Vector2(1f, 2f);

        BuildScaffold scaffold;

        [SetUp]
        public void SetUp() => scaffold = new BuildScaffold();

        [TearDown]
        public void TearDown() => scaffold.Dispose();

        [Test]
        public void Create_AppliesTheDefinitionAndPositionsTheTower()
        {
            Tower tower = scaffold.Place(scaffold.Green, Spot);

            Assert.AreSame(scaffold.Green, tower.Definition);
            Assert.AreEqual(Spot, (Vector2)tower.transform.position);
        }

        [Test]
        public void Create_ParentsToTheGivenParent()
        {
            Tower tower = scaffold.Place(scaffold.Green, Spot);

            Assert.AreSame(scaffold.Level.transform, tower.transform.parent);
        }

        /// <summary>
        /// Not pooled, deliberately — so two Creates are two towers, and there is no recycling
        /// lifecycle to reason about. Pinned so removing that decision has to be a decision.
        /// </summary>
        [Test]
        public void Create_Twice_ReturnsTwoDistinctInstances()
        {
            Tower first = scaffold.Place(scaffold.Green, Spot);
            Tower second = scaffold.Place(scaffold.Green, Spot + Vector2.right);

            Assert.AreNotSame(first, second);
        }

        [Test]
        public void Create_ReturnsATowerThatFiresOnTick()
        {
            Tower tower = scaffold.Place(scaffold.Green, Spot);
            scaffold.AddTargetableEnemyAt(Spot);

            BuildScaffold.TickUntilItFires(tower);

            Assert.AreEqual(1, scaffold.ShotsFired);
        }

        [Test]
        public void Create_DoesNotAddTheTowerToTheLevel()
        {
            scaffold.Place(scaffold.Green, Spot);

            Assert.AreEqual(0, scaffold.Level.Towers.Count,
                "the factory instantiates; the command decides whether the level owns it");
        }

        /// <summary>
        /// An authored tower already knows its type and needs only its collaborators, which is
        /// what lets Tower.Configure have one contract instead of a null-means-keep rule.
        /// </summary>
        [Test]
        public void Configure_OnAnAuthoredTower_KeepsItsDefinitionAndMakesItFire()
        {
            Tower authored = scaffold.NewBareTower("Authored");
            authored.transform.position = Spot;
            SerializedFields.Set(authored, "definition", scaffold.Green);

            scaffold.Towers.Configure(authored);
            scaffold.AddTargetableEnemyAt(Spot);

            Assert.AreSame(scaffold.Green, authored.Definition);

            BuildScaffold.TickUntilItFires(authored);

            Assert.AreEqual(1, scaffold.ShotsFired);
        }

        [Test]
        public void Configure_WithNull_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => scaffold.Towers.Configure(null));
        }
    }
}
