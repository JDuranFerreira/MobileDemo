using System.Collections.Generic;
using UnityEngine;

namespace MobileDemo.Gameplay.Enemies
{
    public sealed class EnemyPath : MonoBehaviour
    {
        [SerializeField] Transform[] waypoints;

        Vector2[] baked;

        // Read-only, and a BCL type rather than this component: it is what lets Enemy.Configure
        // take a literal Vector2[] in a test, and it makes the shared path immutable to enemies.
        public IReadOnlyList<Vector2> Waypoints => baked;

        void Awake() => Bake();

        // Public so a future PathEditor can re-bake after moving a handle -- the bake is a cache,
        // so moving a waypoint during Play does not affect enemies already walking.
        public void Bake()
        {
            if (waypoints == null || waypoints.Length < 2)
            {
                Debug.LogError($"EnemyPath '{name}' needs at least two waypoints.", this);
                baked = new Vector2[0];
                return;
            }

            // One array shared by every enemy, so spawning allocates nothing.
            baked = new Vector2[waypoints.Length];
            for (int i = 0; i < waypoints.Length; i++)
            {
                baked[i] = waypoints[i].position;
            }
        }

        void OnDrawGizmos()
        {
            if (waypoints == null)
            {
                return;
            }

            // Drawn from the Transforms, not the bake, so it stays live while authoring. Green at
            // the spawn end and red at the leak end, because numbered labels would need
            // UnityEditor, which a runtime assembly cannot reference.
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (waypoints[i] == null)
                {
                    continue;
                }

                Gizmos.color = Color.Lerp(Color.green, Color.red, i / Mathf.Max(1f, waypoints.Length - 1f));
                Gizmos.DrawSphere(waypoints[i].position, 0.12f);
                if (i > 0 && waypoints[i - 1] != null)
                {
                    Gizmos.DrawLine(waypoints[i - 1].position, waypoints[i].position);
                }
            }
        }
    }
}
