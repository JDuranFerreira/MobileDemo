using System;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Gameplay.Build
{
    // The project's first spender. §6's rule for its Undo is the one thing this file exists to
    // honour: refunding is not enough, the refund has to *re-announce*, or HudPresenter keeps
    // showing the pre-undo balance while Economy holds the real one. Economy.Refund does both.
    public sealed class PlaceTowerCommand : ICommand
    {
        readonly TowerFactory towers;
        readonly Level level;
        readonly Economy economy;
        readonly TowerDefinition definition;
        readonly Vector2 position;
        readonly int cost;

        Tower placed;

        public PlaceTowerCommand(
            TowerFactory towers, Level level, Economy economy, TowerDefinition definition,
            Vector2 position)
        {
            this.towers = towers ?? throw new ArgumentNullException(nameof(towers));
            this.level = level != null ? level : throw new ArgumentNullException(nameof(level));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.definition = definition != null
                ? definition
                : throw new ArgumentNullException(nameof(definition));
            this.position = position;

            // Captured at construction, so Undo refunds what was actually paid even if the asset
            // is retuned mid-session.
            cost = definition.Cost;
        }

        public Tower Placed => placed;

        public void Execute()
        {
            // Spend first, then instantiate: a failed spend costs no allocation. BuildController
            // has already checked affordability, so this branch is a broken invariant rather than
            // a normal outcome -- and it says so out loud instead of quietly placing a free tower,
            // which is ProjectileFactory.Create's stance on an unknown prefab.
            if (!economy.TrySpend(cost))
            {
                Debug.LogError(
                    $"PlaceTowerCommand spent nothing: '{definition.name}' costs {cost} and only "
                    + $"{economy.Currency} is available. The invoker should have checked first.");
                return;
            }

            placed = towers.Create(definition, position, level.transform);
            level.AddTower(placed);
        }

        public void Undo()
        {
            // A command whose Execute bailed has nothing to undo. Guarding here rather than
            // forbidding it is what makes an insufficient-funds Execute a safe no-op pair.
            if (placed == null)
            {
                return;
            }

            level.RemoveTower(placed);

            // Through the factory rather than Object.Destroy here: it created the instance, so it
            // owns the teardown -- and it is the one place that has to know `Destroy` behaves
            // differently outside play mode.
            towers.Destroy(placed);
            placed = null;

            // Full cost, not the sell fraction: undoing a placement is not a sale. That asymmetry
            // is also what stops a place/undo loop from being a way to launder currency.
            economy.Refund(cost);
        }
    }
}
