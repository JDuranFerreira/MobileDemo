using System;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Levels;

namespace MobileDemo.Gameplay.Phases
{
    // A level is won. §4 names this state as where the swap belongs, and that is now what it does:
    // clearing the last wave of map 1 or 2 brings in the next prefab and returns to Build, and only
    // the last map's victory ends the run.
    //
    // The asymmetry with DefeatState is §1's, not this class's: lives carry across all three maps,
    // so there is one life pool for the run and defeat is terminal while victory is not. Flipping
    // that one answer would flip both states.
    //
    // The swap runs here rather than on a LevelChanged event because every participant is held by
    // construction, and because the order matters -- advance, tell WaveState, then change phase.
    // A bus round trip would make that order look incidental.
    public sealed class VictoryState : IGameState
    {
        readonly GameStateMachine machine;
        readonly LevelRunner levels;
        readonly WaveState waves;

        public VictoryState(GameStateMachine machine, LevelRunner levels, WaveState waves)
        {
            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
            this.levels = levels ?? throw new ArgumentNullException(nameof(levels));
            this.waves = waves ?? throw new ArgumentNullException(nameof(waves));
        }

        public void Enter()
        {
            // The run is over: no next map. The board freezes because nothing is ticked -- not
            // because anything was disabled and not because a timeScale was touched. §9's "pausing
            // is free" argument, cashed in for the third time. EndScreen has already heard
            // PhaseChanged(Victory), which the machine publishes before this call.
            if (!levels.HasNext)
            {
                return;
            }

            if (!levels.Advance())
            {
                // Advance only fails on a prefab the runner has already logged. Staying here is the
                // honest answer: a frozen board on the map just won beats returning to a build
                // phase whose level did not load.
                return;
            }

            waves.OnLevelChanged();

            // Re-entering from inside Enter, which the machine supports by design: it publishes
            // PhaseChanged *before* Enter precisely so this inner change announces after the
            // victory it followed rather than before it. The two publishes land in the same frame,
            // so EndScreen is shown and hidden without a render between -- §13.4 checks that on
            // screen rather than asserting it here.
            machine.Change(GamePhase.Build);
        }

        public void Tick(float dt)
        {
        }

        public void Exit()
        {
        }
    }
}
