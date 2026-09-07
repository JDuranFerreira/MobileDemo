using System;
using System.Collections.Generic;
using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Waves
{
    // One group of one enemy type. ARCHITECTURE.md §7 has described a wave as an ordered list of
    // { EnemyDefinition, count, spawnInterval } since before any of this existed, and that shape
    // survived contact: mixing two types in a wave is two groups, not a field on either.
    //
    // A struct rather than a [Serializable] class, so a wave's groups sit in the array itself and
    // reading one allocates nothing -- WaveRunner reads the current group on every spawn.
    [Serializable]
    public struct SpawnGroup
    {
        [SerializeField] EnemyDefinition enemy;

        [SerializeField] int count;

        [Tooltip("Seconds between spawns within this group. The first of a group arrives on the "
            + "tick that reaches it, so a group's own interval never delays its opening enemy.")]
        [SerializeField] float spawnInterval;

        // Public, and not only for the tests: the authoring script builds these in code too, and
        // a struct whose only construction path is the Inspector could not be authored at all.
        public SpawnGroup(EnemyDefinition enemy, int count, float spawnInterval)
        {
            this.enemy = enemy;
            this.count = count;
            this.spawnInterval = spawnInterval;
        }

        public EnemyDefinition Enemy => enemy;

        public int Count => count;

        public float SpawnInterval => spawnInterval;
    }

    // The asset §7 promised and §13 deferred twice. It absorbs Bootstrap's spawnIntervalSeconds --
    // the scene value that was always labelled "wave data, and WaveDefinition absorbs it" -- and
    // with it the single enemyDefinition reference, which is what finally gives EnemyGreySoldier
    // a spawner.
    //
    // Held by Level as a WaveDefinition[] rather than becoming a LevelDefinition: §7's rule is
    // that a prefab is already an asset, so the level stays a prefab that *references* these.
    [CreateAssetMenu(fileName = "Wave", menuName = "MobileDemo/Wave Definition")]
    public sealed class WaveDefinition : ScriptableObject
    {
        [Tooltip("Spawned in order. Two enemy types in one wave is two groups.")]
        [SerializeField] SpawnGroup[] groups;

        public IReadOnlyList<SpawnGroup> Groups =>
            groups ?? Array.Empty<SpawnGroup>();

        /// <summary>Enemies this wave will spawn, ignoring groups authored with no enemy.</summary>
        /// <remarks>
        /// WaveRunner uses it for one thing only: refusing to start a wave that can never be
        /// cleared. A wave with nothing to spawn is empty on its first tick, so it would complete
        /// instantly and silently — which reads as a wave sequence that skips a step rather than
        /// as the authoring mistake it is.
        /// </remarks>
        public int TotalSpawns
        {
            get
            {
                if (groups == null)
                {
                    return 0;
                }

                int total = 0;
                for (int i = 0; i < groups.Length; i++)
                {
                    if (groups[i].Enemy != null && groups[i].Count > 0)
                    {
                        total += groups[i].Count;
                    }
                }

                return total;
            }
        }
    }
}
