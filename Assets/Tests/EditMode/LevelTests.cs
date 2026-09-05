using MobileDemo.Gameplay.Levels;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // Level's first fixture, and it does not reverse §14's decision to leave EnemyPath untested.
    // Level stopped being two serialized getters the moment it grew a lazy-seeded runtime list and
    // two mutators -- and the seeding hazard below is exactly the kind play mode hides, because in
    // play mode Awake always runs and the ordering happens to work most of the time.
    public class LevelTests
    {
        GameObject root;
        Level level;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("LevelTests");
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        Level NewLevel(params Tower[] authored)
        {
            GameObject go = new GameObject("Level");
            go.transform.SetParent(root.transform);
            level = go.AddComponent<Level>();
            SerializedFields.Set(level, "towers", authored);
            return level;
        }

        Tower NewTower(string name)
        {
            Tower tower = new GameObject(name).AddComponent<Tower>();
            tower.transform.SetParent(root.transform);
            return tower;
        }

        /// <summary>
        /// The ordering hazard the lazy seed exists for. Awake is not sent outside play mode, and
        /// in play mode Unity does not order Awake between GameObjects — so Bootstrap.Awake can
        /// read this list before Level's own Awake would have filled it. A seed done in Awake would
        /// hand it an empty list, leaving every authored tower with no projectile pool and
        /// therefore no shots, with nothing logged.
        /// </summary>
        [Test]
        public void Towers_WithoutAwake_SeedsFromTheSerializedArray()
        {
            Tower authored = NewTower("Authored");

            Assert.AreEqual(1, NewLevel(authored).Towers.Count);
            Assert.AreSame(authored, level.Towers[0]);
        }

        [Test]
        public void Towers_WithAnUnassignedArray_IsEmptyRatherThanNull()
        {
            GameObject go = new GameObject("Bare");
            go.transform.SetParent(root.transform);
            Level bare = go.AddComponent<Level>();

            Assert.IsNotNull(bare.Towers);
            Assert.AreEqual(0, bare.Towers.Count);
        }

        /// <summary>A hole in the authored array is skipped, not carried as a null to tick.</summary>
        [Test]
        public void Towers_SkipsNullsInTheSerializedArray()
        {
            Tower authored = NewTower("Authored");

            Assert.AreEqual(1, NewLevel(null, authored).Towers.Count);
        }

        [Test]
        public void AddTower_AppearsInTowers()
        {
            NewLevel();
            Tower placed = NewTower("Placed");

            level.AddTower(placed);

            Assert.AreEqual(1, level.Towers.Count);
            Assert.AreSame(placed, level.Towers[0]);
        }

        /// <summary>A double add would be ticked twice and would fire twice.</summary>
        [Test]
        public void AddTower_Twice_AddsItOnce()
        {
            NewLevel();
            Tower placed = NewTower("Placed");

            level.AddTower(placed);
            level.AddTower(placed);

            Assert.AreEqual(1, level.Towers.Count);
        }

        [Test]
        public void AddTower_WithNull_IsIgnored()
        {
            NewLevel();

            Assert.DoesNotThrow(() => level.AddTower(null));
            Assert.AreEqual(0, level.Towers.Count);
        }

        [Test]
        public void RemoveTower_DisappearsAndReturnsTrue()
        {
            Tower authored = NewTower("Authored");
            NewLevel(authored);

            Assert.IsTrue(level.RemoveTower(authored));
            Assert.AreEqual(0, level.Towers.Count);
        }

        [Test]
        public void RemoveTower_ForAnUnknownTower_ReturnsFalse()
        {
            NewLevel();

            Assert.IsFalse(level.RemoveTower(NewTower("Stranger")));
            Assert.IsFalse(level.RemoveTower(null));
        }

        /// <summary>
        /// Removing does not touch the serialized array, so the prefab keeps describing the
        /// level's opening state rather than the state a player left it in.
        /// </summary>
        [Test]
        public void RemoveTower_LeavesTheSerializedArrayAlone()
        {
            Tower authored = NewTower("Authored");
            NewLevel(authored);

            level.RemoveTower(authored);

            Tower[] serialized = (Tower[])typeof(Level)
                .GetField("towers", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic)
                .GetValue(level);

            Assert.AreEqual(1, serialized.Length);
            Assert.AreSame(authored, serialized[0]);
        }

        [Test]
        public void Bounds_WithNoMapAssigned_IsEmpty()
        {
            NewLevel();

            Assert.AreEqual(Vector3.zero, level.Bounds.size);
        }

        [Test]
        public void Bounds_ReadsTheAssignedMapRenderer()
        {
            NewLevel();

            SpriteRenderer map = new GameObject("Map").AddComponent<SpriteRenderer>();
            map.transform.SetParent(level.transform);
            map.transform.position = new Vector3(3f, 4f, 0f);
            SerializedFields.Set(level, "map", map);

            Assert.AreEqual(map.bounds.center, level.Bounds.center);
        }
    }
}
