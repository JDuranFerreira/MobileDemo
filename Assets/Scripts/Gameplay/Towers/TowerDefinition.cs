using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    [CreateAssetMenu(fileName = "TowerDefinition", menuName = "MobileDemo/Tower Definition")]
    public sealed class TowerDefinition : ScriptableObject
    {
        [SerializeField] Sprite sprite;
        [SerializeField] float range = 3f;
        [SerializeField] float shotsPerSecond = 2f;
        [SerializeField] int damage = 1;
        [SerializeField] ProjectileDefinition projectile;
        [SerializeField] int cost = 50;

        public Sprite Sprite => sprite;

        public float Range => range;

        public float ShotsPerSecond => shotsPerSecond;

        // The tower owns how hard it hits, and the projectile only scales it. That is the whole of
        // §7's damage decision: range, fire rate and damage are the three numbers a player compares
        // when choosing what to buy, so they are authored together on the thing being bought. A
        // projectile that carried its own damage split that answer across two assets and hid half
        // of it behind a second reference.
        public int Damage => damage;

        // A ProjectileDefinition, not a prefab: a projectile type is data like every other type
        // here, and one Projectile.prefab serves them all -- see ProjectileDefinition and §7. Two
        // towers pointing at different definitions is what makes them different weapons;
        // ProjectileFactory still keys its pools by the prefab those definitions name.
        public ProjectileDefinition Projectile => projectile;

        public int Cost => cost;
    }
}
