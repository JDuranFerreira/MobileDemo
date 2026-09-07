using MobileDemo.Core.Events;
using TMPro;
using UnityEngine;

namespace MobileDemo.UI
{
    // The using block above is the whole point: no MobileDemo.Gameplay, no Economy, no Enemy.
    public sealed class HudPresenter : MonoBehaviour
    {
        [SerializeField] TMP_Text livesLabel;
        [SerializeField] TMP_Text currencyLabel;
        [SerializeField] TMP_Text phaseLabel;

        [Tooltip("Counts waves cleared, not the wave in progress — see OnWaveCompleted.")]
        [SerializeField] TMP_Text waveLabel;

        void OnEnable()
        {
            // All four required, and all reported in one run rather than short-circuiting -- the
            // same reasoning Bootstrap's reference checks use.
            bool ok = Require(livesLabel, nameof(livesLabel));
            ok &= Require(currencyLabel, nameof(currencyLabel));
            ok &= Require(phaseLabel, nameof(phaseLabel));
            ok &= Require(waveLabel, nameof(waveLabel));
            if (!ok)
            {
                enabled = false;
                return;
            }

            EventBus<LivesChanged>.Subscribe(OnLivesChanged);
            EventBus<CurrencyChanged>.Subscribe(OnCurrencyChanged);
            EventBus<PhaseChanged>.Subscribe(OnPhaseChanged);
            EventBus<WaveCompleted>.Subscribe(OnWaveCompleted);

            // Seeded, unlike the other three: lives, currency and phase are all announced from
            // Bootstrap.Start, and no wave has completed yet when the round opens.
            waveLabel.SetText("Wave {0:0}", 0);
        }

        void OnDisable()
        {
            EventBus<LivesChanged>.Unsubscribe(OnLivesChanged);
            EventBus<CurrencyChanged>.Unsubscribe(OnCurrencyChanged);
            EventBus<PhaseChanged>.Unsubscribe(OnPhaseChanged);
            EventBus<WaveCompleted>.Unsubscribe(OnWaveCompleted);
        }

        // SetText formats into TMP's own buffer; `text = ...ToString()` would allocate per change.
        // The `:0` pins zero decimals, since SetText takes the argument as a float.
        void OnLivesChanged(LivesChanged evt) => livesLabel.SetText("Lives {0:0}", evt.Total);

        void OnCurrencyChanged(CurrencyChanged evt) => currencyLabel.SetText("${0:0}", evt.Total);

        // A switch rather than evt.Phase.ToString(): the enum's names are code identifiers, and
        // ToString() on an enum allocates a string on every transition. Four literals cost
        // nothing and leave the wording free to differ from the type.
        void OnPhaseChanged(PhaseChanged evt) =>
            phaseLabel.SetText(
                evt.Phase switch
                {
                    GamePhase.Build => "Build",
                    GamePhase.Wave => "Wave",
                    GamePhase.Victory => "Victory",
                    _ => "Defeat"
                });

        // WaveCompleted is past tense, so this reads as waves *cleared* -- and the payload already
        // says how many, since the indices are zero-based and sequential. Counting them here
        // instead would be a second source of truth for a number the event carries.
        //
        // §8's table used to give this event's consumer as GameStateMachine; the machine turned
        // out to hold its runner by construction and poll it, so the row moved here rather than
        // the event being deleted -- and a HUD is what one-to-many broadcast was for anyway.
        void OnWaveCompleted(WaveCompleted evt) =>
            waveLabel.SetText("Wave {0:0}", evt.WaveIndex + 1);

        bool Require(TMP_Text label, string fieldName)
        {
            if (label != null)
            {
                return true;
            }

            Debug.LogError($"HudPresenter on '{name}' has no {fieldName} assigned.", this);
            return false;
        }
    }
}
