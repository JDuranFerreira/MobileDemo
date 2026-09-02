using UnityEngine;

namespace MobileDemo.Core.Events
{
    public enum GamePhase
    {
        Build,
        Wave,
        Victory,
        Defeat
    }

    public readonly struct EnemyKilled : IEvent
    {
        public readonly int Reward;

        /// <summary>Where it died — the anchor for a death effect or floating reward label.</summary>
        public readonly Vector2 Position;

        public EnemyKilled(int reward, Vector2 position)
        {
            Reward = reward;
            Position = position;
        }
    }
    public readonly struct EnemyLeaked : IEvent
    {
        public readonly int Damage;

        public EnemyLeaked(int damage) => Damage = damage;
    }
    public readonly struct CurrencyChanged : IEvent
    {
        public readonly int Total;

        public CurrencyChanged(int total) => Total = total;
    }

    public readonly struct LivesChanged : IEvent
    {
        public readonly int Total;

        public LivesChanged(int total) => Total = total;
    }

    public readonly struct PhaseChanged : IEvent
    {
        public readonly GamePhase Phase;

        public PhaseChanged(GamePhase phase) => Phase = phase;
    }

    public readonly struct WaveCompleted : IEvent
    {
        public readonly int WaveIndex;

        public WaveCompleted(int waveIndex) => WaveIndex = waveIndex;
    }
}
