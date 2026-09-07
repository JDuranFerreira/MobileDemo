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
        readonly float sellRefundFraction;

        // A list used LIFO, not a Stack<T>, only so the depth is readable for a test. There is no
        // cap: the ceiling is the number of build actions in a round, which is a handful, so a
        // MAX_UNDO_DEPTH would be a serialized number with no failure mode to prevent.
        //
        // Strictly LIFO, and never Undo(int). Arbitrary-index undo is where a demo starts
        // inventing a transaction system, and it would also break the solvency argument in
        // ICommand that lets Undo return void.
        readonly List<ICommand> history = new List<ICommand>();

        public BuildController(
            IInputService input, Economy economy, LevelRunner levels, TowerFactory towers,
            TowerCatalogue catalogue, float sellRefundFraction)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.levels = levels ?? throw new ArgumentNullException(nameof(levels));
            this.towers = towers ?? throw new ArgumentNullException(nameof(towers));
            this.sellRefundFraction = sellRefundFraction;

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

        // Called by the owning MonoBehaviour's OnEnable/OnDisable -- a plain object has neither,
        // and the bus holds a strong reference until unsubscribed. Economy's pattern, and the
        // method group is load-bearing for the same reason: a lambda would remove nothing.
        public void Subscribe() => EventBus<BuildActionRequested>.Subscribe(OnBuildActionRequested);

        public void Unsubscribe() => EventBus<BuildActionRequested>.Unsubscribe(OnBuildActionRequested);

        // No dt, unlike every other Tick in the project, because nothing here is time-based. The
        // absence is deliberate rather than an oversight: a parameter with no reader is exactly
        // what §2's discipline rejects.
        //
        // It also means building pauses by simply not being called, which is what makes the
        // still-absent phase machine cost nothing -- §9's driven-tick decision acquiring a second
        // beneficiary after pausing enemies.
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

            // Sell is checked before legality, so a tap on an existing tower can never be
            // misread as an illegal placement. PlacementRules shares one radius between the two
            // questions, so exactly one branch can be true.
            Tower existing = rules.FindTowerAt(world, level.Towers);
            if (existing != null)
            {
                Run(new SellTowerCommand(towers, level, economy, existing, sellRefundFraction));
                return;
            }

            // Every rejection below is silent. The HUD is the feedback -- currency simply does not
            // move -- and a rejected-tap flash is PrimeTween's job, which is not installed (§15).
            if (Selected == null || !rules.IsLegal(world, level.Towers))
            {
                return;
            }

            // The afford check, asked of Economy directly rather than cached from CurrencyChanged.
            // Subscribing to mirror a number this class can already read would be Observer where a
            // plain reference is correct, and a second source of truth for the balance.
            if (economy.Currency < Selected.Cost)
            {
                return;
            }

            Run(new PlaceTowerCommand(towers, level, economy, Selected, world));
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

        // Called from BuildState.Exit(): once the wave starts, nothing can pop this stack, so
        // every command on it is permanent and the phase boundary is where that becomes true.
        //
        // The `is` check is the whole reason this is not just history.Clear(). A sold tower is
        // deactivated rather than destroyed, because Undo has to restore the instance rather than
        // manufacture a replacement -- so its GameObject is alive and owned by this list, and
        // clearing the list without discarding would leak one inactive tower per sale for the rest
        // of the round.
        //
        // One type test in one place, chosen over an IDiscardable interface with a single
        // implementer and over a third member on ICommand. §6 is explicit that the interface is
        // exactly Execute/Undo, and PlaceTowerCommand has nothing to discard: its own Undo already
        // destroys what it made, and a command left on the stack has *not* been undone, so its
        // tower is one the player still owns.
        public void ClearHistory()
        {
            for (int i = 0; i < history.Count; i++)
            {
                if (history[i] is SellTowerCommand sell)
                {
                    sell.Discard();
                }
            }

            history.Clear();
        }

        void Run(ICommand command)
        {
            command.Execute();
            history.Add(command);
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
                    }

                    break;

                case BuildAction.Undo:
                    Undo();
                    break;
            }
        }
    }
}
