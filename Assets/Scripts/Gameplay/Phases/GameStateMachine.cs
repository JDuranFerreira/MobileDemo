using System;
using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using UnityEngine;

namespace MobileDemo.Gameplay.Phases
{
    // The round's machine. ARCHITECTURE.md §4 ranks State highest of the patterns here for a job
    // signal, and this is the half that was still a diagram: the enemy's micro machine shipped
    // first, deliberately, because a one-state round machine would have demonstrated nothing.
    //
    // It shares IGameState with the enemy states, and that sharing is what decided the shape
    // below. Keying states by GamePhase rather than putting a `GamePhase Phase` member on the
    // interface is not a style preference: EnemySpawningState, EnemyMovingState and
    // EnemyDyingState implement the same interface and have no phase, so the member would have to
    // be answered three times with a lie. §4's claim is that reusing the shape shows the pattern
    // generalises, and a member only one implementer can answer would have quietly withdrawn it.
    //
    // A plain class, not a MonoBehaviour: driven by Bootstrap.Update, testable with an explicit
    // dt, and pausable by not being ticked (§9).
    public sealed class GameStateMachine
    {
        readonly Dictionary<GamePhase, IGameState> states =
            new Dictionary<GamePhase, IGameState>();

        IGameState current;

        /// <summary>The phase last entered. Meaningless until <see cref="Change"/> has run once.</summary>
        public GamePhase Current { get; private set; }

        public bool IsRunning => current != null;

        public void Add(GamePhase phase, IGameState state)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }

            // Replacing rather than adding would leave the previous state subscribed to whatever
            // its Enter took out, with nothing left holding it to Exit.
            if (states.ContainsKey(phase))
            {
                throw new ArgumentException($"Phase {phase} already has a state.", nameof(phase));
            }

            states.Add(phase, state);
        }

        // The one place PhaseChanged is published. Each state's Enter() could raise it instead --
        // it is one line either way -- but then four files would have to agree on a rule, and the
        // failure mode of the fifth forgetting is a HUD that silently stops updating. Here the
        // publish cannot be omitted because it is not a state's job at all.
        //
        // Exit -> assign -> Enter, matching Enemy's swap exactly. §13 declined a shared
        // StateMachine<T> in Core on the grounds that the two implementations could not be shown
        // identical until both existed; both exist now, and the honest answer is that they still
        // should not share one. Enemy's swap is four lines with no dictionary, no phase key and no
        // event -- extracting a common base would leave each with more scaffolding than it saved.
        public void Change(GamePhase phase)
        {
            if (!states.TryGetValue(phase, out IGameState next))
            {
                Debug.LogError(
                    $"GameStateMachine has no state registered for {phase}, so the round is "
                    + $"stuck in {Current}.");
                return;
            }

            // A change to the phase already current is ignored rather than re-entered. The defeat
            // check below is the reason it can happen at all, and re-entering would run Exit and
            // Enter on the same instance -- which for BuildState means unsubscribing and
            // resubscribing its Go button, and clearing an undo stack a second time.
            if (current != null && ReferenceEquals(current, next))
            {
                return;
            }

            current?.Exit();
            current = next;
            Current = phase;

            // Announced *before* Enter, which is the opposite of the obvious order and is load
            // bearing. A state may transition again from inside its own Enter -- WaveState does,
            // when the sequence it was asked for is already exhausted -- and with the publish
            // after Enter that inner change announces first and the outer one announces the
            // abandoned phase last, leaving every subscriber holding a phase the round already
            // left. Publishing first makes the bus carry transitions in the order they happened.
            //
            // The cost is that a subscriber hears about a phase a frame-fraction before its state
            // has set itself up. Nothing reads state through the bus -- PhaseChanged carries the
            // enum and nothing else -- so there is nothing to observe half-built.
            EventBus<PhaseChanged>.Publish(new PhaseChanged(phase));
            current.Enter();
        }

        public void Tick(float dt) => current?.Tick(dt);

        // Called by the owning MonoBehaviour's OnEnable/OnDisable -- Economy's and
        // BuildController's pattern, and the method group is load-bearing for the same reason: a
        // lambda would remove nothing.
        //
        // LivesChanged is the §8 row that has waited three slices for this consumer. Economy
        // publishes only on change and clamps at zero, so the crossing announces exactly once and
        // this handler needs no "have I already lost" flag of its own.
        public void Subscribe() => EventBus<LivesChanged>.Subscribe(OnLivesChanged);

        public void Unsubscribe() => EventBus<LivesChanged>.Unsubscribe(OnLivesChanged);

        // Defeat from whatever phase the round is in, without asking which. Lives only fall during
        // a wave today, so a "only if Current is Wave" guard would be a condition that is always
        // true -- and one that would silently swallow the loss if a future cost ever charged lives
        // outside one. Change's own same-phase guard is what makes a repeat harmless.
        void OnLivesChanged(LivesChanged evt)
        {
            if (evt.Total <= 0)
            {
                Change(GamePhase.Defeat);
            }
        }

        // Teardown for the *current* state, which nothing else can do. A state that subscribes in
        // Enter() -- BuildState does -- is unsubscribed by its Exit(), and scene teardown never
        // calls Exit(). Without this, restarting the scene with domain reload off leaves a dead
        // BuildState on the bus holding a destroyed Level, and the second round throws
        // MissingReferenceException from code that reads as correct. That is precisely the failure
        // EventBus.ClearAll exists to prevent between play *sessions*; ClearAll runs at
        // SubsystemRegistration, which a scene load does not reach.
        public void Shutdown()
        {
            current?.Exit();
            current = null;
        }
    }
}
