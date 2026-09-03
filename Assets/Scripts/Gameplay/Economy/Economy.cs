using MobileDemo.Core.Events;
using UnityEngine;

// Namespace flattens to MobileDemo.Gameplay while the folder stays Economy/ on purpose. With
// this type in MobileDemo.Gameplay.Economy, Bootstrap.cs writing `Economy economy;` resolves
// `Economy` to the namespace and fails CS0118. Do not "fix" the inconsistency.
namespace MobileDemo.Gameplay
{
    public sealed class Economy
    {
        public Economy(int startingLives) => Lives = Mathf.Max(0, startingLives);

        public int Lives { get; private set; }

        // Called by the owning MonoBehaviour's OnEnable/OnDisable: a plain object has neither,
        // and the bus holds a strong reference until it is unsubscribed. The method group is
        // required -- a lambda here would remove nothing.
        public void Subscribe() => EventBus<EnemyLeaked>.Subscribe(OnEnemyLeaked);

        public void Unsubscribe() => EventBus<EnemyLeaked>.Unsubscribe(OnEnemyLeaked);

        // Announced from the owner's Start, not its Awake, so a subscriber wired up in OnEnable
        // sees the opening value without racing another GameObject's Awake.
        public void PublishCurrentState() => EventBus<LivesChanged>.Publish(new LivesChanged(Lives));

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
