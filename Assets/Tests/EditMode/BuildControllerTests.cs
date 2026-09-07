using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // Driven entirely through FakeInputService, which is the concrete payoff §10 now claims for
    // IInputService: the tap dispatch is the new logic this slice adds, and none of it needs a
    // device, a camera or play mode to exercise.
    public class BuildControllerTests
    {
        static readonly Vector2 OnTheRoad = new Vector2(0f, 0f);
        static readonly Vector2 OffTheBoard = new Vector2(0f, 12f);

        BuildScaffold scaffold;
        FakeInputService input;
        BuildController build;

        [SetUp]
        public void SetUp()
        {
            scaffold = new BuildScaffold();
            input = new FakeInputService();
            build = NewController(scaffold.Catalogue);
            build.Subscribe();
        }

        [TearDown]
        public void TearDown()
        {
            build?.Unsubscribe();
            scaffold.Dispose();
        }

        BuildController NewController(TowerCatalogue catalogue) => new BuildController(
            input,
            scaffold.Economy,
            scaffold.Levels,
            scaffold.Towers,
            catalogue,
            BuildScaffold.RefundFraction);

        void TapAt(Vector2 world)
        {
            input.Tap(world);
            build.Tick();
        }

        /// <summary>The catalogue's first entry, so the demo is playable before anything is tapped.</summary>
        [Test]
        public void Constructor_SelectsTheFirstBuildableType()
        {
            Assert.AreSame(scaffold.Green, build.Selected);
        }

        [Test]
        public void Tick_WithNoInput_DoesNothing()
        {
            build.Tick();

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
        }

        [Test]
        public void Tick_TappingLegalGround_PlacesATowerAndSpends()
        {
            TapAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(50, scaffold.Economy.Currency);
            Assert.AreEqual(1, build.UndoDepth);
        }

        [Test]
        public void Tick_TappingTheRoad_PlacesNothingAndSpendsNothing()
        {
            TapAt(OnTheRoad);

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
        }

        [Test]
        public void Tick_TappingOffTheBoard_PlacesNothing()
        {
            TapAt(OffTheBoard);

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
        }

        /// <summary>
        /// A rejected placement leaves nothing on the undo stack, which is what "no half-executed
        /// command reaches the stack" means concretely.
        /// </summary>
        [Test]
        public void Tick_WithInsufficientFunds_PlacesNothingAndRecordsNothing()
        {
            scaffold.Economy.TrySpend(80);

            TapAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(20, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
        }

        [Test]
        public void Tick_TappingAnExistingTower_SellsItAndDoesNotAlsoPlace()
        {
            TapAt(BuildScaffold.LegalSpot);
            Assert.AreEqual(1, scaffold.Level.Towers.Count);

            TapAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(0, scaffold.Level.Towers.Count, "the tap sold rather than placing");
            Assert.AreEqual(75, scaffold.Economy.Currency);
            Assert.AreEqual(2, build.UndoDepth);
        }

        [Test]
        public void Tick_WithNothingSelected_PlacesNothing()
        {
            TowerCatalogue empty = ScriptableObject.CreateInstance<TowerCatalogue>();
            BuildController controller = NewController(empty);

            input.Tap(BuildScaffold.LegalSpot);
            controller.Tick();

            Assert.IsNull(controller.Selected);
            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Object.DestroyImmediate(empty);
        }

        [Test]
        public void SelectTower_ThroughTheBus_ChangesWhatATapBuilds()
        {
            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.SelectTower, scaffold.Red));

            TapAt(BuildScaffold.LegalSpot);

            Assert.AreSame(scaffold.Red, build.Selected);
            Assert.AreEqual(25, scaffold.Economy.Currency);
            Assert.AreSame(scaffold.Red, scaffold.Level.Towers[0].Definition);
        }

        [Test]
        public void SelectTower_WithANullDefinition_KeepsTheCurrentSelection()
        {
            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.SelectTower, null));

            Assert.AreSame(scaffold.Green, build.Selected);
        }

        [Test]
        public void Undo_ThroughTheBus_RemovesTheTowerAndRefunds()
        {
            TapAt(BuildScaffold.LegalSpot);

            EventBus<BuildActionRequested>.Publish(new BuildActionRequested(BuildAction.Undo));

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
        }

        [Test]
        public void Undo_WithAnEmptyStack_ReturnsFalse()
        {
            Assert.IsFalse(build.Undo());
        }

        [Test]
        public void Undo_AfterASell_RestoresTheTower()
        {
            TapAt(BuildScaffold.LegalSpot);
            TapAt(BuildScaffold.LegalSpot);

            Assert.IsTrue(build.Undo());

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(50, scaffold.Economy.Currency);
        }

        [Test]
        public void Undo_PopsInLifoOrder()
        {
            TapAt(BuildScaffold.LegalSpot);
            TapAt(BuildScaffold.OtherLegalSpot);
            Assert.AreEqual(2, scaffold.Level.Towers.Count);

            build.Undo();

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(
                BuildScaffold.LegalSpot,
                (Vector2)scaffold.Level.Towers[0].transform.position,
                "the *second* placement is the one that should have gone");
        }

        /// <summary>
        /// The LIFO-solvency invariant, and the reason ICommand needs no CanUndo and no bool Undo.
        /// Selling then placing spends the refund — so undoing the sell looks like it could fail
        /// for want of funds. It cannot: the place sits above the sell on the stack, so it is
        /// undone and refunded first, and the balance returns to its opening value exactly.
        /// </summary>
        [Test]
        public void Undo_AfterPlacingOnTopOfASell_UnwindsBothAndRestoresTheOpeningBalance()
        {
            TapAt(BuildScaffold.LegalSpot);
            Assert.AreEqual(50, scaffold.Economy.Currency);

            TapAt(BuildScaffold.LegalSpot);
            Assert.AreEqual(75, scaffold.Economy.Currency, "the sale refunded floor(50 * 0.5)");

            TapAt(BuildScaffold.OtherLegalSpot);
            Assert.AreEqual(25, scaffold.Economy.Currency, "the refund has now been spent");

            Assert.IsTrue(build.Undo());
            Assert.AreEqual(75, scaffold.Economy.Currency);

            Assert.IsTrue(build.Undo());
            Assert.AreEqual(50, scaffold.Economy.Currency);

            Assert.IsTrue(build.Undo());
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, scaffold.Level.Towers.Count);
        }

        [Test]
        public void BuildActionRequested_AfterUnsubscribe_DoesNothing()
        {
            TapAt(BuildScaffold.LegalSpot);
            build.Unsubscribe();

            EventBus<BuildActionRequested>.Publish(new BuildActionRequested(BuildAction.Undo));

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(1, build.UndoDepth);
        }

        /// <summary>
        /// BuildState.Exit() calls this at the phase boundary: once the wave starts, nothing can
        /// pop the stack, so everything on it is permanent.
        /// </summary>
        [Test]
        public void ClearHistory_EmptiesTheStackSoNothingCanBeUndone()
        {
            TapAt(BuildScaffold.LegalSpot);
            TapAt(BuildScaffold.OtherLegalSpot);
            Assert.AreEqual(2, build.UndoDepth, "precondition");

            build.ClearHistory();

            Assert.AreEqual(0, build.UndoDepth);
            Assert.IsFalse(build.Undo(), "an empty stack refuses rather than throwing");
            Assert.AreEqual(2, scaffold.Level.Towers.Count, "clearing makes permanent, not gone");
        }

        /// <summary>
        /// The `is SellTowerCommand` branch, at the level it lives. A sold tower is deactivated
        /// rather than destroyed so Undo can restore the instance, which leaves its GameObject
        /// owned by this stack — clearing without discarding leaks one inactive tower per sale.
        /// </summary>
        [Test]
        public void ClearHistory_DestroysTheTowersOfSalesItDiscards()
        {
            TapAt(BuildScaffold.LegalSpot);
            Tower placed = scaffold.Level.Towers[0];
            TapAt(BuildScaffold.LegalSpot);
            Assert.IsTrue(placed != null, "precondition: the sale did not destroy it");

            build.ClearHistory();

            Assert.IsTrue(placed == null);
        }

        [Test]
        public void ClearHistory_OnAnEmptyStack_DoesNothing()
        {
            Assert.DoesNotThrow(() => build.ClearHistory());
            Assert.AreEqual(0, build.UndoDepth);
        }

        /// <summary>
        /// StartWave travels this event because §8 chose one enum over three, but its receiver is
        /// BuildState. This class must ignore it rather than log or throw about a message
        /// correctly addressed elsewhere.
        /// </summary>
        [Test]
        public void BuildActionRequested_WithStartWave_IsIgnoredHere()
        {
            TapAt(BuildScaffold.LegalSpot);

            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.StartWave));

            Assert.AreEqual(1, build.UndoDepth);
            Assert.AreSame(scaffold.Green, build.Selected);
        }
    }
}
