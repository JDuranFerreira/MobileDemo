using MobileDemo.Core.Interfaces;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // IInputService's second implementation, and -- per ARCHITECTURE.md §10 -- its honest
    // justification. PointerInputService is the only runtime implementation, because the Input
    // System already reads a touch and a mouse through one code path; what the interface actually
    // buys is this class, which drives BuildController's tap dispatch deterministically. The
    // alternative was dragging InputTestFixture and a synthetic device into every fixture.
    //
    // A tap is consumed on read, unlike the real service, where wasPressedThisFrame stays true for
    // the whole frame. That difference is deliberate: a test wants one Tick to see exactly one tap,
    // and the real service's "query, not queue" behaviour is safe only because BuildController is
    // its single caller.
    sealed class FakeInputService : IInputService
    {
        Vector2? pending;

        public void Tap(Vector2 world) => pending = world;

        public bool TryGetTap(out Vector2 world)
        {
            if (pending == null)
            {
                world = default;
                return false;
            }

            world = pending.Value;
            pending = null;
            return true;
        }
    }
}
