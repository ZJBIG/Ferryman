using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ferryman.Characters;
using Ferryman.Voyage;
using Ferryman.World;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ferryman.Tests
{
    public sealed class VoyageLifecycleTests
    {
        private readonly List<ScriptableObject> clones = new List<ScriptableObject>();
        private VoyageDefinition definition;
        private EncounterDefinition encounter;
        private SeaEventDefinition special;
        private CrewDefinition escort;
        private CrewDefinition joining;
        private VoyageController controller;
        private VoyageSaveStore store;
        private string directory;
        private float originalTimeScale;
        private SimulationMode2D originalSimulationMode;
        private Scene mainScene;
        private Transform islandStops;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalTimeScale = Time.timeScale;
            originalSimulationMode = Physics2D.simulationMode;
            Time.timeScale = 1f;
            Physics2D.simulationMode = SimulationMode2D.FixedUpdate;
            foreach (var existing in Object.FindObjectsOfType<VoyageController>(true))
            {
                existing.StopAllCoroutines();
                Object.Destroy(existing.gameObject);
            }
            yield return null;
            yield return null;

            var source = Resources.Load<VoyageDefinition>("Voyage/DefaultVoyage");
            Assert.That(source, Is.Not.Null, "Default voyage resources have not been generated.");
            definition = Clone(source);
            definition.id = "lifecycle-" + Guid.NewGuid().ToString("N");
            definition.sailingSeconds = 0.08f;
            definition.transitionSeconds = 0.04f;
            definition.boardingNoticeSeconds = 0.1f;
            escort = Clone(source.initialPassengers[0]);
            escort.id = "lifecycle-escort";
            escort.coinsReward = 13;
            escort.specialItemsReward = 1;
            joining = Clone(source.passengerPool[0]);
            joining.id = "lifecycle-joining";
            joining.coinsReward = 17;
            joining.specialItemsReward = 2;
            definition.initialPassengers = new[] { escort };
            definition.passengerPool = new[] { joining };

            var encounters = source.randomEncounters.Concat(source.stops.Select(stop => stop.fixedEncounter))
                .Where(item => item != null).ToArray();
            var shooter = encounters.SelectMany(item => item.waves).SelectMany(wave => wave.enemies)
                .Select(group => group.prefab).First(item => item.AttackType == EnemyCharacter.AttackKind.Projectile);
            encounter = Clone(encounters[0]);
            encounter.id = "lifecycle-combat";
            encounter.reward = new RewardData { coins = 20, specialItems = 2 };
            encounter.waves = new[]
            {
                new WaveDefinition { enemies = new[]
                {
                    new EnemySpawn { prefab = shooter, count = 2, spawnPosition = new Vector2(0f, 4f) }
                } },
                new WaveDefinition { enemies = new[]
                {
                    new EnemySpawn { prefab = shooter, count = 1, spawnPosition = new Vector2(0f, 4f) }
                } }
            };
            special = Clone(source.specialEvents.Select(entry => entry.definition)
                .Concat(source.stops.Select(stop => stop.fixedSpecialEvent))
                .First(item => item != null && item.obstaclePrefab.GetComponent<TimedBomb>() == null));
            special.id = "lifecycle-stones";
            special.obstacleCount = 2;
            special.reward = new RewardData { coins = 5, specialItems = 1 };
            definition.randomEncounters = new[] { encounter };
            definition.specialEvents = new[] { new SpecialEventEntry { definition = special, maxAppearances = 1 } };
            definition.enemyEncounterCount = 1;
            definition.specialEventCount = 1;
            definition.stops = source.stops.Select(CopyStop).ToArray();
            Assert.That(definition.stops.Length, Is.EqualTo(5), "Lifecycle fixture expects the five-stop prototype route.");
            definition.stops[1].fixedEncounter = encounter;
            definition.stops[3].fixedSpecialEvent = special;
            definition.stops[2].boardingChance = 1f;
            definition.stops[2].maxBoardingPassengers = 1;

            directory = Path.Combine(Path.GetTempPath(), "FerrymanLifecycleTests", Guid.NewGuid().ToString("N"));
            CreateController();
            // The test controller is present before Bootstrap.Awake, so scenes never open the user's save store.
            yield return SceneManager.LoadSceneAsync("main");
            mainScene = SceneManager.GetActiveScene();
            islandStops = GameObject.Find("Environment").transform.Find("Island Stops");
            Assert.That(islandStops, Is.Not.Null);
            Assert.That(Object.FindObjectOfType<VoyageScenePresentation>().IsUnifiedScene, Is.True);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (controller != null)
            {
                controller.StopAllCoroutines();
                Object.Destroy(controller.gameObject);
            }
            yield return null;
            yield return null;
            foreach (var clone in clones)
                if (clone != null)
                    Object.DestroyImmediate(clone);
            clones.Clear();
            if (directory != null && Directory.Exists(directory))
            {
                string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FerrymanLifecycleTests"))
                    + Path.DirectorySeparatorChar;
                string target = Path.GetFullPath(directory);
                Assert.That(target.StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
                Directory.Delete(target, true);
            }
            Physics2D.simulationMode = originalSimulationMode;
            Time.timeScale = originalTimeScale;
        }

        [UnityTest]
        public IEnumerator WavesPauseVoyagePassengersBoardAndSuccessfulSettlementReturnsToAFullShip()
        {
            Assert.That(controller.StartVoyage(escort, 104), Is.True);
            QuiesceActors();
            yield return WaitFor(() => AtEvent(1), "first combat event");
            controller.Vessel.Boat.ApplyDamage(150f, new Vector2(-0.5f, 0.25f));
            yield return CompleteCombat(1);
            yield return WaitFor(() => controller.StopIndex == 2 && controller.Phase == VoyagePhase.Island
                && controller.Passengers.Count == 2, "automatic boarding at the intermediate island");
            Assert.That(controller.DeliveredPassengers, Is.Zero);
            Assert.That(controller.Passengers.Single(item => item.Definition == joining).DestinationStopIndex, Is.EqualTo(4));
            Assert.That(controller.Passengers.Single(item => item.Definition == escort).DestinationStopIndex, Is.EqualTo(4));
            Assert.That(store.Progress.coins, Is.Zero, "Temporary event rewards were committed before settlement.");

            yield return WaitFor(() => AtEvent(3), "special event");
            var stones = Object.FindObjectsOfType<DestructibleObstacle>();
            Assert.That(stones.Length, Is.EqualTo(special.obstacleCount));
            Camera camera = Camera.main;
            foreach (var stone in stones)
            {
                Assert.That(stone.IsInFlight, Is.True);
                Assert.That(stone.GetComponent<Collider2D>().enabled, Is.False);
                Assert.That(camera.WorldToViewportPoint(stone.transform.position).y, Is.GreaterThan(1f));
                stone.Clear();
                Assert.That(stone.IsCleared, Is.False);
            }
            float initialY = stones[0].transform.position.y;
            yield return AssertEventPaused(3);
            Assert.That(stones[0].transform.position.y, Is.LessThan(initialY));
            yield return WaitFor(() => stones.All(stone => !stone.IsInFlight), "special event objects landed");
            foreach (var stone in stones)
            {
                var hitbox = stone.GetComponent<BoxCollider2D>();
                Assert.That(hitbox.enabled, Is.True);
                Assert.That(controller.Vessel.Boat.Deck.CanPlace(stone.transform.position, hitbox.size, hitbox), Is.True,
                    "An event obstacle overlapped ship fixtures, another obstacle or a character.");
                stone.Clear();
            }
            yield return WaitFor(() => controller.Phase == VoyagePhase.Success, "successful arrival and settlement");
            int coins = encounter.reward.coins + special.reward.coins + escort.coinsReward + joining.coinsReward;
            int items = encounter.reward.specialItems + special.reward.specialItems
                + escort.specialItemsReward + joining.specialItemsReward;
            Assert.That(controller.DeliveredPassengers, Is.EqualTo(2));
            Assert.That(controller.Passengers, Is.Empty);
            Assert.That(controller.Vessel.Boat.CurrentHealth, Is.EqualTo(850f));
            Assert.That(controller.ActiveRun, Is.False);
            Assert.That(store.HasCheckpoint, Is.False);
            var persisted = new VoyageSaveStore(directory);
            Assert.That(persisted.Progress.coins, Is.EqualTo(coins));
            Assert.That(persisted.Progress.specialItems, Is.EqualTo(items));

            controller.ReturnHome();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "return to the departure island");
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("main"));
            Assert.That(controller.StartVoyage(escort, 105), Is.True);
            QuiesceActors();
            Assert.That(controller.Vessel.Boat.CurrentHealth, Is.EqualTo(controller.Vessel.Boat.MaxHealth));
            Assert.That(store.Progress.coins, Is.EqualTo(coins));
            controller.AbandonVoyage();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "cleanup of the second departure");
        }

        [UnityTest]
        public IEnumerator ShipFailureSettlesEarnedRewardsAndAbandoningTheNextRunPreservesPermanentProgress()
        {
            definition.stops[2].boardingChance = 0f;
            store.Settle("prior-completed-run", new RewardData { coins = 7, specialItems = 1 });
            Assert.That(controller.StartVoyage(escort, 206), Is.True);
            QuiesceActors();
            yield return CompleteCombat(1);
            yield return WaitFor(() => AtEvent(3), "the later event before ship destruction");
            var falling = Object.FindObjectsOfType<DestructibleObstacle>();
            Assert.That(falling.Length, Is.EqualTo(special.obstacleCount));
            Assert.That(falling.All(item => item.IsInFlight), Is.True);
            Assert.That(controller.PendingReward.coins, Is.EqualTo(encounter.reward.coins));
            controller.Vessel.Boat.ApplyDamage(controller.Vessel.Boat.MaxHealth, Vector2.zero);
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Failure));
            Assert.That(controller.ActiveRun, Is.False);
            Assert.That(controller.DeliveredPassengers, Is.Zero);
            Assert.That(store.HasCheckpoint, Is.False);
            Assert.That(falling.All(item => item == null || !item.gameObject.activeInHierarchy), Is.True,
                "Ship destruction left airborne event obstacles active.");
            int savedCoins = 7 + encounter.reward.coins;
            int savedItems = 1 + encounter.reward.specialItems;
            Assert.That(new VoyageSaveStore(directory).Progress.coins, Is.EqualTo(savedCoins));
            Assert.That(new VoyageSaveStore(directory).Progress.specialItems, Is.EqualTo(savedItems));

            controller.ReturnHome();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "return after ship failure");
            Assert.That(controller.StartVoyage(escort, 207), Is.True);
            QuiesceActors();
            yield return CompleteCombat(1);
            Assert.That(controller.PendingReward.coins, Is.GreaterThan(0));
            controller.AbandonVoyage();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "active run abandonment");
            var persisted = new VoyageSaveStore(directory);
            Assert.That(persisted.HasCheckpoint, Is.False);
            Assert.That(persisted.Progress.coins, Is.EqualTo(savedCoins));
            Assert.That(persisted.Progress.specialItems, Is.EqualTo(savedItems));
            Assert.That(controller.PendingReward.coins, Is.Zero);
        }

        [UnityTest]
        public IEnumerator ImmediateAndRegionalCheckpointsRestoreTheSamePlanShipPassengersAndTemporaryRewards()
        {
            VoyageStop sea = definition.stops[3];
            definition.stops = new[]
            {
                definition.stops[0], definition.stops[1], definition.stops[2],
                CopyStop(sea), CopyStop(sea), CopyStop(sea), definition.stops[4]
            };
            definition.enemyEncounterCount = 2;
            Assert.That(controller.StartVoyage(escort, 309), Is.True);
            QuiesceActors();
            string plan = PlanSignature();
            VoyageCheckpoint initial = store.LoadCheckpoint(definition.id);
            Assert.That(initial.stopIndex, Is.Zero, "Starting a voyage did not synchronously create its first checkpoint.");
            Assert.That(initial.passengers.Single().destinationStopIndex, Is.EqualTo(6));
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            QuiesceActors();
            Assert.That(PlanSignature(), Is.EqualTo(plan));
            AssertRestored(initial);

            yield return WaitFor(() => AtEvent(1), "combat after restoring the immediate checkpoint");
            controller.Vessel.Player.GetComponent<Rigidbody2D>().position = new Vector2(2.5f, -0.5f);
            Physics2D.SyncTransforms();
            controller.Vessel.Boat.ApplyDamage(150f, new Vector2(-0.5f, 0.25f));
            RepairPoint point = controller.Vessel.Boat.RepairPoints.Single();
            Assert.That(point.BeginRepair(controller.Vessel.Player), Is.True);
            point.AddProgress(controller.Vessel.Player, 30f);
            point.EndRepair(controller.Vessel.Player);
            yield return CompleteCombat(1);
            yield return WaitFor(() => controller.StopIndex == 2 && controller.Phase == VoyagePhase.Island
                && controller.Passengers.Count == 2 && store.LoadCheckpoint(definition.id).stopIndex == 2,
                "regional checkpoint after boarding");
            controller.StopAllCoroutines();
            VoyageCheckpoint regional = store.LoadCheckpoint(definition.id);
            Assert.That(regional.ship.health, Is.EqualTo(850f));
            Assert.That(regional.ship.pendingDamage, Is.EqualTo(50f));
            Assert.That(regional.ship.points.Single().progress, Is.EqualTo(30f));
            Assert.That(regional.pendingCoins, Is.EqualTo(encounter.reward.coins));
            Assert.That(regional.pendingSpecialItems, Is.EqualTo(encounter.reward.specialItems));
            Assert.That(regional.passengers.Length, Is.EqualTo(2));
            Assert.That(Vector2.Distance(regional.playerLocalPosition, new Vector2(2.5f, -0.5f)), Is.LessThan(0.001f));

            // Changes after the regional save must be rolled back when the controller is lost.
            controller.Vessel.Boat.ApplyDamage(40f, Vector2.zero);
            controller.Vessel.Player.GetComponent<Rigidbody2D>().position = new Vector2(-1.5f, -0.5f);
            Physics2D.SyncTransforms();
            point = controller.Vessel.Boat.RepairPoints.Single();
            point.BeginRepair(controller.Vessel.Player);
            point.AddProgress(controller.Vessel.Player, 20f);
            point.EndRepair(controller.Vessel.Player);
            Assert.That(controller.Vessel.Boat.CurrentHealth, Is.EqualTo(810f));
            Assert.That(point.Progress, Is.EqualTo(50f));
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            QuiesceActors();
            Assert.That(PlanSignature(), Is.EqualTo(plan));
            AssertRestored(regional);
            yield return WaitFor(() => controller.StopIndex == 2 && controller.Phase == VoyagePhase.Island,
                "entry into the restored region");
            AssertRestored(regional);
            Assert.That(store.LoadCheckpoint(definition.id).runId, Is.EqualTo(regional.runId));
            Assert.That(store.Progress.coins, Is.Zero, "Resuming a checkpoint prematurely committed temporary rewards.");
            controller.AbandonVoyage();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "cleanup of the restored voyage");

            Vector2 oldCabinPosition = new Vector2(-5.5f, 0f);
            regional.playerLocalPosition = oldCabinPosition;
            regional.passengers[0].localPosition = oldCabinPosition + Vector2.up;
            regional.ship.points[0].localPosition = oldCabinPosition;
            regional.ship.points[0].cell = new Vector2Int(0, 2);
            store.SaveCheckpoint(regional);
            Assert.That(controller.ResumeVoyage(), Is.True);
            QuiesceActors();
            BoatDeck deck = controller.Vessel.Boat.Deck;
            foreach (Character actor in controller.Passengers.Select(item => item.Actor).Append(controller.Vessel.Player))
                Assert.That(deck.HasClearFloor(actor.transform.position, actor.GetComponent<BoxCollider2D>().size), Is.True);
            RepairPoint migrated = controller.Vessel.Boat.RepairPoints.Single();
            Assert.That(migrated.Progress, Is.EqualTo(30f));
            Assert.That(deck.HasClearFloor(migrated.transform.position, new Vector2(1.96f, 1.54f)), Is.True);
            Assert.That(controller.Vessel.Boat.CurrentHealth, Is.EqualTo(850f));
            controller.AbandonVoyage();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "cleanup of the migrated voyage");
        }

        private IEnumerator CompleteCombat(int stop)
        {
            yield return WaitFor(() => AtEvent(stop) && controller.WaveIndex == 0 && LiveEnemies().Length == 2,
                "first wave at stop " + stop);
            RewardData before = controller.PendingReward;
            yield return AssertEventPaused(stop);
            Kill(LiveEnemies()[0]);
            yield return null;
            QuiesceActors();
            Assert.That(controller.WaveIndex, Is.Zero, "A new wave appeared before every enemy in the prior wave died.");
            Assert.That(LiveEnemies().Length, Is.EqualTo(1));
            Kill(LiveEnemies()[0]);
            yield return WaitFor(() => AtEvent(stop) && controller.WaveIndex == 1 && LiveEnemies().Length == 1,
                "second wave at stop " + stop);
            Assert.That(controller.PendingReward.coins, Is.EqualTo(before.coins));
            Assert.That(controller.PendingReward.specialItems, Is.EqualTo(before.specialItems));
            Kill(LiveEnemies()[0]);
            yield return WaitFor(() => !AtEvent(stop), "completion of combat at stop " + stop);
            Assert.That(controller.PendingReward.coins, Is.EqualTo(before.coins + encounter.reward.coins));
            Assert.That(controller.PendingReward.specialItems, Is.EqualTo(before.specialItems + encounter.reward.specialItems));
        }

        private IEnumerator AssertEventPaused(int stop)
        {
            QuiesceActors();
            var presentation = Object.FindObjectOfType<VoyageScenePresentation>();
            Assert.That(presentation, Is.Not.Null);
            Transform motion = GameObject.Find("Environment/Sea Motion")?.transform;
            Assert.That(motion, Is.Not.Null);
            Vector3 position = motion.localPosition;
            float remaining = controller.SailingRemaining;
            yield return new WaitForSeconds(0.12f);
            QuiesceActors();
            Assert.That(AtEvent(stop), Is.True, "The voyage advanced while an event was unresolved.");
            Assert.That(controller.SailingRemaining, Is.EqualTo(remaining));
            Assert.That(motion.localPosition, Is.EqualTo(position), "The sea kept scrolling during an event.");
        }

        private void AssertRestored(VoyageCheckpoint saved)
        {
            Assert.That(Vector2.Distance(controller.Vessel.Player.transform.localPosition, saved.playerLocalPosition),
                Is.LessThan(0.001f));
            Assert.That(controller.Vessel.Boat.CurrentHealth, Is.EqualTo(saved.ship.health));
            Assert.That(controller.Vessel.Boat.PendingDamage, Is.EqualTo(saved.ship.pendingDamage));
            Assert.That(controller.Vessel.Boat.RepairPoints.Count, Is.EqualTo(saved.ship.points.Length));
            foreach (var point in saved.ship.points)
            {
                RepairPoint restored = controller.Vessel.Boat.RepairPoints.Single(item => item.Cell == point.cell);
                Assert.That(restored.Progress, Is.EqualTo(point.progress));
                Assert.That(Vector2.Distance(restored.transform.localPosition, point.localPosition), Is.LessThan(0.001f));
            }
            Assert.That(controller.Passengers.Count, Is.EqualTo(saved.passengers.Length));
            foreach (var passenger in saved.passengers)
            {
                Passenger restored = controller.Passengers.Single(item => item.Definition.id == passenger.profileId);
                Assert.That(restored.DestinationStopIndex, Is.EqualTo(passenger.destinationStopIndex));
                Assert.That(Vector2.Distance(restored.transform.localPosition, passenger.localPosition), Is.LessThan(0.001f));
            }
            Assert.That(controller.PendingReward.coins, Is.EqualTo(saved.pendingCoins));
            Assert.That(controller.PendingReward.specialItems, Is.EqualTo(saved.pendingSpecialItems));
            Assert.That(controller.DeliveredPassengers, Is.EqualTo(saved.deliveredPassengers));
        }

        private IEnumerator DestroyForCrash()
        {
            controller.StopAllCoroutines();
            Object.Destroy(controller.gameObject);
            controller = null;
            yield return null;
            yield return null;
            Assert.That(store.HasCheckpoint, Is.True, "Destroying a controller incorrectly abandoned the crash checkpoint.");
        }

        private IEnumerator WaitFor(Func<bool> condition, string description)
        {
            float until = Time.realtimeSinceStartup + 12f;
            while (!condition())
            {
                QuiesceActors();
                if (controller != null && controller.Phase == VoyagePhase.Error)
                    Assert.Fail("Waiting for " + description + ": " + DescribeState());
                if (Time.realtimeSinceStartup >= until)
                    Assert.Fail("Timed out waiting for " + description + ": " + DescribeState());
                yield return null;
            }
            QuiesceActors();
        }

        private void QuiesceActors()
        {
            Assert.That(SceneManager.GetActiveScene(), Is.EqualTo(mainScene), "The complete voyage reloaded or left main.");
            if (controller == null)
                return;
            if (controller.Phase == VoyagePhase.Departure || controller.Phase == VoyagePhase.Island
                || controller.Phase == VoyagePhase.Sailing || controller.Phase == VoyagePhase.Event
                || controller.Phase == VoyagePhase.Success || controller.Phase == VoyagePhase.Failure)
            {
                VoyageStop stop = definition.stops[controller.StopIndex];
                bool island = stop.kind == VoyageStopKind.Island;
                Assert.That(islandStops.gameObject.activeSelf, Is.EqualTo(island));
                foreach (Transform visual in islandStops)
                    Assert.That(visual.gameObject.activeSelf, Is.EqualTo(island && visual.name == stop.sceneName),
                        "The wrong island remains visible: " + visual.name);
            }
            if (controller.Vessel != null)
                foreach (var actor in controller.Vessel.GetComponentsInChildren<Character>())
                    actor.enabled = false;
            foreach (var enemy in controller.Enemies)
                if (enemy != null)
                    enemy.enabled = false;
        }

        private void Kill(EnemyCharacter enemy)
        {
            Assert.That(controller.Vessel.DamageSystem.ApplyDamage(controller.Vessel.Player, enemy, enemy.Health), Is.True);
        }

        private bool AtEvent(int stop) => controller.StopIndex == stop && controller.Phase == VoyagePhase.Event;
        private EnemyCharacter[] LiveEnemies() => controller.Enemies.Where(item => item != null && item.Health > 0f).ToArray();

        private string PlanSignature() => string.Join("|", controller.EventPlan.Select(item =>
            item.kind + ":" + item.encounter?.id + ":" + item.specialEvent?.id + ":" + item.isFixed));

        private string DescribeState() => controller == null ? "controller destroyed"
            : $"phase={controller.Phase}, stop={controller.StopIndex}, wave={controller.WaveIndex}, "
                + $"enemies={LiveEnemies().Length}, passengers={controller.Passengers.Count}, message={controller.Message}";

        private void CreateController()
        {
            store = new VoyageSaveStore(directory);
            controller = new GameObject("Lifecycle Test Voyage").AddComponent<VoyageController>();
            controller.Initialize(definition, store);
        }

        private T Clone<T>(T source) where T : ScriptableObject
        {
            T clone = Object.Instantiate(source);
            clones.Add(clone);
            return clone;
        }

        private static VoyageStop CopyStop(VoyageStop stop) => new VoyageStop
        {
            kind = stop.kind,
            displayName = stop.displayName,
            sceneName = stop.sceneName,
            boardingChance = 0f,
            maxBoardingPassengers = 0
        };
    }
}
