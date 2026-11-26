using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
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
            definition.storyCampaign = false;
            definition.sailingSeconds = 0.08f;
            definition.transitionSeconds = 0.04f;
            definition.boardingNoticeSeconds = 0.01f;
            var encounters = source.randomEncounters.Concat(source.stops.Select(stop => stop.fixedEncounter))
                .Where(item => item != null).ToArray();
            var shooter = encounters.SelectMany(item => item.waves).SelectMany(wave => wave.enemies)
                .Select(group => group.prefab).First(item => item.AttackType == EnemyCharacter.AttackKind.Projectile);
            var encounter = Clone(encounters[0]);
            encounter.waveInterval = 0.5f;
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
            Assert.That(controller.StartVoyage(7), Is.True);
            Assert.That(controller.Passengers, Is.Empty);
            Assert.That(store.LoadCheckpoint(definition.id).passengers, Is.Empty);
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
        public IEnumerator MapShowsBothPassengersOnlyAtFirstEligibleIslandsBeyondBattleStops()
        {
            Assert.That(controller.StartVoyage(7), Is.True);
            yield return WaitFor(() => controller.AwaitingRouteChoice, "departure route selection");
            VoyageCheckpoint checkpoint = store.LoadCheckpoint(definition.id);
            checkpoint.stopIndex = 2;
            checkpoint.routePath = new[] { 0, 1 };
            checkpoint.islandResolved = true;
            VoyageRouteMap map = VoyageRouteMap.Generate(checkpoint.seed, checkpoint.routePath);
            VoyageRouteNode current = map.Nodes[1];
            Assert.That(current.next.Length, Is.InRange(1, 2));
            var shared = new[] { definition.passengerPool[0], definition.passengerPool[4] };
            int destination = (current.depth + 1) * 2;
            checkpoint.passengers = shared.Select(profile => new PassengerState
            {
                profileId = profile.id, destinationStopIndex = destination, localPosition = checkpoint.playerLocalPosition
            }).ToArray();
            store.SaveCheckpoint(checkpoint);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.AwaitingRouteChoice, "route with two passengers for the same stop");

            var groupPassengers = typeof(VoyageExpeditionUI).GetMethod("PassengerBadgesByIsland",
                BindingFlags.Static | BindingFlags.NonPublic);
            var badges = (Dictionary<int, CrewDefinition[]>)groupPassengers.Invoke(null,
                new object[] { controller.RouteMap, controller });
            Assert.That(controller.RouteMap.Nodes[4].kind, Is.EqualTo(VoyageIslandKind.Battle));
            Assert.That(controller.RouteMap.Nodes[7].kind, Is.EqualTo(VoyageIslandKind.Battle));
            Assert.That(controller.RouteMap.Nodes[10].kind, Is.EqualTo(VoyageIslandKind.Elite));
            Assert.That(badges.Keys, Is.EquivalentTo(new[] { 8, 13, 14 }));
            foreach (CrewDefinition[] passengers in badges.Values)
                Assert.That(passengers, Is.EqualTo(shared));
        }

        [UnityTest]
        public IEnumerator BattleAndEliteStopsCarryDuePassengersUntilTheNextIsland()
        {
            Assert.That(controller.StartVoyage(7), Is.True);
            VoyageCheckpoint checkpoint = store.LoadCheckpoint(definition.id);
            checkpoint.stopIndex = 2;
            checkpoint.routePath = new[] { 0, 1 };
            checkpoint.islandResolved = true;
            CrewDefinition profile = definition.passengerPool[0];
            checkpoint.passengers = new[]
            {
                new PassengerState { profileId = profile.id, destinationStopIndex = 4, localPosition = new Vector2(6.5f, 1f) }
            };
            store.SaveCheckpoint(checkpoint);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            foreach (VoyageStop stop in controller.ActiveStops)
                stop.boardingChance = 0f;
            yield return WaitFor(() => controller.AwaitingRouteChoice, "battle route with due passenger");

            foreach (int nodeId in new[] { 4, 7, 10 })
            {
                Assert.That(controller.SelectDestination(nodeId), Is.True);
                yield return CompleteLeg(controller.RouteMap.Nodes[nodeId].depth * 2);
                Assert.That(controller.CurrentIsland, Is.EqualTo(nodeId == 10 ? VoyageIslandKind.Elite : VoyageIslandKind.Battle));
                Assert.That(controller.DeliveredPassengers, Is.Zero);
                Assert.That(controller.Passengers.Single().Definition, Is.SameAs(profile));
                Assert.That(controller.ResolveIslandOption(0), Is.True);
                Assert.That(controller.ChooseUpgrade(controller.UpgradeChoices[0]), Is.True);
            }
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            foreach (VoyageStop stop in controller.ActiveStops)
                stop.boardingChance = 0f;
            yield return WaitFor(() => controller.AwaitingRouteChoice, "restored elite stop with overdue passenger");
            Assert.That(controller.Passengers.Single().DestinationStopIndex, Is.EqualTo(4));
            Assert.That(controller.SelectDestination(13), Is.True);
            yield return CompleteLeg(10);
            Assert.That(controller.CurrentIsland, Is.EqualTo(VoyageIslandKind.Treasure));
            Assert.That(controller.Passengers, Is.Empty);
            Assert.That(controller.DeliveredPassengers, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator RescueSkipsRestoredDuplicateAnimalsAndCanLeaveWhenNoNewCrewIsAvailable()
        {
            CrewDefinition monkey = definition.passengerPool.Single(profile => profile.id == "gunner-monkey");
            CrewDefinition owl = definition.passengerPool.Single(profile => profile.id == "sharpshooter-owl");
            CrewDefinition beaver = definition.passengerPool.Single(profile => profile.id == "carpenter-beaver");
            definition.passengerPool = new[] { monkey, owl, beaver };
            Assert.That(controller.StartVoyage(7), Is.True);
            VoyageCheckpoint checkpoint = store.LoadCheckpoint(definition.id);
            checkpoint.stopIndex = 8;
            checkpoint.routePath = new[] { 0, 2, 6, 8, 12 };
            checkpoint.islandResolved = false;
            var monkeyPosition = new Vector2(6.5f, 1f);
            var beaverPosition = new Vector2(6.5f, -1f);
            checkpoint.passengers = new[]
            {
                new PassengerState { profileId = monkey.id, destinationStopIndex = 14, localPosition = monkeyPosition },
                new PassengerState { profileId = monkey.id, destinationStopIndex = 12, localPosition = new Vector2(7.5f, 0f) },
                new PassengerState { profileId = beaver.id, destinationStopIndex = 14, localPosition = beaverPosition }
            };
            store.SaveCheckpoint(checkpoint);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.Phase == VoyagePhase.Island, "restored rescue island");
            Assert.That(controller.CurrentIsland, Is.EqualTo(VoyageIslandKind.Rescue));
            Assert.That(controller.Passengers.Count, Is.EqualTo(2));
            Passenger firstMonkey = controller.Passengers.Single(item => item.Definition.id == monkey.id);
            Assert.That(firstMonkey.DestinationStopIndex, Is.EqualTo(14));
            Assert.That(Vector2.Distance(firstMonkey.transform.localPosition, monkeyPosition), Is.LessThan(0.001f));
            Assert.That(Vector2.Distance(controller.Passengers.Single(item => item.Definition.id == beaver.id).transform.localPosition,
                beaverPosition), Is.LessThan(0.001f));
            Assert.That(controller.ResolveIslandOption(0), Is.True);
            Assert.That(controller.Passengers.Select(item => item.Definition.id), Is.EquivalentTo(new[] { monkey.id, owl.id, beaver.id }));

            checkpoint = store.LoadCheckpoint(definition.id);
            checkpoint.islandResolved = false;
            store.SaveCheckpoint(checkpoint);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.Phase == VoyagePhase.Island, "rescue island with every animal aboard");
            Assert.That(controller.ResolveIslandOption(0), Is.False);
            Assert.That(controller.ResolveIslandOption(1), Is.False);
            Assert.That(controller.Passengers.Count, Is.EqualTo(3));
            Assert.That(controller.ResolveIslandOption(2), Is.True);
            Assert.That(controller.AwaitingRouteChoice, Is.True);
        }

        [UnityTest]
        public IEnumerator FullyUpgradedShipyardSkipsChoicesWithoutSpendingRewards()
        {
            Assert.That(controller.StartVoyage(7), Is.True);
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
            Assert.That(controller.BuyPermanentUpgrade(VoyageUpgradeKind.Attack), Is.True);
            Assert.That(controller.CanChoosePermanentUpgrade, Is.False);
            Assert.That(controller.BuyPermanentUpgrade(VoyageUpgradeKind.AttackSpeed), Is.False);
            // Seed ranks earned on earlier departures for the combined-upgrade restore checks.
            foreach (VoyageUpgradeKind kind in Enum.GetValues(typeof(VoyageUpgradeKind)))
                if (kind != VoyageUpgradeKind.Attack)
                    Assert.That(store.TryBuyUpgrade(kind), Is.True);
            Assert.That(controller.StartVoyage(7), Is.True);
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
            Assert.That(controller.CanChoosePermanentUpgrade, Is.True);
            Assert.That(controller.StartVoyage(8), Is.True);
            AssertUpgrades(Enumerable.Repeat(1, VoyageUpgrades.Count).ToArray());
        }

        [UnityTest]
        public IEnumerator FailedExpeditionSettlesOnceAndAbandonDiscardsTemporaryRewards()
        {
            Assert.That(controller.StartVoyage(7), Is.True);
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
            Assert.That(controller.StartVoyage(7), Is.True);
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

        [UnityTest]
        public IEnumerator StoryRunsThreeRoundsAndRestoresTheFinalAlliesBeforeOneSettlement()
        {
            definition.storyCampaign = true;
            Assert.That(Resources.Load<VoyageDefinition>("Voyage/DefaultVoyage").storyCampaign, Is.True);
            Assert.That(controller.StartVoyage(7), Is.True);
            VoyageCheckpoint departure = store.LoadCheckpoint(definition.id);
            string runId = departure.runId;
            string[] companions = departure.specialCompanionOrder;
            Assert.That(companions, Has.Length.EqualTo(3));
            Assert.That(companions, Is.Unique);
            string monkeyId = "gunner-monkey";
            string rhinoId = "mechanic-rhino";
            string owlId = "sharpshooter-owl";
            var storyOffers = new List<string>();
            bool Answer(CrewDefinition profile)
            {
                if (profile.id != monkeyId && profile.id != rhinoId && profile.id != owlId)
                    return false;
                Assert.That(controller.BoardingDestination, Is.EqualTo("终点港"));
                Assert.That(controller.BoardingDialogue, Is.Not.Empty);
                Assert.That(controller.BoardingOrigin, Is.Not.Empty);
                if (profile.id == owlId)
                    Assert.That(controller.BoardingConflict, Does.Contain("犀牛"));
                storyOffers.Add(profile.id);
                return true;
            }

            for (int round = 1; round <= VoyageController.FinalStoryRound; round++)
            {
                Assert.That(controller.StoryRound, Is.EqualTo(round));
                yield return WaitFor(() => controller.AwaitingRouteChoice, "story departure");
                for (int depth = 1; depth <= VoyageRouteMap.FinalDepth; depth++)
                {
                    Assert.That(controller.SelectDestination(controller.RouteMap.Nodes[controller.CurrentNodeId].next[0]), Is.True);
                    if (round == 1 && depth == 1)
                    {
                        yield return WaitFor(() => controller.AwaitingStoryDialogue, "first companion introduction");
                        string introduction = controller.StoryDialogueLine;
                        Assert.That(store.LoadCheckpoint(definition.id).specialCompanions, Is.Empty);
                        yield return DestroyForCrash();
                        CreateController();
                        Assert.That(controller.ResumeVoyage(), Is.True);
                        yield return WaitFor(() => controller.AwaitingStoryDialogue, "restored companion introduction");
                        Assert.That(controller.StoryDialogueLine, Is.EqualTo(introduction));
                        Assert.That(controller.SkipStoryDialogue(), Is.True);
                        yield return WaitFor(() => controller.Vessel.GetComponentInChildren<SpecialCompanion>() != null,
                            "companion after skipping the introduction");
                        Assert.That(store.LoadCheckpoint(definition.id).specialCompanions.Single().profileId, Is.EqualTo(companions[0]));
                    }
                    yield return CompleteLeg(depth * 2, Answer);
                    if (depth == VoyageRouteMap.FinalDepth)
                        break;
                    int option = controller.CurrentIsland switch
                    {
                        VoyageIslandKind.Rescue => 2,
                        VoyageIslandKind.Market => 4,
                        VoyageIslandKind.Rest => 1,
                        _ => 0
                    };
                    Assert.That(controller.ResolveIslandOption(option), Is.True, controller.CurrentIsland.ToString());
                    if (controller.UpgradeChoices.Length > 0)
                        Assert.That(controller.ChooseUpgrade(controller.UpgradeChoices[0]), Is.True);
                }

                yield return WaitFor(() => controller.AwaitingStoryDeparture, "terminal port action");
                Assert.That(controller.Passengers, Is.Empty);
                Assert.That(store.Progress.lastSettledRunId, Is.Null.Or.Empty);
                VoyageCheckpoint port = store.LoadCheckpoint(definition.id);
                Assert.That(port.runId, Is.EqualTo(runId));
                Assert.That(port.storyRound, Is.EqualTo(round));
                Assert.That(port.stopIndex, Is.EqualTo(14));
                Assert.That(port.specialCompanionOrder, Is.EqualTo(companions));
                Assert.That(port.specialCompanions.Select(state => state.profileId), Is.EqualTo(companions.Take(round)));
                Assert.That(controller.Vessel.GetComponentsInChildren<SpecialCompanion>(), Has.Length.EqualTo(round));
                if (round < VoyageController.FinalStoryRound)
                {
                    controller.Vessel.Boat.ApplyDamage(23f, Vector2.zero);
                    float health = controller.Vessel.Boat.CurrentHealth;
                    Assert.That(controller.ContinueStoryVoyage(), Is.True);
                    Assert.That(controller.ContinueStoryVoyage(), Is.False);
                    yield return WaitFor(() => controller.StoryRound == round + 1, "saved next round");
                    VoyageCheckpoint next = store.LoadCheckpoint(definition.id);
                    Assert.That(next.routePath, Is.EqualTo(new[] { 0 }));
                    Assert.That(next.runUpgrades, Is.EqualTo(port.runUpgrades));
                    Assert.That(next.pendingCoins, Is.EqualTo(port.pendingCoins));
                    Assert.That(next.runId, Is.EqualTo(runId));
                    yield return DestroyForCrash();
                    CreateController();
                    Assert.That(controller.ResumeVoyage(), Is.True);
                    Assert.That(controller.Vessel.Boat.CurrentHealth, Is.EqualTo(health));
                    Assert.That(controller.Vessel.GetComponentsInChildren<SpecialCompanion>(), Has.Length.EqualTo(round));
                }
            }

            Assert.That(storyOffers, Is.EqualTo(new[] { monkeyId, rhinoId, owlId }));
            var finalPort = store.LoadCheckpoint(definition.id);
            Assert.That(finalPort.deliveredStoryProfiles, Is.EquivalentTo(storyOffers));
            finalPort.deliveredStoryProfiles = finalPort.deliveredStoryProfiles
                .Select(id => id == owlId ? "lookout-rabbit" : id).ToArray();
            store.SaveCheckpoint(finalPort);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.AwaitingStoryDeparture, "restored final port");
            Assert.That(controller.ContinueStoryVoyage(), Is.True);
            yield return WaitFor(() => controller.StopIndex == 15, "saved Boss entrance");
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.Enemies.Any(enemy => enemy != null), "restored Boss");
            string[] allies = controller.Vessel.GetComponentsInChildren<Passenger>().Select(crew => crew.Definition.id).ToArray();
            Assert.That(allies, Is.EquivalentTo(storyOffers));
            Assert.That(controller.Vessel.GetComponentsInChildren<SpecialCompanion>(), Has.Length.EqualTo(3));
            EnemyCharacter boss = controller.Enemies.Single();
            Assert.That(controller.Vessel.DamageSystem.ApplyDamage(controller.Vessel.Player, boss, boss.Health), Is.True);
            yield return WaitFor(() => controller.AwaitingStoryDialogue && controller.Phase == VoyagePhase.Sailing,
                "recovery dialogue at sea");
            Assert.That(controller.StopIndex, Is.EqualTo(15));
            Assert.That(store.LoadCheckpoint(definition.id).stopIndex, Is.EqualTo(15));
            yield return WaitFor(() => controller.AwaitingStoryDialogue && controller.Phase == VoyagePhase.Island,
                "the captain returned to port");
            Assert.That(controller.StopIndex, Is.EqualTo(14));
            Assert.That(controller.AwaitingStoryDeparture, Is.False);
            Assert.That(store.HasCheckpoint, Is.True);
            yield return WaitFor(() => controller.Phase == VoyagePhase.Success, "campaign settlement");
            Assert.That(store.HasCheckpoint, Is.False);
            Assert.That(store.Progress.lastSettledRunId, Is.EqualTo(runId));
            Assert.That(store.Progress.coins, Is.EqualTo(controller.PendingReward.coins));
        }

        [UnityTest]
        public IEnumerator DeclinedRescueStaysResolvedAndTheCaptainCanBeatTheBossWithoutAllies()
        {
            definition.storyCampaign = true;
            Assert.That(controller.StartVoyage(7), Is.True);
            VoyageCheckpoint checkpoint = store.LoadCheckpoint(definition.id);
            checkpoint.stopIndex = 8;
            checkpoint.routePath = ActivityPath.Take(5).ToArray();
            checkpoint.islandResolved = false;
            checkpoint.storyDockOfferResolved = true;
            store.SaveCheckpoint(checkpoint);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.Phase == VoyagePhase.Island && !controller.AwaitingStoryDialogue, "rescue island");
            Assert.That(controller.CurrentIsland, Is.EqualTo(VoyageIslandKind.Rescue));
            Assert.That(controller.ResolveIslandOption(0), Is.True);
            Assert.That(controller.BoardingIsChoice, Is.True);
            Assert.That(controller.Passengers, Is.Empty);
            Assert.That(controller.SelectDestination(ActivityPath[5]), Is.False);
            Assert.That(controller.ResolveBoardingOffer(false), Is.True);
            Assert.That(controller.ResolveBoardingOffer(true), Is.False);
            Assert.That(controller.Passengers, Is.Empty);
            checkpoint = store.LoadCheckpoint(definition.id);
            Assert.That(checkpoint.islandResolved, Is.True);
            Assert.That(checkpoint.passengers, Is.Empty);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.AwaitingRouteChoice, "rescue decision restored");
            Assert.That(controller.BoardingProfile, Is.Null);

            checkpoint.storyRound = VoyageController.FinalStoryRound;
            checkpoint.stopIndex = 14;
            checkpoint.routePath = new[] { 0, 1, 4, 7, 10, 13, 16, 19 };
            store.SaveCheckpoint(checkpoint);
            yield return DestroyForCrash();
            CreateController();
            Assert.That(controller.ResumeVoyage(), Is.True);
            yield return WaitFor(() => controller.AwaitingStoryDeparture, "final port with no passengers");
            Assert.That(controller.ContinueStoryVoyage(), Is.True);
            yield return WaitFor(() => controller.Enemies.Any(enemy => enemy != null), "solo final battle");
            Assert.That(controller.Vessel.GetComponentsInChildren<Passenger>(), Is.Empty);

            PlayerCharacter captain = controller.Vessel.Player;
            EnemyCharacter boss = controller.Enemies.Single();
            captain.ConfigureCombat(captain.DamageSystem, captain.ProjectilePrefab, boss.Health / 8f, captain.FireInterval);
            boss.enabled = true;
            bool sawBomb = false;
            float until = Time.realtimeSinceStartup + 24f;
            while (controller.Phase != VoyagePhase.Success)
            {
                Assert.That(controller.Phase, Is.Not.EqualTo(VoyagePhase.Error), controller.Message);
                Assert.That(controller.Phase, Is.Not.EqualTo(VoyagePhase.Failure));
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(until), "The captain could not defeat the boss with normal projectiles.");
                sawBomb |= Object.FindObjectsOfType<TimedBomb>().Length > 0;
                if (controller.AwaitingStoryDialogue)
                    controller.ContinueStoryDialogue();
                else if (sawBomb && boss != null && boss.Health > 0f)
                    captain.TryFire(boss.Hitbox.bounds.center - captain.transform.position);
                yield return null;
            }
            Assert.That(sawBomb, Is.True);
            Assert.That(controller.Vessel.Boat.CurrentHealth, Is.GreaterThan(0f));
            Assert.That(Object.FindObjectsOfType<TimedBomb>(), Is.Empty);
        }

        [UnityTest]
        public IEnumerator StoryWavesUnlockEnemiesAndGrowAcrossRounds()
        {
            var source = Resources.Load<VoyageDefinition>("Voyage/DefaultVoyage");
            definition.storyCampaign = true;
            definition.randomEncounters = source.randomEncounters;
            var fixedEncounter = source.stops.First(stop => stop.fixedEncounter != null).fixedEncounter;
            definition.stops[1].fixedEncounter = fixedEncounter;
            var unlocks = definition.randomEncounters.Append(fixedEncounter)
                .SelectMany(encounter => encounter.waves).SelectMany(wave => wave.enemies)
                .GroupBy(group => group.prefab).ToDictionary(groups => groups.Key, groups => groups.Min(group => group.firstStoryLeg));

            Assert.That(controller.StartVoyage(7), Is.True);
            var checkpoint = store.LoadCheckpoint(definition.id);
            int[] expectedUnits = { 6, 9, 12 };
            int[] expectedTypes = { 3, 4, 4 };
            for (int round = 1; round <= VoyageController.FinalStoryRound; round++)
            {
                checkpoint.storyRound = round;
                checkpoint.stopIndex = 14;
                checkpoint.routePath = new[] { 0, 1, 4, 7, 10, 13, 16, 19 };
                store.SaveCheckpoint(checkpoint);
                yield return DestroyForCrash();
                CreateController();
                Assert.That(controller.ResumeVoyage(), Is.True);

                if (round == 1)
                {
                    var opening = controller.EventPlan[1].encounter.waves.Single().enemies.Single();
                    Assert.That(opening.count, Is.EqualTo(3));
                    Assert.That(opening.prefab, Is.SameAs(fixedEncounter.waves[0].enemies[0].prefab));
                }
                for (int depth = 1; depth <= VoyageRouteMap.FinalDepth; depth++)
                {
                    var encounter = controller.EventPlan[depth * 2 - 1].encounter;
                    if (encounter == null)
                        continue;
                    int leg = (round - 1) * VoyageRouteMap.FinalDepth + depth;
                    foreach (var group in encounter.waves.SelectMany(wave => wave.enemies))
                    {
                        Assert.That(unlocks[group.prefab], Is.LessThanOrEqualTo(leg));
                        if (group.prefab.AttackType == EnemyCharacter.AttackKind.BoardingMelee)
                            Assert.That(group.count, Is.EqualTo(1));
                    }
                }
                var finalWaves = controller.EventPlan[13].encounter.waves;
                for (int waveIndex = 0; waveIndex < finalWaves.Length; waveIndex++)
                {
                    Assert.That(finalWaves[waveIndex].enemies.Sum(group => group.count),
                        Is.EqualTo(Mathf.Min(12, expectedUnits[round - 1] + waveIndex)));
                    Assert.That(finalWaves[waveIndex].enemies.Length, Is.EqualTo(expectedTypes[round - 1]));
                }
            }
        }

        private IEnumerator CompleteLeg(int destinationStop, Func<CrewDefinition, bool> answer = null)
        {
            bool checkedMotion = false;
            float until = Time.realtimeSinceStartup + 20f;
            while (!(controller.StopIndex == destinationStop && controller.Phase == VoyagePhase.Island
                && controller.BoardingProfile == null && (answer == null || controller.Message.StartsWith("抵达 ", StringComparison.Ordinal)))
                && controller.Phase != VoyagePhase.Success)
            {
                QuiesceActors();
                if (controller.AwaitingStoryDialogue)
                    Assert.That(controller.ContinueStoryDialogue(), Is.True);
                if (answer != null && controller.BoardingIsChoice)
                    Assert.That(controller.ResolveBoardingOffer(answer(controller.BoardingProfile)), Is.True);
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
                if (controller.AwaitingStoryDialogue)
                    controller.ContinueStoryDialogue();
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
