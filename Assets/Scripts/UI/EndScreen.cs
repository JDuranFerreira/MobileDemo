using MobileDemo.Core.Events;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace MobileDemo.UI
{
    // The panel §5 and §11 have named since the first slice, arriving now because there is finally
    // something for it to say: PhaseChanged has a publisher, and two of its four values are ends.
    //
    // Like HudPresenter, it names no Gameplay type. It learns the round is over from the bus and
    // asks for a restart on the bus, which is §3's rule holding in both directions at once -- the
    // first UI file to do that, since BuildMenu only ever published.
    public sealed class EndScreen : MonoBehaviour
    {
        [Tooltip("The panel itself, not this component's GameObject — this component has to keep "
            + "receiving PhaseChanged while the panel is hidden.")]
        [SerializeField] GameObject panel;

        [SerializeField] TMP_Text headlineLabel;
        [SerializeField] Button restartButton;

        void OnEnable()
        {
            if (!HasRequiredReferences())
            {
                enabled = false;
                return;
            }

            restartButton.onClick.AddListener(RequestRestart);
            EventBus<PhaseChanged>.Subscribe(OnPhaseChanged);

            // Closed until a phase says otherwise. Authoring it closed in the scene would work
            // equally well and would also be one more thing to get wrong in the Inspector.
            panel.SetActive(false);
        }

        void OnDisable()
        {
            EventBus<PhaseChanged>.Unsubscribe(OnPhaseChanged);

            if (restartButton != null)
            {
                restartButton.onClick.RemoveAllListeners();
            }
        }

        // The panel is toggled, not this component: a component that disabled itself would stop
        // receiving the event that has to bring it back. The distinction is why `panel` is a
        // separate serialized reference rather than `gameObject`.
        void OnPhaseChanged(PhaseChanged evt)
        {
            bool over = evt.Phase == GamePhase.Victory || evt.Phase == GamePhase.Defeat;
            panel.SetActive(over);

            if (over)
            {
                headlineLabel.SetText(evt.Phase == GamePhase.Victory ? "Victory" : "Defeat");
            }
        }

        // Bootstrap reloads the scene on this. The UI does not know that and should not: what it
        // knows is that the player asked to play again.
        static void RequestRestart() =>
            EventBus<RestartRequested>.Publish(new RestartRequested());

        // Deliberately does not short-circuit, so one run reports every missing reference --
        // HudPresenter's, BuildMenu's and Bootstrap's stance.
        bool HasRequiredReferences()
        {
            bool ok = Require(panel, nameof(panel));
            ok &= Require(headlineLabel, nameof(headlineLabel));
            ok &= Require(restartButton, nameof(restartButton));
            return ok;
        }

        bool Require(Object reference, string fieldName)
        {
            if (reference != null)
            {
                return true;
            }

            Debug.LogError($"EndScreen on '{name}' has no {fieldName} assigned.", this);
            return false;
        }
    }
}
