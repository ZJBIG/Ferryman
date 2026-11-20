using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Ferryman.Characters;
using Ferryman.Combat;
using Ferryman.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ferryman.Tests
{
    public sealed class CrewBehaviourTests
    {
        private BoatHealth boat;
        private PlayerCharacter player;
        private EnemyCharacter enemy;
        private SimulationMode2D originalSimulationMode;
        private readonly List<CrewDefinition> definitions = new List<CrewDefinition>();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalSimulationMode = Physics2D.simulationMode;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            yield return SceneManager.LoadSceneAsync("Prototype");
            boat = Object.FindObjectOfType<BoatHealth>();
            player = Object.FindObjectOfType<PlayerCharacter>();
            enemy = Object.FindObjectOfType<EnemyCharacter>();
            Assert.That(boat, Is.Not.Null);
            Assert.That(player, Is.Not.Null);
            Assert.That(enemy, Is.Not.Null);
            foreach (var actor in Object.FindObjectsOfType<EnemyCharacter>())
                actor.enabled = false;
            foreach (var obstacle in Object.FindObjectsOfType<DestructibleObstacle>())
                obstacle.gameObject.SetActive(false);
            foreach (var projectile in Object.FindObjectsOfType<Projectile>())
                Object.Destroy(projectile.gameObject);
            player.enabled = false;
            player.GetComponent<Rigidbody2D>().position = new Vector2(5.4f, -0.8f);
            player.Move(Vector2.zero);
            Physics2D.SyncTransforms();
            yield return null;
        }

        [TearDown]
        public void TearDown()
        {
            Physics2D.simulationMode = originalSimulationMode;
            foreach (var definition in definitions)
                Object.DestroyImmediate(definition);
            definitions.Clear();
        }

        [UnityTest]
        public IEnumerator MaintenanceCrewFindsRemoteDamageAndWalksAroundSolidObstacles()
        {
            var stone = Object.FindObjectsOfType<DestructibleObstacle>(true)
                .First(item => item.GetComponent<TimedBomb>() == null);
            stone.transform.position = Vector2.zero;
            stone.gameObject.SetActive(true);
            Vector2Int damagedCell = new Vector2Int(1, 1);
            boat.ApplyDamage(100f, boat.Deck.CellToWorld(damagedCell));
            RepairPoint point = boat.RepairPoints.Single();
            MaintenanceCrewMember crew = CreateMaintenance(new Vector2(4.5f, 0f), 4f, 10f, 1);
            Physics2D.SyncTransforms();
            float largestY = 0f;
            float until = Time.realtimeSinceStartup + 5f;
            while (point.Progress <= 0f && Time.realtimeSinceStartup < until)
            {
                yield return null;
                largestY = Mathf.Max(largestY, Mathf.Abs(crew.transform.position.y));
                Bounds boundary = boat.Deck.GetComponent<EdgeCollider2D>().bounds;
                Bounds box = crew.GetComponent<BoxCollider2D>().bounds;
                Assert.That(box.min.x, Is.GreaterThanOrEqualTo(boundary.min.x - 0.02f));
                Assert.That(box.max.x, Is.LessThanOrEqualTo(boundary.max.x + 0.02f));
                Assert.That(box.min.y, Is.GreaterThanOrEqualTo(boundary.min.y - 0.02f));
                Assert.That(box.max.y, Is.LessThanOrEqualTo(boundary.max.y + 0.02f));
                Assert.That(Physics2D.Distance(crew.GetComponent<Collider2D>(), stone.GetComponent<Collider2D>()).distance,
                    Is.GreaterThanOrEqualTo(-0.02f), "Navigation passed through a solid obstacle.");
            }
            Assert.That(point.Progress, Is.GreaterThan(0f), "Crew did not reach damage outside its initial repair range.");
            Assert.That(largestY, Is.GreaterThan(0.7f), "Crew did not take the free upper or lower row around the stone.");
            Assert.That(crew.GetComponent<CombatCharacter>(), Is.Null);
            Assert.That(Object.FindObjectsOfType<Projectile>().Length, Is.Zero);
            Assert.That(boat.CurrentHealth, Is.EqualTo(900f));
            crew.SetActionsAllowed(false);
            float saved = point.Progress;
            yield return new WaitForSeconds(0.1f);
            Assert.That(point.Progress, Is.EqualTo(saved));
            Assert.That(point.IsBeingRepaired, Is.False);
        }

        [UnityTest]
        public IEnumerator CrewJobsStaySeparateAndResumeAfterStun()
        {
            foreach (var actor in Object.FindObjectsOfType<EnemyCharacter>())
                if (actor != enemy)
                    actor.gameObject.SetActive(false);
            enemy.transform.position = new Vector2(0f, 3f);
            boat.ApplyDamage(100f, new Vector2(-1.5f, 0f));
            RepairPoint point = boat.RepairPoints.Single();
            var fighterObject = new GameObject("Test Combat Crew");
            fighterObject.transform.position = new Vector2(-1.5f, 0.85f);
            var fighter = fighterObject.AddComponent<CombatCrewMember>();
            fighter.GetComponent<BoxCollider2D>().size = new Vector2(0.45f, 0.7f);
            CrewDefinition fighterDefinition = Definition(CrewRole.Combat);
            fighterDefinition.attackPower = 30f;
            fighterDefinition.fireInterval = 0.2f;
            fighter.InitializeCrew(boat, player.DamageSystem, player.ProjectilePrefab, fighterDefinition);
            Physics2D.SyncTransforms();
            Assert.That(fighter.TryReceiveDamage(10f), Is.True);
            yield return new WaitForSeconds(0.3f);
            Assert.That(fighter.IsStunned, Is.True);
            Assert.That(Object.FindObjectsOfType<Projectile>().Length, Is.Zero);
            Assert.That(point.Progress, Is.Zero);
            yield return WaitFor(() => enemy == null || enemy.Health < 100f, 2f,
                "Combat crew did not resume shooting after stun.");
            Assert.That(fighter.IsStunned, Is.False);
            Assert.That(fighter.Health, Is.EqualTo(100f));
            Assert.That(fighter.GetComponent<MaintenanceCrewMember>(), Is.Null);
            Assert.That(point.Progress, Is.Zero, "Combat crew repaired the boat.");
            fighter.SetActionsAllowed(false);

            MaintenanceCrewMember mechanic = CreateMaintenance(new Vector2(-1.5f, 0f), 2f, 25f, 1);
            Physics2D.SyncTransforms();
            yield return WaitFor(() => point.Progress > 0f, 1f, "Maintenance crew did not begin repair.");
            Assert.That(mechanic.TryReceiveDamage(10f), Is.True);
            float saved = point.Progress;
            Assert.That(mechanic.IsRepairing, Is.False);
            Assert.That(point.IsBeingRepaired, Is.False);
            yield return new WaitForSeconds(0.4f);
            Assert.That(point.Progress, Is.EqualTo(saved));
            Assert.That(mechanic.TryReceiveDamage(10f), Is.False);
            yield return WaitFor(() => !mechanic.IsStunned && point.Progress > saved, 1f,
                "Maintenance crew did not resume its saved task after stun.");
            Assert.That(mechanic.Health, Is.EqualTo(100f));
            Assert.That(mechanic.GetComponent<CombatCharacter>(), Is.Null);
            boat.ApplyDamage(10000f, Vector2.zero);
            Assert.That(mechanic.IsRepairing, Is.False);
            Assert.That(mechanic.GetComponent<Rigidbody2D>().velocity, Is.EqualTo(Vector2.zero));
            Assert.That(fighter.TryFire(Vector2.up), Is.False);
        }

        [UnityTest]
        public IEnumerator MultipleCrewAndPlayerStackProgressWithoutSplittingParallelSpeed()
        {
            Vector2Int origin = new Vector2Int(6, 1);
            boat.ApplyDamage(100f, boat.Deck.CellToWorld(origin));
            boat.ApplyDamage(100f, boat.Deck.CellToWorld(origin + Vector2Int.up));
            RepairPoint first = boat.RepairPoints.Single(point => point.Cell == origin);
            RepairPoint second = boat.RepairPoints.Single(point => point.Cell != origin);
            first.BeginRepair(enemy);
            first.AddProgress(enemy, 20f);
            first.EndRepair(enemy);
            player.GetComponent<Rigidbody2D>().position = boat.Deck.CellToWorld(origin);
            MaintenanceCrewMember left = CreateMaintenance(new Vector2(-0.5f, 0f), 2f, 10f, 2);
            MaintenanceCrewMember right = CreateMaintenance(new Vector2(1.5f, 0f), 2f, 20f, 2);
            Physics2D.SyncTransforms();
            yield return WaitFor(() => left.IsRepairing && right.IsRepairing, 1f, "Crew did not cooperate on shared repair points.");
            float firstBefore = first.Progress;
            float secondBefore = second.Progress;
            float started = Time.time;
            yield return new WaitForSeconds(0.2f);
            float elapsed = Time.time - started;
            Assert.That(first.Progress - firstBefore, Is.EqualTo(second.Progress - secondBefore).Within(0.01f));
            Assert.That(second.Progress - secondBefore, Is.EqualTo(30f * elapsed).Within(30f * Time.deltaTime + 0.02f),
                "Each of the two parallel points should receive both crew members' full speeds.");

            firstBefore = first.Progress;
            secondBefore = second.Progress;
            float playerTime = 0f;
            float until = Time.time + 0.2f;
            while (Time.time < until)
            {
                yield return null;
                Assert.That(player.TryRepair(Time.deltaTime), Is.True);
                playerTime += Time.deltaTime;
            }
            Assert.That((first.Progress - firstBefore) - (second.Progress - secondBefore),
                Is.EqualTo(50f * playerTime).Within(0.02f), "Player speed did not add to the crew's shared point.");
            int heals = 0;
            boat.HealthChanged.AddListener((current, maximum) => heals++);
            until = Time.realtimeSinceStartup + 2f;
            while (first != null && !first.IsComplete && Time.realtimeSinceStartup < until)
            {
                yield return null;
                player.TryRepair(Time.deltaTime);
            }
            player.StopRepairing();
            left.SetActionsAllowed(false);
            right.SetActionsAllowed(false);
            Assert.That(first == null || first.IsComplete, Is.True);
            Assert.That(boat.CurrentHealth, Is.EqualTo(900f));
            Assert.That(heals, Is.EqualTo(1), "A shared point paid out more than once.");
            Assert.That(boat.RepairPoints.Count, Is.EqualTo(1));
            Assert.That(second.Progress, Is.GreaterThan(0f).And.LessThan(100f));
            Assert.That(second.IsBeingRepaired, Is.False);
        }

        private MaintenanceCrewMember CreateMaintenance(Vector2 position, float movement, float repair, int parallel)
        {
            var root = new GameObject("Test Maintenance Crew");
            root.transform.position = position;
            var crew = root.AddComponent<MaintenanceCrewMember>();
            crew.GetComponent<BoxCollider2D>().size = new Vector2(0.45f, 0.7f);
            CrewDefinition definition = Definition(CrewRole.Maintenance);
            definition.moveSpeed = movement;
            definition.repairSpeed = repair;
            definition.simultaneousRepairs = parallel;
            crew.InitializeCrew(boat, definition);
            return crew;
        }

        private CrewDefinition Definition(CrewRole role)
        {
            var definition = ScriptableObject.CreateInstance<CrewDefinition>();
            definition.role = role;
            definitions.Add(definition);
            return definition;
        }

        private static IEnumerator WaitFor(Func<bool> condition, float timeout, string message)
        {
            float deadline = Time.realtimeSinceStartup + timeout;
            while (!condition() && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(condition(), Is.True, message);
        }
    }
}
