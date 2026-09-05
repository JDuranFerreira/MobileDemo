using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Enemies;
using MobileDemo.Gameplay.Towers;
using NUnit.Framework;
using UnityEngine;

namespace MobileDemo.Tests.EditMode
{
    // No `using System;`, for the reason ObjectPoolTests gives.
    //
    // Driven entirely through Projectile.Tick(dt) with an explicit dt -- the same payoff §9's
    // driven-tick decision buys EnemyTests.
    public class ProjectileTests
    {
        const float Speed = 10f;
        const int Damage = 2;
        const float Dt = 0.1f;
        const int SafetyCap = 1000;

        GameObject root;
        EnemyDefinition definition;
        EnemyRegistry registry;

        [SetUp]
        public void SetUp()
        {
            root = new GameObject("ProjectileTests");
            definition = ScriptableObject.CreateInstance<EnemyDefinition>();

            // Nothing here releases: these tests never tick the registry, so the callback exists
            // only to satisfy the constructor.
            registry = new EnemyRegistry(_ => true);
        }

        [TearDown]
        public void TearDown()
        {
            EventBus.ClearAll();

            if (root != null)
            {
                Object.DestroyImmediate(root);
            }

            if (definition != null)
            {
                Object.DestroyImmediate(definition);
            }
        }

        Projectile NewProjectile(float impactRadius = 0f)
        {
            Projectile projectile = new GameObject("Projectile").AddComponent<Projectile>();
            projectile.transform.SetParent(root.transform);
            SerializedFields.Set(projectile, "speed", Speed);
            SerializedFields.Set(projectile, "damage", Damage);
            SerializedFields.Set(projectile, "impactRadius", impactRadius);
            return projectile;
        }

        /// <summary>A registered enemy, already past its spawn delay so it is targetable.</summary>
        Enemy NewTargetableEnemyAt(Vector2 position)
        {
            Enemy enemy = new GameObject("Enemy").AddComponent<Enemy>();
            enemy.transform.SetParent(root.transform);

            // A path from `position` to well beyond it, so the enemy is at `position` and stays
            // near it for the handful of ticks these tests run.
            enemy.Configure(definition, new[] { position, position + new Vector2(0f, 1000f) });
            enemy.Tick(definition.SpawnDelaySeconds);
            registry.Add(enemy);

            Assert.IsTrue(enemy.IsTargetable, "precondition: the enemy must be shootable");
            return enemy;
        }

        void TickToImpact(Projectile projectile)
        {
            for (int i = 0; i < SafetyCap && !projectile.IsFinished; i++)
            {
                projectile.Tick(Dt);
            }

            Assert.IsTrue(projectile.IsFinished, $"the projectile did not land within {SafetyCap} ticks");
        }

        [Test]
        public void Tick_MovesTowardTheTarget()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(5f, 0f));
            Projectile projectile = NewProjectile();
            projectile.Configure(target, registry);

            projectile.Tick(Dt);

            Assert.AreEqual(Speed * Dt, projectile.transform.position.x, 1e-4f);
        }

        [Test]
        public void Tick_OnArrival_DamagesTheTargetAndFinishes()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            projectile.Configure(target, registry);

            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth - Damage, target.CurrentHealth);
        }

        [Test]
        public void Tick_AfterFinishing_DoesNothingFurther()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            projectile.Configure(target, registry);
            TickToImpact(projectile);
            int healthAfterImpact = target.CurrentHealth;

            projectile.Tick(Dt);
            projectile.Tick(Dt);

            Assert.AreEqual(healthAfterImpact, target.CurrentHealth, "it must not hit twice");
        }

        /// <summary>
        /// Splash is the whole reason <c>EnemyRegistry</c> is handed to the projectile rather than
        /// only to the tower: at impact it needs everyone nearby, not just who it was aimed at.
        /// </summary>
        [Test]
        public void Tick_WithAnImpactRadius_DamagesEveryEnemyInside()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(2f, 0f));
            Enemy neighbour = NewTargetableEnemyAt(new Vector2(2.5f, 0f));
            Projectile projectile = NewProjectile(1f);
            projectile.Configure(target, registry);

            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth - Damage, target.CurrentHealth);
            Assert.AreEqual(definition.MaxHealth - Damage, neighbour.CurrentHealth);
        }

        [Test]
        public void Tick_WithAnImpactRadius_LeavesEnemiesOutsideItAlone()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(2f, 0f));
            Enemy distant = NewTargetableEnemyAt(new Vector2(9f, 0f));
            Projectile projectile = NewProjectile(1f);
            projectile.Configure(target, registry);

            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth, distant.CurrentHealth);
        }

        /// <summary>
        /// The aimed-at enemy sits at the centre of its own splash, so damaging it directly *and*
        /// splashing would hit it twice. Pins the either/or in <c>Impact</c>.
        /// </summary>
        [Test]
        public void Tick_WithAnImpactRadius_DamagesTheTargetExactlyOnce()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(2f, 0f));
            Projectile projectile = NewProjectile(1f);
            projectile.Configure(target, registry);

            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth - Damage, target.CurrentHealth);
        }

        /// <summary>
        /// A target killed by another tower mid-flight. The shot still lands where it was aimed,
        /// which is what keeps a splash projectile worth firing into a cluster.
        /// </summary>
        [Test]
        public void Tick_WhenTheTargetDiesMidFlight_StillSplashesAtItsLastPosition()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(3f, 0f));
            Enemy neighbour = NewTargetableEnemyAt(new Vector2(3.5f, 0f));
            Projectile projectile = NewProjectile(1f);
            projectile.Configure(target, registry);

            projectile.Tick(Dt);
            target.TakeDamage(definition.MaxHealth);
            Assert.IsTrue(target.IsFinished, "precondition: the target died in flight");

            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth - Damage, neighbour.CurrentHealth);
        }

        [Test]
        public void Tick_WithNoTarget_FliesToItsAimPointAndFinishesWithoutThrowing()
        {
            Projectile projectile = NewProjectile();

            projectile.Configure(null, registry);

            Assert.DoesNotThrow(() => projectile.Tick(Dt));
            Assert.IsTrue(projectile.IsFinished, "an aim point it is already standing on is reached at once");
        }

        [Test]
        public void OnSpawn_ClearsIsFinished()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            projectile.Configure(target, registry);
            TickToImpact(projectile);

            projectile.OnDespawn();
            projectile.OnSpawn();

            Assert.IsFalse(projectile.IsFinished);
        }

        /// <summary>
        /// A recycled projectile holding its last life's registry could damage enemies from a
        /// level that has already been swapped out.
        /// </summary>
        [Test]
        public void OnDespawn_ThenTick_DamagesNothing()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            projectile.Configure(target, registry);

            projectile.OnDespawn();
            projectile.OnSpawn();
            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth, target.CurrentHealth);
        }
    }
}
