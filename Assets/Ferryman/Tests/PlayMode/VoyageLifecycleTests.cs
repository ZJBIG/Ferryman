using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
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
            definition.branchingRoutes = false;
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
            encounter.waveInterval = 0.5f;
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
        public IEnumerator VoyageAudioChangesScoreAndStopsInterruptedRepairAndFuse()
        {
            Type audio = typeof(VoyageController).Assembly.GetType("Ferryman.Voyage.VoyageAudio", true);
            controller.gameObject.AddComponent(audio);
            yield return WaitFor(() => FindCue(controller.gameObject, "Harbor")?.volume > 0f, "harbor music");
            Assert.That(FindCue(controller.gameObject, "Harbor").loop, Is.True);

            yield return StartVoyageWithLegacyPassenger(63);
            controller.gameObject.AddComponent(audio);
            yield return WaitFor(() => FindCue(controller.gameObject, "Sailing")?.volume > 0f, "sailing music");
            yield return WaitFor(() => FindCue(controller.gameObject, "Battle")?.volume > 0f
                && FindCue(controller.gameObject, "WaterBubbles") != null, "battle music and first wave cue");

            BoatHealth boat = controller.Vessel.Boat;
            Assert.That(boat.ApplyDamage(100f, boat.Deck.transform.position), Is.True);
            RepairPoint point = boat.RepairPoints.Single();
            var crew = controller.Passengers[0].Actor;
            Assert.That(point.BeginRepair(controller.Vessel.Player), Is.True);
            Assert.That(point.BeginRepair(crew), Is.True);
            Assert.That(point.GetComponents<AudioSource>().Length, Is.EqualTo(1));
            AudioSource repair = FindCue(point.gameObject, "Repair");
            Assert.That(repair, Is.Not.Null);
            point.EndRepair(controller.Vessel.Player);
            Assert.That(repair.isPlaying, Is.True);
            point.EndRepair(crew);
            Assert.That(repair.isPlaying, Is.False);

            GameObject prefab = Resources.Load<SeaEventDefinition>("Voyage/Events/DriftingBombs").obstaclePrefab;
            Vector2 target = boat.Deck.transform.position;
            var bomb = Object.Instantiate(prefab, target + Vector2.up * 4f, Quaternion.identity).GetComponent<TimedBomb>();
            bomb.Throw(boat, target);
            Assert.That(FindCue(bomb.gameObject, "Fuse"), Is.Null);
            yield return WaitFor(() => bomb != null && FindCue(bomb.gameObject, "Fuse") != null, "fuse starts after landing");
            Assert.That(bomb.IsInFlight, Is.False);
            Assert.That(FindCue(controller.gameObject, "BombLand"), Is.Not.Null);
            AudioSource fuse = FindCue(bomb.gameObject, "Fuse");
            bomb.GetComponent<DestructibleObstacle>().Clear();
            Assert.That(fuse.isPlaying, Is.False);

            boat.ApplyDamage(boat.CurrentHealth, target);
            yield return WaitFor(() => FindCue(controller.gameObject, "Defeat")?.volume > 0f, "defeat cue");
            Assert.That(FindCue(controller.gameObject, "Defeat").loop, Is.False);
        }

        private static AudioSource FindCue(GameObject owner, string name)
        {
            return owner.GetComponents<AudioSource>().FirstOrDefault(source => source.clip != null && source.clip.name == name);
        }

        [UnityTest]
        public IEnumerator CombatSeagullCanDropABombAndTheWaveWaitsForItsFuse()
        {
            special.obstaclePrefab = Resources.Load<SeaEventDefinition>("Voyage/Events/DriftingBombs").obstaclePrefab;
            Assert.That(controller.StartVoyage(104), Is.True);
            yield return WaitFor(() => AtEvent(1) && Object.FindObjectsOfType<TimedBomb>().Length > 0,
                "seagull bomb during combat");
            var bomb = Object.FindObjectsOfType<TimedBomb>().Single();
            Assert.That(bomb.IsInFlight, Is.True);
            typeof(TimedBomb).GetField("fuseSeconds", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bomb, 30f);
            Assert.That(Object.FindObjectsOfType<SpriteRenderer>().Count(renderer => renderer.name == "Obstacle Seagull"),
                Is.EqualTo(1));
            while (controller.WaveIndex < encounter.waves.Length)
            {
                QuiesceActors();
                foreach (EnemyCharacter enemy in LiveEnemies())
                    Kill(enemy);
                yield return null;
            }
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Event), "Combat left before the seagull bomb was handled.");
            yield return WaitFor(() => bomb != null && !bomb.IsInFlight, "seagull bomb landing");
            bomb.GetComponent<DestructibleObstacle>().Clear();
            yield return WaitFor(() => controller.StopIndex >= 2, "island after clearing seagull bomb");
        }

        [UnityTest]
        public IEnumerator EmptyDepartureAndMultiPassengerBoardingNeverDuplicateTheSameAnimal()
        {
            definition.stops[2].maxBoardingPassengers = 3;
            Assert.That(controller.StartVoyage(104), Is.True);
            Assert.That(controller.Passengers, Is.Empty);
            Assert.That(store.LoadCheckpoint(definition.id).passengers, Is.Empty);
            yield return CompleteCombat(1);
            yield return WaitFor(() => AtEvent(3), "departure after island boarding");
            Assert.That(controller.DeliveredPassengers, Is.Zero);
            Assert.That(controller.Passengers.Count, Is.EqualTo(1), "The island admitted the same animal more than once.");
            Assert.That(controller.Passengers.Single().Definition, Is.SameAs(joining));
            Assert.That(controller.Passengers.Single().DestinationStopIndex, Is.EqualTo(4));
        }

        [UnityTest]
        public IEnumerator SeaRestIsSplitEvenlyAndFallingBombsKeepSailing()
        {
            definition.sailingSeconds = 0.4f;
            definition.enemyEncounterCount = 0;
            definition.stops[1].fixedEncounter = null;
            definition.stops[1].fixedSpecialEvent = special;
            definition.stops[3].fixedSpecialEvent = null;
            special.obstaclePrefab = Resources.Load<SeaEventDefinition>("Voyage/Events/DriftingBombs").obstaclePrefab;
            special.obstacleCount = 2;
            Assert.That(controller.StartVoyage(104), Is.True);
            yield return WaitFor(() => controller.StopIndex == 1 && controller.Phase == VoyagePhase.Sailing,
                "rest before the bomb event");
            float remaining = controller.SailingRemaining;
            Assert.That(remaining, Is.InRange(0.1f, 0.2f));
            float started = Time.time;
            yield return WaitFor(() => AtEvent(1), "bomb event after rest");
            Assert.That(Object.FindObjectsOfType<SpriteRenderer>().Count(renderer => renderer.name == "Obstacle Seagull"),
                Is.EqualTo(2), "Each bomb must have its own seagull.");
            Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(remaining - Time.deltaTime));
            yield return AssertEventKeepsSailing(1);
            yield return WaitFor(() => Object.FindObjectsOfType<TimedBomb>().Length == special.obstacleCount, "both bombs released");
            var bombs = Object.FindObjectsOfType<TimedBomb>();
            Assert.That(bombs.Length, Is.EqualTo(2), "A seagull released more than one bomb.");
            yield return WaitFor(() => bombs.All(bomb => !bomb.IsInFlight), "bomb landing");
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Event));
            Assert.That(bombs[0].Explode(), Is.True);
            yield return null;
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Event), "The voyage left while a second bomb was pending.");
            Assert.That(bombs[1].Explode(), Is.True);
            yield return WaitFor(() => controller.Phase == VoyagePhase.Sailing, "rest after the bomb event");
            Assert.That(Object.FindObjectsOfType<TimedBomb>().All(bomb => bomb.GetComponent<DestructibleObstacle>().IsCleared), Is.True);
            remaining = controller.SailingRemaining;
            Assert.That(remaining, Is.InRange(0.1f, 0.2f));
            started = Time.time;
            yield return WaitFor(() => controller.Phase == VoyagePhase.Leaving, "leaving after rest");
            Assert.That(Time.time - started, Is.GreaterThanOrEqualTo(remaining - Time.deltaTime));
        }

        [UnityTest]
        public IEnumerator WavesKeepSailingPassengersBoardAndSuccessfulSettlementReturnsToAFullShip()
        {
            yield return StartVoyageWithLegacyPassenger(104);
            QuiesceActors();
            yield return WaitFor(() => AtEvent(1), "first combat event");
            controller.Vessel.Boat.ApplyDamage(150f, new Vector2(-0.5f, 0.25f));
            yield return VerifyCombatWaves(1);
            yield return WaitFor(() => controller.BoardingProfile == joining, "boarding dialogue");
            Assert.That(controller.BoardingDestination, Is.EqualTo(definition.stops[4].displayName));
            Assert.That(controller.Passengers.Count, Is.EqualTo(1));
            yield return WaitFor(() => controller.StopIndex == 2 && controller.Phase == VoyagePhase.Island
                && controller.Passengers.Count == 2, "automatic boarding at the intermediate island");
            Assert.That(controller.BoardingProfile, Is.Null);
            Assert.That(controller.BoardingDestination, Is.Null);
            Assert.That(controller.DeliveredPassengers, Is.Zero);
            Assert.That(controller.Passengers.Single(item => item.Definition == joining).DestinationStopIndex, Is.EqualTo(4));
            Assert.That(controller.Passengers.Single(item => item.Definition == escort).DestinationStopIndex, Is.EqualTo(4));
            Assert.That(store.Progress.coins, Is.Zero, "Temporary event rewards were committed before settlement.");

            yield return WaitFor(() => AtEvent(3), "special event");
            var carried = Object.FindObjectsOfType<DestructibleObstacle>().ToHashSet();
            yield return WaitFor(() => Object.FindObjectsOfType<DestructibleObstacle>()
                .Any(stone => !carried.Contains(stone) && stone.IsInFlight), "first stone released");
            var flying = Object.FindObjectsOfType<DestructibleObstacle>().First(stone => !carried.Contains(stone) && stone.IsInFlight);
            Assert.That(flying.GetComponent<Collider2D>().enabled, Is.False);
            flying.Clear();
            Assert.That(flying.IsCleared, Is.False);
            Vector2 initialPosition = flying.transform.position;
            yield return AssertEventKeepsSailing(3);
            Assert.That(Vector2.Distance(initialPosition, flying.transform.position), Is.GreaterThan(0.02f));
            yield return WaitFor(() => Object.FindObjectsOfType<DestructibleObstacle>()
                .Count(stone => !carried.Contains(stone)) == special.obstacleCount, "both stones released");
            var stones = Object.FindObjectsOfType<DestructibleObstacle>();
            yield return WaitFor(() => stones.All(stone => !stone.IsInFlight), "special event objects landed");
            foreach (var stone in stones)
            {
                var hitbox = stone.GetComponent<BoxCollider2D>();
                Assert.That(hitbox.enabled, Is.True);
                Assert.That(controller.Vessel.Boat.Deck.CanPlace(stone.transform.position, hitbox.size, hitbox), Is.True,
                    "An event obstacle overlapped ship fixtures, another obstacle or a character.");
            }
            yield return WaitFor(() => controller.Phase != VoyagePhase.Event, "continued voyage with uncleared stones");
            Assert.That(stones.All(stone => stone != null && !stone.IsCleared), Is.True);
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
            Assert.That(controller.StartVoyage(105), Is.True);
            Assert.That(controller.Passengers, Is.Empty);
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
            Assert.That(controller.StartVoyage(206), Is.True);
            QuiesceActors();
            yield return CompleteCombat(1);
            yield return WaitFor(() => AtEvent(3) && Object.FindObjectsOfType<DestructibleObstacle>().Any(item => item.IsInFlight),
                "a stone in the air during the later event");
            var falling = Object.FindObjectsOfType<DestructibleObstacle>().Where(item => item.IsInFlight).ToArray();
            Assert.That(GameObject.Find("Obstacle Seagull"), Is.Not.Null);
            Assert.That(controller.PendingReward.coins, Is.EqualTo(encounter.reward.coins));
            controller.Vessel.Boat.ApplyDamage(controller.Vessel.Boat.MaxHealth, Vector2.zero);
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Failure));
            Assert.That(controller.ActiveRun, Is.False);
            Assert.That(controller.DeliveredPassengers, Is.Zero);
            Assert.That(store.HasCheckpoint, Is.False);
            Assert.That(falling.All(item => item == null || !item.gameObject.activeInHierarchy), Is.True,
                "Ship destruction left airborne event obstacles active.");
            Assert.That(GameObject.Find("Obstacle Seagull"), Is.Null, "Ship destruction left the seagull active.");
            int savedCoins = 7 + encounter.reward.coins;
            int savedItems = 1 + encounter.reward.specialItems;
            Assert.That(new VoyageSaveStore(directory).Progress.coins, Is.EqualTo(savedCoins));
            Assert.That(new VoyageSaveStore(directory).Progress.specialItems, Is.EqualTo(savedItems));

            controller.ReturnHome();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "return after ship failure");
            Assert.That(controller.StartVoyage(207), Is.True);
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
            Assert.That(controller.StartVoyage(309), Is.True);
            QuiesceActors();
            string plan = PlanSignature();
            VoyageCheckpoint initial = store.LoadCheckpoint(definition.id);
            Assert.That(initial.stopIndex, Is.Zero, "Starting a voyage did not synchronously create its first checkpoint.");
            Assert.That(initial.passengers, Is.Empty, "A new voyage boarded a passenger at departure.");
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            QuiesceActors();
            Assert.That(PlanSignature(), Is.EqualTo(plan));
            AssertRestored(initial);

            initial.passengers = new[]
            {
                new PassengerState { profileId = escort.id, destinationStopIndex = 6, localPosition = new Vector2(6.5f, 1f) }
            };
            store.SaveCheckpoint(initial);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            QuiesceActors();
            AssertRestored(initial);

            yield return WaitFor(() => AtEvent(1), "combat after restoring the immediate checkpoint");
            var playerBowPosition = new Vector2(7.5f, 0f);
            var crewBowPosition = new Vector2(6.5f, 1f);
            controller.Vessel.Player.GetComponent<Rigidbody2D>().position = playerBowPosition;
            controller.Passengers.Single().Actor.GetComponent<Rigidbody2D>().position = crewBowPosition;
            Physics2D.SyncTransforms();
            controller.Vessel.Boat.ApplyDamage(150f, new Vector2(-0.5f, 0.25f));
            RepairPoint point = controller.Vessel.Boat.RepairPoints.Single();
            Assert.That(point.BeginRepair(controller.Vessel.Player), Is.True);
            point.AddProgress(controller.Vessel.Player, 30f);
            point.EndRepair(controller.Vessel.Player);
            yield return WaitFor(() => Object.FindObjectsOfType<DestructibleObstacle>().Any(stone => !stone.IsInFlight),
                "the seagull's stone to carry to the next checkpoint");
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
            Assert.That(regional.obstacles, Is.Not.Empty, "Carried obstacles were not checkpointed at the next island.");
            Assert.That(Vector2.Distance(regional.playerLocalPosition, playerBowPosition), Is.LessThan(0.001f));
            Assert.That(Vector2.Distance(regional.passengers.Single(item => item.profileId == escort.id).localPosition,
                crewBowPosition), Is.LessThan(0.001f));

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
            yield return WaitFor(() => AtEvent(stop), "combat at stop " + stop);
            float deadline = Time.realtimeSinceStartup + 12f;
            while (AtEvent(stop))
            {
                QuiesceActors();
                foreach (EnemyCharacter enemy in LiveEnemies())
                    Kill(enemy);
                if (Time.realtimeSinceStartup >= deadline)
                    Assert.Fail("Combat did not finish: " + DescribeState());
                yield return null;
            }
        }

        [UnityTest]
        public IEnumerator SeaMonsterWavesWaitForEveryPriorEnemyAndKeepSpaceAboveOrRightOfTheShip()
        {
            Camera.main.aspect = 4f / 3f;
            definition.specialEvents = Array.Empty<SpecialEventEntry>();
            EnemyCharacter prefab = encounter.waves[0].enemies[0].prefab;
            encounter.waves[0].enemies = new[]
            {
                new EnemySpawn { prefab = prefab, count = 6 },
                new EnemySpawn { prefab = prefab, count = 6 }
            };
            encounter.waves = new[]
            {
                encounter.waves[0],
                new WaveDefinition { enemies = new[] { new EnemySpawn { prefab = prefab, count = 4 } } },
                new WaveDefinition { enemies = new[] { new EnemySpawn { prefab = prefab, count = 5 } } }
            };
            Type audio = typeof(VoyageController).Assembly.GetType("Ferryman.Voyage.VoyageAudio", true);
            controller.gameObject.AddComponent(audio);
            Assert.That(controller.StartVoyage(104), Is.True);
            yield return WaitFor(() => Object.FindObjectsOfType<WaveBubbles>().Length == 12, "first wave water warning");
            Assert.That(Camera.main.orthographicSize * Camera.main.aspect,
                Is.GreaterThanOrEqualTo(encounter.cameraSize * (16f / 9f) - 0.001f));
            WaveBubbles[] warnings = Object.FindObjectsOfType<WaveBubbles>();
            Vector3[] origins = warnings.Select(item => item.transform.position).ToArray();
            Assert.That(origins.Select(origin => origin.y).Distinct().Count(), Is.GreaterThan(8),
                "Crowded wave warnings formed straight rows.");
            Assert.That(LiveEnemies(), Is.Empty);
            Assert.That(FindCue(controller.gameObject, "WaterBubbles"), Is.Not.Null);
            Assert.That(warnings.All(item => item.GetComponentsInChildren<SpriteRenderer>()
                .Any(renderer => renderer.sprite != null && renderer.color.a > 0f)), Is.True);
            yield return WaitFor(() => AtEvent(1) && LiveEnemies().Length == 12, "twelve sea monsters");
            AssertSeaMonsterLayout(LiveEnemies());
            EnemyCharacter[] firstWave = LiveEnemies();
            Assert.That(firstWave.Select(enemy => enemy.transform.position), Is.EquivalentTo(origins));
            Assert.That(Object.FindObjectsOfType<WaveBubbles>(), Is.Empty);
            var sinkingSprite = firstWave[0].GetComponentInChildren<SpriteRenderer>();
            Bounds monster = sinkingSprite.bounds;
            float aboveWater = sinkingSprite.transform.position.y;
            Kill(firstWave[0]);
            Assert.That(controller.Vessel.DamageSystem.ApplyDamage(controller.Vessel.Player, firstWave[0], 5f), Is.False);
            SpriteEffect splash = Object.FindObjectsOfType<SpriteEffect>().Single(effect => effect.name == "EnemyDeath(Clone)");
            Assert.That(splash.transform.position.x, Is.EqualTo(monster.center.x).Within(0.001f));
            Assert.That(splash.transform.position.y, Is.InRange(monster.min.y, monster.center.y));
            Assert.That(splash.GetComponentInChildren<SpriteRenderer>().sprite.name, Is.EqualTo("WaterSplash"));
            Assert.That(FindCue(controller.gameObject, "WaterSplash"), Is.Not.Null);
            yield return new WaitForSeconds(0.22f);
            Assert.That(firstWave[0] != null, Is.True);
            Assert.That(sinkingSprite.transform.position.y, Is.LessThan(aboveWater));
            Assert.That(firstWave[0].Hitbox.enabled, Is.False);
            foreach (EnemyCharacter enemy in firstWave.Skip(1).Take(10))
                Kill(enemy);
            yield return new WaitForSeconds(encounter.waveInterval + 0.08f);
            Assert.That(controller.WaveIndex, Is.Zero);
            Assert.That(LiveEnemies(), Is.EqualTo(new[] { firstWave[11] }));
            Assert.That(Object.FindObjectsOfType<WaveBubbles>(), Is.Empty);
            Kill(firstWave[11]);
            yield return WaitFor(() => AtEvent(1) && controller.WaveIndex == 1 && LiveEnemies().Length == 4, "second wave after clearing the first");
            AssertSeaMonsterLayout(LiveEnemies());
            foreach (EnemyCharacter enemy in LiveEnemies())
                Kill(enemy);
            yield return WaitFor(() => AtEvent(1) && controller.WaveIndex == 2 && LiveEnemies().Length == 5, "third wave after clearing the second");
            AssertSeaMonsterLayout(LiveEnemies());
        }

        [UnityTest]
        public IEnumerator EncountersWaitForDialogueAndChangeAfterTheFirstFight()
        {
            escort.id = "gunner-monkey";
            definition.stops[2].boardingChance = 0f;
            definition.stops[3].fixedSpecialEvent = null;
            definition.stops[3].fixedEncounter = encounter;
            definition.enemyEncounterCount = 2;
            yield return StartVoyageWithLegacyPassenger(104);
            yield return WaitFor(() => controller.AwaitingStoryDialogue, "first encounter dialogue");
            string firstReaction = controller.StoryDialogueLine;
            Assert.That(firstReaction, Does.StartWith(escort.displayName + "：").And.Contain("头一回"));
            Assert.That(controller.ContinueStoryDialogue(), Is.True);
            Assert.That(controller.StoryDialogueLine, Does.StartWith("兔子船长："));
            yield return new WaitForSeconds(0.24f);
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Sailing));
            Assert.That(Object.FindObjectsOfType<WaveBubbles>(), Is.Empty);
            Assert.That(LiveEnemies(), Is.Empty);
            Assert.That(controller.ContinueStoryDialogue(), Is.True);
            yield return VerifyCombatWaves(1);
            yield return WaitFor(() => controller.AwaitingStoryDialogue, "second encounter dialogue");
            Assert.That(store.LoadCheckpoint(definition.id).passengers.Single().encountersAboard, Is.EqualTo(1));
            Assert.That(controller.StoryDialogueLine, Does.StartWith(escort.displayName + "：").And.Not.EqualTo(firstReaction));
            Assert.That(controller.SkipStoryDialogue(), Is.True);
            yield return WaitFor(() => AtEvent(3) && LiveEnemies().Length > 0, "combat after skipping");
        }

        [UnityTest]
        public IEnumerator BoardingRemainsSurviveARegionalCheckpointAndCanBeKickedAway()
        {
            var raid = Resources.Load<EncounterDefinition>("Voyage/Events/BoardingRaid");
            encounter.waves = new[] { new WaveDefinition { enemies = new[]
            {
                new EnemySpawn { prefab = raid.waves[0].enemies[0].prefab, count = 1 }
            } } };
            definition.specialEvents = Array.Empty<SpecialEventEntry>();
            definition.specialEventCount = 0;
            definition.stops[3].fixedSpecialEvent = null;
            Assert.That(controller.StartVoyage(104), Is.True);
            yield return WaitFor(() => AtEvent(1) && LiveEnemies().Length == 1, "the boarding crab appears");
            var crab = LiveEnemies().Single();
            BoatDeck deck = controller.Vessel.Boat.Deck;
            controller.Vessel.Player.enabled = false;
            crab.enabled = true;
            float landingDeadline = Time.realtimeSinceStartup + 4f;
            while (crab.Hitbox.isTrigger || !deck.ContainsFloor(crab.transform.position))
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(landingDeadline), "The crab did not land on the ship.");
                yield return null;
            }
            crab.enabled = false;
            Kill(crab);
            var remains = Object.FindObjectsOfType<DestructibleObstacle>().Single(item => item.name.StartsWith("BoardingCrabRemains"));
            Vector2 position = remains.transform.position;
            Assert.That(remains.GetComponent<BoxCollider2D>().isTrigger, Is.False);
            Assert.That(deck.CanPlace(position, Vector2.one * 0.1f), Is.False, "The corpse did not block deck placement.");
            yield return WaitFor(() => controller.StopIndex == 2 && controller.Phase == VoyagePhase.Island
                && store.LoadCheckpoint(definition.id).stopIndex == 2, "the corpse reaches the next island");
            controller.StopAllCoroutines();
            var saved = store.LoadCheckpoint(definition.id).obstacles.Single(item => item.eventId == "BoardingCrabRemains");
            Assert.That(Vector2.Distance(saved.localPosition, position), Is.LessThan(0.001f));
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.StopIndex == 2 && controller.Phase == VoyagePhase.Island,
                "the regional checkpoint restores the corpse");
            controller.StopAllCoroutines();
            QuiesceActors();
            remains = Object.FindObjectsOfType<DestructibleObstacle>().Single(item => item.name.StartsWith("BoardingCrabRemains"));
            Assert.That(Vector2.Distance(remains.transform.position, position), Is.LessThan(0.001f));
            Assert.That(remains.GetComponent<BoxCollider2D>().enabled, Is.True);
            var captain = controller.Vessel.Player;
            captain.GetComponent<Rigidbody2D>().position = (Vector2)remains.transform.position + Vector2.left * 1.4f;
            Physics2D.SyncTransforms();
            Assert.That(captain.TryMelee(Vector2.right), Is.True);
            Assert.That(remains.IsCleared, Is.True);
            typeof(VoyageController).GetMethod("SaveCheckpoint", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(controller, null);
            Assert.That(store.LoadCheckpoint(definition.id).obstacles.Any(item => item.eventId == "BoardingCrabRemains"), Is.False);
        }

        [UnityTest]
        public IEnumerator SeagullFliesStraightFromFourDirectionsAndReleasesStoneFromItsTalons()
        {
            var quadrants = new HashSet<Vector2Int>();
            var flightOrigin = typeof(DestructibleObstacle).GetField("flightOrigin", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (int seed in new[] { 0, 9, 1, 12 })
            {
                Assert.That(controller.StartVoyage(seed), Is.True);
                yield return WaitFor(() => AtEvent(1) && GameObject.Find("Obstacle Seagull") != null, "seagull begins its combat flyby");
                var gull = GameObject.Find("Obstacle Seagull").transform;
                Vector2 start = gull.position;
                Vector3 initialViewport = Camera.main.WorldToViewportPoint(start);
                Assert.That(initialViewport.x < 0f || initialViewport.x > 1f || initialViewport.y < 0f || initialViewport.y > 1f, Is.True);
                yield return WaitFor(() => Vector2.Distance(start, gull.position) > 0.8f, "seagull moves along first flight segment");
                Vector2 middle = gull.position;
                yield return WaitFor(() => Vector2.Distance(middle, gull.position) > 0.8f, "seagull moves along second flight segment");
                Vector2 heading = (middle - start).normalized;
                Assert.That(Vector2.Dot(heading, ((Vector2)gull.position - middle).normalized), Is.GreaterThan(0.9999f));
                quadrants.Add(new Vector2Int(heading.x > 0f ? 1 : -1, heading.y > 0f ? 1 : -1));
                var sprite = gull.GetComponent<SpriteRenderer>();
                Assert.That(sprite.flipX, Is.EqualTo(heading.x < 0f));
                Assert.That(gull.localScale, Is.EqualTo(new Vector3(1.3f, 1.3f, 1f)));
                var flightFrames = new HashSet<Sprite> { sprite.sprite };

                yield return WaitFor(() =>
                {
                    flightFrames.Add(sprite.sprite);
                    return Object.FindObjectsOfType<DestructibleObstacle>().Any(item => item.IsInFlight);
                }, "stone releases during flyby");
                Assert.That(flightFrames, Is.EquivalentTo(definition.obstacleCarrierFrames), "The seagull did not play all four flight frames.");
                var stone = Object.FindObjectsOfType<DestructibleObstacle>().Single(item => item.IsInFlight);
                BoatDeck deck = controller.Vessel.Boat.Deck;
                Vector2 origin = deck.transform.TransformPoint((Vector2)flightOrigin.GetValue(stone));
                Vector2 talons = start + (Vector2)gull.TransformVector(Vector2.down * sprite.sprite.bounds.extents.y * 0.66f);
                Vector2 acrossFlight = Vector2.Perpendicular(heading);
                Assert.That(Vector2.Dot(origin - talons, acrossFlight), Is.EqualTo(0f).Within(0.02f));
                yield return WaitFor(() => !stone.IsInFlight, "stone lands on the deck");
                Assert.That(Vector2.Dot((Vector2)stone.transform.position - origin, heading), Is.GreaterThan(0.8f));

                controller.AbandonVoyage();
                yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "return between seagull direction checks");
            }
            Assert.That(quadrants.Count, Is.EqualTo(4));
        }

        private void AssertSeaMonsterLayout(EnemyCharacter[] actors)
        {
            Bounds deck = controller.Vessel.Boat.Deck.GetComponent<EdgeCollider2D>().bounds;
            var bodies = new List<Bounds>();
            Camera camera = Camera.main;
            foreach (EnemyCharacter actor in actors)
            {
                Transform model = actor.transform.Find("Sea Monster");
                Assert.That(model.localScale, Is.EqualTo(new Vector3(1.56f, 1.56f, 1f)));
                Bounds body = model.GetComponent<SpriteRenderer>().bounds;
                Assert.That(body.min.x, Is.GreaterThanOrEqualTo(deck.min.x));
                Assert.That(body.min.y, Is.GreaterThanOrEqualTo(deck.min.y));
                Assert.That(body.min.y >= deck.max.y + 2f || body.min.x >= deck.max.x + 2f, Is.True,
                    "A sea monster spawned beside the left or lower side, or on the ship.");
                Vector3 min = camera.WorldToViewportPoint(body.min);
                Vector3 max = camera.WorldToViewportPoint(body.max);
                Assert.That(min.x >= 0f && min.y >= 0f && max.x <= 1f && max.y <= 1f, Is.True,
                    "A sea monster spawned outside the visible sea.");
                foreach (Bounds other in bodies)
                    Assert.That(body.min.x - other.max.x >= 0.5f || other.min.x - body.max.x >= 0.5f
                        || body.min.y - other.max.y >= 0.5f || other.min.y - body.max.y >= 0.5f, Is.True,
                        "Sea monster models spawned too close together.");
                bodies.Add(body);
            }
        }

        private IEnumerator VerifyCombatWaves(int stop)
        {
            yield return WaitFor(() => AtEvent(stop) && controller.WaveIndex == 0 && LiveEnemies().Length == 2,
                "first wave at stop " + stop);
            AssertSeaMonsterLayout(LiveEnemies());
            RewardData before = controller.PendingReward;
            yield return AssertEventKeepsSailing(stop);
            Assert.That(controller.WaveIndex, Is.Zero);
            foreach (EnemyCharacter enemy in LiveEnemies())
                Kill(enemy);
            yield return WaitFor(() => AtEvent(stop) && controller.WaveIndex == 1 && LiveEnemies().Length == 1,
                "second wave at stop " + stop);
            AssertSeaMonsterLayout(LiveEnemies());
            Assert.That(controller.PendingReward.coins, Is.EqualTo(before.coins));
            Assert.That(controller.PendingReward.specialItems, Is.EqualTo(before.specialItems));
            foreach (EnemyCharacter enemy in LiveEnemies())
                Kill(enemy);
            yield return WaitFor(() => !AtEvent(stop), "completion of combat at stop " + stop);
            Assert.That(controller.PendingReward.coins, Is.EqualTo(before.coins + encounter.reward.coins));
            Assert.That(controller.PendingReward.specialItems, Is.EqualTo(before.specialItems + encounter.reward.specialItems));
        }

        private IEnumerator AssertEventKeepsSailing(int stop)
        {
            QuiesceActors();
            Transform motion = GameObject.Find("Environment/Sea Motion")?.transform;
            Assert.That(motion, Is.Not.Null);
            Vector3 position = motion.localPosition;
            float remaining = controller.SailingRemaining;
            float started = Time.time;
            float timeScale = Time.timeScale;
            yield return new WaitForSeconds(0.12f);
            QuiesceActors();
            Assert.That(AtEvent(stop), Is.True, "The voyage advanced while an event was unresolved.");
            Assert.That(controller.SailingRemaining, Is.EqualTo(remaining));
            Assert.That(Time.time, Is.GreaterThan(started), "An event paused game time.");
            Assert.That(Time.timeScale, Is.EqualTo(timeScale));
            Assert.That(motion.localPosition.x, Is.LessThan(position.x), "A sea event stopped sailing.");
            var wake = motion.parent.Find("Sailing Wake");
            Assert.That(wake.gameObject.activeSelf, Is.True);
            Assert.That(wake.GetComponent<Animator>().enabled, Is.True);
            foreach (var actor in controller.Vessel.GetComponentsInChildren<Character>())
            {
                Assert.That(actor.ActionsEnabled, Is.True);
                Assert.That(actor.GetComponent<Rigidbody2D>().simulated, Is.True);
            }
        }

        private void AssertRestored(VoyageCheckpoint saved)
        {
            var carried = Object.FindObjectsOfType<DestructibleObstacle>()
                .Where(obstacle => obstacle.GetComponent<TimedBomb>() == null).ToArray();
            Assert.That(carried.Length, Is.EqualTo(saved.obstacles?.Length ?? 0));
            foreach (ObstacleState obstacle in saved.obstacles ?? Array.Empty<ObstacleState>())
                Assert.That(carried.Any(item => Vector2.Distance(item.transform.localPosition, obstacle.localPosition) < 0.001f), Is.True);
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

        private IEnumerator StartVoyageWithLegacyPassenger(int voyageSeed)
        {
            Assert.That(controller.StartVoyage(voyageSeed), Is.True);
            VoyageCheckpoint checkpoint = store.LoadCheckpoint(definition.id);
            Assert.That(controller.Passengers, Is.Empty);
            Assert.That(checkpoint.passengers, Is.Empty);
            checkpoint.passengers = new[]
            {
                new PassengerState
                {
                    profileId = escort.id, destinationStopIndex = definition.stops.Length - 1,
                    localPosition = new Vector2(6.5f, 1f)
                }
            };
            store.SaveCheckpoint(checkpoint);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            QuiesceActors();
            AssertRestored(checkpoint);
        }

        private IEnumerator WaitFor(Func<bool> condition, string description)
        {
            float until = Time.realtimeSinceStartup + 12f;
            while (!condition())
            {
                QuiesceActors();
                if (controller != null && controller.AwaitingStoryDialogue)
                    controller.SkipStoryDialogue();
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
