using System.Collections.Generic;
using System.Text.RegularExpressions;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileDemo.Tests.EditMode
{
    public class PlaceTowerCommandTests
    {
        BuildScaffold scaffold;
        List<int> currencyChanges;

        [SetUp]
        public void SetUp()
        {
            scaffold = new BuildScaffold();
            currencyChanges = new List<int>();
            EventBus<CurrencyChanged>.Subscribe(OnCurrencyChanged);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<CurrencyChanged>.Unsubscribe(OnCurrencyChanged);
            scaffold.Dispose();
        }

        void OnCurrencyChanged(CurrencyChanged evt) => currencyChanges.Add(evt.Total);

        PlaceTowerCommand NewCommand(TowerDefinition definition = null, Vector2? at = null)
        {
            return new PlaceTowerCommand(
                scaffold.Towers,
                scaffold.Level,
                scaffold.Economy,
                definition ?? scaffold.Green,
                at ?? BuildScaffold.LegalSpot);
        }

        [Test]
        public void Execute_SpendsTheCostAndAddsTheTowerToTheLevel()
        {
            NewCommand().Execute();

            Assert.AreEqual(100 - BuildScaffold.GreenCost, scaffold.Economy.Currency);
            Assert.AreEqual(1, scaffold.Level.Towers.Count);
        }

        [Test]
        public void Execute_GivesTheTowerItsDefinitionAndPosition()
        {
            PlaceTowerCommand command = NewCommand();

            command.Execute();

            Assert.AreSame(scaffold.Green, command.Placed.Definition);
            Assert.AreEqual(BuildScaffold.LegalSpot, (Vector2)command.Placed.transform.position);
        }

        /// <summary>
        /// Parented to the level, not to the scene root — which is what makes a runtime placement
        /// leave with its level on a swap, where a pooled enemy deliberately does not.
        /// </summary>
        [Test]
        public void Execute_ParentsTheTowerToTheLevel()
        {
            PlaceTowerCommand command = NewCommand();

            command.Execute();

            Assert.AreSame(scaffold.Level.transform, command.Placed.transform.parent);
        }

        /// <summary>
        /// The projectile-pool hole, pinned. ProjectileFactory is prewarmed once with the prefabs
        /// Bootstrap collected, and Create on an unknown one returns null — so a runtime-placed
        /// tower whose projectile was never collected fires nothing, silently. Asserted through
        /// the pool's own counter rather than a private target field (§14).
        /// </summary>
        [Test]
        public void Execute_ThenTick_PutsAProjectileInTheAir()
        {
            PlaceTowerCommand command = NewCommand();
            command.Execute();

            scaffold.AddTargetableEnemyAt(BuildScaffold.LegalSpot);
            BuildScaffold.TickUntilItFires(command.Placed);

            Assert.AreEqual(1, scaffold.ShotsFired);
        }

        /// <summary>
        /// Destroyed, not merely delisted — the opposite of a sale, which deactivates so its own
        /// undo can restore the same instance. Undoing a placement has nothing to restore.
        /// </summary>
        [Test]
        public void Undo_DestroysTheTowerRefundsTheFullCostAndDelistsIt()
        {
            PlaceTowerCommand command = NewCommand();
            command.Execute();
            Tower placed = command.Placed;

            command.Undo();

            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.IsTrue(placed == null, "the tower should be gone, not just off the list");
        }

        /// <summary>
        /// §6's rule: the refund has to re-announce, or the HUD keeps the pre-undo balance while
        /// Economy holds the real one. The sequence is the assertion.
        /// </summary>
        [Test]
        public void Undo_RepublishesCurrencyChanged()
        {
            PlaceTowerCommand command = NewCommand();
            command.Execute();
            command.Undo();

            Assert.AreEqual(new[] { 100 - BuildScaffold.GreenCost, 100 }, currencyChanges);
        }

        /// <summary>
        /// The full cost, not the sell fraction: undoing a placement is not a sale, and that
        /// asymmetry is what stops place-and-undo being a way to launder currency.
        /// </summary>
        [Test]
        public void Undo_RefundsMoreThanASellWould()
        {
            PlaceTowerCommand command = NewCommand();
            command.Execute();
            command.Undo();

            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.Greater(
                BuildScaffold.GreenCost,
                Mathf.FloorToInt(BuildScaffold.GreenCost * BuildScaffold.RefundFraction));
        }

        [Test]
        public void Execute_WithInsufficientFunds_PlacesNothingAndUndoChangesNothing()
        {
            scaffold.Economy.TrySpend(80);
            currencyChanges.Clear();

            PlaceTowerCommand command = NewCommand();

            // The invoker is supposed to check affordability, so Execute reaching a rejected spend
            // is a broken invariant and says so out loud. Expected rather than left to fail the run.
            LogAssert.Expect(LogType.Error, new Regex("PlaceTowerCommand spent nothing"));
            command.Execute();

            Assert.AreEqual(20, scaffold.Economy.Currency);
            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.IsNull(command.Placed);

            command.Undo();

            Assert.AreEqual(20, scaffold.Economy.Currency);
            Assert.IsEmpty(currencyChanges);
        }
    }
}
