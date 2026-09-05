using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Towers;

namespace MobileDemo.Gameplay.Build
{
    public enum BuildAction
    {
        SelectTower,
        Undo
    }

    // The build UI's one route into Gameplay. §3 leaves exactly one: UI may not be referenced by
    // Gameplay, and a Button.onClick persistent call into a Gameplay component is the loophole
    // rather than the answer -- §3's whole claim is that the boundary is "enforced by the
    // compiler, not by discipline", and an Inspector-wired call target is a name resolved at
    // runtime that no compiler and no test can check.
    //
    // One event with an enum rather than a TowerSelected plus an UndoRequested, because §4 already
    // names a third UI intent -- `Build -> Wave : StartWave (player taps "Go")` -- which takes the
    // same route. The enum is the shape that absorbs it, exactly as PhaseChanged carries a
    // GamePhase instead of splitting into four events. The cost is a field that means nothing for
    // one of the values, said out loud rather than hidden: the trigger to split this into separate
    // events is the third action needing a payload of its own.
    //
    // This is the one event in §8's catalogue that is *not* declared in Core's GameEvents.cs, and
    // it cannot be: TowerDefinition is a MobileDemo.Gameplay type and Core references nothing
    // project-specific, so a field of that type there does not compile. A payload's type decides
    // which assembly its event can live in -- and EventBus<T> being a Core generic closed over a
    // Gameplay type is the mechanism working as designed, not a workaround.
    //
    // And it is an imperative, which §6 says an event is not. What the bus carries is not the
    // command -- BuildController still constructs and owns those -- but the player's *request*,
    // which is a past-tense fact about the player.
    public readonly struct BuildActionRequested : IEvent
    {
        public readonly BuildAction Action;

        /// <summary>Meaningful only for <see cref="BuildAction.SelectTower"/>.</summary>
        public readonly TowerDefinition Tower;

        public BuildActionRequested(BuildAction action, TowerDefinition tower = null)
        {
            Action = action;
            Tower = tower;
        }
    }
}
