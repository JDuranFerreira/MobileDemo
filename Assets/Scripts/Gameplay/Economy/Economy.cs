using MobileDemo.Core.Events;
using UnityEngine;

namespace MobileDemo.Gameplay
{
    public sealed class Economy
    {
        public Economy(int startingLives, int startingCurrency)
        {
            Lives = Mathf.Max(0, startingLives);
            Currency = Mathf.Max(0, startingCurrency);
        }

        public int Lives { get; private set; }

        public int Currency { get; private set; }

        // Called by the owning MonoBehaviour's OnEnable/OnDisable: a plain object has neither,
        // and the bus holds a strong reference until it is unsubscribed. The method group is
        // required -- a lambda here would remove nothing.
        public void Subscribe()
        {
            EventBus<EnemyLeaked>.Subscribe(OnEnemyLeaked);
            EventBus<EnemyKilled>.Subscribe(OnEnemyKilled);
        }

        public void Unsubscribe()
        {
            EventBus<EnemyLeaked>.Unsubscribe(OnEnemyLeaked);
            EventBus<EnemyKilled>.Unsubscribe(OnEnemyKilled);
        }

        // Announced from the owner's Start, not its Awake, so a subscriber wired up in OnEnable
        // sees the opening values without racing another GameObject's Awake.
        public void PublishCurrentState()
        {
            EventBus<LivesChanged>.Publish(new LivesChanged(Lives));
            EventBus<CurrencyChanged>.Publish(new CurrencyChanged(Currency));
        }

        // Returns false rather than throwing: tapping the board with an empty purse is a normal
        // player-reachable outcome, not a bug, and this class already prefers a recoverable answer
        // (the constructor clamps a negative rather than throwing) -- as do ObjectPool.Release and
        // ProjectileFactory.Create.
        //
        // There is no CanAfford beside this. Currency is already a public getter and
        // BuildController holds this object by construction, so an afford check is a comparison at
        // the call site; a method for it would be a wrapper over a property. That is also why
        // BuildController subscribes to no events at all -- see §8.
        public bool TrySpend(int amount)
        {
            // A cost of zero is legal authoring, so it succeeds; it just changes nothing, and
            // announcing an unchanged total would break the publish-only-on-change invariant the
            // two handlers below keep.
            if (amount <= 0)
            {
                return true;
            }

            if (amount > Currency)
            {
                return false;
            }

            Currency -= amount;
            EventBus<CurrencyChanged>.Publish(new CurrencyChanged(Currency));
            return true;
        }

        // Named Refund rather than Earn, though the arithmetic is identical to a kill's reward.
        // Earning arrives through the bus as a past-tense fact; a refund is an imperative from a
        // command that holds this object, which is §6's command-versus-event distinction made a
        // method name. It also keeps the two separable if a refund fee ever lands.
        //
        // This is the other half of §6's rule for undo: it restores the balance *and* lets
        // CurrencyChanged raise again, or the HUD sits on the pre-undo number while this object
        // holds the real one.
        public void Refund(int amount)
        {
            if (amount <= 0)
            {
                return;
            }

            Currency += amount;
            EventBus<CurrencyChanged>.Publish(new CurrencyChanged(Currency));
        }

        void OnEnemyKilled(EnemyKilled evt)
        {
            if (evt.Reward <= 0)
            {
                return;
            }

            Currency += evt.Reward;
            EventBus<CurrencyChanged>.Publish(new CurrencyChanged(Currency));
        }

        void OnEnemyLeaked(EnemyLeaked evt)
        {
            int next = Mathf.Max(0, Lives - evt.Damage);
            if (next == Lives)
            {
                return;
            }

            // Only on change, so the crossing to zero announces exactly once.
            Lives = next;
            EventBus<LivesChanged>.Publish(new LivesChanged(Lives));
        }
    }
}
