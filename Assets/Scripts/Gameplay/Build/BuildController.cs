using System;
using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Gameplay.Build
{
    // The invoker. It validates, builds the right command, executes it and keeps it for undo --
    // and the split matters: validation lives here, the action lives in the command. That keeps
    // every Execute to a few lines that cannot fail, which is why no half-executed command can
    // ever reach the stack.
    //
    // A plain class, like every other non-visual system here, which is what makes §14's "these
    // pass without a scene" true for it.
    public sealed class BuildController
    {
        readonly IInputService input;
        readonly Economy economy;

        // The runner rather than a Level and a PlacementRules, because both of those are per map
        // and this object outlives a swap. Reading Current and Rules per tap is not indirection
        // for its own sake: it is what stops a level change having to rebuild the invoker, its
        // undo stack and its bus subscription along with the map.
        readonly LevelRunner levels;
        readonly TowerFactory towers;

        // A list used LIFO, not a Stack<T>, only so the depth is readable for a test. There is no
        // cap: the ceiling is the number of build actions in a round, which is a handful, so a
        // MAX_UNDO_DEPTH would be a serialized number with no failure mode to prevent.
        //
        // Strictly LIFO, and never Undo(int). Arbitrary-index undo is where a demo starts
        // inventing a transaction system, and it would also break the solvency argument in
        // ICommand that lets Undo return void.
        readonly List<ICommand> history = new List<ICommand>();

        // The half-made placement: what the player pointed at, and where. Held as state here
        // rather than as a half-built PlaceTowerCommand, because §6 is explicit that no
        // half-executed command reaches the stack -- an armed command waiting for a second tap
        // would be exactly that, and the confirm has to re-validate anyway.
        //
        // Two fields rather than a small PendingPlacement struct: the definition doubles as the
        // "is anything pending" flag, so a struct would need a third bool to say the same thing.
        TowerDefinition pending;
        Vector2 pendingPosition;

        // Whether a command that runs now can still be undone. Opened and closed by the phase, so
        // this class still contains no phase check and no reference to GamePhase -- the rule is
        // owned by whoever ticks it. Opens true because a controller that has never been told is
        // one nothing has taken the undo button away from yet.
        bool recordsUndo = true;

        // The refund fraction was the sixth argument and is gone with the sell tap: nothing here
        // constructs a SellTowerCommand any more, so holding the number would be a field with no
        // reader. GameConfig still carries it (§7), which is where it waits if selling returns.
        public BuildController(
            IInputService input, Economy economy, LevelRunner levels, TowerFactory towers,
            TowerCatalogue catalogue)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.levels = levels ?? throw new ArgumentNullException(nameof(levels));
            this.towers = towers ?? throw new ArgumentNullException(nameof(towers));

            // Opens on the first buildable type so the demo is playable before anything is tapped
            // in the menu. Null when the catalogue is empty, which Tick treats as "nothing to
            // build" rather than as an error.
            IReadOnlyList<TowerDefinition> buildable = catalogue != null
                ? catalogue.Buildable
                : Array.Empty<TowerDefinition>();
            Selected = buildable.Count > 0 ? buildable[0] : null;
        }

        public TowerDefinition Selected { get; private set; }

        public int UndoDepth => history.Count;

        /// <summary>The type waiting on a confirming tap, or null if nothing is pending.</summary>
        public TowerDefinition Pending => pending;

        public Vector2 PendingPosition => pendingPosition;

        // Called by the owning MonoBehaviour's OnEnable/OnDisable -- a plain object has neither,
        // and the bus holds a strong reference until unsubscribed. Economy's pattern, and the
        // method group is load-bearing for the same reason: a lambda would remove nothing.
        public void Subscribe() => EventBus<BuildActionRequested>.Subscribe(OnBuildActionRequested);

        public void Unsubscribe() => EventBus<BuildActionRequested>.Unsubscribe(OnBuildActionRequested);

        // No dt, unlike every other Tick in the project, because nothing here is time-based. The
        // absence is deliberate rather than an oversight: a parameter with no reader is exactly
        // what §2's discipline rejects.
        //
        // It also means building pauses by simply not being called, which is what makes the phase
        // machine cost nothing -- §9's driven-tick decision acquiring a second beneficiary after
        // pausing enemies. BuildState and WaveState both call this; VictoryState and DefeatState do
        // not, which is the whole of the gate.
        //
        // A tap no longer buys anything on its own. The first one on legal ground arms a pending
        // placement and says so on the bus; a second tap on that spot is what spends. Two taps
        // rather than a drag because there is no hover on a touch screen: a ghost has to be put
        // somewhere before it can be looked at.
        public void Tick()
        {
            if (!input.TryGetTap(out Vector2 world))
            {
                return;
            }

            // Read once per tap. A swap replaces both together, and reading them apart would let
            // a rule from the outgoing map judge a tap on the incoming one.
            Level level = levels.Current;
            PlacementRules rules = levels.Rules;
            if (level == null || rules == null)
            {
                return;
            }

            // The confirm is tested first, and the ordering still matters now that a tap has only
            // two possible answers. A pending ghost stands on legal ground, so it is a full spacing
            // clear of every tower -- but a tap between the two is inside both radii, and there the
            // player's own pending intent wins over a reading of the same tap as "that spot is
            // taken".
            if (pending != null && rules.IsTheSameSpot(world, pendingPosition))
            {
                Confirm(level, rules);
                return;
            }

            // A tap on a placed tower used to sell it, and a placed tower is now permanent, so
            // there is no branch here for it. It needs none: IsLegal already rejects a tap within
            // towerSpacing of an existing tower, so the tap falls through to the silent rejection
            // below rather than to a special case. The spacing radius that once decided *which*
            // tower a tap hit is the radius that now makes tapping one mean nothing.
            //
            // What that leaves standing: undo, which is a mistake in this phase rather than a
            // change of mind about a tower already fought behind. SellTowerCommand itself is kept
            // rather than deleted -- see ClearHistory and Retire, and §6.
            //
            // Every rejection below is silent. The HUD is the feedback -- currency simply does not
            // move -- and a rejected-tap flash is PrimeTween's job, which is not installed (§15).
            //
            // A rejected tap also leaves an existing pending placement alone rather than clearing
            // it: a mistap near the road would otherwise throw away the ghost the player had
            // already positioned, which is a worse answer than doing nothing.
            if (Selected == null || !rules.IsLegal(world, level.Towers))
            {
                return;
            }

            // The afford check, asked of Economy directly rather than cached from CurrencyChanged.
            // Subscribing to mirror a number this class can already read would be Observer where a
            // plain reference is correct, and a second source of truth for the balance.
            //
            // Asked here as well as at the confirm, so no ghost ever appears for a tower the player
            // cannot buy.
            if (economy.Currency < Selected.Cost)
            {
                return;
            }

            Arm(Selected, world);
        }

        // Idempotent, and it publishes only when something actually went away -- a Cancel on every
        // phase exit would otherwise put an event on the bus for most of the round's transitions.
        public void CancelPending()
        {
            if (pending == null)
            {
                return;
            }

            pending = null;
            EventBus<PlacementPreviewChanged>.Publish(
                new PlacementPreviewChanged(false, Vector2.zero, null));
        }

        // Called from BuildState.Enter/Exit. The pair exists rather than a settable property so the
        // close can do both halves of its job in one call: stop recording, and hand back the towers
        // the stack still owns.
        public void OpenUndoScope() => recordsUndo = true;

        public void CloseUndoScope()
        {
            recordsUndo = false;
            ClearHistory();
        }

        public bool Undo()
        {
            if (history.Count == 0)
            {
                return false;
            }

            int last = history.Count - 1;
            ICommand command = history[last];
            history.RemoveAt(last);
            command.Undo();
            return true;
        }

        // Reached through CloseUndoScope() from BuildState.Exit(): once the wave starts, nothing can
        // pop this stack, so every command on it is permanent and the phase boundary is where that
        // becomes true. Still public, and still the method §6 points at.
        //
        // Retire is the whole reason this is not just history.Clear(). A sold tower is deactivated
        // rather than destroyed, because Undo has to restore the instance rather than manufacture a
        // replacement -- so its GameObject is alive and owned by this list, and clearing the list
        // without discarding would leak one inactive tower per sale for the rest of the round.
        public void ClearHistory()
        {
            for (int i = 0; i < history.Count; i++)
            {
                Retire(history[i]);
            }

            history.Clear();
        }

        void Arm(TowerDefinition definition, Vector2 world)
        {
            pending = definition;
            pendingPosition = world;
            EventBus<PlacementPreviewChanged>.Publish(
                new PlacementPreviewChanged(true, world, definition));
        }

        // The pending state is dropped *before* the command runs, so there is no path on which a
        // placement succeeds and a ghost survives it.
        //
        // Both checks are asked again rather than trusted from the arming tap. Currency moves
        // between the two taps -- another placement, a sale's refund, an undo -- and during a wave
        // the board does too, since the confirm can arrive after something else has been built.
        void Confirm(Level level, PlacementRules rules)
        {
            TowerDefinition definition = pending;
            Vector2 position = pendingPosition;
            CancelPending();

            if (!rules.IsLegal(position, level.Towers) || economy.Currency < definition.Cost)
            {
                return;
            }

            Run(new PlaceTowerCommand(towers, level, economy, definition, position));
        }

        // Recorded while the undo scope is open, retired immediately when it is not. A command run
        // with the scope closed is permanent the instant it executes, which is what makes mid-wave
        // building safe to allow: there is no stack for a wave to have to protect, so nothing has
        // to check what phase it is.
        //
        // Retiring rather than simply dropping matters for the same reason ClearHistory is not
        // history.Clear() -- a sale deactivates its tower and hands ownership to the stack. With no
        // stack to hand it to, the sale destroys it now.
        void Run(ICommand command)
        {
            command.Execute();

            if (recordsUndo)
            {
                history.Add(command);
                return;
            }

            Retire(command);
        }

        // One type test in one place, now with two callers. §6 is explicit that ICommand is exactly
        // Execute/Undo, and PlaceTowerCommand has nothing to retire: its own Undo already destroys
        // what it made, and a command that was never undone made a tower the player still owns.
        static void Retire(ICommand command)
        {
            if (command is SellTowerCommand sell)
            {
                sell.Discard();
            }
        }

        // BuildAction.StartWave is deliberately absent, and there is no default. It travels this
        // event because §8 chose one enum over three events, but its receiver is BuildState -- the
        // phase, not the builder. Two subscribers owning disjoint values of one enum is the shape
        // that choice implies; a default that logged or threw here would make this class complain
        // about a message correctly addressed elsewhere.
        void OnBuildActionRequested(BuildActionRequested evt)
        {
            switch (evt.Action)
            {
                case BuildAction.SelectTower:
                    // A null selection is ignored rather than clearing the current one: the menu
                    // has no "select nothing" affordance, so a null here is a wiring mistake.
                    if (evt.Tower != null)
                    {
                        Selected = evt.Tower;

                        // Re-tapping a tower button is also the dismiss gesture, because there is
                        // no Cancel button and a ghost the player has stopped wanting has to be
                        // clearable somehow. Cancelling on every select rather than only on a
                        // *changed* select is what makes tapping the armed type mean "start over".
                        CancelPending();
                    }

                    break;

                case BuildAction.Undo:
                    Undo();
                    break;
            }
        }
    }
}
