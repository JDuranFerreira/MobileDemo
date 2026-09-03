using UnityEngine;

namespace MobileDemo.Core.Config
{
    [CreateAssetMenu(fileName = "GameConfig", menuName = "MobileDemo/Game Config")]
    public sealed class GameConfig : ScriptableObject
    {
        [SerializeField] int targetFrameRate = 60;
        [SerializeField] int startingLives = 20;
        [SerializeField] int enemyPoolPrewarm = 64;

        public int TargetFrameRate => targetFrameRate;

        public int StartingLives => startingLives;

        // Supplied to ObjectPool<T>'s constructor by Bootstrap. The pool still knows nothing about
        // this asset, which is what keeps it testable with no asset and no scene.
        public int EnemyPoolPrewarm => enemyPoolPrewarm;
    }
}
