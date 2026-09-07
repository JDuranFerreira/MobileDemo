using System;
using MobileDemo.Core.Interfaces;
using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Gameplay.Build
{
    public sealed class SellTowerCommand : ICommand
    {
        readonly TowerFactory towers;
        readonly Level level;
        readonly Economy economy;
        readonly Tower tower;
        readonly int refund;

        bool sold;

        public SellTowerCommand(
            TowerFactory towers, Level level, Economy economy, Tower tower, float refundFraction)
        {
            this.towers = towers ?? throw new ArgumentNullException(nameof(towers));
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
            sold = true;
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
            sold = false;
        }

        // Not part of ICommand, and that is the point. §6 stakes real weight on the interface
        // being exactly Execute/Undo -- "an imperative with exactly one execution" -- and a third
        // member would have to be answered by PlaceTowerCommand, for which it means nothing.
        //
        // What it *is*: the other end of the deactivate-don't-destroy rule. A sold tower survives
        // its own sale so that Undo can restore the instance rather than manufacture a
        // replacement, which leaves the GameObject alive and owned by the undo stack. When the
        // stack is cleared the sale becomes unreachable, and this is the moment the object has no
        // owner left. §6 named BuildState.Exit() as the trigger before either existed.
        public void Discard()
        {
            if (!sold)
            {
                return;
            }

            // Through the factory, for PlaceTowerCommand.Undo's reason: it is the one place that
            // knows Destroy behaves differently outside play mode (§14).
            towers.Destroy(tower);
            sold = false;
        }
    }
}
