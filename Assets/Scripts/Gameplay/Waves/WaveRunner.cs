using System;
using System.Collections.Generic;
using MobileDemo.Core.Events;
using MobileDemo.Gameplay.Enemies;
using UnityEngine;

namespace MobileDemo.Gameplay.Waves
{
    // Reads a WaveDefinition and schedules its spawns over time -- Bootstrap.TickSpawner's
    // successor, and the last of the three jobs systems/bootstrap.md named for this type.
    //
    // It *ticks* the EnemyRegistry but does not construct it. That guide's Status table said this
    // type would "own the registry outright", and building it proved the sentence half wrong:
    // Tower and Projectile both hold the registry and are wired in Bootstrap.Awake, long before
    // any wave exists, so the object has to outlive every wave. What moved here is the ten-line
    // tick-and-release loop, which is the half that was actually about waves.
    //
    // A plain class, not a MonoBehaviour, for §9's reason: a driven Tick(dt) is callable from an
    // EditMode test with a deterministic dt, and there is no PlayMode assembly (§12).
    public sealed class WaveRunner
    {
        // A group authored with a non-positive interval would spawn its whole count inside one
        // frame's while-loop, and a zero would not terminate at all. Clamped rather than rejected,
        // on object-pool.md's stance: a bad tuning number should be recoverable where a missing
        // dependency is not. StartWave logs the mistake once so it is fixable.
        const float MinSpawnInterval = 0.01f;

        readonly EnemyFactory factory;
        readonly EnemyRegistry enemies;

        // The one mutable dependency here, and the level swap is why: this object outlives every
        // map, so the path it sends enemies along changes under it. Rebound rather than rebuilt,
        // because rebuilding would mean WaveState holding a runner it has to replace -- and the
        // constructor validation below is the check that would then run per swap anyway.
        IReadOnlyList<Vector2> waypoints;

        WaveDefinition wave;
        int groupIndex;
        int spawnedInGroup;
        float timer;
        bool running;

        public WaveRunner(
            EnemyFactory factory, EnemyRegistry enemies, IReadOnlyList<Vector2> waypoints)
        {
            this.factory = factory ?? throw new ArgumentNullException(nameof(factory));
            this.enemies = enemies ?? throw new ArgumentNullException(nameof(enemies));
            this.waypoints = waypoints ?? throw new ArgumentNullException(nameof(waypoints));

            if (waypoints.Count < 2)
            {
                throw new ArgumentException(
                    "A wave needs a path of at least two waypoints to send enemies along.",
                    nameof(waypoints));
            }
        }

        // Point the runner at another map's path. Refused mid-wave rather than applied late: the
        // enemies already walking hold the array they were configured with, so a swap during a
        // wave would leave two paths live at once with only one of them visible on the board. It
        // cannot happen today -- a level changes on victory, which requires the wave to be cleared
        // -- so the guard is what keeps that ordering a fact rather than a convention.
        public bool Bind(IReadOnlyList<Vector2> path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path));
            }

            if (running)
            {
                Debug.LogError(
                    $"WaveRunner was asked to change path during wave {WaveIndex}, and refused. "
                    + "A level swap belongs between waves.");
                return false;
            }

            if (path.Count < 2)
            {
                Debug.LogError(
                    "WaveRunner was given a path of fewer than two waypoints, and kept the one it "
                    + "had. Enemies would have nowhere to walk.");
                return false;
            }

            waypoints = path;
            return true;
        }

        /// <summary>The index passed to <see cref="StartWave"/>, or -1 before the first one.</summary>
        public int WaveIndex { get; private set; } = -1;

        /// <summary>True between <see cref="StartWave"/> and the wave being cleared.</summary>
        public bool IsRunning => running;

        // Everything authored has been spawned and nothing is still alive. WaveState polls this
        // rather than subscribing to WaveCompleted: it holds this object by construction, so
        // subscribing in order to mirror a value it can read is the Observer misuse §6 warns
        // about -- the same call §13.2 made when it moved CurrencyChanged off BuildController.
        //
        // The event still publishes, because the HUD is a genuine one-to-many consumer.
        public bool IsCleared => wave != null && AllSpawned && enemies.Active.Count == 0;

        bool AllSpawned => groupIndex >= wave.Groups.Count;

        public void StartWave(WaveDefinition definition, int index)
        {
            if (definition == null)
            {
                throw new ArgumentNullException(nameof(definition));
            }

            // A wave with nothing to spawn is cleared on its first tick, so it would complete
            // instantly and the sequence would look like it skipped a step. Said out loud instead,
            // the way ProjectileFactory.Create refuses an unknown prefab.
            if (definition.TotalSpawns == 0)
            {
                Debug.LogError(
                    $"WaveRunner was given wave {index} ('{definition.name}') with nothing to "
                    + "spawn, so it completes immediately. Check its groups for a missing enemy "
                    + "or a count of zero.");
            }

            WarnOnNonPositiveIntervals(definition, index);

            wave = definition;
            WaveIndex = index;
            groupIndex = 0;
            spawnedInGroup = 0;

            // Zero, not the first group's interval: a wave's opening enemy arrives on the tick
            // that starts it rather than one interval later, so tapping Go has an immediate
            // consequence. The interval is the gap *between* spawns, which is what it reads as.
            timer = 0f;
            running = true;

            SkipEmptyGroups();
        }

        // Ticking the registry first is §9's ordering unchanged, just relocated: enemies move,
        // and WaveState then scans and fires at the positions they moved to. Spawning after the
        // move means an enemy created this frame waits one frame before advancing, which is the
        // right way round -- the reverse would step it before it was ever drawn at its origin.
        public void Tick(float dt)
        {
            enemies.Tick(dt);

            if (!running)
            {
                return;
            }

            Advance(dt);

            if (!IsCleared)
            {
                return;
            }

            // Cleared once, announced once. `running` is what makes that true rather than hoped
            // for: IsCleared stays true for every later tick, so without this the bus would carry
            // a WaveCompleted per frame until the next wave started.
            running = false;
            EventBus<WaveCompleted>.Publish(new WaveCompleted(WaveIndex));
        }

        void Advance(float dt)
        {
            timer -= dt;

            // A while, not an if: at a 0.1 s interval a long frame can owe more than one spawn,
            // and dropping the surplus would silently shorten the wave. The loop is bounded by
            // the group's count, and MinSpawnInterval is what stops a zero-interval group from
            // making it unbounded.
            while (!AllSpawned && timer <= 0f)
            {
                SpawnGroup group = wave.Groups[groupIndex];
                enemies.Add(factory.Create(group.Enemy, waypoints));
                spawnedInGroup++;
                timer += Mathf.Max(group.SpawnInterval, MinSpawnInterval);

                if (spawnedInGroup >= group.Count)
                {
                    groupIndex++;
                    spawnedInGroup = 0;
                    SkipEmptyGroups();
                }
            }
        }

        // A group with no enemy or a count of zero is authoring noise rather than a pause: giving
        // it meaning would make an empty group a hidden delay knob, which is a field with two
        // jobs. StartWave has already logged if that leaves the wave with nothing at all.
        void SkipEmptyGroups()
        {
            while (groupIndex < wave.Groups.Count)
            {
                SpawnGroup group = wave.Groups[groupIndex];
                if (group.Enemy != null && group.Count > 0)
                {
                    return;
                }

                groupIndex++;
            }
        }

        static void WarnOnNonPositiveIntervals(WaveDefinition definition, int index)
        {
            IReadOnlyList<SpawnGroup> groups = definition.Groups;
            for (int i = 0; i < groups.Count; i++)
            {
                if (groups[i].Count > 1 && groups[i].SpawnInterval < MinSpawnInterval)
                {
                    Debug.LogWarning(
                        $"WaveRunner: wave {index} ('{definition.name}') group {i} has a spawn "
                        + $"interval of {groups[i].SpawnInterval}, clamped to {MinSpawnInterval}. "
                        + "Its enemies will arrive effectively at once.");
                }
            }
        }
    }
}
