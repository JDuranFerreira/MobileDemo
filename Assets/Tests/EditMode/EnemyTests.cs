using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Enemies;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // No `using System;`, for the reason ObjectPoolTests gives.
    //
    // Unity does not send Awake outside play mode, which is why Enemy.Initialize is idempotent
    // and called from Configure. Everything here is driven through Enemy.Tick(dt) with an
    // explicit dt -- Update would not be callable from a test at all.
    public class EnemyTests
    {
        const float Dt = 0.1f;
        const int SafetyCap = 1000;

        GameObject root;
        Enemy enemy;
        EnemyDefinition definition;
        List<int> leaks;
        List<EnemyKilled> kills;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("EnemyTests");
            enemy = new GameObject("Enemy").AddComponent<Enemy>();
            enemy.transform.SetParent(root.transform);

            // The field defaults make this a valid, deterministic fixture with no seam --
            // moveSpeed 1.5, damageOnLeak 1, spawnDelaySeconds 0.15, maxHealth 3,
            // currencyReward 5.
            definition = ScriptableObject.CreateInstance<EnemyDefinition>();

            leaks = new List<int>();
            kills = new List<EnemyKilled>();
            EventBus<EnemyLeaked>.Subscribe(OnEnemyLeaked);
            EventBus<EnemyKilled>.Subscribe(OnEnemyKilled);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();

            // DestroyImmediate for the same reason ObjectPoolTests gives, and the definition gets
            // the same treatment: an unparented ScriptableObject otherwise leaks for the whole
            // editor session.
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            if (definition != null)
            {
                Object.DestroyImmediate(definition);
            }
        }

        void OnEnemyLeaked(EnemyLeaked evt) => leaks.Add(evt.Damage);

        void OnEnemyKilled(EnemyKilled evt) => kills.Add(evt);

        static Vector2[] StraightPath() => new[] { new Vector2(0f, 0f), new Vector2(0f, 1f) };

        /// <summary>Ticks until the enemy finishes, or fails rather than hanging the run.</summary>
        void TickToTheEnd()
        {
            for (int i = 0; i < SafetyCap && !enemy.IsFinished; i++)
            {
                enemy.Tick(Dt);
            }

            Assert.IsTrue(enemy.IsFinished, $"the enemy did not finish within {SafetyCap} ticks");
        }

        /// <summary>Advances past the spawn delay without asserting anything about movement.</summary>
        void TickPastTheSpawnDelay() => enemy.Tick(definition.SpawnDelaySeconds);

        /// <summary>
        /// The placement <c>ObjectPool&lt;T&gt;.Get</c> deliberately does not do -- the factory's
        /// Configure call is what does it, in the statement right after Get.
        /// </summary>
        [Test]
        public void Configure_PlacesTheEnemyAtTheFirstWaypoint()
        {
            Vector2[] path = StraightPath();

            enemy.Configure(definition, path);

            Assert.AreEqual(path[0], (Vector2)enemy.transform.position);
        }

        [Test]
        public void Tick_DuringTheSpawnDelay_DoesNotMove()
        {
            Vector2[] path = StraightPath();
            enemy.Configure(definition, path);

            enemy.Tick(definition.SpawnDelaySeconds * 0.5f);

            Assert.AreEqual(path[0], (Vector2)enemy.transform.position);
        }

        [Test]
        public void Tick_AfterTheSpawnDelay_MovesTowardTheSecondWaypoint()
        {
            enemy.Configure(definition, StraightPath());
            TickPastTheSpawnDelay();

            enemy.Tick(Dt);

            // Asserted relatively, so retuning the asset's defaults cannot break this.
            Assert.AreEqual(definition.MoveSpeed * Dt, enemy.transform.position.y, 1e-4f);
        }

        [Test]
        public void Tick_UntilTheEndOfThePath_PublishesEnemyLeakedOnce()
        {
            enemy.Configure(definition, StraightPath());

            TickToTheEnd();

            Assert.AreEqual(new[] { definition.DamageOnLeak }, leaks);
        }

        [Test]
        public void Tick_UntilTheEndOfThePath_SetsIsFinished()
        {
            enemy.Configure(definition, StraightPath());

            TickToTheEnd();

            Assert.IsTrue(enemy.IsFinished);
        }

        [Test]
        public void Tick_AfterFinishing_PublishesNothingFurther()
        {
            enemy.Configure(definition, StraightPath());
            TickToTheEnd();

            enemy.Tick(Dt);
            enemy.Tick(Dt);

            Assert.AreEqual(1, leaks.Count);
        }

        /// <summary>
        /// The gap between <c>Get()</c> and <c>Configure()</c>: the instance is active, at last
        /// life's transform, and must be inert. This is what <c>current?.Tick</c> guards.
        /// </summary>
        [Test]
        public void Tick_OnAnUnconfiguredEnemy_DoesNotThrow()
        {
            Assert.DoesNotThrow(() => enemy.Tick(Dt));
            Assert.IsEmpty(leaks);
        }

        [Test]
        public void OnDespawn_MakesTheEnemyInert()
        {
            enemy.Configure(definition, StraightPath());
            TickPastTheSpawnDelay();

            enemy.OnDespawn();
            Vector2 resting = enemy.transform.position;
            enemy.Tick(Dt);

            Assert.AreEqual(resting, (Vector2)enemy.transform.position);
            Assert.IsEmpty(leaks);
        }

        [Test]
        public void OnSpawn_ClearsIsFinished()
        {
            enemy.Configure(definition, StraightPath());
            TickToTheEnd();

            enemy.OnDespawn();
            enemy.OnSpawn();

            Assert.IsFalse(enemy.IsFinished);
        }

        /// <summary>
        /// The most valuable test in the slice: it proves <c>EnemyMovingState.Enter</c> re-zeroes
        /// its own waypoint cursor, so a recycled enemy restarts instead of resuming mid-path.
        /// That reset being a consequence of the State pattern rather than another line in
        /// <c>OnSpawn</c> is the concrete reason §4's states are classes and not an enum.
        /// </summary>
        [Test]
        public void OnDespawn_ThenOnSpawn_ThenConfigure_RestartsFromTheFirstWaypoint()
        {
            Vector2[] path = StraightPath();
            enemy.Configure(definition, path);
            TickPastTheSpawnDelay();
            enemy.Tick(Dt);
            Assert.AreNotEqual(path[0], (Vector2)enemy.transform.position, "precondition: it moved");

            enemy.OnDespawn();
            enemy.OnSpawn();
            enemy.Configure(definition, path);

            Assert.AreEqual(path[0], (Vector2)enemy.transform.position);

            // And it walks the whole path again from the start, publishing exactly once more.
            TickToTheEnd();
            Assert.AreEqual(new[] { definition.DamageOnLeak }, leaks);
        }

        /// <summary>Advances into Moving, which is the only state in which an enemy is shootable.</summary>
        void ConfigureAndStartMoving()
        {
            enemy.Configure(definition, StraightPath());
            TickPastTheSpawnDelay();
        }

        [Test]
        public void Configure_SetsHealthToTheDefinitionsMaximum()
        {
            enemy.Configure(definition, StraightPath());

            Assert.AreEqual(definition.MaxHealth, enemy.CurrentHealth);
        }

        [Test]
        public void TakeDamage_ReducesHealthWithoutKilling()
        {
            ConfigureAndStartMoving();

            enemy.TakeDamage(1);

            Assert.AreEqual(definition.MaxHealth - 1, enemy.CurrentHealth);
            Assert.IsFalse(enemy.IsFinished);
            Assert.IsEmpty(kills);
        }

        [Test]
        public void TakeDamage_ToZero_PublishesEnemyKilledWithTheReward()
        {
            ConfigureAndStartMoving();

            enemy.TakeDamage(definition.MaxHealth);

            Assert.AreEqual(1, kills.Count);
            Assert.AreEqual(definition.CurrencyReward, kills[0].Reward);
            Assert.IsTrue(enemy.IsFinished);
        }

        /// <summary>
        /// The two exits from <c>EnemyDyingState</c> are mutually exclusive. A kill that also
        /// leaked would cost the player a life for an enemy they successfully stopped.
        /// </summary>
        [Test]
        public void TakeDamage_ToZero_PublishesNoEnemyLeaked()
        {
            ConfigureAndStartMoving();

            enemy.TakeDamage(definition.MaxHealth);

            Assert.IsEmpty(leaks);
        }

        [Test]
        public void TakeDamage_BeyondZero_ClampsHealthAtZero()
        {
            ConfigureAndStartMoving();

            enemy.TakeDamage(definition.MaxHealth + 99);

            Assert.AreEqual(0, enemy.CurrentHealth);
        }

        /// <summary>
        /// Two projectiles landing on the same enemy in the same frame. Without the targetable
        /// gate in <c>TakeDamage</c> both would drive health below zero and both would transition
        /// to Dying, paying the reward twice for one enemy.
        /// </summary>
        [Test]
        public void TakeDamage_AfterDying_PublishesNothingFurther()
        {
            ConfigureAndStartMoving();
            enemy.TakeDamage(definition.MaxHealth);

            enemy.TakeDamage(definition.MaxHealth);

            Assert.AreEqual(1, kills.Count);
        }

        /// <summary>
        /// The spawn window's second job, and the one that turns it from a cosmetic delay into a
        /// gameplay rule: a just-placed enemy cannot be shot.
        /// </summary>
        [Test]
        public void TakeDamage_DuringTheSpawnDelay_IsIgnored()
        {
            enemy.Configure(definition, StraightPath());

            enemy.TakeDamage(definition.MaxHealth);

            Assert.AreEqual(definition.MaxHealth, enemy.CurrentHealth);
            Assert.IsFalse(enemy.IsFinished);
            Assert.IsEmpty(kills);
        }

        [Test]
        public void IsTargetable_IsTrueOnlyWhileMoving()
        {
            Assert.IsFalse(enemy.IsTargetable, "unconfigured");

            enemy.Configure(definition, StraightPath());
            Assert.IsFalse(enemy.IsTargetable, "spawning");

            TickPastTheSpawnDelay();
            Assert.IsTrue(enemy.IsTargetable, "moving");

            TickToTheEnd();
            Assert.IsFalse(enemy.IsTargetable, "finished");
        }

        [Test]
        public void TakeDamage_NonPositiveAmount_ChangesNothing()
        {
            ConfigureAndStartMoving();

            enemy.TakeDamage(0);
            enemy.TakeDamage(-3);

            Assert.AreEqual(definition.MaxHealth, enemy.CurrentHealth);
        }

        /// <summary>
        /// The pooling reset for health. A recycled enemy that kept its last life's health would
        /// die to a single hit, which is the kind of bug that only shows up deep into a wave.
        /// </summary>
        [Test]
        public void OnDespawn_ThenOnSpawn_ThenConfigure_RestoresFullHealth()
        {
            ConfigureAndStartMoving();
            enemy.TakeDamage(definition.MaxHealth);
            Assert.AreEqual(0, enemy.CurrentHealth, "precondition: it died");

            enemy.OnDespawn();
            enemy.OnSpawn();
            enemy.Configure(definition, StraightPath());

            Assert.AreEqual(definition.MaxHealth, enemy.CurrentHealth);
        }
    }
}
