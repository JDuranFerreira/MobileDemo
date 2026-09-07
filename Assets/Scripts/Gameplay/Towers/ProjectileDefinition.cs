using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    // A projectile type, as data -- the same shape TowerDefinition and EnemyDefinition already
    // have, and the reason ARCHITECTURE.md §7 no longer carves projectiles out as the exception.
    // Every projectile type shares one Projectile.prefab and differs only as one of these assets,
    // exactly as the two tower types share Tower.prefab.
    //
    // What it deliberately does not carry is a damage figure. Damage is the *tower's* number
    // (TowerDefinition.damage); this asset scales it. A projectile that owned its own damage made
    // two assets answer "how hard does this tower hit", and the tower -- the thing the player buys,
    // upgrades and compares -- was not one of them.
    [CreateAssetMenu(fileName = "ProjectileDefinition", menuName = "MobileDemo/Projectile Definition")]
    public sealed class ProjectileDefinition : ScriptableObject
    {
        [SerializeField] Sprite sprite;
        [SerializeField] float speed = 8f;

        [Tooltip("Scales the firing tower's damage. 1 passes it through unchanged; a slower or "
            + "splash projectile is where a value below 1 earns its keep.")]
        [SerializeField] float damageMultiplier = 1f;

        [Tooltip("0 means single-target. Above 0, everything within this world-space radius of "
            + "the impact point is damaged -- including the enemy that was aimed at.")]
        [SerializeField] float impactRadius;

        // The prefab is a field here rather than a Bootstrap reference, which is the one place
        // this type is not shaped like TowerDefinition, and it is what keeps ProjectileFactory's
        // pool-per-prefab honest: the factory keys pools by prefab, so the prefab has to be
        // reachable from the data that names a projectile type. Today every definition points at
        // the same Projectile.prefab and there is one pool; a type that ever needs its own
        // GameObject -- a trail, a particle system, a second renderer -- gets a second prefab here
        // and a second pool for free, with no other file touched.
        [SerializeField] Projectile prefab;

        public Sprite Sprite => sprite;

        public float Speed => speed;

        public float DamageMultiplier => damageMultiplier;

        public float ImpactRadius => impactRadius;

        public Projectile Prefab => prefab;
    }
}
