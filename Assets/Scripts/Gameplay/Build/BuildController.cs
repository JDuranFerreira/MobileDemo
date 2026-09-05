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
        readonly Level level;
        readonly PlacementRules rules;
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
            IInputService input, Economy economy, Level level, PlacementRules rules,
            TowerFactory towers, TowerCatalogue catalogue, float sellRefundFraction)
        {
            this.input = input ?? throw new ArgumentNullException(nameof(input));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.level = level != null ? level : throw new ArgumentNullException(nameof(level));
            this.rules = rules ?? throw new ArgumentNullException(nameof(rules));
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

            // Sell is checked before legality, so a tap on an existing tower can never be
            // misread as an illegal placement. PlacementRules shares one radius between the two
            // questions, so exactly one branch can be true.
            Tower existing = rules.FindTowerAt(world, level.Towers);
            if (existing != null)
            {
                Run(new SellTowerCommand(level, economy, existing, sellRefundFraction));
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

        void Run(ICommand command)
        {
            command.Execute();
            history.Add(command);
        }

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
