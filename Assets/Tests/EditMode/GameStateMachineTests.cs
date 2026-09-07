using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Phases;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace MobileDemo.Tests.EditMode
{
    // The cleanest fixture in the project: the machine's collaborators are an interface and a
    // static bus, so this runs with no scene, no GameObject and no ScriptableObject at all.
    // EconomyTests is the only other one that can say that.
    public class GameStateMachineTests
    {
        // Records the calls in order, so a test can assert the *sequence* Exit -> Enter rather
        // than only that both happened. A NSubstitute-style mock would say the same thing with a
        // package this project does not have (§15).
        sealed class RecordingState : IGameState
        {
            readonly List<string> log;
            readonly string name;

            internal RecordingState(List<string> log, string name)
            {
                this.log = log;
                this.name = name;
            }

            internal Action OnEnter { get; set; }

            internal float TickedWith { get; private set; }

            public void Enter()
            {
                log.Add($"{name}.Enter");
                OnEnter?.Invoke();
            }

            public void Tick(float dt)
            {
                TickedWith = dt;
                log.Add($"{name}.Tick");
            }

            public void Exit() => log.Add($"{name}.Exit");
        }

        GameStateMachine machine;
        List<string> log;
        List<GamePhase> published;
        RecordingState build;
        RecordingState wave;

        [SetUp]
        public void SetUp()
        {
            machine = new GameStateMachine();
            log = new List<string>();
            published = new List<GamePhase>();

            build = new RecordingState(log, "Build");
            wave = new RecordingState(log, "Wave");
            machine.Add(GamePhase.Build, build);
            machine.Add(GamePhase.Wave, wave);

            EventBus<PhaseChanged>.Subscribe(OnPhaseChanged);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<PhaseChanged>.Unsubscribe(OnPhaseChanged);
            EventBus.ClearAll();
        }

        void OnPhaseChanged(PhaseChanged evt) => published.Add(evt.Phase);

        [Test]
        public void Add_WithNull_Throws() =>
            Assert.Throws<ArgumentNullException>(() => machine.Add(GamePhase.Victory, null));

        /// <summary>
        /// Replacing rather than refusing would leave the previous state subscribed to whatever
        /// its Enter took out, with nothing left holding it to Exit.
        /// </summary>
        [Test]
        public void Add_ForAPhaseThatAlreadyHasAState_Throws() =>
            Assert.Throws<ArgumentException>(
                () => machine.Add(GamePhase.Build, new RecordingState(log, "Other")));

        [Test]
        public void BeforeAnyChange_IsNotRunning()
        {
            Assert.IsFalse(machine.IsRunning);
            Assert.IsEmpty(log);
            Assert.IsEmpty(published);
        }

        [Test]
        public void Change_EntersTheStateAndPublishesThePhase()
        {
            machine.Change(GamePhase.Build);

            Assert.AreEqual(new[] { "Build.Enter" }, log);
            Assert.AreEqual(new[] { GamePhase.Build }, published);
            Assert.AreEqual(GamePhase.Build, machine.Current);
            Assert.IsTrue(machine.IsRunning);
        }

        [Test]
        public void Change_ExitsTheOldStateBeforeEnteringTheNew()
        {
            machine.Change(GamePhase.Build);
            log.Clear();

            machine.Change(GamePhase.Wave);

            Assert.AreEqual(new[] { "Build.Exit", "Wave.Enter" }, log);
        }

        /// <summary>
        /// Announced before Enter, which is the opposite of the obvious order and is what keeps a
        /// transition raised from inside Enter — WaveState does it, when its sequence is already
        /// exhausted — from announcing the abandoned phase last. See the next test for the case.
        /// </summary>
        [Test]
        public void Change_PublishesBeforeEnteringTheState()
        {
            List<string> interleaved = new List<string>();
            EventBus<PhaseChanged>.Subscribe(evt => interleaved.Add($"published {evt.Phase}"));
            build.OnEnter = () => interleaved.Add("Build.Enter");

            machine.Change(GamePhase.Build);

            Assert.AreEqual(new[] { "published Build", "Build.Enter" }, interleaved);
        }

        /// <summary>
        /// The re-entrancy case the ordering above exists for: a state that transitions again from
        /// inside its own Enter. With the publish after Enter, the bus would carry Wave last —
        /// leaving every subscriber holding a phase the round had already left.
        /// </summary>
        [Test]
        public void Change_FromInsideEnter_PublishesInTheOrderTheTransitionsHappened()
        {
            machine.Add(GamePhase.Victory, new RecordingState(log, "Victory"));
            wave.OnEnter = () => machine.Change(GamePhase.Victory);

            machine.Change(GamePhase.Wave);

            Assert.AreEqual(new[] { GamePhase.Wave, GamePhase.Victory }, published);
            Assert.AreEqual(GamePhase.Victory, machine.Current);
        }

        /// <summary>
        /// The defeat check can ask for a phase the round is already in. Re-entering would run
        /// Exit and Enter on the same instance, which for BuildState means unsubscribing and
        /// resubscribing its Go button and clearing an undo stack a second time.
        /// </summary>
        [Test]
        public void Change_ToThePhaseAlreadyCurrent_DoesNothing()
        {
            machine.Change(GamePhase.Build);
            log.Clear();
            published.Clear();

            machine.Change(GamePhase.Build);

            Assert.IsEmpty(log);
            Assert.IsEmpty(published);
        }

        [Test]
        public void Change_ToAnUnregisteredPhase_LogsAnErrorAndStays()
        {
            machine.Change(GamePhase.Build);
            LogAssert.Expect(LogType.Error, new Regex("no state registered"));

            machine.Change(GamePhase.Defeat);

            Assert.AreEqual(GamePhase.Build, machine.Current);
        }

        [Test]
        public void Tick_ForwardsToTheCurrentStateOnly()
        {
            machine.Change(GamePhase.Build);

            machine.Tick(0.25f);

            Assert.AreEqual(0.25f, build.TickedWith);
            Assert.AreEqual(0f, wave.TickedWith, "the state that is not current must not tick");
        }

        [Test]
        public void Tick_BeforeAnyChange_DoesNothing() => Assert.DoesNotThrow(() => machine.Tick(1f));

        /// <summary>
        /// The teardown path a restart depends on. BuildState subscribes in Enter and unsubscribes
        /// in Exit, and scene teardown never reaches Exit on its own — so without this a dead
        /// state stays on the bus holding a destroyed Level, and the second round throws
        /// MissingReferenceException from code that reads as correct.
        /// </summary>
        [Test]
        public void Shutdown_ExitsTheCurrentState()
        {
            machine.Change(GamePhase.Build);
            log.Clear();

            machine.Shutdown();

            Assert.AreEqual(new[] { "Build.Exit" }, log);
            Assert.IsFalse(machine.IsRunning);
        }

        [Test]
        public void Shutdown_Twice_ExitsOnlyOnce()
        {
            machine.Change(GamePhase.Build);
            log.Clear();

            machine.Shutdown();
            machine.Shutdown();

            Assert.AreEqual(new[] { "Build.Exit" }, log);
        }

        [Test]
        public void Shutdown_BeforeAnyChange_DoesNothing() =>
            Assert.DoesNotThrow(() => machine.Shutdown());

        /// <summary>
        /// The LivesChanged row in §8 has waited three slices for this consumer. Economy publishes
        /// only on change and clamps at zero, so the crossing announces exactly once and this
        /// handler needs no "have I already lost" flag of its own.
        /// </summary>
        [Test]
        public void LivesChangedToZero_WhileSubscribed_EntersDefeat()
        {
            machine.Add(GamePhase.Defeat, new RecordingState(log, "Defeat"));
            machine.Change(GamePhase.Wave);
            machine.Subscribe();

            EventBus<LivesChanged>.Publish(new LivesChanged(0));

            Assert.AreEqual(GamePhase.Defeat, machine.Current);
            machine.Unsubscribe();
        }

        [Test]
        public void LivesChangedAboveZero_DoesNotEndTheRound()
        {
            machine.Add(GamePhase.Defeat, new RecordingState(log, "Defeat"));
            machine.Change(GamePhase.Wave);
            machine.Subscribe();

            EventBus<LivesChanged>.Publish(new LivesChanged(1));

            Assert.AreEqual(GamePhase.Wave, machine.Current);
            machine.Unsubscribe();
        }

        [Test]
        public void LivesChangedToZero_AfterUnsubscribe_IsIgnored()
        {
            machine.Add(GamePhase.Defeat, new RecordingState(log, "Defeat"));
            machine.Change(GamePhase.Wave);
            machine.Subscribe();
            machine.Unsubscribe();

            EventBus<LivesChanged>.Publish(new LivesChanged(0));

            Assert.AreEqual(GamePhase.Wave, machine.Current);
        }
    }
}
