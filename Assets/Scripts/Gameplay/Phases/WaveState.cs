using System;
using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using MobileDemo.Gameplay.Waves;
using UnityEngine;

namespace MobileDemo.Gameplay.Phases
{
    // Drives the active WaveRunner, and owns the combat tick order that used to live in
    // Bootstrap.Update.
    //
    // It also owns which wave comes next. That could have been a field on the machine or a small
    // RoundProgress object, and neither earns its place: exactly one type reads the number and
    // exactly one type advances it, so it is a private int here rather than shared state two
    // things can disagree about.
    public sealed class WaveState : IGameState
    {
        readonly GameStateMachine machine;
        readonly WaveRunner waves;
        readonly LevelRunner levels;
        readonly ProjectileFactory projectiles;

        int nextWaveIndex;

        public WaveState(
            GameStateMachine machine, WaveRunner waves, LevelRunner levels,
            ProjectileFactory projectiles)
        {
            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
            this.waves = waves ?? throw new ArgumentNullException(nameof(waves));
            this.levels = levels ?? throw new ArgumentNullException(nameof(levels));
            this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        }

        // Called by VictoryState after a swap, not by a subscription. This object holds the runner
        // by construction, so learning about the change from the bus would be the Observer misuse
        // §6 has now declined three times -- and the call has to happen *between* the swap and the
        // next Enter, which an event's delivery order would only appear to guarantee.
        //
        // Both halves are the same fact stated to two objects: the new map's waves start at zero,
        // and its path is the one enemies now walk.
        public void OnLevelChanged()
        {
            nextWaveIndex = 0;
            waves.Bind(levels.Waypoints);
        }

        public void Enter()
        {
            Level level = levels.Current;
            IReadOnlyList<WaveDefinition> sequence = level.Waves;

            // A level with no waves cannot be played, and the honest answer is to say so and end
            // the round rather than to sit in a wave that never starts. Bootstrap's unusable-path
            // and no-bounds checks are the same shape: one legible error beats a board that looks
            // alive and does nothing.
            if (nextWaveIndex >= sequence.Count)
            {
                Debug.LogError(
                    $"WaveState has no wave {nextWaveIndex} to run: level '{level.name}' "
                    + $"defines {sequence.Count}. Going straight to victory.");
                machine.Change(GamePhase.Victory);
                return;
            }

            waves.StartWave(sequence[nextWaveIndex], nextWaveIndex);
        }

        // §9's ordering, moved rather than changed: enemies move first (inside WaveRunner.Tick),
        // then towers scan the positions they moved to, then projectiles fly at those same
        // positions. Ticking towers first would aim every shot one frame stale.
        //
        // "Building goes first of all" is gone from that order, and it is not an omission. It
        // existed so a tower added or removed this frame was settled before anything iterated the
        // list; build and combat now run in different phases and so never in the same frame, which
        // is a stronger guarantee than the ordering was.
        public void Tick(float dt)
        {
            waves.Tick(dt);
            TickTowers(dt);
            projectiles.Tick(dt);

            if (!waves.IsCleared)
            {
                return;
            }

            nextWaveIndex++;
            machine.Change(
                nextWaveIndex < levels.Current.Waves.Count ? GamePhase.Build : GamePhase.Victory);
        }

        // Nothing to undo. The wave's own teardown is the runner going quiet on its own, and the
        // enemies it spawned are already back in the pool -- IsCleared is what says so.
        public void Exit()
        {
        }

        // Backwards, because the list can change during a round. Moved here from Bootstrap intact:
        // the level still owns the live tower set, because a runtime-placed tower is the level's
        // child and must not outlive a level swap (§6).
        void TickTowers(float dt)
        {
            IReadOnlyList<Tower> towers = levels.Current.Towers;
            for (int i = towers.Count - 1; i >= 0; i--)
            {
                if (towers[i] != null)
                {
                    towers[i].Tick(dt);
                }
            }
        }
    }
}
