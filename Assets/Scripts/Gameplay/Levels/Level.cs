using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Levels
{
    // Sits on a level prefab's root. The path is a serialized reference resolved inside the
    // prefab, so a swapper can instantiate level N and read its path without searching for it.
    public sealed class Level : MonoBehaviour
    {
        [SerializeField] EnemyPath path;

        public EnemyPath Path => path;
    }
}
