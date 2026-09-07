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
        public void Enter() => EventBus<BuildActionRequested>.Subscribe(OnBuildActionRequested);

        // Building is allowed because this state is ticked, and stops during a wave because
        // WaveState does not tick it. §9 argued that a driven tick makes pausing free and §10
        // recorded an allocation-budget violation that would "repair itself with no code change"
        // when this landed; the repair is this line's absence from WaveState, which is as close to
        // no code change as the prediction could have got.
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
            // wave starts, nothing can pop that stack, so ownership ends here.
            build.ClearHistory();
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
