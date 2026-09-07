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

        [Tooltip("Ends the build phase. Interactable only during it.")]
        [SerializeField] Button goButton;

        [Tooltip("Applied to the armed tower button's own image.")]
        [SerializeField] Color selectedTint = new Color(1f, 0.82f, 0.35f);

        [SerializeField] Color normalTint = Color.white;

        int currency;

        // Opens closed and is opened by the PhaseChanged that Bootstrap.Start publishes, rather
        // than opening true and being corrected. A menu that is live for the frame before the
        // round has a phase is a menu that can start a wave before the machine exists.
        //
        // Two flags rather than one, because the two questions stopped having the same answer:
        // towers can be bought during a wave, undo and Go still cannot.
        bool canBuild;
        bool inBuildPhase;

        // Which button is armed. Opens at 0 to match BuildController, whose constructor seeds
        // Selected from the catalogue's first entry -- the same fact stated on both sides of §3's
        // seam, because neither may reference the other and nothing publishes the selection.
        //
        // The trigger for making it an event instead: a second thing that can change the selection.
        int selectedIndex;

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
            goButton.onClick.AddListener(RequestStartWave);
            EventBus<CurrencyChanged>.Subscribe(OnCurrencyChanged);
            EventBus<PhaseChanged>.Subscribe(OnPhaseChanged);

            canBuild = false;
            inBuildPhase = false;
            selectedIndex = 0;
            WriteLabels();
            Refresh();
        }

        void OnDisable()
        {
            EventBus<CurrencyChanged>.Unsubscribe(OnCurrencyChanged);
            EventBus<PhaseChanged>.Unsubscribe(OnPhaseChanged);

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

            if (goButton != null)
            {
                goButton.onClick.RemoveAllListeners();
            }
        }

        void SelectTower(int index)
        {
            TowerDefinition definition = DefinitionAt(index);
            if (definition == null)
            {
                return;
            }

            selectedIndex = index;
            Refresh();

            EventBus<BuildActionRequested>.Publish(
                new BuildActionRequested(BuildAction.SelectTower, definition));
        }

        static void RequestUndo() =>
            EventBus<BuildActionRequested>.Publish(new BuildActionRequested(BuildAction.Undo));

        // Same route as the other two, because §3 leaves exactly one -- and the same event, because
        // §8 chose one enum over three. Its receiver is BuildState rather than BuildController,
        // which the UI has no way of knowing and no reason to.
        static void RequestStartWave() =>
            EventBus<BuildActionRequested>.Publish(new BuildActionRequested(BuildAction.StartWave));

        void OnCurrencyChanged(CurrencyChanged evt)
        {
            currency = evt.Total;
            Refresh();
        }

        // Greyed rather than hidden. The menu keeps its footprint across a phase change, so the
        // board does not reflow under the player's thumb mid-wave -- and the buttons stay legible
        // as things that will come back.
        //
        // This is presentation only, and deliberately not the enforcement. BuildState stops
        // listening for StartWave when it exits, and it closes BuildController's undo scope on the
        // way out, so Go and Undo are already dead during a wave whatever this class does. Two
        // mechanisms for the same rule would be one too many if this were the load-bearing one; it
        // is not.
        //
        // The tower buttons are the one place where phase and buildability are now the same
        // question in both directions: Victory and Defeat tick nothing that builds, and neither
        // arms a button here.
        void OnPhaseChanged(PhaseChanged evt)
        {
            inBuildPhase = evt.Phase == GamePhase.Build;
            canBuild = inBuildPhase || evt.Phase == GamePhase.Wave;
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

        // interactable, not colour, for the afford check: it is one property, it greys the button
        // through the Button's own disabled tint, and it also stops the click rather than merely
        // discouraging it -- so an unaffordable tap cannot reach the bus at all.
        //
        // The armed tower is colour, and it has to be something else: interactable is already
        // spoken for, and a player has to be able to see which type a board tap will buy while both
        // buttons are still buyable. It is written to the button's own image rather than to its
        // ColorBlock, so the Button's ColorTint transition keeps multiplying its pressed and
        // disabled states over the top -- "armed" and "cannot afford" are both legible at once.
        void Refresh()
        {
            for (int i = 0; i < towerButtons.Length; i++)
            {
                TowerDefinition definition = DefinitionAt(i);
                towerButtons[i].interactable =
                    canBuild && definition != null && currency >= definition.Cost;

                if (towerButtons[i].image != null)
                {
                    towerButtons[i].image.color = i == selectedIndex ? selectedTint : normalTint;
                }
            }

            // The narrower gate, and the two reasons differ. Undo is dead outside the build phase
            // because the stack is: BuildState closes the undo scope on the way out, so a mid-wave
            // command is permanent the moment it runs. Go is dead because BuildState is the only
            // subscriber for StartWave and it stops listening when it exits.
            undoButton.interactable = inBuildPhase;
            goButton.interactable = inBuildPhase;
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
            ok &= Require(goButton, nameof(goButton));

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
