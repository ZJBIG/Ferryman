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
    public sealed class VoyageExpeditionTests
    {
        private static readonly int[] ActivityPath = { 0, 2, 6, 8, 12, 14, 16, 19 };
        private readonly List<ScriptableObject> clones = new List<ScriptableObject>();
        private VoyageDefinition definition;
        private VoyageController controller;
        private VoyageSaveStore store;
        private string directory;
        private int mainHandle;
        private float originalTimeScale;
        private SimulationMode2D originalSimulationMode;

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
            Assert.That(source, Is.Not.Null);
            definition = Clone(source);
            definition.id = "expedition-" + Guid.NewGuid().ToString("N");
            definition.branchingRoutes = true;
            definition.sailingSeconds = 0.08f;
            definition.transitionSeconds = 0.04f;
            definition.boardingNoticeSeconds = 0.01f;
            var encounters = source.randomEncounters.Concat(source.stops.Select(stop => stop.fixedEncounter))
                .Where(item => item != null).ToArray();
            var shooter = encounters.SelectMany(item => item.waves).SelectMany(wave => wave.enemies)
                .Select(group => group.prefab).First(item => item.AttackType == EnemyCharacter.AttackKind.Projectile);
            var encounter = Clone(encounters[0]);
            encounter.waves = new[]
            {
                new WaveDefinition { enemies = new[]
                {
                    new EnemySpawn { prefab = shooter, count = 1, spawnPosition = new Vector2(0f, 4f) }
                } }
            };
            definition.randomEncounters = new[] { encounter };
            definition.stops = source.stops.Select(stop => new VoyageStop
            {
                kind = stop.kind, displayName = stop.displayName, sceneName = stop.sceneName,
                boardingChance = 0f, maxBoardingPassengers = 0
            }).ToArray();
            definition.specialEvents = source.specialEvents.Select(entry =>
            {
                SeaEventDefinition special = Clone(entry.definition);
                special.obstacleCount = 1;
                special.reward = new RewardData { coins = 150, specialItems = 1 };
                return new SpecialEventEntry { definition = special, maxAppearances = entry.maxAppearances };
            }).ToArray();

            directory = Path.Combine(Path.GetTempPath(), "FerrymanExpeditionTests", Guid.NewGuid().ToString("N"));
            // Install the test controller before Bootstrap can open the user's saves.
            CreateController();
            yield return SceneManager.LoadSceneAsync("main");
            mainHandle = SceneManager.GetActiveScene().handle;
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
                string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FerrymanExpeditionTests"))
                    + Path.DirectorySeparatorChar;
                Assert.That(Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
                Directory.Delete(directory, true);
            }
            Physics2D.simulationMode = originalSimulationMode;
            Time.timeScale = originalTimeScale;
        }

        [UnityTest]
        public IEnumerator BranchesResolveAllIslandActivitiesOnceAndReturnHomeInMain()
        {
            Assert.That(controller.StartVoyage(definition.initialPassengers[0], 7), Is.True);
            yield return WaitFor(() => controller.AwaitingRouteChoice, "departure route selection");
            Assert.That(controller.RoutePath, Is.EqualTo(new[] { 0 }));
            Assert.That(controller.SelectDestination(19), Is.False, "The destination skipped unexplored layers.");
            var visited = new HashSet<VoyageIslandKind>();
            bool sawCombat = false;
            bool sawSpecial = false;
            for (int depth = 1; depth < ActivityPath.Length; depth++)
            {
                int previous = controller.CurrentNodeId;
                int nodeId = ActivityPath[depth];
                Assert.That(controller.SelectDestination(previous), Is.False);
                Assert.That(controller.SelectDestination(nodeId), Is.True);
                Assert.That(controller.SelectDestination(nodeId), Is.False, "A branch was selected twice.");
                Assert.That(controller.RoutePath, Is.EqualTo(ActivityPath.Take(depth + 1).ToArray()));
                Assert.That(controller.RouteMap.IsExploredEdge(previous, nodeId, controller.RoutePath), Is.True);
                VoyageEventKind kind = controller.EventPlan[depth * 2 - 1].kind;
                sawCombat |= kind == VoyageEventKind.Combat;
                sawSpecial |= kind == VoyageEventKind.Special;
                yield return CompleteLeg(depth * 2);
                if (depth == VoyageRouteMap.FinalDepth)
                    break;

                visited.Add(controller.CurrentIsland);
                int option = 0;
                if (controller.CurrentIsland == VoyageIslandKind.Rest)
                {
                    controller.Vessel.Boat.ApplyDamage(125f, Vector2.zero);
                    Assert.That(controller.ResolveIslandOption(0), Is.True);
                    Assert.That(controller.Vessel.Boat.CurrentHealth, Is.EqualTo(controller.Vessel.Boat.MaxHealth));
                }
                else if (controller.CurrentIsland == VoyageIslandKind.Market)
                {
                    int beforeCoins = controller.AvailableCoins;
                    int beforeRank = controller.RunUpgrades[(int)VoyageUpgradeKind.Attack];
                    Assert.That(controller.ResolveIslandOption(0), Is.True);
                    Assert.That(controller.AvailableCoins, Is.EqualTo(beforeCoins - VoyageUpgrades.Price(VoyageUpgradeKind.Attack, beforeRank)));
                    Assert.That(controller.RunUpgrades[(int)VoyageUpgradeKind.Attack], Is.EqualTo(beforeRank + 1));
                    Assert.That(controller.IslandResolved, Is.False);
                    option = 4;
                    Assert.That(controller.ResolveIslandOption(option), Is.True);
                }
                else
                {
                    int beforePassengers = controller.Passengers.Count;
                    int beforeCoins = controller.AvailableCoins;
                    if (controller.CurrentIsland == VoyageIslandKind.Mystery)
                        option = 1;
                    Assert.That(controller.ResolveIslandOption(option), Is.True);
                    if (controller.CurrentIsland == VoyageIslandKind.Rescue)
                        Assert.That(controller.Passengers.Count, Is.EqualTo(beforePassengers + 1));
                    if (controller.CurrentIsland == VoyageIslandKind.Mystery)
                        Assert.That(controller.AvailableCoins, Is.EqualTo(beforeCoins));
                }
                RewardData resolvedReward = controller.PendingReward;
                int[] ranks = controller.RunUpgrades;
                Assert.That(controller.ResolveIslandOption(option), Is.False, "An island activity paid twice.");
                Assert.That(controller.PendingReward.coins, Is.EqualTo(resolvedReward.coins));
                Assert.That(controller.PendingReward.specialItems, Is.EqualTo(resolvedReward.specialItems));
                Assert.That(controller.RunUpgrades, Is.EqualTo(ranks));
                if (controller.UpgradeChoices.Length > 0)
                {
                    VoyageUpgradeKind upgrade = controller.UpgradeChoices[0];
                    Assert.That(controller.SelectDestination(ActivityPath[depth + 1]), Is.False,
                        "Leaving an island discarded an unchosen upgrade.");
                    Assert.That(controller.ChooseUpgrade(upgrade), Is.True);
                    Assert.That(controller.RunUpgrades[(int)upgrade], Is.EqualTo(ranks[(int)upgrade] + 1));
                    Assert.That(controller.ChooseUpgrade(upgrade), Is.False);
                }
                Assert.That(controller.AwaitingRouteChoice, Is.True);
                AssertUpgrades(controller.RunUpgrades);
            }
            Assert.That(visited, Is.EquivalentTo(new[]
            {
                VoyageIslandKind.Rest, VoyageIslandKind.Shipyard, VoyageIslandKind.Market,
                VoyageIslandKind.Rescue, VoyageIslandKind.Treasure, VoyageIslandKind.Mystery
            }));
            Assert.That(sawCombat && sawSpecial, Is.True);
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Success));
            Assert.That(controller.ActiveRun, Is.False);
            Assert.That(controller.HasCheckpoint, Is.False);
            Assert.That(store.Progress.coins, Is.EqualTo(controller.PendingReward.coins));
            Assert.That(store.Progress.specialItems, Is.EqualTo(controller.PendingReward.specialItems));
            var persisted = new VoyageSaveStore(directory);
            Assert.That(persisted.Progress.coins, Is.EqualTo(store.Progress.coins));
            controller.ReturnHome();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "return home");
            Assert.That(controller.RoutePath, Is.Empty);
            Assert.That(controller.IsExpedition, Is.False);
        }

        [UnityTest]
        public IEnumerator FullyUpgradedShipyardSkipsChoicesWithoutSpendingRewards()
        {
            Assert.That(controller.StartVoyage(definition.initialPassengers[0], 7), Is.True);
            yield return WaitFor(() => controller.AwaitingRouteChoice, "departure");
            VoyageCheckpoint checkpoint = store.LoadCheckpoint(definition.id);
            checkpoint.stopIndex = 4;
            checkpoint.routePath = new[] { 0, 2, 6 };
            checkpoint.islandResolved = false;
            checkpoint.runUpgrades = Enumerable.Repeat(VoyageUpgrades.MaxRank, VoyageUpgrades.Count).ToArray();
            checkpoint.pendingCoins = 80;
            checkpoint.pendingSpecialItems = 2;
            store.SaveCheckpoint(checkpoint);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.Phase == VoyagePhase.Island && controller.BoardingProfile == null, "maxed shipyard");
            Assert.That(controller.CurrentIsland, Is.EqualTo(VoyageIslandKind.Shipyard));
            Assert.That(controller.ResolveIslandOption(0), Is.True);
            Assert.That(controller.UpgradeChoices, Is.Empty);
            Assert.That(controller.AwaitingRouteChoice, Is.True);
            Assert.That(controller.AvailableCoins, Is.EqualTo(80));
            Assert.That(controller.AvailableSpecialItems, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator CrashRestoresSelectedBranchPendingChoiceAndAllCombinedUpgrades()
        {
            store.Settle("upgrade-funding", new RewardData { coins = 1000, specialItems = 10 });
            foreach (VoyageUpgradeKind kind in Enum.GetValues(typeof(VoyageUpgradeKind)))
                Assert.That(controller.BuyPermanentUpgrade(kind), Is.True);
            Assert.That(controller.StartVoyage(definition.initialPassengers[0], 7), Is.True);
            AssertUpgrades(Enumerable.Repeat(1, VoyageUpgrades.Count).ToArray());
            Assert.That(controller.BuyPermanentUpgrade(VoyageUpgradeKind.Attack), Is.False);
            yield return WaitFor(() => controller.AwaitingRouteChoice, "departure");
            Assert.That(controller.SelectDestination(2), Is.True);
            VoyageCheckpoint seaCheckpoint = store.LoadCheckpoint(definition.id);
            Assert.That(seaCheckpoint.stopIndex, Is.EqualTo(1));
            Assert.That(seaCheckpoint.routePath, Is.EqualTo(new[] { 0, 2 }));
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            Assert.That(controller.RoutePath, Is.EqualTo(seaCheckpoint.routePath));
            yield return CompleteLeg(2);
            Assert.That(controller.ResolveIslandOption(1), Is.True);
            Assert.That(controller.SelectDestination(6), Is.True);
            yield return CompleteLeg(4);
            Assert.That(controller.ResolveIslandOption(0), Is.True);
            VoyageCheckpoint saved = store.LoadCheckpoint(definition.id);
            Assert.That(saved.upgradeChoices, Has.Length.EqualTo(3));
            // A valid upgraded checkpoint exercises restoration above the prefab HP limit.
            saved.runUpgrades = Enumerable.Repeat(1, VoyageUpgrades.Count).ToArray();
            saved.ship = new BoatHealth.State
            {
                health = 1250f, pendingDamage = 50f, points = Array.Empty<BoatHealth.PointState>()
            };
            store.SaveCheckpoint(saved);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.Phase == VoyagePhase.Island, "restored upgrade choice");
            Assert.That(controller.StopIndex, Is.EqualTo(saved.stopIndex));
            Assert.That(controller.RoutePath, Is.EqualTo(saved.routePath));
            Assert.That(controller.UpgradeChoices, Is.EqualTo(saved.upgradeChoices));
            Assert.That(controller.IslandResolved, Is.True);
            Assert.That(controller.Vessel.Boat.CurrentHealth, Is.EqualTo(1250f));
            Assert.That(controller.Vessel.Boat.PendingDamage, Is.EqualTo(50f));
            Assert.That(controller.PendingReward.coins, Is.EqualTo(saved.pendingCoins));
            AssertUpgrades(Enumerable.Repeat(2, VoyageUpgrades.Count).ToArray());
            Assert.That(controller.SelectDestination(8), Is.False);
            Assert.That(controller.ResolveIslandOption(0), Is.False);
            VoyageUpgradeKind selected = saved.upgradeChoices[0];
            Assert.That(controller.ChooseUpgrade(selected), Is.True);
            Assert.That(controller.ChooseUpgrade(selected), Is.False);
            Assert.That(controller.RunUpgrades[(int)selected], Is.EqualTo(2));
            Assert.That(store.LoadCheckpoint(definition.id).upgradeChoices, Is.Empty);
            controller.AbandonVoyage();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "abandon restored voyage");
            Assert.That(controller.HasCheckpoint, Is.False);
            Assert.That(controller.RunUpgrades, Is.EqualTo(new int[VoyageUpgrades.Count]));
            Assert.That(controller.PermanentUpgrades, Is.EqualTo(Enumerable.Repeat(1, VoyageUpgrades.Count).ToArray()));
            Assert.That(controller.StartVoyage(definition.initialPassengers[0], 8), Is.True);
            AssertUpgrades(Enumerable.Repeat(1, VoyageUpgrades.Count).ToArray());
        }

        [UnityTest]
        public IEnumerator FailedExpeditionSettlesOnceAndAbandonDiscardsTemporaryRewards()
        {
            Assert.That(controller.StartVoyage(definition.initialPassengers[0], 7), Is.True);
            yield return WaitFor(() => controller.AwaitingRouteChoice, "departure");
            Assert.That(controller.SelectDestination(2), Is.True);
            yield return CompleteLeg(2);
            Assert.That(controller.ResolveIslandOption(1), Is.True);
            RewardData earned = controller.PendingReward;
            Assert.That(earned.coins, Is.GreaterThan(0));
            Assert.That(controller.Vessel.Boat.ApplyDamage(controller.Vessel.Boat.CurrentHealth, Vector2.zero), Is.True);
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Failure));
            Assert.That(controller.HasCheckpoint, Is.False);
            Assert.That(store.Progress.coins, Is.EqualTo(earned.coins));
            Assert.That(store.Progress.specialItems, Is.EqualTo(earned.specialItems));
            controller.RetrySettlement();
            Assert.That(store.Progress.coins, Is.EqualTo(earned.coins));
            controller.ReturnHome();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "return after failure");
            Assert.That(controller.StartVoyage(definition.initialPassengers[0], 7), Is.True);
            yield return WaitFor(() => controller.AwaitingRouteChoice, "second departure");
            Assert.That(controller.SelectDestination(2), Is.True);
            yield return CompleteLeg(2);
            Assert.That(controller.ResolveIslandOption(1), Is.True);
            controller.AbandonVoyage();
            yield return WaitFor(() => controller.Phase == VoyagePhase.Departure, "abandon second voyage");
            Assert.That(controller.PendingReward.coins, Is.Zero);
            Assert.That(controller.HasCheckpoint, Is.False);
            Assert.That(store.Progress.coins, Is.EqualTo(earned.coins));
            Assert.That(store.Progress.specialItems, Is.EqualTo(earned.specialItems));
        }

        private IEnumerator CompleteLeg(int destinationStop)
        {
            bool checkedMotion = false;
            float until = Time.realtimeSinceStartup + 20f;
            while (!(controller.StopIndex == destinationStop && controller.Phase == VoyagePhase.Island && controller.BoardingProfile == null)
                && controller.Phase != VoyagePhase.Success)
            {
                QuiesceActors();
                Assert.That(controller.Phase, Is.Not.EqualTo(VoyagePhase.Error), controller.Message);
                Assert.That(controller.Phase, Is.Not.EqualTo(VoyagePhase.Failure));
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(until), "Timed out completing a branch: " + controller.Message);
                var enemies = controller.Enemies.Where(enemy => enemy != null && enemy.Health > 0f).ToArray();
                var obstacles = Object.FindObjectsOfType<DestructibleObstacle>();
                if (controller.Phase == VoyagePhase.Event && (enemies.Length > 0 || obstacles.Length > 0))
                {
                    if (!checkedMotion)
                    {
                        yield return AssertEventKeepsSailing();
                        checkedMotion = true;
                    }
                    foreach (var enemy in enemies)
                        if (enemy != null && enemy.Health > 0f)
                            Assert.That(controller.Vessel.DamageSystem.ApplyDamage(controller.Vessel.Player, enemy, enemy.Health), Is.True);
                    foreach (var obstacle in obstacles)
                        if (obstacle != null && !obstacle.IsInFlight)
                            obstacle.Clear();
                }
                yield return null;
            }
            QuiesceActors();
            if (controller.EventPlan[destinationStop - 1].kind != VoyageEventKind.None)
                Assert.That(checkedMotion, Is.True, "A planned sea event was skipped.");
        }

        private IEnumerator AssertEventKeepsSailing()
        {
            Transform motion = GameObject.Find("Environment/Sea Motion")?.transform;
            Assert.That(motion, Is.Not.Null);
            Vector3 position = motion.localPosition;
            float started = Time.time;
            yield return new WaitForSeconds(0.12f);
            QuiesceActors();
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Event));
            Assert.That(Time.time, Is.GreaterThan(started));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Assert.That(motion.localPosition.x, Is.LessThan(position.x));
            Transform wake = motion.parent.Find("Sailing Wake");
            Assert.That(wake.gameObject.activeSelf, Is.True);
            Assert.That(wake.GetComponent<Animator>().enabled, Is.True);
            Assert.That(controller.Vessel.Player.ActionsEnabled, Is.True);
            Assert.That(controller.Vessel.Player.GetComponent<Rigidbody2D>().simulated, Is.True);
        }

        private void AssertUpgrades(int[] combinedRanks)
        {
            VoyageVessel basis = definition.vesselPrefab.GetComponent<VoyageVessel>();
            PlayerCharacter player = controller.Vessel.Player;
            Assert.That(player.AttackPower, Is.EqualTo(basis.Player.AttackPower * (1f + combinedRanks[0] * 0.2f)).Within(0.001f));
            Assert.That(player.FireInterval, Is.EqualTo(basis.Player.FireInterval / (1f + combinedRanks[1] * 0.15f)).Within(0.001f));
            Assert.That(controller.Vessel.Boat.MaxHealth, Is.EqualTo(basis.Boat.MaxHealth + combinedRanks[2] * 150f));
            Assert.That(player.RepairSpeed, Is.EqualTo(basis.Player.RepairSpeed * (1f + combinedRanks[3] * 0.25f)).Within(0.001f));
            Assert.That(player.SimultaneousRepairs, Is.EqualTo(basis.Player.SimultaneousRepairs + combinedRanks[4]));
            Assert.That(player.RepairRange, Is.EqualTo(basis.Player.RepairRange + combinedRanks[5]));
        }

        private IEnumerator WaitFor(Func<bool> condition, string description)
        {
            float until = Time.realtimeSinceStartup + 12f;
            while (!condition())
            {
                QuiesceActors();
                Assert.That(controller.Phase, Is.Not.EqualTo(VoyagePhase.Error), controller.Message);
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(until), "Timed out waiting for " + description);
                yield return null;
            }
            QuiesceActors();
        }

        private void QuiesceActors()
        {
            Assert.That(SceneManager.GetActiveScene().handle, Is.EqualTo(mainHandle), "The expedition reloaded or left main.");
            if (controller.Vessel != null)
                foreach (var actor in controller.Vessel.GetComponentsInChildren<Character>())
                    actor.enabled = false;
            foreach (var enemy in controller.Enemies)
                if (enemy != null)
                    enemy.enabled = false;
        }

        private IEnumerator DestroyForCrash()
        {
            controller.StopAllCoroutines();
            Object.Destroy(controller.gameObject);
            controller = null;
            yield return null;
            yield return null;
            Assert.That(store.HasCheckpoint, Is.True);
        }

        private void CreateController()
        {
            store = new VoyageSaveStore(directory);
            controller = new GameObject("Expedition Test Voyage").AddComponent<VoyageController>();
            controller.Initialize(definition, store);
        }

        private T Clone<T>(T source) where T : ScriptableObject
        {
            T clone = Object.Instantiate(source);
            clones.Add(clone);
            return clone;
        }
    }
}
