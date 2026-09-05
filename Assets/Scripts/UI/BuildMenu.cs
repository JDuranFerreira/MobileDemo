using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Towers;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MobileDemo.UI
{
    // Which tower to build, and undo. It publishes intents and reads a catalogue asset; it holds
    // no reference to BuildController, Economy or any live tower, which is §3's seam doing its job
    // in the direction that is easy to get wrong.
    //
    // This is also where the afford check actually belongs, and §8's table used to say otherwise.
    // Greying out a button the player cannot afford is a *presentation* decision about a number
    // the UI already receives; BuildController holds the Economy and asks it directly. Listing
    // BuildController as a CurrencyChanged consumer had it subscribing to mirror a value it can
    // read, which is the Observer misuse §6 warns about.
    public sealed class BuildMenu : MonoBehaviour
    {
        [SerializeField] TowerCatalogue catalogue;

        [Tooltip("One per catalogue entry, in the same order. Fewer buttons than entries leaves "
            + "the surplus entries unbuildable and says so.")]
        [SerializeField] Button[] towerButtons;

        [SerializeField] TMP_Text[] towerLabels;
        [SerializeField] Button undoButton;

        int currency;

        void OnEnable()
        {
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }

            for (int i = 0; i < towerButtons.Length; i++)
            {
                // The index is captured per iteration rather than shared: a closure over the loop
                // variable would give every button the last definition.
                int index = i;
                towerButtons[i].onClick.AddListener(() => SelectTower(index));
            }

            undoButton.onClick.AddListener(RequestUndo);
            EventBus<CurrencyChanged>.Subscribe(OnCurrencyChanged);

            WriteLabels();
            Refresh();
        }

        void OnDisable()
        {
            EventBus<CurrencyChanged>.Unsubscribe(OnCurrencyChanged);

            // Removed rather than left, because these are lambdas over this instance and the
            // buttons outlive a disable.
            for (int i = 0; i < towerButtons.Length; i++)
            {
                if (towerButtons[i] != null)
                {
                    towerButtons[i].onClick.RemoveAllListeners();
                }
            }

            if (undoButton != null)
            {
                undoButton.onClick.RemoveAllListeners();
            }
        }

        void SelectTower(int index)
        {
            TowerDefinition definition = DefinitionAt(index);
            if (definition == null)
            {
                return;
            }

            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.SelectTower, definition));
        }

        static void RequestUndo() =>
            EventBus<BuildActionRequested>.Publish(new BuildActionRequested(BuildAction.Undo));

        void OnCurrencyChanged(CurrencyChanged evt)
        {
            currency = evt.Total;
            Refresh();
        }

        // The label is written once, on enable: a name and a cost are authored data and do not
        // change during a run, so rebuilding the string on every currency change would allocate
        // for nothing.
        void WriteLabels()
        {
            for (int i = 0; i < towerLabels.Length; i++)
            {
                TowerDefinition definition = DefinitionAt(i);
                if (towerLabels[i] != null && definition != null)
                {
                    // Interpolated rather than TMP's SetText format overloads, which take floats
                    // only and so cannot substitute the name. Safe here precisely because this
                    // runs once: the per-change path below touches no strings.
                    towerLabels[i].SetText($"{definition.name}\n${definition.Cost}");
                }
            }
        }

        // interactable, not colour: it is one property, it greys the button through the
        // Button's own disabled tint, and it also stops the click rather than merely discouraging
        // it -- so an unaffordable tap cannot reach the bus at all.
        void Refresh()
        {
            for (int i = 0; i < towerButtons.Length; i++)
            {
                TowerDefinition definition = DefinitionAt(i);
                towerButtons[i].interactable = definition != null && currency >= definition.Cost;
            }
        }

        TowerDefinition DefinitionAt(int index)
        {
            IReadOnlyList<TowerDefinition> buildable = catalogue.Buildable;
            return index >= 0 && index < buildable.Count ? buildable[index] : null;
        }

        // Deliberately does not short-circuit, so one run reports every missing reference --
        // HudPresenter's and Bootstrap's stance.
        bool HasRequiredReferences()
        {
            bool ok = Require(catalogue, nameof(catalogue));
            ok &= Require(undoButton, nameof(undoButton));

            if (towerButtons == null || towerLabels == null
                || towerButtons.Length != towerLabels.Length)
            {
                Debug.LogError(
                    $"BuildMenu on '{name}' needs one label per tower button.", this);
                ok = false;
            }

            return ok;
        }

        bool Require(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"BuildMenu on '{name}' has no {fieldName} assigned.", this);
            return false;
        }
    }
}
