using System.Collections.Generic;
using MobileDemo.Gameplay.Build;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // Literal coordinates and a literal Bounds, no scene and no EnemyPath -- which is the payoff
    // of PlacementRules taking baked data rather than a Level, the same reasoning §14 gives for
    // Enemy.Configure taking IReadOnlyList<Vector2>.
    public class PlacementRulesTests
    {
        const float RoadClearance = 0.9f;
        const float TowerSpacing = 0.7f;

        GameObject root;
        PlacementRules rules;

        /// <summary>An L: right along y=0 to x=4, then up to y=4. One elbow, at (4,0).</summary>
        static readonly Vector2[] Path =
        {
            new Vector2(0f, 0f),
            new Vector2(4f, 0f),
            new Vector2(4f, 4f)
        };

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("PlacementRulesTests");
            rules = new PlacementRules(
                Path,
                new Bounds(Vector3.zero, new Vector3(20f, 20f, 0f)),
                RoadClearance,
                TowerSpacing);
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        Tower NewTowerAt(Vector2 position)
        {
            Tower tower = new GameObject("Tower").AddComponent<Tower>();
            tower.transform.SetParent(root.transform);
            tower.transform.position = position;
            return tower;
        }

        static readonly IReadOnlyList<Tower> NoTowers = new Tower[0];

        [Test]
        public void IsLegal_WellClearOfEverything_IsTrue()
        {
            Assert.IsTrue(rules.IsLegal(new Vector2(2f, 3f), NoTowers));
        }

        [Test]
        public void IsLegal_OnASegmentMidpoint_IsFalse()
        {
            Assert.IsFalse(rules.IsLegal(new Vector2(2f, 0f), NoTowers));
        }

        [Test]
        public void IsLegal_JustInsideRoadClearance_IsFalse()
        {
            Assert.IsFalse(rules.IsLegal(new Vector2(2f, RoadClearance - 0.05f), NoTowers));
        }

        [Test]
        public void IsLegal_JustOutsideRoadClearance_IsTrue()
        {
            Assert.IsTrue(rules.IsLegal(new Vector2(2f, RoadClearance + 0.05f), NoTowers));
        }

        /// <summary>
        /// The bug the segment projection exists to prevent, pinned where it actually bites.
        /// (2, 0.5) is 0.5 from the middle of segment one and more than two units from *every*
        /// waypoint — so a rule that measured to waypoints instead of to segments would happily
        /// call it clear and let a tower be built in the middle of the road. The fixture asserts
        /// that premise rather than trusting it, or the test could pass for the wrong reason.
        /// </summary>
        [Test]
        public void IsLegal_BesideTheMiddleOfASegment_IsFalse()
        {
            Vector2 besideTheRoad = new Vector2(2f, 0.5f);

            for (int i = 0; i < Path.Length; i++)
            {
                Assert.Greater((besideTheRoad - Path[i]).magnitude, RoadClearance,
                    $"fixture premise: the point must be clear of waypoint {i}");
            }

            Assert.IsFalse(rules.IsLegal(besideTheRoad, NoTowers));
        }

        /// <summary>
        /// The corner case, which the projection handles for free: at a waypoint the nearest point
        /// on both adjoining segments *is* that waypoint, so the clearance zones simply union.
        /// Diagonally outside it, both segments are clear and the spot is legal.
        /// </summary>
        [Test]
        public void IsLegal_DiagonallyOutsideACorner_IsTrue()
        {
            Assert.IsTrue(rules.IsLegal(new Vector2(5.2f, -0.95f), NoTowers));
        }

        [Test]
        public void IsLegal_DiagonallyInsideACorner_IsFalse()
        {
            Assert.IsFalse(rules.IsLegal(new Vector2(4.6f, 0.6f), NoTowers));
        }

        [Test]
        public void IsLegal_OffTheBoard_IsFalse()
        {
            Assert.IsFalse(rules.IsLegal(new Vector2(0f, 11f), NoTowers));
            Assert.IsFalse(rules.IsLegal(new Vector2(-11f, 5f), NoTowers));
        }

        [Test]
        public void IsLegal_WithinSpacingOfATower_IsFalse()
        {
            Tower[] towers = { NewTowerAt(new Vector2(2f, 3f)) };

            Assert.IsFalse(rules.IsLegal(new Vector2(2f + TowerSpacing - 0.05f, 3f), towers));
        }

        [Test]
        public void IsLegal_BeyondSpacingOfATower_IsTrue()
        {
            Tower[] towers = { NewTowerAt(new Vector2(2f, 3f)) };

            Assert.IsTrue(rules.IsLegal(new Vector2(2f + TowerSpacing + 0.05f, 3f), towers));
        }

        [Test]
        public void FindTowerAt_WithinSpacing_ReturnsTheTower()
        {
            Tower tower = NewTowerAt(new Vector2(2f, 3f));
            Tower[] towers = { tower };

            Assert.AreSame(tower, rules.FindTowerAt(new Vector2(2.1f, 3f), towers));
        }

        [Test]
        public void FindTowerAt_BeyondSpacing_ReturnsNull()
        {
            Tower[] towers = { NewTowerAt(new Vector2(2f, 3f)) };

            Assert.IsNull(rules.FindTowerAt(new Vector2(2f + TowerSpacing + 0.05f, 3f), towers));
        }

        /// <summary>Nearest, not first, or the answer would depend on array order.</summary>
        [Test]
        public void FindTowerAt_WithTwoCandidates_ReturnsTheNearer()
        {
            Tower far = NewTowerAt(new Vector2(2.5f, 3f));
            Tower near = NewTowerAt(new Vector2(2.05f, 3f));
            Tower[] towers = { far, near };

            Assert.AreSame(near, rules.FindTowerAt(new Vector2(2f, 3f), towers));
        }

        [Test]
        public void FindTowerAt_WithNoTowers_ReturnsNull()
        {
            Assert.IsNull(rules.FindTowerAt(Vector2.zero, NoTowers));
            Assert.IsNull(rules.FindTowerAt(Vector2.zero, null));
        }
    }
}
