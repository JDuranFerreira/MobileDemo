using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // Driven entirely through FakeInputService, which is the concrete payoff §10 now claims for
    // IInputService: the tap dispatch is the new logic this slice adds, and none of it needs a
    // device, a camera or play mode to exercise.
    //
    // A placement takes two taps here for the same reason it does on a phone -- the first arms a
    // ghost, the second buys it -- so ConfirmAt is what most of these fixtures use. The tests that
    // still call TapAt once are the ones about what a *single* tap does.
    public class BuildControllerTests
    {
        static readonly Vector2 OnTheRoad = new Vector2(0f, 0f);
        static readonly Vector2 OffTheBoard = new Vector2(0f, 12f);

        /// <summary>Inside <see cref="BuildScaffold.TowerSpacing"/> of the legal spot, so it confirms it.</summary>
        static readonly Vector2 JustOffTheLegalSpot =
            BuildScaffold.LegalSpot + new Vector2(BuildScaffold.TowerSpacing * 0.5f, 0f);

        BuildScaffold scaffold;
        FakeInputService input;
        BuildController build;
        List<PlacementPreviewChanged> previews;

        [SetUp]
        public void SetUp()
        {
            scaffold = new BuildScaffold();
            input = new FakeInputService();
            build = NewController(scaffold.Catalogue);
            build.Subscribe();

            previews = new List<PlacementPreviewChanged>();
            EventBus<PlacementPreviewChanged>.Subscribe(OnPreviewChanged);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus<PlacementPreviewChanged>.Unsubscribe(OnPreviewChanged);
            build?.Unsubscribe();
            scaffold.Dispose();
        }

        void OnPreviewChanged(PlacementPreviewChanged evt) => previews.Add(evt);

        BuildController NewController(TowerCatalogue catalogue) => new BuildController(
            input,
            scaffold.Economy,
            scaffold.Levels,
            scaffold.Towers,
            catalogue);

        void TapAt(Vector2 world)
        {
            input.Tap(world);
            build.Tick();
        }

        /// <summary>Arms a placement and confirms it — one purchase, as the player makes it.</summary>
        void ConfirmAt(Vector2 world)
        {
            TapAt(world);
            TapAt(world);
        }

        /// <summary>The catalogue's first entry, so the demo is playable before anything is tapped.</summary>
        [Test]
        public void Constructor_SelectsTheFirstBuildableType()
        {
            Assert.AreSame(scaffold.Green, build.Selected);
        }

        [Test]
        public void Tick_WithNoInput_DoesNothing()
        {
            build.Tick();

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
            Assert.IsNull(build.Pending);
        }

        /// <summary>
        /// The first half of a placement buys nothing. It is the whole reason the ghost exists: the
        /// player sees where the tower would go while the currency is still theirs.
        /// </summary>
        [Test]
        public void Tick_TappingLegalGround_ArmsAPendingPlacementAndSpendsNothing()
        {
            TapAt(BuildScaffold.LegalSpot);

            Assert.AreSame(scaffold.Green, build.Pending);
            Assert.AreEqual(BuildScaffold.LegalSpot, build.PendingPosition);
            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);

            Assert.AreEqual(1, previews.Count);
            Assert.IsTrue(previews[0].Active);
            Assert.AreEqual(BuildScaffold.LegalSpot, previews[0].Position);
            Assert.AreSame(scaffold.Green, previews[0].Tower);
        }

        [Test]
        public void Tick_ConfirmingAPendingPlacement_PlacesATowerAndSpends()
        {
            ConfirmAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(50, scaffold.Economy.Currency);
            Assert.AreEqual(1, build.UndoDepth);
            Assert.IsNull(build.Pending, "the ghost does not outlive the tower it asked for");
            Assert.IsFalse(previews[previews.Count - 1].Active);
        }

        /// <summary>
        /// The confirm radius is <c>TowerSpacing</c>, reused from PlacementRules — the same radius
        /// that blocks a placement beside another tower, so a thumb does not have to hit the first
        /// tap's pixel.
        /// </summary>
        [Test]
        public void Tick_ConfirmingWithinTheSpacing_CountsAsTheSameSpot()
        {
            TapAt(BuildScaffold.LegalSpot);
            TapAt(JustOffTheLegalSpot);

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(
                BuildScaffold.LegalSpot,
                (Vector2)scaffold.Level.Towers[0].transform.position,
                "the tower stands where the ghost stood, not where the confirming tap landed");
        }

        [Test]
        public void Tick_TappingAnotherLegalSpot_MovesThePendingPlacement()
        {
            TapAt(BuildScaffold.LegalSpot);
            TapAt(BuildScaffold.OtherLegalSpot);

            Assert.AreEqual(BuildScaffold.OtherLegalSpot, build.PendingPosition);
            Assert.AreEqual(0, scaffold.Level.Towers.Count, "moving a ghost buys nothing");
            Assert.AreEqual(100, scaffold.Economy.Currency);
        }

        [Test]
        public void Tick_TappingTheRoad_PlacesNothingAndSpendsNothing()
        {
            TapAt(OnTheRoad);
            TapAt(OnTheRoad);

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
            Assert.IsNull(build.Pending);
        }

        /// <summary>
        /// A mistap near the road does not throw away a ghost the player has already positioned —
        /// silent rejection means the board is left exactly as it was.
        /// </summary>
        [Test]
        public void Tick_TappingTheRoadWithSomethingPending_LeavesItPending()
        {
            TapAt(BuildScaffold.LegalSpot);

            TapAt(OnTheRoad);

            Assert.AreSame(scaffold.Green, build.Pending);
            Assert.AreEqual(BuildScaffold.LegalSpot, build.PendingPosition);
            Assert.AreEqual(1, previews.Count, "nothing was published, because nothing changed");
        }

        [Test]
        public void Tick_TappingOffTheBoard_PlacesNothing()
        {
            ConfirmAt(OffTheBoard);

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
        }

        /// <summary>
        /// A rejected placement leaves nothing on the undo stack, which is what "no half-executed
        /// command reaches the stack" means concretely — and no ghost either, so nothing is ever
        /// shown for a tower that cannot be bought.
        /// </summary>
        [Test]
        public void Tick_WithInsufficientFunds_ArmsNothingAndRecordsNothing()
        {
            scaffold.Economy.TrySpend(80);

            ConfirmAt(BuildScaffold.LegalSpot);

            Assert.IsNull(build.Pending);
            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(20, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
            Assert.IsEmpty(previews);
        }

        /// <summary>
        /// The confirm re-validates rather than trusting the arming tap. Currency moves between the
        /// two taps — and during a wave, so can the board.
        /// </summary>
        [Test]
        public void Tick_ConfirmingAfterTheCurrencyIsSpent_PlacesNothing()
        {
            TapAt(BuildScaffold.LegalSpot);
            scaffold.Economy.TrySpend(80);

            TapAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(20, scaffold.Economy.Currency);
            Assert.IsNull(build.Pending, "the refused confirm still clears the ghost");
        }

        /// <summary>
        /// A tower built where the ghost stood, by another route, makes the pending spot illegal —
        /// the second half of what the confirm re-checks.
        /// </summary>
        [Test]
        public void Tick_ConfirmingASpotSomethingElseTook_PlacesNothing()
        {
            TapAt(BuildScaffold.LegalSpot);
            scaffold.Level.AddTower(scaffold.Place(scaffold.Red, BuildScaffold.LegalSpot));

            TapAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(1, scaffold.Level.Towers.Count, "only the one placed directly");
            Assert.AreEqual(100, scaffold.Economy.Currency);
        }

        /// <summary>
        /// A placed tower is permanent: tapping it neither sells it nor places a second one. There
        /// is no branch in the dispatch for this — the spacing rule rejects the tap as an illegal
        /// placement, which is the same silence the road gets.
        /// </summary>
        [Test]
        public void Tick_TappingAPlacedTower_DoesNothing()
        {
            ConfirmAt(BuildScaffold.LegalSpot);
            Tower placed = scaffold.Level.Towers[0];
            Assert.AreEqual(50, scaffold.Economy.Currency, "precondition");

            TapAt(BuildScaffold.LegalSpot);
            TapAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.IsTrue(placed != null, "and it is the same instance, not a replacement");
            Assert.AreEqual(50, scaffold.Economy.Currency, "no refund, no second purchase");
            Assert.AreEqual(1, build.UndoDepth, "nothing was recorded for either tap");
        }

        /// <summary>
        /// And it does not throw away a ghost positioned elsewhere, because a tap on a tower is now
        /// a rejection rather than an action — the same treatment as a tap on the road.
        /// </summary>
        [Test]
        public void Tick_TappingAPlacedTowerWithSomethingPending_LeavesItPending()
        {
            ConfirmAt(BuildScaffold.LegalSpot);
            TapAt(BuildScaffold.OtherLegalSpot);
            Assert.IsNotNull(build.Pending, "precondition");

            TapAt(BuildScaffold.LegalSpot);

            Assert.AreSame(scaffold.Green, build.Pending);
            Assert.AreEqual(BuildScaffold.OtherLegalSpot, build.PendingPosition);
        }

        [Test]
        public void Tick_WithNothingSelected_PlacesNothing()
        {
            TowerCatalogue empty = ScriptableObject.CreateInstance<TowerCatalogue>();
            BuildController controller = NewController(empty);

            input.Tap(BuildScaffold.LegalSpot);
            controller.Tick();

            Assert.IsNull(controller.Selected);
            Assert.IsNull(controller.Pending);
            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Object.DestroyImmediate(empty);
        }

        [Test]
        public void SelectTower_ThroughTheBus_ChangesWhatATapBuilds()
        {
            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.SelectTower, scaffold.Red));

            ConfirmAt(BuildScaffold.LegalSpot);

            Assert.AreSame(scaffold.Red, build.Selected);
            Assert.AreEqual(25, scaffold.Economy.Currency);
            Assert.AreSame(scaffold.Red, scaffold.Level.Towers[0].Definition);
        }

        /// <summary>
        /// Re-tapping a tower button is the dismiss gesture, because there is no Cancel button. It
        /// cancels on every select rather than only on a changed one, so tapping the armed type
        /// means "start over".
        /// </summary>
        [Test]
        public void SelectTower_CancelsThePendingPlacement()
        {
            TapAt(BuildScaffold.LegalSpot);

            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.SelectTower, scaffold.Green));

            Assert.IsNull(build.Pending);
            Assert.AreEqual(2, previews.Count);
            Assert.IsFalse(previews[1].Active);
            Assert.IsNull(previews[1].Tower);
        }

        [Test]
        public void SelectTower_WithANullDefinition_KeepsTheCurrentSelection()
        {
            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.SelectTower, null));

            Assert.AreSame(scaffold.Green, build.Selected);
        }

        [Test]
        public void CancelPending_WithNothingPending_PublishesNothing()
        {
            build.CancelPending();

            Assert.IsEmpty(previews, "an event per phase exit would be an event about nothing");
        }

        [Test]
        public void Undo_ThroughTheBus_RemovesTheTowerAndRefunds()
        {
            ConfirmAt(BuildScaffold.LegalSpot);

            EventBus<BuildActionRequested>.Publish(new BuildActionRequested(BuildAction.Undo));

            Assert.AreEqual(0, scaffold.Level.Towers.Count);
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
        }

        [Test]
        public void Undo_WithAnEmptyStack_ReturnsFalse()
        {
            Assert.IsFalse(build.Undo());
        }

        [Test]
        public void Undo_PopsInLifoOrder()
        {
            ConfirmAt(BuildScaffold.LegalSpot);
            ConfirmAt(BuildScaffold.OtherLegalSpot);
            Assert.AreEqual(2, scaffold.Level.Towers.Count);

            build.Undo();

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(
                BuildScaffold.LegalSpot,
                (Vector2)scaffold.Level.Towers[0].transform.position,
                "the *second* placement is the one that should have gone");
        }

        /// <summary>
        /// The LIFO-solvency invariant used to be asserted here with a sell in the middle of the
        /// stack, and cannot be any more: nothing this class does constructs a
        /// <c>SellTowerCommand</c> now that a placed tower is permanent. What is left of the
        /// invariant at this level is that undo unwinds placements in reverse and returns the
        /// opening balance exactly; the refund arithmetic that made the invariant interesting lives
        /// in <c>SellTowerCommandTests</c>, and ARCHITECTURE.md §6 has the proof.
        /// </summary>
        [Test]
        public void Undo_AfterTwoPlacements_UnwindsBothAndRestoresTheOpeningBalance()
        {
            ConfirmAt(BuildScaffold.LegalSpot);
            Assert.AreEqual(50, scaffold.Economy.Currency);

            ConfirmAt(BuildScaffold.OtherLegalSpot);
            Assert.AreEqual(0, scaffold.Economy.Currency);

            Assert.IsTrue(build.Undo());
            Assert.AreEqual(50, scaffold.Economy.Currency);

            Assert.IsTrue(build.Undo());
            Assert.AreEqual(100, scaffold.Economy.Currency);
            Assert.AreEqual(0, scaffold.Level.Towers.Count);
        }

        [Test]
        public void BuildActionRequested_AfterUnsubscribe_DoesNothing()
        {
            ConfirmAt(BuildScaffold.LegalSpot);
            build.Unsubscribe();

            EventBus<BuildActionRequested>.Publish(new BuildActionRequested(BuildAction.Undo));

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(1, build.UndoDepth);
        }

        /// <summary>
        /// BuildState.Exit() reaches this through CloseUndoScope at the phase boundary: once the
        /// wave starts, nothing can pop the stack, so everything on it is permanent.
        /// </summary>
        [Test]
        public void ClearHistory_EmptiesTheStackSoNothingCanBeUndone()
        {
            ConfirmAt(BuildScaffold.LegalSpot);
            ConfirmAt(BuildScaffold.OtherLegalSpot);
            Assert.AreEqual(2, build.UndoDepth, "precondition");

            build.ClearHistory();

            Assert.AreEqual(0, build.UndoDepth);
            Assert.IsFalse(build.Undo(), "an empty stack refuses rather than throwing");
            Assert.AreEqual(2, scaffold.Level.Towers.Count, "clearing makes permanent, not gone");
        }

        [Test]
        public void ClearHistory_OnAnEmptyStack_DoesNothing()
        {
            Assert.DoesNotThrow(() => build.ClearHistory());
            Assert.AreEqual(0, build.UndoDepth);
        }

        /// <summary>
        /// What makes mid-wave building safe to allow: with the scope closed a purchase still
        /// happens, and there is simply no stack for a wave to have to protect.
        /// </summary>
        [Test]
        public void Tick_WithTheUndoScopeClosed_PlacesButRecordsNothing()
        {
            build.CloseUndoScope();

            ConfirmAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(1, scaffold.Level.Towers.Count);
            Assert.AreEqual(50, scaffold.Economy.Currency);
            Assert.AreEqual(0, build.UndoDepth);
            Assert.IsFalse(build.Undo());
        }

        /// <summary>
        /// The retire path — <c>ClearHistory</c>'s <c>is SellTowerCommand</c> check, shared with
        /// <c>Run</c> — has no producer left now that a tap cannot sell, so what a closed scope
        /// observably does is refuse to record. <c>SellTowerCommandTests</c> covers
        /// <c>Discard</c> itself, which is the half a sale would need if selling returns.
        /// </summary>
        [Test]
        public void CloseUndoScope_ClearsWhatWasAlreadyRecorded()
        {
            ConfirmAt(BuildScaffold.LegalSpot);
            Tower placed = scaffold.Level.Towers[0];
            Assert.AreEqual(1, build.UndoDepth, "precondition");

            build.CloseUndoScope();

            Assert.AreEqual(0, build.UndoDepth);
            Assert.IsTrue(placed != null, "a placement it kept is a tower the player owns");
        }

        [Test]
        public void OpenUndoScope_AfterAClose_RecordsAgain()
        {
            build.CloseUndoScope();
            build.OpenUndoScope();

            ConfirmAt(BuildScaffold.LegalSpot);

            Assert.AreEqual(1, build.UndoDepth);
            Assert.IsTrue(build.Undo());
        }

        /// <summary>
        /// StartWave travels this event because §8 chose one enum over three, but its receiver is
        /// BuildState. This class must ignore it rather than log or throw about a message
        /// correctly addressed elsewhere.
        /// </summary>
        [Test]
        public void BuildActionRequested_WithStartWave_IsIgnoredHere()
        {
            ConfirmAt(BuildScaffold.LegalSpot);

            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.StartWave));

            Assert.AreEqual(1, build.UndoDepth);
            Assert.AreSame(scaffold.Green, build.Selected);
        }
    }
}
