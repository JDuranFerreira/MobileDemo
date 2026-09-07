using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Phases;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // The state that makes §4's shared IGameState pay: Exit() finally has work to do, and both of
    // its jobs are ones a guard clause would have done worse. These tests are about the two claims
    // the design rests on -- that the Go button is dead outside the build phase because nothing is
    // listening, and that a sold tower's GameObject stops being the undo stack's problem here.
    public class BuildStateTests
    {
        BuildScaffold scaffold;
        FakeInputService input;
        BuildController build;
        GameStateMachine machine;
        BuildState state;
        List<GamePhase> published;

        [SetUp]
        public void SetUp()
        {
            scaffold = new BuildScaffold();
            input = new FakeInputService();
            build = new BuildController(
                input,
                scaffold.Economy,
                scaffold.Levels,
                scaffold.Towers,
                scaffold.Catalogue,
                BuildScaffold.RefundFraction);
            build.Subscribe();

            machine = new GameStateMachine();
            state = new BuildState(machine, build, scaffold.Projectiles);
            machine.Add(GamePhase.Build, state);
            machine.Add(GamePhase.Wave, new NullState());

            published = new List<GamePhase>();
            EventBus<PhaseChanged>.Subscribe(OnPhaseChanged);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<PhaseChanged>.Unsubscribe(OnPhaseChanged);
            build?.Unsubscribe();
            scaffold.Dispose();
        }

        void OnPhaseChanged(PhaseChanged evt) => published.Add(evt.Phase);

        sealed class NullState : IGameState
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

        static void RequestStartWave() =>
            EventBus<BuildActionRequested>.Publish(new BuildActionRequested(BuildAction.StartWave));

        /// <summary>
        /// A whole purchase, driven through the machine: two taps, because the first arms a ghost
        /// and the second buys it.
        /// </summary>
        void ConfirmAt(Vector2 world)
        {
            input.Tap(world);
            machine.Tick(0.1f);
            input.Tap(world);
            machine.Tick(0.1f);
        }

        /// <summary>A sold tower, still alive and owned by the undo stack.</summary>
        Tower SellATower()
        {
            Tower tower = scaffold.Place(scaffold.Green, BuildScaffold.LegalSpot);
            scaffold.Level.AddTower(tower);

            input.Tap(BuildScaffold.LegalSpot);
            build.Tick();

            Assert.AreEqual(1, build.UndoDepth, "precondition: the sale is on the stack");
            Assert.IsTrue(tower != null, "precondition: a sale deactivates rather than destroys");
            return tower;
        }

        [Test]
        public void StartWave_WhileTheBuildPhaseIsCurrent_TransitionsToWave()
        {
            machine.Change(GamePhase.Build);

            RequestStartWave();

            Assert.AreEqual(GamePhase.Wave, machine.Current);
            Assert.AreEqual(new[] { GamePhase.Build, GamePhase.Wave }, published);
        }

        /// <summary>
        /// The claim the whole subscribe-in-Enter design rests on: the Go button cannot start a
        /// second wave mid-wave because nothing is listening for it, not because something checked
        /// a phase. The subscription's lifetime *is* the rule.
        /// </summary>
        [Test]
        public void StartWave_AfterTheBuildPhaseHasExited_IsIgnored()
        {
            machine.Change(GamePhase.Build);
            machine.Change(GamePhase.Wave);
            published.Clear();

            RequestStartWave();

            Assert.AreEqual(GamePhase.Wave, machine.Current);
            Assert.IsEmpty(published);
        }

        [Test]
        public void StartWave_BeforeTheBuildPhaseHasBeenEntered_IsIgnored()
        {
            RequestStartWave();

            Assert.IsFalse(machine.IsRunning);
            Assert.IsEmpty(published);
        }

        /// <summary>Re-entering the phase re-arms the button rather than leaving it dead.</summary>
        [Test]
        public void StartWave_AfterReturningToBuild_TransitionsAgain()
        {
            machine.Change(GamePhase.Build);
            machine.Change(GamePhase.Wave);
            machine.Change(GamePhase.Build);
            published.Clear();

            RequestStartWave();

            Assert.AreEqual(GamePhase.Wave, machine.Current);
            Assert.AreEqual(new[] { GamePhase.Wave }, published);
        }

        /// <summary>
        /// Subscribing twice would call the handler twice — EventBusTests pins that there is no
        /// hidden de-duplication — so an unbalanced Enter/Exit shows up as a double transition
        /// rather than as nothing at all.
        /// </summary>
        [Test]
        public void EnterAndExit_AreBalanced()
        {
            machine.Change(GamePhase.Build);
            machine.Change(GamePhase.Wave);
            machine.Change(GamePhase.Build);
            machine.Change(GamePhase.Wave);
            machine.Change(GamePhase.Build);
            published.Clear();

            RequestStartWave();

            Assert.AreEqual(new[] { GamePhase.Wave }, published);
        }

        [Test]
        public void Tick_DrivesTheBuildController()
        {
            machine.Change(GamePhase.Build);

            ConfirmAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(1, build.UndoDepth);
        }

        [Test]
        public void Exit_ClearsTheUndoStack()
        {
            machine.Change(GamePhase.Build);
            ConfirmAt(BuildScaffold.LegalSpot);
            Assert.AreEqual(1, build.UndoDepth, "precondition");

            machine.Change(GamePhase.Wave);

            Assert.AreEqual(0, build.UndoDepth);
        }

        /// <summary>
        /// The undo scope, which is what lets building carry on into a wave without the wave having
        /// a stack to protect: after this phase exits, a purchase still happens and records nothing.
        /// </summary>
        [Test]
        public void Exit_ClosesTheUndoScopeSoLaterPurchasesAreNotRecorded()
        {
            machine.Change(GamePhase.Build);
            machine.Change(GamePhase.Wave);

            input.Tap(BuildScaffold.LegalSpot);
            build.Tick();
            input.Tap(BuildScaffold.LegalSpot);
            build.Tick();

            Assert.AreEqual(1, scaffold.Level.Towers.Count, "the tower was still bought");
            Assert.AreEqual(0, build.UndoDepth);
        }

        /// <summary>Re-entering the phase re-opens the scope, as it re-arms the Go button.</summary>
        [Test]
        public void Enter_ReopensTheUndoScope()
        {
            machine.Change(GamePhase.Build);
            machine.Change(GamePhase.Wave);
            machine.Change(GamePhase.Build);

            ConfirmAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(1, build.UndoDepth);
        }

        /// <summary>A ghost is a question asked in this phase, so it does not outlive it.</summary>
        [Test]
        public void Exit_CancelsAPendingPlacement()
        {
            machine.Change(GamePhase.Build);
            input.Tap(BuildScaffold.LegalSpot);
            machine.Tick(0.1f);
            Assert.IsNotNull(build.Pending, "precondition");

            machine.Change(GamePhase.Wave);

            Assert.IsNull(build.Pending);
        }

        /// <summary>
        /// §6 named this exact moment as the trigger for destroying a sold tower. A sale
        /// deactivates rather than destroys so Undo can restore the instance, which leaves the
        /// GameObject owned by the undo stack; once the wave starts nothing can pop that stack, so
        /// clearing it without discarding would leak one inactive tower per sale for the round.
        /// </summary>
        [Test]
        public void Exit_DestroysTowersSoldDuringThePhase()
        {
            machine.Change(GamePhase.Build);
            Tower sold = SellATower();

            machine.Change(GamePhase.Wave);

            Assert.IsTrue(sold == null, "a sale the player can no longer undo owns nothing");
        }

        /// <summary>
        /// A placement still on the stack is a tower the player owns and is looking at. Clearing
        /// the stack makes it permanent, not gone.
        /// </summary>
        [Test]
        public void Exit_LeavesTowersThatWerePlaced()
        {
            machine.Change(GamePhase.Build);
            ConfirmAt(BuildScaffold.LegalSpot);
            Tower placed = scaffold.Level.Towers[0];

            machine.Change(GamePhase.Wave);

            Assert.IsTrue(placed != null);
            Assert.AreEqual(1, scaffold.Level.Towers.Count);
        }
    }
}
