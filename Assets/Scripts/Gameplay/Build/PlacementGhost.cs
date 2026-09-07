using MobileDemo.Core.Events;
using UnityEngine;

namespace MobileDemo.Gameplay.Build
{
    // The transparent tower between the two taps of a placement. One SpriteRenderer that moves and
    // changes sprite; it decides nothing.
    //
    // A MonoBehaviour, unlike the rest of this folder, because a sprite in the world is the one
    // thing a plain class cannot be. What keeps that from spreading is that it holds no reference
    // to BuildController and asks it nothing: it reads PlacementPreviewChanged and that is all it
    // knows, so the dispatch stays testable without a scene.
    //
    // Not the UI assembly either, even though it is presentation: it is world-space and positioned
    // in world units, and §3 puts UI on the other side of a seam that may not reference Gameplay.
    [RequireComponent(typeof(SpriteRenderer))]
    public sealed class PlacementGhost : MonoBehaviour
    {
        [Tooltip("Multiplied over the tower's own sprite. The alpha is the whole point.")]
        [SerializeField] Color tint = new Color(1f, 1f, 1f, 0.45f);

        SpriteRenderer spriteRenderer;

        void Awake()
        {
            spriteRenderer = GetComponent<SpriteRenderer>();
            spriteRenderer.color = tint;

            // Hidden until asked for, rather than shown and then corrected: the first
            // PlacementPreviewChanged does not arrive until the player taps the board, which can be
            // most of a build phase later.
            spriteRenderer.enabled = false;
        }

        void OnEnable() => EventBus<PlacementPreviewChanged>.Subscribe(OnPreviewChanged);

        void OnDisable() => EventBus<PlacementPreviewChanged>.Unsubscribe(OnPreviewChanged);

        // enabled on the renderer, not SetActive on the GameObject: this component's subscription
        // lives on the same object, and deactivating it would take the subscription off the bus
        // along with the sprite -- after which nothing could ever turn it back on.
        void OnPreviewChanged(PlacementPreviewChanged evt)
        {
            if (!evt.Active || evt.Tower == null)
            {
                spriteRenderer.enabled = false;
                return;
            }

            // One tint, and no red variant, because an illegal spot never gets this far:
            // BuildController arms a pending placement only where a tower could legally be bought,
            // so a rejected-placement colour would be unreachable state. §15's PrimeTween still
            // owns telling the player about the taps that were refused.
            spriteRenderer.sprite = evt.Tower.Sprite;
            transform.position = evt.Position;
            spriteRenderer.enabled = true;
        }
    }
}
