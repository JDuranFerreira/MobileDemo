using System;
using System.Collections.Generic;
using MobileDemo.Gameplay.Towers;
using UnityEngine;

namespace MobileDemo.Gameplay.Build
{
    // Where a tower may stand, and which tower a tap landed on. No pattern -- a plain class with
    // two queries, in the shape EnemyRegistry established: there is one implementation, one owner
    // and no lifetime question, so a strategy or an interface would buy nothing.
    //
    // It takes data rather than a Level, which is the same principle §14 gives for why
    // Enemy.Configure takes IReadOnlyList<Vector2>: it means the geometry tests with a literal
    // array and a literal Bounds, no scene, no EnemyPath and no prefab. LevelRunner will
    // re-create one per level.
    public sealed class PlacementRules
    {
        readonly IReadOnlyList<Vector2> waypoints;
        readonly Bounds bounds;
        readonly float roadClearance;
        readonly float towerSpacing;

        public PlacementRules(
            IReadOnlyList<Vector2> waypoints, Bounds bounds, float roadClearance, float towerSpacing)
        {
            this.waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));
            this.bounds = bounds;
            this.roadClearance = roadClearance;
            this.towerSpacing = towerSpacing;
        }

        public bool IsLegal(Vector2 world, IReadOnlyList<Tower> towers)
        {
            return IsOnTheBoard(world) && IsClearOfTheRoad(world) && IsClearOfTowers(world, towers);
        }

        // The radius that blocks a placement is the radius that selects a tower for sale, so the
        // two branches of a tap can never both be true. Nearest wins, not first, or two towers
        // within a spacing of each other would make the selection depend on array order.
        public Tower FindTowerAt(Vector2 world, IReadOnlyList<Tower> towers)
        {
            if (towers == null)
            {
                return null;
            }

            float bestSqr = towerSpacing * towerSpacing;
            Tower best = null;

            for (int i = 0; i < towers.Count; i++)
            {
                Tower candidate = towers[i];
                if (candidate == null)
                {
                    continue;
                }

                float sqr = ((Vector2)candidate.transform.position - world).sqrMagnitude;
                if (sqr <= bestSqr)
                {
                    bestSqr = sqr;
                    best = candidate;
                }
            }

            return best;
        }

        // Two axes explicitly, rather than Bounds.Contains. A sprite's bounds are zero-thickness
        // in z, so Contains demands z == 0 exactly -- true today only because the tap arrives as a
        // Vector2, and one float of drift away from rejecting the whole board.
        bool IsOnTheBoard(Vector2 world)
        {
            return world.x >= bounds.min.x && world.x <= bounds.max.x
                && world.y >= bounds.min.y && world.y <= bounds.max.y;
        }

        bool IsClearOfTheRoad(Vector2 world)
        {
            float clearanceSqr = roadClearance * roadClearance;

            for (int i = 0; i < waypoints.Count - 1; i++)
            {
                if (DistanceToSegmentSqr(world, waypoints[i], waypoints[i + 1]) < clearanceSqr)
                {
                    return false;
                }
            }

            return true;
        }

        bool IsClearOfTowers(Vector2 world, IReadOnlyList<Tower> towers)
        {
            return FindTowerAt(world, towers) == null;
        }

        // The nearest point on the *segment*, clamped to it -- not the distance to the nearer
        // waypoint. Measuring to waypoints passes a casual test near a corner, where the nearest
        // point on both adjoining segments *is* the corner, and then lets a tower be built in the
        // middle of a long straight run: on Level_01's switchback the legs are two to four units,
        // so mid-leg is further from either waypoint than any sane clearance.
        static float DistanceToSegmentSqr(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSqr = ab.sqrMagnitude;
            if (lengthSqr <= Mathf.Epsilon)
            {
                return (point - a).sqrMagnitude;
            }

            // Squared distances throughout, so there is no Sqrt on a path that runs once per
            // segment per tap (§10).
            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSqr);
            return (point - (a + ab * t)).sqrMagnitude;
        }
    }
}
