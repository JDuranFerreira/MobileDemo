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

    // The second UI intent, and the contrast case to BuildActionRequested -- which cannot live in
    // this file because its payload names a Gameplay type. This one has no payload at all, so
    // nothing forces it out of Core, and the pair together is what makes the rule legible: a
    // payload's type decides which assembly its event can live in, not which layer publishes it.
    //
    // Empty rather than carrying "restart what". There is one run to restart, and a field naming
    // it would be a field with no reader -- the discipline §2 applies to config numbers, applied
    // to an event payload.
    public readonly struct RestartRequested : IEvent
    {
    }
}
