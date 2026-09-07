using MobileDemo.Core.Interfaces;

namespace MobileDemo.Gameplay.Phases
{
    // The run is lost, and unlike victory this is terminal by design rather than by scope.
    //
    // §1's lives carry over across all three maps -- one pool for the whole run -- so hitting zero
    // on map 3 is not "retry map 3". That single decision is what makes Defeat asymmetric with
    // Victory, and flipping it would flip both transitions. Recorded here because an empty class
    // beside an empty VictoryState reads as two of the same thing, and they are not: this one
    // stays empty when LevelRunner lands.
    //
    // Nothing is ticked, so the board freezes mid-wave with the enemies that beat the player still
    // on it -- which is the readable outcome, where clearing them would look like the wave was
    // survived.
    public sealed class DefeatState : IGameState
    {
        public void Enter()
        {
        }

        public void Tick(float dt)
        {
        }

        public void Exit()
        {
        }
    }
}
