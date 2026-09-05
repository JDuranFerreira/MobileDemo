using System;
using System.Collections.Generic;
using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    // Which tower types the player may build. It reads like a convenience for the build menu, and
    // that is the smaller half of why it exists.
    //
    // The larger half is pooling. ProjectileFactory takes every projectile prefab up front by
    // explicit decision -- a pool built on the first shot would allocate its whole prewarm
    // mid-wave -- and Create on a prefab it was never told about logs an error and returns null.
    // So a buildable tower whose projectile was not collected at boot would silently lose every
    // shot, and *something* has to enumerate the buildable definitions before the factory is
    // constructed. This asset would therefore have to exist even if the buildable type were
    // hardcoded, which is why adopting it now is not anticipation.
    //
    // It is not on GameConfig, and that is not a preference: GameConfig lives in MobileDemo.Core,
    // which references nothing project-specific, so a TowerDefinition field there does not
    // compile. §3's dependency direction, enforced rather than asserted. Nor on Level -- the
    // buildable set is a *run* constant like StartingLives, and §7 warns that per-level is the
    // plausible wrong guess.
    [CreateAssetMenu(fileName = "TowerCatalogue", menuName = "MobileDemo/Tower Catalogue")]
    public sealed class TowerCatalogue : ScriptableObject
    {
        [SerializeField] TowerDefinition[] buildable;

        // Never null for a caller -- an unassigned array reads as a catalogue with nothing to
        // build, which is Level.Towers' precedent.
        public IReadOnlyList<TowerDefinition> Buildable =>
            buildable ?? Array.Empty<TowerDefinition>();

        // Appends into a caller-supplied list rather than returning one, so Bootstrap can union
        // this with the level's pre-placed towers without a second allocation.
        public void CollectProjectilePrefabs(List<Projectile> into)
        {
            if (into == null)
            {
                throw new ArgumentNullException(nameof(into));
            }

            IReadOnlyList<TowerDefinition> definitions = Buildable;
            for (int i = 0; i < definitions.Count; i++)
            {
                TowerDefinition definition = definitions[i];
                Projectile prefab = definition != null ? definition.ProjectilePrefab : null;

                // Two tower types sharing a projectile is normal, not an error -- they share the
                // pool, so a duplicate here would build a second one for nothing.
                if (prefab != null && !into.Contains(prefab))
                {
                    into.Add(prefab);
                }
            }
        }
    }
}
