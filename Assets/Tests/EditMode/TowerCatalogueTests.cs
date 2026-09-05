using System.Collections.Generic;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // The catalogue's real job is the projectile-prefab closure, not the menu: ProjectileFactory is
    // prewarmed once and refuses to build a pool later, so a buildable tower whose projectile was
    // never collected fires nothing, silently. These tests are the cheap half of catching that;
    // PlaceTowerCommandTests.Execute_ThenTick_PutsAProjectileInTheAir is the expensive half.
    public class TowerCatalogueTests
    {
        GameObject root;
        TowerCatalogue catalogue;
        readonly List<TowerDefinition> definitions = new List<TowerDefinition>();

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("TowerCatalogueTests");
            catalogue = ScriptableObject.CreateInstance<TowerCatalogue>();
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            for (int i = 0; i < definitions.Count; i++)
            {
                if (definitions[i] != null)
                {
                    Object.DestroyImmediate(definitions[i]);
                }
            }

            definitions.Clear();

            if (catalogue != null)
            {
                Object.DestroyImmediate(catalogue);
            }
        }

        Projectile NewProjectile(string name)
        {
            Projectile projectile = new GameObject(name).AddComponent<Projectile>();
            projectile.transform.SetParent(root.transform);
            return projectile;
        }

        TowerDefinition NewDefinition(Projectile prefab)
        {
            TowerDefinition definition = ScriptableObject.CreateInstance<TowerDefinition>();
            SerializedFields.Set(definition, "projectilePrefab", prefab);
            definitions.Add(definition);
            return definition;
        }

        /// <summary>An unassigned array reads as a catalogue with nothing to build, never null.</summary>
        [Test]
        public void Buildable_WhenUnassigned_IsEmptyRatherThanNull()
        {
            Assert.IsNotNull(catalogue.Buildable);
            Assert.AreEqual(0, catalogue.Buildable.Count);
        }

        [Test]
        public void Buildable_ReturnsTheAuthoredDefinitionsInOrder()
        {
            TowerDefinition first = NewDefinition(NewProjectile("A"));
            TowerDefinition second = NewDefinition(NewProjectile("B"));
            SerializedFields.Set(catalogue, "buildable", new[] { first, second });

            Assert.AreSame(first, catalogue.Buildable[0]);
            Assert.AreSame(second, catalogue.Buildable[1]);
        }

        [Test]
        public void CollectProjectilePrefabs_IncludesEveryBuildableDefinitionsPrefab()
        {
            Projectile bullet = NewProjectile("Bullet");
            Projectile fire = NewProjectile("Fire");
            SerializedFields.Set(
                catalogue, "buildable", new[] { NewDefinition(bullet), NewDefinition(fire) });

            List<Projectile> collected = new List<Projectile>();
            catalogue.CollectProjectilePrefabs(collected);

            Assert.AreEqual(2, collected.Count);
            Assert.Contains(bullet, collected);
            Assert.Contains(fire, collected);
        }

        /// <summary>
        /// Two tower types sharing a projectile is normal — they share the pool — so a duplicate
        /// here would prewarm a second one for nothing.
        /// </summary>
        [Test]
        public void CollectProjectilePrefabs_DoesNotDuplicateASharedPrefab()
        {
            Projectile bullet = NewProjectile("Bullet");
            SerializedFields.Set(
                catalogue, "buildable", new[] { NewDefinition(bullet), NewDefinition(bullet) });

            List<Projectile> collected = new List<Projectile>();
            catalogue.CollectProjectilePrefabs(collected);

            Assert.AreEqual(1, collected.Count);
        }

        [Test]
        public void CollectProjectilePrefabs_AppendsRatherThanReplacing()
        {
            Projectile existing = NewProjectile("Existing");
            Projectile bullet = NewProjectile("Bullet");
            SerializedFields.Set(catalogue, "buildable", new[] { NewDefinition(bullet) });

            List<Projectile> collected = new List<Projectile> { existing };
            catalogue.CollectProjectilePrefabs(collected);

            Assert.AreEqual(2, collected.Count, "Bootstrap unions this with the level's own prefabs");
            Assert.Contains(existing, collected);
        }

        [Test]
        public void CollectProjectilePrefabs_SkipsNullEntriesAndNullPrefabs()
        {
            SerializedFields.Set(
                catalogue, "buildable", new[] { null, NewDefinition(null) });

            List<Projectile> collected = new List<Projectile>();

            Assert.DoesNotThrow(() => catalogue.CollectProjectilePrefabs(collected));
            Assert.AreEqual(0, collected.Count);
        }
    }
}
