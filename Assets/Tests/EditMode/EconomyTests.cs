using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay;
using NUnit.Framework;

namespace MobileDemo.Tests.EditMode
{
    // The clean case ARCHITECTURE.md §14 describes: a plain class whose only collaborator is a
    // static bus, so there is no scene, no GameObject and no play mode anywhere in this fixture.
    public class EconomyTests
    {
        Economy economy;
        List<int> livesChanges;

        [SetUp]
        public void SetUp()
        {
            livesChanges = new List<int>();
            EventBus<LivesChanged>.Subscribe(OnLivesChanged);
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

        Economy NewSubscribedEconomy(int startingLives)
        {
            economy = new Economy(startingLives);
            economy.Subscribe();
            return economy;
        }

        [Test]
        public void Constructor_SetsStartingLives()
        {
            economy = new Economy(20);

            Assert.AreEqual(20, economy.Lives);
        }

        /// <summary>
        /// Mirrors <c>ObjectPool</c>'s stance that a bad tuning number clamps rather than throws.
        /// </summary>
        [Test]
        public void Constructor_NegativeStartingLives_ClampsToZero()
        {
            economy = new Economy(-5);

            Assert.AreEqual(0, economy.Lives);
        }

        /// <summary>
        /// Pins the construct-in-Awake / announce-in-Start contract: the opening value reaches
        /// subscribers only because someone asks for it, never from the constructor.
        /// </summary>
        [Test]
        public void PublishCurrentState_RaisesLivesChangedWithTheOpeningValue()
        {
            economy = new Economy(20);

            economy.PublishCurrentState();

            Assert.AreEqual(new[] { 20 }, livesChanges);
        }

        [Test]
        public void Constructor_RaisesNothing()
        {
            economy = new Economy(20);

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
    }
}
