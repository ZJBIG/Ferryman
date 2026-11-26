using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ferryman.Characters;
using UnityEngine;
using Random = System.Random;

namespace Ferryman.Voyage
{
    public sealed partial class VoyageController
    {
        private VoyageStop[] expeditionStops;
        private readonly List<int> routePath = new List<int>();
        private readonly List<EncounterDefinition> expeditionEncounters = new List<EncounterDefinition>();
        private int[] runUpgrades = new int[VoyageUpgrades.Count];
        private bool permanentUpgradeChosen;
        internal static readonly VoyageUpgradeKind[] MarketUpgrades =
        {
            VoyageUpgradeKind.Attack, VoyageUpgradeKind.AttackSpeed, VoyageUpgradeKind.RepairSpeed
        };
        internal static readonly VoyageUpgradeKind[] FinalPortUpgrades =
        {
            VoyageUpgradeKind.Attack, VoyageUpgradeKind.AttackSpeed, VoyageUpgradeKind.Durability, VoyageUpgradeKind.RepairSpeed
        };

        public VoyageRouteMap RouteMap { get; private set; }
        public IReadOnlyList<int> RoutePath => routePath;
        public int CurrentNodeId => routePath.Count == 0 ? 0 : routePath[routePath.Count - 1];
        public VoyageIslandKind CurrentIsland => RouteMap == null ? VoyageIslandKind.Departure : RouteMap.Nodes[CurrentNodeId].kind;
        public bool IsExpedition => RouteMap != null;
        public VoyageStop[] ActiveStops => expeditionStops ?? definition.stops;
        public bool IslandResolved { get; private set; }
        public VoyageUpgradeKind[] UpgradeChoices { get; private set; } = Array.Empty<VoyageUpgradeKind>();
        public int[] RunUpgrades => (int[])runUpgrades.Clone();
        public int[] PermanentUpgrades => VoyageUpgrades.ReadRanks(Progress?.upgrades);
        public bool CanChoosePermanentUpgrade => !ActiveRun && Phase == VoyagePhase.Departure
            && !HasCheckpoint && !permanentUpgradeChosen;
        public int AvailableCoins => pendingReward.coins;
        public int AvailableSpecialItems => pendingReward.specialItems;
        public bool CanUseFinalPortShop => AwaitingStoryDeparture && StoryRound == FinalStoryRound && Time.timeScale > 0f;
        public bool AwaitingRouteChoice => IsExpedition && ActiveRun && Phase == VoyagePhase.Island
            && BoardingProfile == null && !AwaitingStoryDialogue && StopIndex % 2 == 0 && IslandResolved && UpgradeChoices.Length == 0
            && RouteMap.Nodes[CurrentNodeId].depth < VoyageRouteMap.FinalDepth;

        private void InitializeExpedition(bool enabled, VoyageCheckpoint checkpoint)
        {
            ClearExpedition();
            if (!enabled)
            {
                eventPlan = VoyageEventPlanner.Build(definition, seed);
                return;
            }
            RouteMap = VoyageRouteMap.Generate(ExpeditionSeed, checkpoint?.routePath);
            routePath.AddRange(checkpoint?.routePath ?? new[] { 0 });
            RouteMap.ValidatePath(routePath.ToArray());
            runUpgrades = VoyageUpgrades.ReadRanks(checkpoint?.runUpgrades, checkpoint?.CanHaveFinalPortUpgrades == true);
            IslandResolved = checkpoint == null || checkpoint.islandResolved;
            UpgradeChoices = checkpoint?.upgradeChoices ?? Array.Empty<VoyageUpgradeKind>();
            expeditionStops = new VoyageStop[DestinationStop + (StoryRound == FinalStoryRound ? 2 : 1)];
            eventPlan = new PlannedVoyageEvent[expeditionStops.Length];
            for (int i = 0; i < expeditionStops.Length; i++)
            {
                bool island = i % 2 == 0;
                expeditionStops[i] = new VoyageStop
                {
                    kind = island ? VoyageStopKind.Island : VoyageStopKind.Sea,
                    sceneName = i == 0 ? "Departure" : i == DestinationStop ? "FinalIsland" : island ? "Island" : "Sea",
                    displayName = i == 0 ? "出发岛" : i == DestinationStop ? "终点港" : island ? "第 " + (i / 2) + " 层岛屿" : "群岛航路",
                    boardingChance = island && i > 0 ? 0.4f : 0f,
                    maxBoardingPassengers = island && i > 0 ? 1 : 0
                };
                eventPlan[i] = new PlannedVoyageEvent();
            }
            for (int i = 1; i < routePath.Count; i++)
                PrepareLeg(RouteMap.Nodes[routePath[i]]);
            if (StoryRound == FinalStoryRound)
            {
                expeditionStops[BossStop].displayName = "终点港外海";
                eventPlan[BossStop] = new PlannedVoyageEvent
                {
                    kind = VoyageEventKind.Combat, encounter = definition.finalBossEncounter, isFixed = true
                };
            }
        }

        private void PrepareLeg(VoyageRouteNode node)
        {
            int seaIndex = node.depth * 2 - 1;
            expeditionStops[seaIndex + 1].displayName = node.Name;
            expeditionStops[seaIndex].displayName = "前往" + node.Name;
            var random = new Random(ExpeditionSeed ^ node.id * 7919);
            int storyLeg = StoryRound > 0 ? (StoryRound - 1) * VoyageRouteMap.FinalDepth + node.depth : 0;
            double combatChance = StoryRound > 0 ? (StoryRound + 1) / 5d
                : node.depth <= 2 ? 0.2f : node.depth <= 4 ? 0.3f : 0.4f;
            bool fixedCombat = node.kind == VoyageIslandKind.Battle || node.kind == VoyageIslandKind.Elite
                || node.kind == VoyageIslandKind.Destination;
            if (fixedCombat || random.NextDouble() < combatChance)
            {
                EncounterDefinition[] sources = definition.randomEncounters
                    .Concat(definition.stops.Select(stop => stop.fixedEncounter)).Where(item => item != null).Distinct().ToArray();
                if (sources.Length == 0)
                    throw new InvalidDataException("分支航路需要海战配置。");
                EncounterDefinition source = sources[random.Next(sources.Length)];
                var encounter = ScriptableObject.CreateInstance<EncounterDefinition>();
                expeditionEncounters.Add(encounter);
                encounter.displayName = node.kind == VoyageIslandKind.Destination ? "最后的封锁线"
                    : node.kind == VoyageIslandKind.Elite ? "精英海域"
                    : StoryRound > 0 && !fixedCombat ? "海怪来袭" : "群岛海战";
                encounter.cameraSize = source.cameraSize;
                encounter.cameraHeight = source.cameraHeight;
                encounter.waveInterval = source.waveInterval;
                int waves = node.depth <= 2 ? 1 : 2;
                if (node.depth == VoyageRouteMap.FinalDepth || node.kind == VoyageIslandKind.Elite && node.depth >= 5)
                    waves = 3;
                if (StoryRound >= 2)
                    waves = Mathf.Min(4, waves + 1);
                if (storyLeg > 0)
                {
                    EnemySpawn[] available = sources.SelectMany(item => item.waves).SelectMany(wave => wave.enemies)
                        .Where(group => group.firstStoryLeg <= storyLeg)
                        .GroupBy(group => group.prefab)
                        .Select(groups => groups.OrderBy(group => group.firstStoryLeg).First()).ToArray();
                    if (available.Length == 0)
                        throw new InvalidDataException("剧情航路当前没有可用的海怪。");
                    encounter.waves = Enumerable.Range(0, waves)
                        .Select(wave => BuildStoryWave(available, storyLeg, wave, random)).ToArray();
                }
                else
                {
                    encounter.waves = Enumerable.Range(0, waves).Select(wave => new WaveDefinition
                    {
                        enemies = source.waves[wave % source.waves.Length].enemies.Select((group, groupIndex) => new EnemySpawn
                        {
                            prefab = group.prefab,
                            spawnPosition = group.spawnPosition,
                            count = group.prefab.AttackType == EnemyCharacter.AttackKind.BoardingMelee ? group.count
                                : Mathf.Min(4, group.count + 1 + (node.depth - 1 + wave + groupIndex
                                    + (node.kind == VoyageIslandKind.Elite ? 2 : 0)) / 3)
                        }).ToArray()
                    }).ToArray();
                }
                encounter.reward = new RewardData
                {
                    coins = (30 + node.depth * 10) * (node.kind == VoyageIslandKind.Elite ? 2 : 1),
                    specialItems = node.kind == VoyageIslandKind.Elite || node.kind == VoyageIslandKind.Destination ? 2 : 1
                };
                eventPlan[seaIndex] = new PlannedVoyageEvent
                {
                    kind = VoyageEventKind.Combat,
                    encounter = encounter,
                    isFixed = StoryRound == 0 || fixedCombat
                };
            }
            else
            {
                SeaEventDefinition[] specials = definition.specialEvents.Where(entry => entry.maxAppearances > 0)
                    .Select(entry => entry.definition)
                    .Where(item => item != null).ToArray();
                if (specials.Length > 0 && random.NextDouble() < 0.65)
                    eventPlan[seaIndex] = new PlannedVoyageEvent
                    {
                        kind = VoyageEventKind.Special,
                        specialEvent = specials[random.Next(specials.Length)]
                    };
            }
        }

        private static WaveDefinition BuildStoryWave(EnemySpawn[] available, int storyLeg, int wave, Random random)
        {
            int unitBudget = Mathf.Min(12, 3 + (storyLeg - 1) / 2 + wave);
            int typeBudget = Mathf.Min(4, 2 + (storyLeg - 1) / 5);
            var groups = available.OrderBy(group => wave == 0 && group.firstStoryLeg == storyLeg ? 0 : 1)
                .ThenBy(_ => random.Next())
                .Take(Mathf.Min(unitBudget, typeBudget))
                .Select(group => new EnemySpawn { prefab = group.prefab, spawnPosition = group.spawnPosition }).ToArray();
            EnemySpawn[] repeatable = groups.Where(group => group.prefab.AttackType != EnemyCharacter.AttackKind.BoardingMelee).ToArray();
            for (int i = groups.Length; i < unitBudget && repeatable.Length > 0; i++)
                repeatable[(i - groups.Length) % repeatable.Length].count++;
            return new WaveDefinition { enemies = groups };
        }

        public bool SelectDestination(int nodeId)
        {
            if (!AwaitingRouteChoice || !RouteMap.CanTravel(CurrentNodeId, nodeId))
                return false;
            PrepareLeg(RouteMap.Nodes[nodeId]);
            routePath.Add(nodeId);
            IslandResolved = false;
            StoryDockOfferResolved = false;
            UpgradeChoices = Array.Empty<VoyageUpgradeKind>();
            // Save the sea entrance now so a crash during departure keeps the chosen branch.
            StopIndex++;
            SaveCheckpoint();
            VoyageAudio.Play(Sound.Confirm);
            return true;
        }

        public bool ResolveIslandOption(int option)
        {
            if (!IsExpedition || !ActiveRun || Phase != VoyagePhase.Island || BoardingProfile != null || StopIndex % 2 != 0
                || AwaitingStoryDialogue || IslandResolved || UpgradeChoices.Length > 0 || option < 0)
                return false;
            int coinsBefore = pendingReward.coins;
            switch (CurrentIsland)
            {
                case VoyageIslandKind.Rest:
                    if (option > 1) return false;
                    if (option == 0) vessel.Boat.Resupply(300f);
                    else AddReward(new RewardData { coins = 40 });
                    break;
                case VoyageIslandKind.Market:
                    if (option < 3)
                    {
                        if (!BuyRunUpgrade(MarketUpgrades[option])) return false;
                        SaveCheckpoint();
                        VoyageAudio.Play(Sound.Upgrade, 0.70f);
                        return true;
                    }
                    if (option == 3)
                    {
                        if (pendingReward.coins < 40 || vessel.Boat.CurrentHealth >= vessel.Boat.MaxHealth) return false;
                        pendingReward.coins -= 40;
                        vessel.Boat.Resupply(200f);
                        SaveCheckpoint();
                        return true;
                    }
                    if (option != 4) return false;
                    break;
                case VoyageIslandKind.Rescue:
                    if (option == 2) break;
                    if (option > 1) return false;
                    CrewRole role = option == 0 ? CrewRole.Combat : CrewRole.Maintenance;
                    if (StoryRound > 0)
                        return OfferStoryRescue(role);
                    CrewDefinition profile = definition.passengerPool.FirstOrDefault(item => item.role == role && !HasPassenger(item));
                    if (profile == null) return false;
                    int destination = Mathf.Min(ActiveStops.Length - 1, StopIndex + 4);
                    BoardPassenger(profile, destination);
                    break;
                case VoyageIslandKind.Mystery:
                    if (option == 0)
                    {
                        if (vessel.Boat.CurrentHealth <= 100f) return false;
                        vessel.Boat.ApplyDamage(100f, vessel.Boat.Deck.transform.position);
                        AddReward(new RewardData { coins = 80, specialItems = 1 });
                    }
                    else if (option == 1)
                    {
                        OfferUpgrades();
                    }
                    else if (option != 2) return false;
                    break;
                case VoyageIslandKind.Treasure:
                    if (option != 0) return false;
                    AddReward(new RewardData { coins = 60, specialItems = 1 });
                    OfferUpgrades();
                    break;
                case VoyageIslandKind.Shipyard:
                case VoyageIslandKind.Battle:
                case VoyageIslandKind.Elite:
                    if (option != 0) return false;
                    OfferUpgrades();
                    break;
                default:
                    return false;
            }
            IslandResolved = true;
            SaveCheckpoint();
            if (pendingReward.coins > coinsBefore)
                VoyageAudio.Play(Sound.Reward, 0.64f);
            return true;
        }

        private void OfferUpgrades()
        {
            var random = new Random(ExpeditionSeed ^ CurrentNodeId * 3571);
            UpgradeChoices = Enumerable.Range(0, VoyageUpgrades.Count)
                .Where(index => runUpgrades[index] < VoyageUpgrades.MaxRank)
                .OrderBy(index => random.Next()).Take(3).Select(index => (VoyageUpgradeKind)index).ToArray();
        }

        public bool ChooseUpgrade(VoyageUpgradeKind kind)
        {
            if (!ActiveRun || !IsExpedition || Phase != VoyagePhase.Island || !UpgradeChoices.Contains(kind))
                return false;
            runUpgrades[(int)kind]++;
            UpgradeChoices = Array.Empty<VoyageUpgradeKind>();
            ApplyUpgrades();
            SaveCheckpoint();
            VoyageAudio.Play(Sound.Upgrade, 0.70f);
            return true;
        }

        public bool BuyFinalPortUpgrade(VoyageUpgradeKind kind)
        {
            if (!CanUseFinalPortShop || !FinalPortUpgrades.Contains(kind) || !BuyRunUpgrade(kind, finalPort: true))
                return false;
            SaveCheckpoint();
            VoyageAudio.Play(Sound.Upgrade, 0.70f);
            return true;
        }

        private bool BuyRunUpgrade(VoyageUpgradeKind kind, bool finalPort = false)
        {
            int rank = runUpgrades[(int)kind];
            long price = VoyageUpgrades.Price(kind, rank);
            bool special = VoyageUpgrades.UsesSpecialItems(kind);
            if ((!finalPort && rank >= VoyageUpgrades.MaxRank) || (special ? pendingReward.specialItems : pendingReward.coins) < price)
                return false;
            if (special) pendingReward.specialItems -= (int)price;
            else pendingReward.coins -= (int)price;
            runUpgrades[(int)kind]++;
            ApplyUpgrades();
            return true;
        }

        public bool BuyPermanentUpgrade(VoyageUpgradeKind kind)
        {
            if (!CanChoosePermanentUpgrade || !store.TryBuyUpgrade(kind))
                return false;
            permanentUpgradeChosen = true;
            VoyageAudio.Play(Sound.Upgrade, 0.70f);
            return true;
        }

        private void ApplyUpgrades()
        {
            int[] ranks = PermanentUpgrades;
            for (int i = 0; i < ranks.Length; i++) ranks[i] += runUpgrades[i];
            var basis = definition.vesselPrefab.GetComponent<VoyageVessel>();
            PlayerCharacter player = vessel.Player;
            player.ConfigureCombat(vessel.DamageSystem, basis.Player.ProjectilePrefab,
                basis.Player.AttackPower * (1f + ranks[(int)VoyageUpgradeKind.Attack] * 0.2f),
                basis.Player.FireInterval / (1f + ranks[(int)VoyageUpgradeKind.AttackSpeed] * 0.15f));
            player.ConfigureRepair(basis.Player.RepairSpeed * (1f + ranks[(int)VoyageUpgradeKind.RepairSpeed] * 0.25f),
                basis.Player.RepairRange + ranks[(int)VoyageUpgradeKind.RepairRange],
                basis.Player.SimultaneousRepairs + ranks[(int)VoyageUpgradeKind.RepairCount]);
            vessel.Boat.ConfigureMaximumHealth(basis.Boat.MaxHealth + ranks[(int)VoyageUpgradeKind.Durability] * 150f);
        }

        private void ClearExpedition()
        {
            foreach (var encounter in expeditionEncounters)
                if (encounter != null) Destroy(encounter);
            expeditionEncounters.Clear();
            RouteMap = null;
            expeditionStops = null;
            routePath.Clear();
            runUpgrades = new int[VoyageUpgrades.Count];
            IslandResolved = false;
            UpgradeChoices = Array.Empty<VoyageUpgradeKind>();
        }
    }
}
