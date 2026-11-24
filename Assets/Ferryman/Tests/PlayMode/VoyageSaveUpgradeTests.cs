using System;
using System.IO;
using System.Linq;
using Ferryman.Voyage;
using Ferryman.World;
using NUnit.Framework;

namespace Ferryman.Tests
{
    public sealed class VoyageSaveUpgradeTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "FerrymanSaveUpgradeTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FerrymanSaveUpgradeTests")) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(directory);
            Assert.That(target.StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(target))
                Directory.Delete(target, true);
        }

        [Test]
        public void FreePermanentUpgradesPreserveRewardsRespectCapsAndPersistThroughSettlement()
        {
            var store = new VoyageSaveStore(directory);
            Assert.That(store.TryBuyUpgrade(VoyageUpgradeKind.Durability), Is.True);
            Assert.That(store.Progress.coins, Is.Zero);
            Assert.That(store.Progress.specialItems, Is.Zero);
            store.Settle("funding", new RewardData { coins = 1000, specialItems = 30 });
            for (int rank = 0; rank < VoyageUpgrades.MaxRank; rank++)
            {
                int coins = store.Progress.coins;
                int items = store.Progress.specialItems;
                Assert.That(store.TryBuyUpgrade(VoyageUpgradeKind.Attack), Is.True);
                Assert.That(store.Progress.coins, Is.EqualTo(coins));
                Assert.That(store.Progress.specialItems, Is.EqualTo(items));
                Assert.That(store.TryBuyUpgrade(VoyageUpgradeKind.RepairCount), Is.True);
                Assert.That(store.Progress.specialItems, Is.EqualTo(items));
                Assert.That(store.Progress.coins, Is.EqualTo(coins));
            }
            Assert.That(store.TryBuyUpgrade(VoyageUpgradeKind.Attack), Is.False);
            Assert.That(store.TryBuyUpgrade(VoyageUpgradeKind.RepairCount), Is.False);
            Assert.That(store.TryBuyUpgrade((VoyageUpgradeKind)99), Is.False);
            store.Settle("next-run", new RewardData { coins = 10, specialItems = 2 });
            Assert.That(store.Settle("next-run", new RewardData { coins = 10, specialItems = 2 }), Is.False);
            var restored = new VoyageSaveStore(directory);
            Assert.That(restored.Progress.coins, Is.EqualTo(1010));
            Assert.That(restored.Progress.specialItems, Is.EqualTo(32));
            Assert.That(restored.Progress.upgrades[(int)VoyageUpgradeKind.Attack], Is.EqualTo(5));
            Assert.That(restored.Progress.upgrades[(int)VoyageUpgradeKind.RepairCount], Is.EqualTo(5));
            VoyageCheckpoint stale = CreateCheckpoint();
            stale.runId = "next-run";
            restored.SaveCheckpoint(stale);
            Assert.That(restored.LoadCheckpoint("test-voyage"), Is.Null);
            Assert.That(restored.HasCheckpoint, Is.False);
            Assert.That(new VoyageSaveStore(directory).Progress.upgrades, Is.EqualTo(restored.Progress.upgrades));
        }

        [Test]
        public void FailedPurchaseWritePreservesMemoryAndThePreviousSave()
        {
            var store = new VoyageSaveStore(directory);
            store.Settle("funding", new RewardData { coins = 100 });
            PermanentProgress previous = store.Progress;
            string saved = File.ReadAllText(Path.Combine(directory, "progress.json"));
            Directory.CreateDirectory(Path.Combine(directory, "progress.json.tmp"));
            Assert.That(() => store.TryBuyUpgrade(VoyageUpgradeKind.Attack), Throws.Exception);
            Assert.That(store.Progress, Is.SameAs(previous));
            Assert.That(store.Progress.coins, Is.EqualTo(100));
            Assert.That(store.Progress.upgrades[(int)VoyageUpgradeKind.Attack], Is.Zero);
            Assert.That(File.ReadAllText(Path.Combine(directory, "progress.json")), Is.EqualTo(saved));
        }

        [Test]
        public void BranchCheckpointRestoresEveryIslandAndSeaStopIncludingPendingChoices()
        {
            var store = new VoyageSaveStore(directory);
            int[] fullPath = { 0, 1, 4, 7, 10, 13, 16, 19 };
            for (int stop = 0; stop < 15; stop++)
            {
                VoyageCheckpoint checkpoint = CreateCheckpoint();
                checkpoint.stopIndex = stop;
                checkpoint.routePath = fullPath.Take((stop + 1) / 2 + 1).ToArray();
                checkpoint.runUpgrades = new[] { 1, 2, 3, 0, 0, 0 };
                checkpoint.islandResolved = true;
                checkpoint.upgradeChoices = new[] { VoyageUpgradeKind.Attack, VoyageUpgradeKind.RepairCount };
                store.SaveCheckpoint(checkpoint);
                VoyageCheckpoint restored = new VoyageSaveStore(directory).LoadCheckpoint("test-voyage");
                Assert.That(restored.stopIndex, Is.EqualTo(stop));
                Assert.That(restored.routePath, Is.EqualTo(checkpoint.routePath));
                Assert.That(restored.runUpgrades, Is.EqualTo(checkpoint.runUpgrades));
                Assert.That(restored.islandResolved, Is.True);
                Assert.That(restored.upgradeChoices, Is.EqualTo(checkpoint.upgradeChoices));
            }
        }

        [Test]
        public void InvalidBranchPathsAndUpgradeChoicesCannotReplaceASavedCheckpoint()
        {
            var store = new VoyageSaveStore(directory);
            store.SaveCheckpoint(CreateCheckpoint());
            string saved = File.ReadAllText(Path.Combine(directory, "checkpoint.json"));
            var invalid = new[]
            {
                new[] { 0, 19 }, new[] { 0, 1, 6 }, new[] { 0, 1, 0 }, new[] { 0, 1, 20 }, new[] { 1 }
            };
            foreach (int[] path in invalid)
            {
                VoyageCheckpoint checkpoint = CreateCheckpoint();
                checkpoint.stopIndex = 1;
                checkpoint.routePath = path;
                Assert.Throws<InvalidDataException>(() => store.SaveCheckpoint(checkpoint));
            }
            VoyageCheckpoint mismatched = CreateCheckpoint();
            mismatched.routePath = new[] { 0, 1 };
            Assert.Throws<InvalidDataException>(() => store.SaveCheckpoint(mismatched));
            mismatched.stopIndex = 15;
            Assert.Throws<InvalidDataException>(() => store.SaveCheckpoint(mismatched));
            var invalidChoices = new[]
            {
                new[] { VoyageUpgradeKind.Attack, VoyageUpgradeKind.Attack },
                new[] { (VoyageUpgradeKind)(-1) },
                new[] { VoyageUpgradeKind.Attack, VoyageUpgradeKind.AttackSpeed, VoyageUpgradeKind.Durability, VoyageUpgradeKind.RepairSpeed }
            };
            foreach (VoyageUpgradeKind[] choices in invalidChoices)
            {
                VoyageCheckpoint checkpoint = CreateCheckpoint();
                checkpoint.upgradeChoices = choices;
                Assert.Throws<InvalidDataException>(() => store.SaveCheckpoint(checkpoint));
            }
            VoyageCheckpoint maxed = CreateCheckpoint();
            maxed.runUpgrades = new[] { 5, 0, 0, 0, 0, 0 };
            maxed.upgradeChoices = new[] { VoyageUpgradeKind.Attack };
            Assert.Throws<InvalidDataException>(() => store.SaveCheckpoint(maxed));
            maxed.upgradeChoices = null;
            maxed.runUpgrades[0] = 6;
            Assert.Throws<InvalidDataException>(() => store.SaveCheckpoint(maxed));
            maxed.runUpgrades = new[] { 0 };
            Assert.Throws<InvalidDataException>(() => store.SaveCheckpoint(maxed));
            Assert.That(File.ReadAllText(Path.Combine(directory, "checkpoint.json")), Is.EqualTo(saved));
        }

        [Test]
        public void OldVersionOneFilesLoadWithoutNewFieldsAndKeepLinearRoutes()
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "progress.json"), "{\"version\":1,\"coins\":80,\"specialItems\":2,\"lastSettledRunId\":\"old-run\"}");
            File.WriteAllText(Path.Combine(directory, "checkpoint.json"), "{\"version\":1,\"voyageId\":\"test-voyage\",\"runId\":\"legacy\",\"stopIndex\":20,\"passengers\":[],\"ship\":{\"health\":1000,\"pendingDamage\":0,\"points\":[]}}");
            var store = new VoyageSaveStore(directory);
            Assert.That(store.Progress.upgrades, Is.EqualTo(new int[VoyageUpgrades.Count]));
            VoyageCheckpoint legacy = store.LoadCheckpoint("test-voyage");
            Assert.That(legacy.stopIndex, Is.EqualTo(20));
            Assert.That(legacy.routePath, Is.Null);
            Assert.That(VoyageUpgrades.ReadRanks(legacy.runUpgrades), Is.EqualTo(new int[VoyageUpgrades.Count]));
            legacy.routePath = new int[0];
            store.SaveCheckpoint(legacy);
            Assert.That(store.TryBuyUpgrade(VoyageUpgradeKind.Attack), Is.True);
            Assert.That(new VoyageSaveStore(directory).Progress.coins, Is.EqualTo(80));
        }

        [Test]
        public void InvalidPermanentRanksAreRejectedWithoutChangingTheFile()
        {
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "progress.json");
            string saved = "{\"version\":1,\"coins\":80,\"upgrades\":[6,0,0,0,0,0]}";
            File.WriteAllText(path, saved);
            Assert.Throws<InvalidDataException>(() => new VoyageSaveStore(directory));
            Assert.That(File.ReadAllText(path), Is.EqualTo(saved));
        }

        private static VoyageCheckpoint CreateCheckpoint()
        {
            return new VoyageCheckpoint
            {
                version = 1,
                voyageId = "test-voyage",
                runId = "active-run",
                seed = 7301,
                passengers = Array.Empty<PassengerState>(),
                ship = new BoatHealth.State { health = 1000f, points = Array.Empty<BoatHealth.PointState>() }
            };
        }
    }
}
