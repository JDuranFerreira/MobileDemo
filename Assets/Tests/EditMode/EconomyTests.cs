using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // The clean case ARCHITECTURE.md §14 describes: a plain class whose only collaborator is a
    // static bus, so there is no scene, no GameObject and no play mode anywhere in this fixture.
    public class EconomyTests
    {
        const int NoCurrency = 0;

        Economy economy;
        List<int> livesChanges;
        List<int> currencyChanges;

        [SetUp]
        public void SetUp()
        {
            livesChanges = new List<int>();
            currencyChanges = new List<int>();
            EventBus<LivesChanged>.Subscribe(OnLivesChanged);
            EventBus<CurrencyChanged>.Subscribe(OnCurrencyChanged);
        }

        [TearDown]
        public void TearDown()
        {
            // Unsubscribe first so the pairing is exercised even by tests that never call it,
            // then ClearAll because the bus is global state (event-bus.md).
            economy?.Unsubscribe();
            EventBus.ClearAll();
        }

        void OnLivesChanged(LivesChanged evt) => livesChanges.Add(evt.Total);

        void OnCurrencyChanged(CurrencyChanged evt) => currencyChanges.Add(evt.Total);

        Economy NewSubscribedEconomy(int startingLives, int startingCurrency = NoCurrency)
        {
            economy = new Economy(startingLives, startingCurrency);
            economy.Subscribe();
            return economy;
        }

        [Test]
        public void Constructor_SetsStartingLives()
        {
            economy = new Economy(20, NoCurrency);

            Assert.AreEqual(20, economy.Lives);
        }

        /// <summary>
        /// Mirrors <c>ObjectPool</c>'s stance that a bad tuning number clamps rather than throws.
        /// </summary>
        [Test]
        public void Constructor_NegativeStartingLives_ClampsToZero()
        {
            economy = new Economy(-5, NoCurrency);

            Assert.AreEqual(0, economy.Lives);
        }

        /// <summary>
        /// Pins the construct-in-Awake / announce-in-Start contract: the opening value reaches
        /// subscribers only because someone asks for it, never from the constructor.
        /// </summary>
        [Test]
        public void PublishCurrentState_RaisesLivesChangedWithTheOpeningValue()
        {
            economy = new Economy(20, NoCurrency);

            economy.PublishCurrentState();

            Assert.AreEqual(new[] { 20 }, livesChanges);
        }

        [Test]
        public void Constructor_RaisesNothing()
        {
            economy = new Economy(20, NoCurrency);

            Assert.IsEmpty(livesChanges, "a constructor that published would race the HUD's OnEnable");
        }

        [Test]
        public void EnemyLeaked_WhileSubscribed_ReducesLivesByTheDamage()
        {
            NewSubscribedEconomy(20);

            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(3));

            Assert.AreEqual(17, economy.Lives);
        }

        [Test]
        public void EnemyLeaked_WhileSubscribed_RaisesLivesChangedWithTheNewTotal()
        {
            NewSubscribedEconomy(20);

            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(1));

            Assert.AreEqual(new[] { 19 }, livesChanges);
        }

        /// <summary>
        /// The method-group pairing this design depends on: <c>Subscribe(OnEnemyLeaked)</c> and
        /// <c>Unsubscribe(OnEnemyLeaked)</c> are distinct delegate instances, and removal still
        /// works because <c>Delegate</c> compares target + method. Were these lambdas, this test
        /// would fail and the leak would be invisible in production.
        /// </summary>
        [Test]
        public void EnemyLeaked_AfterUnsubscribe_ChangesNothing()
        {
            NewSubscribedEconomy(20);

            economy.Unsubscribe();
            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(5));

            Assert.AreEqual(20, economy.Lives);
            Assert.IsEmpty(livesChanges);
        }

        [Test]
        public void EnemyLeaked_ForMoreThanRemainingLives_ClampsAtZero()
        {
            NewSubscribedEconomy(2);

            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(10));

            Assert.AreEqual(0, economy.Lives);
        }

        /// <summary>
        /// The event §8 hands the defeat check. Raising it twice would run the check twice.
        /// </summary>
        [Test]
        public void EnemyLeaked_CrossingZero_RaisesLivesChangedExactlyOnce()
        {
            NewSubscribedEconomy(2);

            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(10));

            Assert.AreEqual(new[] { 0 }, livesChanges);
        }

        /// <summary>Pins the publish-only-on-change decision.</summary>
        [Test]
        public void EnemyLeaked_AtZeroLives_RaisesNothing()
        {
            NewSubscribedEconomy(0);

            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(1));

            Assert.AreEqual(0, economy.Lives);
            Assert.IsEmpty(livesChanges);
        }

        [Test]
        public void EnemyLeaked_ZeroDamage_RaisesNothing()
        {
            NewSubscribedEconomy(20);

            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(0));

            Assert.AreEqual(20, economy.Lives);
            Assert.IsEmpty(livesChanges);
        }

        [Test]
        public void Constructor_SetsStartingCurrency()
        {
            economy = new Economy(20, 100);

            Assert.AreEqual(100, economy.Currency);
        }

        [Test]
        public void Constructor_NegativeStartingCurrency_ClampsToZero()
        {
            economy = new Economy(20, -5);

            Assert.AreEqual(0, economy.Currency);
        }

        /// <summary>
        /// Both halves announce, and the HUD has a label for each. A currency label left blank
        /// until the first kill would look broken for the whole build phase.
        /// </summary>
        [Test]
        public void PublishCurrentState_RaisesBothOpeningValues()
        {
            economy = new Economy(20, 100);

            economy.PublishCurrentState();

            Assert.AreEqual(new[] { 20 }, livesChanges);
            Assert.AreEqual(new[] { 100 }, currencyChanges);
        }

        [Test]
        public void EnemyKilled_WhileSubscribed_AddsTheReward()
        {
            NewSubscribedEconomy(20, 100);

            EventBus<EnemyKilled>.Publish(new EnemyKilled(5, Vector2.zero));

            Assert.AreEqual(105, economy.Currency);
            Assert.AreEqual(new[] { 105 }, currencyChanges);
        }

        [Test]
        public void EnemyKilled_Repeatedly_Accumulates()
        {
            NewSubscribedEconomy(20, 0);

            EventBus<EnemyKilled>.Publish(new EnemyKilled(5, Vector2.zero));
            EventBus<EnemyKilled>.Publish(new EnemyKilled(7, Vector2.zero));

            Assert.AreEqual(12, economy.Currency);
            Assert.AreEqual(new[] { 5, 12 }, currencyChanges);
        }

        /// <summary>Mirrors the zero-damage leak: publish only when something changed.</summary>
        [Test]
        public void EnemyKilled_ZeroReward_RaisesNothing()
        {
            NewSubscribedEconomy(20, 100);

            EventBus<EnemyKilled>.Publish(new EnemyKilled(0, Vector2.zero));

            Assert.AreEqual(100, economy.Currency);
            Assert.IsEmpty(currencyChanges);
        }

        /// <summary>
        /// The two halves are independent: a kill must not touch lives, and a leak must not touch
        /// currency. They share one subscriber, so nothing but this test says so.
        /// </summary>
        [Test]
        public void EnemyKilled_DoesNotTouchLives_AndEnemyLeaked_DoesNotTouchCurrency()
        {
            NewSubscribedEconomy(20, 100);

            EventBus<EnemyKilled>.Publish(new EnemyKilled(5, Vector2.zero));
            EventBus<EnemyLeaked>.Publish(new EnemyLeaked(1));

            Assert.AreEqual(19, economy.Lives);
            Assert.AreEqual(105, economy.Currency);
            Assert.AreEqual(new[] { 19 }, livesChanges);
            Assert.AreEqual(new[] { 105 }, currencyChanges);
        }

        [Test]
        public void EnemyKilled_AfterUnsubscribe_ChangesNothing()
        {
            NewSubscribedEconomy(20, 100);

            economy.Unsubscribe();
            EventBus<EnemyKilled>.Publish(new EnemyKilled(5, Vector2.zero));

            Assert.AreEqual(100, economy.Currency);
            Assert.IsEmpty(currencyChanges);
        }

        // ---- spending, the half §13.2 added ---------------------------------------------

        [Test]
        public void TrySpend_WithSufficientFunds_ReturnsTrueAndReducesCurrency()
        {
            NewSubscribedEconomy(20, 100);

            Assert.IsTrue(economy.TrySpend(50));
            Assert.AreEqual(50, economy.Currency);
        }

        [Test]
        public void TrySpend_WithSufficientFunds_PublishesTheNewTotal()
        {
            NewSubscribedEconomy(20, 100);

            economy.TrySpend(50);

            Assert.AreEqual(new[] { 50 }, currencyChanges);
        }

        [Test]
        public void TrySpend_ForExactlyCurrency_SucceedsAndPublishesZero()
        {
            NewSubscribedEconomy(20, 100);

            Assert.IsTrue(economy.TrySpend(100));
            Assert.AreEqual(0, economy.Currency);
            Assert.AreEqual(new[] { 0 }, currencyChanges);
        }

        [Test]
        public void TrySpend_ForMoreThanCurrency_ReturnsFalseAndChangesNothing()
        {
            NewSubscribedEconomy(20, 40);

            Assert.IsFalse(economy.TrySpend(50));
            Assert.AreEqual(40, economy.Currency);
        }

        /// <summary>
        /// The publish-only-on-change invariant, on the branch most likely to break it: a rejected
        /// spend that re-announced the unchanged total would make every failed tap look like a
        /// transaction.
        /// </summary>
        [Test]
        public void TrySpend_ForMoreThanCurrency_PublishesNothing()
        {
            NewSubscribedEconomy(20, 40);

            economy.TrySpend(50);

            Assert.IsEmpty(currencyChanges);
        }

        /// <summary>A cost of zero is legal authoring, so it succeeds -- and announces nothing.</summary>
        [Test]
        public void TrySpend_Zero_SucceedsAndPublishesNothing()
        {
            NewSubscribedEconomy(20, 100);

            Assert.IsTrue(economy.TrySpend(0));
            Assert.AreEqual(100, economy.Currency);
            Assert.IsEmpty(currencyChanges);
        }

        [Test]
        public void TrySpend_Negative_SucceedsWithoutCreatingCurrency()
        {
            NewSubscribedEconomy(20, 100);

            Assert.IsTrue(economy.TrySpend(-50));
            Assert.AreEqual(100, economy.Currency);
            Assert.IsEmpty(currencyChanges);
        }

        [Test]
        public void Refund_AddsCurrencyAndPublishesTheNewTotal()
        {
            NewSubscribedEconomy(20, 50);

            economy.Refund(25);

            Assert.AreEqual(75, economy.Currency);
            Assert.AreEqual(new[] { 75 }, currencyChanges);
        }

        [Test]
        public void Refund_ZeroOrNegative_PublishesNothing()
        {
            NewSubscribedEconomy(20, 50);

            economy.Refund(0);
            economy.Refund(-10);

            Assert.AreEqual(50, economy.Currency);
            Assert.IsEmpty(currencyChanges);
        }

        /// <summary>
        /// §6's rule for undo, and the reason it is a rule: restoring the balance is not enough,
        /// the restoration has to *re-announce*, or the HUD keeps showing the pre-undo number.
        /// Asserting the sequence rather than the final total is the whole point -- a Refund that
        /// silently corrected the field would pass an equality check on Currency alone.
        /// </summary>
        [Test]
        public void SpendThenRefund_PublishesCurrencyChangedBothTimes()
        {
            NewSubscribedEconomy(20, 100);

            economy.TrySpend(50);
            economy.Refund(50);

            Assert.AreEqual(new[] { 50, 100 }, currencyChanges);
        }

        [Test]
        public void TrySpend_DoesNotTouchLives()
        {
            NewSubscribedEconomy(20, 100);

            economy.TrySpend(50);

            Assert.AreEqual(20, economy.Lives);
            Assert.IsEmpty(livesChanges);
        }

        [Test]
        public void Refund_DoesNotTouchLives()
        {
            NewSubscribedEconomy(20, 100);

            economy.Refund(50);

            Assert.AreEqual(20, economy.Lives);
            Assert.IsEmpty(livesChanges);
        }
    }
}
