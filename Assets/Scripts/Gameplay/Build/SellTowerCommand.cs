using System;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Gameplay.Build
{
    public sealed class SellTowerCommand : ICommand
    {
        readonly Level level;
        readonly Economy economy;
        readonly Tower tower;
        readonly int refund;

        public SellTowerCommand(Level level, Economy economy, Tower tower, float refundFraction)
        {
            this.level = level != null ? level : throw new ArgumentNullException(nameof(level));
            this.economy = economy ?? throw new ArgumentNullException(nameof(economy));
            this.tower = tower != null ? tower : throw new ArgumentNullException(nameof(tower));

            int cost = tower.Definition != null ? tower.Definition.Cost : 0;

            // Floored, not rounded. floor(75 * 0.5) = 37, where rounding would give 38 and make an
            // odd-cost sell-and-rebuy loop print a coin each time. Cached so Undo charges back
            // exactly what was paid out, even if the fraction is retuned mid-session.
            refund = Mathf.FloorToInt(cost * refundFraction);
        }

        public int Refund => refund;

        public void Execute()
        {
            // This line, not the SetActive below, is what stops the tower firing -- Bootstrap
            // drives towers from the level's list. An unexpected payoff of §9's driven tick.
            level.RemoveTower(tower);
            tower.gameObject.SetActive(false);
            economy.Refund(refund);
        }

        public void Undo()
        {
            // Charging the refund back cannot fail here, and that is a property of the stack
            // rather than of this method: BuildController pops strictly LIFO, so any spend made
            // after this sell is a command above it, already undone and already refunded. See
            // ICommand for the full argument -- it is the reason Undo returns void.
            if (!economy.TrySpend(refund))
            {
                Debug.LogError(
                    $"SellTowerCommand could not reclaim {refund} on undo, which the LIFO undo "
                    + "order should make impossible. A spender outside BuildController has "
                    + "consumed it.");
            }

            tower.gameObject.SetActive(true);
            level.AddTower(tower);
        }
    }
}
