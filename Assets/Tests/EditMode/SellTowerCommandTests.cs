using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;

namespace MobileDemo.Tests.EditMode
{
    public class SellTowerCommandTests
    {
        BuildScaffold scaffold;
        List<int> currencyChanges;
        Tower tower;

        [SetUp]
        public void SetUp()
        {
            scaffold = new BuildScaffold();
            currencyChanges = new List<int>();
            EventBus<CurrencyChanged>.Subscribe(OnCurrencyChanged);

            tower = scaffold.Place(scaffold.Red, BuildScaffold.OtherLegalSpot);
            scaffold.Level.AddTower(tower);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<CurrencyChanged>.Unsubscribe(OnCurrencyChanged);
            scaffold.Dispose();
        }

        void OnCurrencyChanged(CurrencyChanged evt) => currencyChanges.Add(evt.Total);

        SellTowerCommand NewCommand() => new SellTowerCommand(
            scaffold.Towers, scaffold.Level, scaffold.Economy, tower, BuildScaffold.RefundFraction);

        [Test]
        public void Execute_RemovesTheTowerFromTheLevelAndDeactivatesIt()
        {
            NewCommand().Execute();

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.IsFalse(tower.gameObject.activeSelf);
        }

        /// <summary>
        /// Deactivated, never destroyed — and this is load-bearing rather than a preference. With
        /// destroy-and-recreate, the stack [Place, Sell] breaks: undoing the sell would yield a
        /// *new* instance, so undoing the place beneath it would then destroy a reference that is
        /// already gone, leaving the new tower on the board and refunding its cost anyway.
        /// </summary>
        [Test]
        public void Execute_DoesNotDestroyTheTower()
        {
            NewCommand().Execute();

            Assert.IsTrue(tower != null, "the tower must survive its own sale");
        }

        /// <summary>
        /// Floored, not rounded: floor(75 * 0.5) = 37, where rounding would give 38 and make an
        /// odd-cost sell-and-rebuy loop mint a coin each time.
        /// </summary>
        [Test]
        public void Execute_RefundsTheFlooredFractionOfCost()
        {
            SellTowerCommand command = NewCommand();

            command.Execute();

            Assert.AreEqual(37, command.Refund);
            Assert.AreEqual(137, scaffold.Economy.Currency);
            Assert.AreEqual(new[] { 137 }, currencyChanges);
        }

        [Test]
        public void Undo_RestoresTheSameInstanceReactivatedAndBackOnTheList()
        {
            SellTowerCommand command = NewCommand();
            command.Execute();

            command.Undo();

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreSame(tower, scaffold.Level.Towers[0]);
            Assert.IsTrue(tower.gameObject.activeSelf);
        }

        [Test]
        public void Undo_ChargesTheRefundBackAndRepublishes()
        {
            SellTowerCommand command = NewCommand();
            command.Execute();

            command.Undo();

            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(new[] { 137, 100 }, currencyChanges);
        }

        /// <summary>
        /// The money-printer guard. Selling and rebuying the same tower must cost the player, or
        /// the loop is an income source: 100 -> +37 -> -75 leaves 62, not 100.
        /// </summary>
        [Test]
        public void SellThenRebuy_CostsTheDifference()
        {
            NewCommand().Execute();

            Assert.IsTrue(scaffold.Economy.TrySpend(BuildScaffold.RedCost));
            Assert.AreEqual(62, scaffold.Economy.Currency);
        }

        /// <summary>A definition-less tower refunds nothing rather than throwing.</summary>
        [Test]
        public void Execute_OnATowerWithNoDefinition_RefundsNothing()
        {
            Tower bare = scaffold.NewBareTower("Bare");
            scaffold.Level.AddTower(bare);

            new SellTowerCommand(
                scaffold.Towers, scaffold.Level, scaffold.Economy, bare,
                BuildScaffold.RefundFraction).Execute();

            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.IsEmpty(currencyChanges);
        }

        /// <summary>
        /// The other end of the deactivate-don't-destroy rule. A sold tower stays alive so Undo can
        /// restore the instance, which leaves it owned by the undo stack — so when that stack is
        /// cleared at the phase boundary, the sale becomes permanent and the object has no owner
        /// left. §6 named BuildState.Exit() as this trigger before either existed.
        /// </summary>
        [Test]
        public void Discard_AfterExecute_DestroysTheTower()
        {
            SellTowerCommand command = NewCommand();
            command.Execute();

            command.Discard();

            Assert.IsTrue(tower == null, "a discarded sale must not leave its tower behind");
        }

        /// <summary>
        /// An undone sale put the tower back on the board, so it belongs to the player again and a
        /// later stack clear must not destroy it. Without the guard this is a tower that vanishes
        /// one wave after the player took its sale back.
        /// </summary>
        [Test]
        public void Discard_AfterUndo_LeavesTheTowerAlone()
        {
            SellTowerCommand command = NewCommand();
            command.Execute();
            command.Undo();

            command.Discard();

            Assert.IsTrue(tower != null, "an undone sale leaves the tower the player's");
            Assert.AreEqual(1, scaffold.Level.Towers.Count);
        }

        /// <summary>A sale that never executed has nothing to destroy.</summary>
        [Test]
        public void Discard_WithoutExecute_DoesNothing()
        {
            NewCommand().Discard();

            Assert.IsTrue(tower != null);
        }
    }
}
