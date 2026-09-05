using System.Collections.Generic;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Gameplay.Levels
{
    // Sits on a level prefab's root. The path is a serialized reference resolved inside the
    // prefab, so a swapper can instantiate level N and read its path without searching for it.
    public sealed class Level : MonoBehaviour
    {
        [SerializeField] EnemyPath path;

        [Tooltip("Authored inside the prefab. Runtime placements are appended to the live list "
            + "rather than to this array, which stays the level's opening state.")]
        [SerializeField] Tower[] towers;

        [Tooltip("The map art. Read at runtime for its bounds, which is what keeps a placement "
            + "on the board.")]
        [SerializeField] SpriteRenderer map;

        // Seeded from the serialized array on first access, never in Awake. See EnsureSeeded.
        readonly List<Tower> live = new List<Tower>();
        bool seeded;

        public EnemyPath Path => path;

        // Level does not own the map art -- it does not draw it or tune it -- but it is the only
        // thing that knows the art's playable extent, and a placement rule needs that at runtime.
        // This reverses systems/level.md's earlier "nothing reads it at runtime"; the slice-two
        // authoring script read the same bounds for the same reason, at author time.
        public Bounds Bounds => map != null ? map.bounds : default;

        // The live list itself, not a copy: Bootstrap iterates it every frame and PlacementRules
        // reads it on every tap, so a defensive copy would allocate on a path §10 polices.
        // Read-only is what makes handing it out safe -- EnemyRegistry.Active's reasoning, reused.
        public IReadOnlyList<Tower> Towers
        {
            get
            {
                EnsureSeeded();
                return live;
            }
        }

        // Appends only. It does not instantiate or parent -- TowerFactory does both, so this type
        // stays ignorant of how a tower comes to exist.
        public void AddTower(Tower tower)
        {
            EnsureSeeded();

            // A null or a double add is rejected rather than thrown: both are recoverable, and a
            // duplicate would be ticked twice and fire twice.
            if (tower == null || live.Contains(tower))
            {
                return;
            }

            live.Add(tower);
        }

        // This -- not SetActive(false) -- is what actually stops a tower ticking, because Bootstrap
        // drives towers from the list above. An unexpected payoff of §9's driven-tick decision.
        public bool RemoveTower(Tower tower)
        {
            EnsureSeeded();
            return tower != null && live.Remove(tower);
        }

        // Lazy and idempotent rather than done in Awake, and that is not a style choice. Unity
        // does not order Awake between GameObjects, and Bootstrap.Awake reads Towers to collect
        // projectile prefabs -- so a seed in this component's Awake could hand it an empty list,
        // leaving every authored tower with no pool and therefore no shots, silently. Being lazy
        // also makes this component usable in an EditMode test, where Awake is never sent at all.
        // The third instance of the pattern, after Enemy.Initialize and Tower.Initialize.
        void EnsureSeeded()
        {
            if (seeded)
            {
                return;
            }

            seeded = true;
            if (towers == null)
            {
                return;
            }

            for (int i = 0; i < towers.Length; i++)
            {
                if (towers[i] != null)
                {
                    live.Add(towers[i]);
                }
            }
        }
    }
}
