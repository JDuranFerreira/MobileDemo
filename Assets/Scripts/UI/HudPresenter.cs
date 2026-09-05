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

        void OnEnable()
        {
            // Both required, and both reported in one run rather than short-circuiting -- the
            // same reasoning Bootstrap's reference checks use.
            bool ok = Require(livesLabel, nameof(livesLabel));
            ok &= Require(currencyLabel, nameof(currencyLabel));
            if (!ok)
            {
                enabled = false;
                return;
            }

            EventBus<LivesChanged>.Subscribe(OnLivesChanged);
            EventBus<CurrencyChanged>.Subscribe(OnCurrencyChanged);
        }

        void OnDisable()
        {
            EventBus<LivesChanged>.Unsubscribe(OnLivesChanged);
            EventBus<CurrencyChanged>.Unsubscribe(OnCurrencyChanged);
        }

        // SetText formats into TMP's own buffer; `text = ...ToString()` would allocate per change.
        // The `:0` pins zero decimals, since SetText takes the argument as a float.
        void OnLivesChanged(LivesChanged evt) => livesLabel.SetText("Lives {0:0}", evt.Total);

        void OnCurrencyChanged(CurrencyChanged evt) => currencyLabel.SetText("${0:0}", evt.Total);

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
