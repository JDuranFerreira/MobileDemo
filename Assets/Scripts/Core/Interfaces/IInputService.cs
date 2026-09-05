using UnityEngine;

namespace MobileDemo.Core.Interfaces
{
    // World coordinates, not screen: the conversion needs a Camera, and putting it behind this
    // interface is what keeps every consumer camera-free -- including the test fake, which could
    // not otherwise produce a world point at all.
    //
    // Core does not reference Unity.InputSystem, deliberately. The contract is device-agnostic,
    // which is what confines that package to MobileDemo.Gameplay (§11's per-assembly policy).
    public interface IInputService
    {
        bool TryGetTap(out Vector2 world);
    }
}
