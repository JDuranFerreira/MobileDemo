using UnityEngine;

namespace MobileDemo.Gameplay.Towers
{
    [CreateAssetMenu(fileName = "TowerDefinition", menuName = "MobileDemo/Tower Definition")]
    public sealed class TowerDefinition : ScriptableObject
    {
        [SerializeField] Sprite sprite;
        [SerializeField] float range = 3f;
        [SerializeField] float shotsPerSecond = 2f;
        [SerializeField] Projectile projectilePrefab;
        [SerializeField] int cost = 50;

        public Sprite Sprite => sprite;

        public float Range => range;

        public float ShotsPerSecond => shotsPerSecond;

        // A prefab reference, not a ProjectileDefinition: the projectile's speed, damage and
        // impact radius are serialized on the prefab itself -- see Projectile's own note. Two
        // towers pointing at different prefabs is therefore what makes them different weapons,
        // and it is also why ProjectileFactory needs a pool per prefab.
        public Projectile ProjectilePrefab => projectilePrefab;

        // Authored but unread until BuildController exists, the same deliberate call
        // EnemyDefinition made for MaxHealth and CurrencyReward: the asset is authored once and
        // completely, rather than revisited when the spender lands.
        public int Cost => cost;
    }
}
