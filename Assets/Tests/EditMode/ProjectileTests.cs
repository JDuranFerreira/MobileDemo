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
    //
    // The damage asserted here is the *tower's* figure scaled by the definition's multiplier, so
    // TowerDamage x Multiplier is what a hit must cost -- the arithmetic §7 moved onto the tower.
    public class ProjectileTests
    {
        const float Speed = 10f;
        const int TowerDamage = 4;
        const float Multiplier = 0.5f;
        const int Damage = 2;
        const float Dt = 0.1f;
        const int SafetyCap = 1000;

        GameObject root;
        EnemyDefinition definition;
        EnemyRegistry registry;
        readonly System.Collections.Generic.List<ProjectileDefinition> projectileDefinitions =
            new System.Collections.Generic.List<ProjectileDefinition>();

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

            for (int i = 0; i < projectileDefinitions.Count; i++)
            {
                if (projectileDefinitions[i] != null)
                {
                    Object.DestroyImmediate(projectileDefinitions[i]);
                }
            }

            projectileDefinitions.Clear();
        }

        ProjectileDefinition NewDefinition(float impactRadius)
        {
            ProjectileDefinition created = ScriptableObject.CreateInstance<ProjectileDefinition>();
            SerializedFields.Set(created, "speed", Speed);
            SerializedFields.Set(created, "damageMultiplier", Multiplier);
            SerializedFields.Set(created, "impactRadius", impactRadius);
            projectileDefinitions.Add(created);
            return created;
        }

        /// <summary>
        /// A projectile plus the definition it will be configured with. The prefab field is left
        /// unset: only ProjectileFactory reads it, and these tests build the instance themselves.
        /// </summary>
        Projectile NewProjectile(float impactRadius = 0f)
        {
            Projectile projectile = new GameObject("Projectile").AddComponent<Projectile>();
            projectile.transform.SetParent(root.transform);
            return projectile;
        }

        void Configure(Projectile projectile, Enemy enemy, float impactRadius = 0f) =>
            projectile.Configure(NewDefinition(impactRadius), enemy, registry, TowerDamage);

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
            Configure(projectile, target);

            projectile.Tick(Dt);

            Assert.AreEqual(Speed * Dt, projectile.transform.position.x, 1e-4f);
        }

        [Test]
        public void Tick_OnArrival_DamagesTheTargetAndFinishes()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            Configure(projectile, target);

            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth - Damage, target.CurrentHealth);
        }

        [Test]
        public void Tick_AfterFinishing_DoesNothingFurther()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            Configure(projectile, target);
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
            Projectile projectile = NewProjectile();
            Configure(projectile, target, 1f);

            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth - Damage, target.CurrentHealth);
            Assert.AreEqual(definition.MaxHealth - Damage, neighbour.CurrentHealth);
        }

        [Test]
        public void Tick_WithAnImpactRadius_LeavesEnemiesOutsideItAlone()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(2f, 0f));
            Enemy distant = NewTargetableEnemyAt(new Vector2(9f, 0f));
            Projectile projectile = NewProjectile();
            Configure(projectile, target, 1f);

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
            Projectile projectile = NewProjectile();
            Configure(projectile, target, 1f);

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
            Projectile projectile = NewProjectile();
            Configure(projectile, target, 1f);

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

            Configure(projectile, null);

            Assert.DoesNotThrow(() => projectile.Tick(Dt));
            Assert.IsTrue(projectile.IsFinished, "an aim point it is already standing on is reached at once");
        }

        [Test]
        public void OnSpawn_ClearsIsFinished()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            Configure(projectile, target);
            TickToImpact(projectile);

            projectile.OnDespawn();
            projectile.OnSpawn();

            Assert.IsFalse(projectile.IsFinished);
        }

        /// <summary>
        /// A recycled projectile holding its last life's registry could damage enemies from a
        /// level that has already been swapped out. It holds neither afterwards, and with no
        /// definition it does not fly either — the pool's next Create supplies all three.
        /// </summary>
        [Test]
        public void OnDespawn_ThenTick_DamagesNothing()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            Configure(projectile, target);

            projectile.OnDespawn();
            projectile.OnSpawn();
            for (int i = 0; i < 10; i++)
            {
                projectile.Tick(Dt);
            }

            Assert.AreEqual(definition.MaxHealth, target.CurrentHealth);
            Assert.IsNull(projectile.Definition, "OnDespawn drops the definition with the target");
        }

        /// <summary>
        /// A projectile with no definition has no speed, so left in flight it would never reach an
        /// aim point and ProjectileFactory would hold it in its live list forever. It finishes at
        /// once instead, and the pool gets it back on the next tick.
        /// </summary>
        [Test]
        public void Configure_WithNoDefinition_LogsAndFinishesImmediately()
        {
            Projectile projectile = NewProjectile();

            UnityEngine.TestTools.LogAssert.Expect(
                LogType.Error, new System.Text.RegularExpressions.Regex("configured with no definition"));
            projectile.Configure(null, null, registry, TowerDamage);

            Assert.IsTrue(projectile.IsFinished);
        }

        /// <summary>
        /// The floor in <c>Configure</c>: a multiplier that rounds to nothing is a weak shot, not a
        /// silently disarmed one.
        /// </summary>
        [Test]
        public void Tick_WithAMultiplierThatRoundsToZero_StillCostsOneHealth()
        {
            Enemy target = NewTargetableEnemyAt(new Vector2(1f, 0f));
            Projectile projectile = NewProjectile();
            ProjectileDefinition weak = NewDefinition(0f);
            SerializedFields.Set(weak, "damageMultiplier", 0.01f);
            projectile.Configure(weak, target, registry, 1);

            TickToImpact(projectile);

            Assert.AreEqual(definition.MaxHealth - 1, target.CurrentHealth);
        }
    }
}
