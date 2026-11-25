using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Ferryman.Characters;
using Ferryman.Combat;
using Ferryman.Voyage;
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
            player.GetComponent<Rigidbody2D>().interpolation = RigidbodyInterpolation2D.None;
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
        public IEnumerator IdleCrewWanderInsideDeckAndStopWhenActionsAreDisabled()
        {
            foreach (var actor in Object.FindObjectsOfType<EnemyCharacter>())
                actor.gameObject.SetActive(false);
            var profile = Resources.Load<CrewDefinition>("Voyage/Crew/gunner");
            var fighter = (CombatCrewMember)Object.Instantiate(profile.prefab, Vector2.zero, Quaternion.identity);
            fighter.InitializeCrew(boat, player.DamageSystem, profile);
            var mechanic = CreateMaintenance(new Vector2(2f, 0f), 2f, 10f, 1);
            Vector2 fighterStart = fighter.transform.position;
            Vector2 mechanicStart = mechanic.transform.position;
            float until = Time.realtimeSinceStartup + 3f;
            bool fighterMoved = false;
            bool mechanicMoved = false;
            while ((!fighterMoved || !mechanicMoved) && Time.realtimeSinceStartup < until)
            {
                yield return null;
                fighterMoved |= Vector2.Distance(fighter.transform.position, fighterStart) > 0.25f;
                mechanicMoved |= Vector2.Distance(mechanic.transform.position, mechanicStart) > 0.25f;
                foreach (var actor in new Character[] { fighter, mechanic })
                    Assert.That(boat.Deck.HasClearFloor(actor.Hitbox.bounds.center,
                        (Vector2)actor.Hitbox.bounds.size - Vector2.one * 0.04f), Is.True);
            }
            Assert.That(fighterMoved && mechanicMoved, Is.True, "Idle crew did not wander.");
            fighter.SetActionsAllowed(false);
            mechanic.SetActionsAllowed(false);
            Vector2 fighterStopped = fighter.transform.position;
            Vector2 mechanicStopped = mechanic.transform.position;
            yield return new WaitForSeconds(0.1f);
            Assert.That((Vector2)fighter.transform.position, Is.EqualTo(fighterStopped));
            Assert.That((Vector2)mechanic.transform.position, Is.EqualTo(mechanicStopped));
        }

        [UnityTest]
        public IEnumerator KnockbackInterruptsMaintenanceAndResumesWithoutStun()
        {
            boat.ApplyDamage(100f, Vector2.zero);
            RepairPoint point = boat.RepairPoints.Single();
            var mechanic = CreateMaintenance(new Vector2(0.8f, 0f), 2f, 10f, 1);
            yield return WaitFor(() => mechanic.IsRepairing, 1f, "Maintenance crew did not begin repair.");
            float progress = point.Progress;
            Vector2 position = mechanic.transform.position;
            Assert.That(mechanic.TryKnockBack(Vector2.right * 8f), Is.True);
            Assert.That(mechanic.IsRepairing || point.IsBeingRepaired || mechanic.IsStunned, Is.False);
            yield return new WaitForSeconds(0.12f);
            Assert.That(point.Progress, Is.EqualTo(progress));
            Assert.That(mechanic.transform.position.x, Is.GreaterThan(position.x + 0.2f));
            yield return WaitFor(() => point.Progress > progress, 2f, "Maintenance did not resume after knockback.");
            Assert.That(mechanic.IsStunned, Is.False);
            Assert.That(mechanic.Health, Is.EqualTo(100f));
        }

        [UnityTest]
        public IEnumerator MaintenanceCrewFindsRemoteDamageAndWalksAroundSolidObstacles()
        {
            var stone = Object.FindObjectsOfType<DestructibleObstacle>(true)
                .First(item => item.GetComponent<TimedBomb>() == null);
            stone.transform.position = Vector2.zero;
            stone.gameObject.SetActive(true);
            Vector2Int damagedCell = new Vector2Int(3, 1);
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
            crew.SetActionsAllowed(false);
            float saved = point.Progress;
            yield return new WaitForSeconds(0.1f);
            Assert.That(point.Progress, Is.EqualTo(saved));
            Assert.That(point.IsBeingRepaired, Is.False);
        }

        [UnityTest]
        public IEnumerator ShootingEnemiesChooseAvailableCrewAndPlayerByPreference()
        {
            var encounter = Resources.Load<EncounterDefinition>("Voyage/Events/CoastalRaid");
            var hunterPrefab = encounter.waves.SelectMany(wave => wave.enemies).Select(group => group.prefab)
                .First(prefab => prefab.PreferredTarget == EnemyCharacter.TargetPreference.CrewFirst);
            Vector2 origin = new Vector2(3f, 6f);
            var hunter = Object.Instantiate(hunterPrefab, origin, Quaternion.identity);
            hunter.enabled = false;
            enemy.transform.position = origin;
            var crewObject = new GameObject("Targetable Crew");
            crewObject.transform.position = new Vector2(3f, 1f);
            var crew = crewObject.AddComponent<CrewMember>();
            var playerBody = player.GetComponent<Rigidbody2D>();
            playerBody.position = new Vector2(1f, 0f);
            yield return null;

            var selectTarget = typeof(EnemyCharacter).GetMethod("SelectTarget", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(selectTarget.Invoke(enemy, null), Is.SameAs(crew));
            playerBody.position = new Vector2(3f, 2f);
            player.transform.position = playerBody.position;
            Physics2D.SyncTransforms();
            Assert.That(selectTarget.Invoke(enemy, null), Is.SameAs(player));
            Assert.That(selectTarget.Invoke(hunter, null), Is.SameAs(crew));

            Assert.That(crew.TryReceiveDamage(1f), Is.True);
            Assert.That(selectTarget.Invoke(hunter, null), Is.SameAs(player));
            Assert.That(player.TryReceiveDamage(1f), Is.True);
            Assert.That(selectTarget.Invoke(enemy, null), Is.Null);
            Assert.That(selectTarget.Invoke(hunter, null), Is.Null);
        }

        [UnityTest]
        public IEnumerator MaintenanceCrewWalksIntoBowToRepairDamage()
        {
            player.GetComponent<Rigidbody2D>().position = Vector2.zero;
            var impact = new Vector2(7f, 0f);
            boat.ApplyDamage(100f, impact);
            RepairPoint point = boat.RepairPoints.Single();
            Assert.That(point.Cell.x, Is.GreaterThanOrEqualTo(12));
            MaintenanceCrewMember crew = CreateMaintenance(new Vector2(3.5f, 0f), 4f, 10f, 1);
            Physics2D.SyncTransforms();
            yield return WaitFor(() => point.Progress > 0f, 4f, "Maintenance crew did not reach the bow repair point.");
            Assert.That(BoatDeck.CellDistance(boat.Deck.WorldToCell(crew.transform.position), point.Cell),
                Is.LessThanOrEqualTo(1), "Maintenance crew began repair outside its one-cell range.");
        }

        [UnityTest]
        public IEnumerator CrewJobsStaySeparateAndResumeAfterStun()
        {
            var snapshot = SceneCharacters();
            Assert.That(snapshot, Does.Contain(player), "Disabled scripts must remain valid combat targets.");
            Assert.That(snapshot, Does.Contain(enemy), "Disabled scripts must remain valid combat targets.");
            Assert.That(SceneCharacters(), Is.SameAs(snapshot), "Actors repeated the scene search in the same frame.");
            foreach (var actor in Object.FindObjectsOfType<EnemyCharacter>())
                if (actor != enemy)
                    actor.gameObject.SetActive(false);
            enemy.transform.position = new Vector2(0f, 3f);
            boat.ApplyDamage(100f, new Vector2(-1.5f, 0f));
            RepairPoint point = boat.RepairPoints.Single();
            var fighterPrefab = Resources.Load<CrewDefinition>("Voyage/Crew/gunner").prefab as CombatCrewMember;
            var fighter = Object.Instantiate(fighterPrefab, new Vector2(-1.5f, 0.85f), Quaternion.identity);
            CrewDefinition fighterDefinition = Definition(CrewRole.Combat);
            fighterDefinition.attackPower = 30f;
            fighterDefinition.fireInterval = 0.2f;
            fighter.InitializeCrew(boat, player.DamageSystem, fighterDefinition);
            Physics2D.SyncTransforms();
            Assert.That(fighter.TryReceiveDamage(10f), Is.True);
            yield return new WaitForSeconds(0.3f);
            var nextSnapshot = SceneCharacters();
            Assert.That(nextSnapshot, Is.Not.SameAs(snapshot));
            Assert.That(nextSnapshot, Does.Contain(fighter), "A newly boarded combat crew member did not enter the next snapshot.");
            Assert.That(fighter.IsStunned, Is.True);
            Assert.That(Object.FindObjectsOfType<Projectile>().Length, Is.Zero);
            Assert.That(point.Progress, Is.Zero);
            yield return WaitFor(() => enemy == null || enemy.Health < 100f, 2f,
                "Combat crew did not resume shooting after stun.");
            Assert.That(fighter.IsStunned, Is.False);
            Assert.That(fighter.Health, Is.EqualTo(100f));
            Assert.That(point.Progress, Is.Zero, "Combat crew repaired the boat.");
            fighter.SetActionsAllowed(false);

            Vector2 stance = new Vector2(-0.5f, 0f);
            MaintenanceCrewMember mechanic = CreateMaintenance(stance, 2f, 25f, 1);
            Physics2D.SyncTransforms();
            yield return WaitFor(() => point.Progress > 0f, 1f, "Maintenance crew did not begin repair.");
            Assert.That(Vector2.Distance(mechanic.transform.position, stance), Is.LessThan(0.01f),
                "Crew walked onto the damage even though it was already in repair range.");
            Transform bar = point.transform.Find("Progress Bar");
            Transform fill = bar.Find("Fill");
            Assert.That(bar.gameObject.activeInHierarchy, Is.True, "Crew-only repair did not display its progress bar.");
            Assert.That(fill.localScale.x, Is.EqualTo(point.Progress / point.RequiredProgress).Within(0.001f));
            Assert.That(mechanic.TryReceiveDamage(10f), Is.True);
            float saved = point.Progress;
            Assert.That(mechanic.IsRepairing, Is.False);
            Assert.That(point.IsBeingRepaired, Is.False);
            Assert.That(bar.gameObject.activeSelf, Is.False);
            yield return new WaitForSeconds(0.4f);
            Assert.That(point.Progress, Is.EqualTo(saved));
            Assert.That(mechanic.TryReceiveDamage(10f), Is.False);
            yield return WaitFor(() => !mechanic.IsStunned && point.Progress > saved, 1f,
                "Maintenance crew did not resume its saved task after stun.");
            Assert.That(bar.gameObject.activeInHierarchy, Is.True);
            Assert.That(fill.localScale.x, Is.EqualTo(point.Progress / point.RequiredProgress).Within(0.001f));
            Assert.That(mechanic.Health, Is.EqualTo(100f));
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

        private static Character[] SceneCharacters()
        {
            return (Character[])typeof(Character).GetProperty("SceneCharacters", BindingFlags.Static | BindingFlags.NonPublic)
                .GetValue(null);
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
