using UnityEngine;

namespace MobileDemo.Gameplay.Enemies
{
    [CreateAssetMenu(fileName = "EnemyDefinition", menuName = "MobileDemo/Enemy Definition")]
    public sealed class EnemyDefinition : ScriptableObject
    {
        [SerializeField] Sprite sprite;
        [SerializeField] int maxHealth = 3;
        [SerializeField] float moveSpeed = 1.5f;
        [SerializeField] int currencyReward = 5;
        [SerializeField] int damageOnLeak = 1;
        [SerializeField] float spawnDelaySeconds = 0.15f;

        public Sprite Sprite => sprite;

        // MaxHealth and CurrencyReward are authored but unread until towers exist. Enemy
        // deliberately gains no matching health field in the meantime.
        public int MaxHealth => maxHealth;

        public float MoveSpeed => moveSpeed;

        public int CurrencyReward => currencyReward;

        public int DamageOnLeak => damageOnLeak;

        // The placed-but-not-moving window EnemySpawningState owns.
        public float SpawnDelaySeconds => spawnDelaySeconds;
    }
}
