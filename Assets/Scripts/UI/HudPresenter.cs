using MobileDemo.Core.Events;
using TMPro;
using UnityEngine;

namespace MobileDemo.UI
{
    // The using block above is the whole point: no MobileDemo.Gameplay, no Economy, no Enemy.
    public sealed class HudPresenter : MonoBehaviour
    {
        [SerializeField] TMP_Text livesLabel;

        void OnEnable()
        {
            if (livesLabel == null)
            {
                Debug.LogError($"HudPresenter on '{name}' has no livesLabel assigned.", this);
                enabled = false;
                return;
            }

            EventBus<LivesChanged>.Subscribe(OnLivesChanged);
        }

        void OnDisable() => EventBus<LivesChanged>.Unsubscribe(OnLivesChanged);

        // SetText formats into TMP's own buffer; `text = ...ToString()` would allocate per change.
        // The `:0` pins zero decimals, since SetText takes the argument as a float.
        void OnLivesChanged(LivesChanged evt) => livesLabel.SetText("Lives {0:0}", evt.Total);
    }
}
