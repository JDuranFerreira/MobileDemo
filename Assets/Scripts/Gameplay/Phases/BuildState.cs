using System;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Towers;

namespace MobileDemo.Gameplay.Phases
{
    // Place and sell towers; tap Go to send the wave.
    //
    // This is the state that makes §4's shared IGameState pay. That section recorded three empty
    // Exit() bodies as the price of sharing the interface with the enemy's micro machine, and
    // predicted Exit would earn its keep here. It does two jobs, and both are ones a guard clause
    // would have done worse.
    public sealed class BuildState : IGameState
    {
        readonly GameStateMachine machine;
        readonly BuildController build;
        readonly ProjectileFactory projectiles;

        public BuildState(
            GameStateMachine machine, BuildController build, ProjectileFactory projectiles)
        {
            this.machine = machine ?? throw new ArgumentNullException(nameof(machine));
            this.build = build ?? throw new ArgumentNullException(nameof(build));
            this.projectiles = projectiles ?? throw new ArgumentNullException(nameof(projectiles));
        }

        // Subscribed per phase rather than for the round, which is the interesting half: the Go
        // button cannot start a second wave mid-wave because nothing is listening for it, rather
        // than because something checked. The subscription's lifetime *is* the rule.
        public void Enter()
        {
            EventBus<BuildActionRequested>.Subscribe(OnBuildActionRequested);

            // The undo button works in this phase and nowhere else, and this is where that becomes
            // true. BuildController records to its stack only while the scope is open, so a tower
            // built during a wave is permanent the instant it is bought -- without the controller
            // ever learning what a phase is.
            build.OpenUndoScope();
        }

        // Building is allowed because this state is ticked -- and it is now allowed during a wave
        // too, because WaveState ticks it as well. What the two phases still do not share is undo:
        // see Enter.
        //
        // Projectiles still fly, and that is not an oversight. A wave is cleared when the last
        // enemy dies, which can leave a shot mid-air; without this it would hang there, frozen,
        // until the next wave started, and never return to its pool.
        public void Tick(float dt)
        {
            build.Tick();
            projectiles.Tick(dt);
        }

        public void Exit()
        {
            EventBus<BuildActionRequested>.Unsubscribe(OnBuildActionRequested);

            // §6 named this exact method as the trigger for destroying a sold tower: a sale
            // deactivates rather than destroys because undo has to restore the instance, not
            // manufacture a replacement, so the GameObject is owned by the undo stack. Once the
            // wave starts, nothing can pop that stack, so ownership ends here. CloseUndoScope
            // clears it and stops the next phase's commands joining it.
            build.CloseUndoScope();

            // A ghost is a question the player was asked in this phase, so it does not outlive it.
            // WaveState.Exit says the same thing for the other direction.
            build.CancelPending();
        }

        void OnBuildActionRequested(BuildActionRequested evt)
        {
            if (evt.Action != BuildAction.StartWave)
            {
                return;
            }

            // Changing phase from inside a dispatch unsubscribes this handler mid-publish, via
            // Exit above. That is safe by construction rather than by luck: EventBus holds
            // subscribers in a multicast Action, which is immutable, so the in-flight call runs to
            // completion on the snapshot it started with. §14 pins that behaviour deliberately.
            machine.Change(GamePhase.Wave);
        }
    }
}
