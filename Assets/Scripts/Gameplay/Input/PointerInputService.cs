using System;
using MobileDemo.Core.Interfaces;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

// Namespace flattened to MobileDemo.Gameplay rather than MobileDemo.Gameplay.Input, the second
// instance of the rule Economy.cs already carries -- and the more dangerous of the two, because
// `Input` is a live identifier in Unity code. With this type in a namespace called Input, any file
// inside MobileDemo.Gameplay writing `Input.something` resolves against its enclosing namespace's
// members before consulting usings, finds the namespace, and fails. The folder keeps its name.
namespace MobileDemo.Gameplay
{
    // One implementation, not the two §10 planned -- and that plan was wrong rather than merely
    // superseded. With the project on the Input System alone (activeInputHandler: 1),
    // Touchscreen.current and Mouse.current read through a single code path, so a TouchInputService
    // and an EditorInputService would have been the same six lines with a different device line,
    // and *nothing would ever have chosen between them at runtime*. That is a Strategy with no
    // varying strategy, which is what §6 calls architecture cosplay when it declines IFactory<T>.
    // The Input System had already absorbed the abstraction §10 credited to the interface.
    //
    // IInputService still earns its place, for a smaller and more honest reason: substitution in
    // tests. BuildController's tap dispatch is the new logic this slice adds, and a ten-line
    // FakeInputService drives it deterministically where the alternative is dragging
    // InputTestFixture and a synthetic device into every fixture. That is a test-shaped
    // justification and is recorded as one -- though it is not the seam §14 declined for internal
    // setters, which is a back door into a production type where this is a whole implementation of
    // a published contract. IPoolStats is the precedent for a one-implementation interface with a
    // written reason.
    public sealed class PointerInputService : IInputService
    {
        readonly Camera camera;

        // A constructor argument rather than Camera.main, which is a tag search -- and Bootstrap
        // already holds a serialized reference for everything else it composes.
        public PointerInputService(Camera camera)
        {
            this.camera = camera != null ? camera : throw new ArgumentNullException(nameof(camera));
        }

        public bool TryGetTap(out Vector2 world)
        {
            world = default;

            if (!TryGetPressPosition(out Vector2 screen))
            {
                return false;
            }

            // Without this a tap on a build-menu button also places a tower behind it. The check
            // needs UnityEngine.EventSystems, which is why MobileDemo.Gameplay references
            // UnityEngine.UI -- Unity's input plumbing, not this project's UI assembly, so §3's
            // rule (Gameplay never references MobileDemo.UI) is intact. Solving it with screen
            // geometry instead would mean Gameplay knowing HUD layout, which *would* breach it.
            //
            // Known caveat, accepted: with InputSystemUIInputModule this state can be one frame
            // stale on the press frame. The trigger to do better is a real misfire in play, not
            // this comment.
            if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                return false;
            }

            // Correct because the camera is orthographic and the board is the z = 0 plane -- which
            // §2 pins by having no camera controller and no scrolling, so this is safe by decision
            // rather than by luck. Under a perspective camera the x and y would both be wrong,
            // which is the classic 2D tap bug.
            world = camera.ScreenToWorldPoint(screen);
            return true;
        }

        // Press, not release. One frame, no held state, and both device APIs offer it directly.
        // The misfire it risks -- a tap registering somewhere the player did not quite mean -- is
        // exactly the affordance §6 says undo models, so the responsive choice is also the one the
        // undo stack already covers.
        //
        // It is a query, not a queue: wasPressedThisFrame stays true for the whole frame and
        // nothing here consumes it, so two callers in one frame would both see one tap.
        // BuildController must stay the only caller.
        static bool TryGetPressPosition(out Vector2 screen)
        {
            Touchscreen touch = Touchscreen.current;
            if (touch != null && touch.primaryTouch.press.wasPressedThisFrame)
            {
                screen = touch.primaryTouch.position.ReadValue();
                return true;
            }

            // Not an #if UNITY_EDITOR branch: a mouse is simply a device that may or may not be
            // present, which is the whole reason one class covers both targets.
            Mouse mouse = Mouse.current;
            if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            {
                screen = mouse.position.ReadValue();
                return true;
            }

            screen = default;
            return false;
        }
    }
}
