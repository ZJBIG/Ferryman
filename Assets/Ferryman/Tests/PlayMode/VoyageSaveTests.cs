using System;
using System.IO;
using Ferryman.Voyage;
using Ferryman.World;
using NUnit.Framework;

namespace Ferryman.Tests
{
    public sealed class VoyageSaveTests
    {
        private string directory;

        [SetUp]
        public void SetUp()
        {
            directory = Path.Combine(Path.GetTempPath(), "FerrymanSaveTests", Guid.NewGuid().ToString("N"));
        }

        [TearDown]
        public void TearDown()
        {
            string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FerrymanSaveTests")) + Path.DirectorySeparatorChar;
            string target = Path.GetFullPath(directory);
            Assert.That(target.StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
            if (Directory.Exists(directory))
                Directory.Delete(target, true);
        }

        [Test]
        public void CheckpointAndAbandonPreserveProgressAndSettlementPaysOnce()
        {
            var store = new VoyageSaveStore(directory);
            store.Settle("older-run", new RewardData { coins = 15, specialItems = 2 });
            VoyageCheckpoint checkpoint = CreateCheckpoint("active-run");
            store.SaveCheckpoint(checkpoint);
            var restarted = new VoyageSaveStore(directory);
            Assert.That(restarted.Progress.coins, Is.EqualTo(15));
            Assert.That(restarted.Progress.specialItems, Is.EqualTo(2));
            VoyageCheckpoint loaded = restarted.LoadCheckpoint("test-voyage");
            Assert.That(loaded.stopIndex, Is.EqualTo(1));
            Assert.That(loaded.pendingCoins, Is.EqualTo(40));
            Assert.That(loaded.ship.health, Is.EqualTo(850f));
            Assert.That(loaded.ship.pendingDamage, Is.EqualTo(50f));
            Assert.That(loaded.ship.points[0].progress, Is.EqualTo(30f));
            restarted.AbandonCheckpoint();
            Assert.That(new VoyageSaveStore(directory).Progress.coins, Is.EqualTo(15));
            Assert.That(restarted.HasCheckpoint, Is.False);

            restarted.SaveCheckpoint(checkpoint);
            var reward = new RewardData { coins = loaded.pendingCoins, specialItems = loaded.pendingSpecialItems };
            Assert.That(restarted.Settle(loaded.runId, reward), Is.True);
            Assert.That(restarted.Settle(loaded.runId, reward), Is.False);
            var after = new VoyageSaveStore(directory);
            Assert.That(after.Progress.coins, Is.EqualTo(55));
            Assert.That(after.Progress.specialItems, Is.EqualTo(3));
            Assert.That(after.HasCheckpoint, Is.False);
            // Recreate the stale checkpoint to reproduce a crash after progress was committed.
            after.SaveCheckpoint(checkpoint);
            Assert.That(after.LoadCheckpoint("test-voyage"), Is.Null);
            Assert.That(after.HasCheckpoint, Is.False);
            Assert.That(after.Progress.coins, Is.EqualTo(55));
        }

        [Test]
        public void CorruptSaveDoesNotOverwriteExistingProgress()
        {
            var store = new VoyageSaveStore(directory);
            store.Settle("safe-run", new RewardData { coins = 25 });
            string permanent = File.ReadAllText(Path.Combine(directory, "progress.json"));
            File.WriteAllText(Path.Combine(directory, "checkpoint.json"), "{broken");
            Assert.Throws<InvalidDataException>(() => new VoyageSaveStore(directory).LoadCheckpoint("test-voyage"));
            Assert.That(File.ReadAllText(Path.Combine(directory, "progress.json")), Is.EqualTo(permanent));
            Assert.Throws<ArgumentException>(() => store.Settle("negative-reward", new RewardData { coins = -1 }));
            Assert.That(store.Progress.coins, Is.EqualTo(25));
        }

        private static VoyageCheckpoint CreateCheckpoint(string runId)
        {
            return new VoyageCheckpoint
            {
                version = 1,
                voyageId = "test-voyage",
                runId = runId,
                seed = 7,
                stopIndex = 1,
                pendingCoins = 40,
                pendingSpecialItems = 1,
                passengers = new[] { new PassengerState { profileId = "mechanic", destinationStopIndex = 4 } },
                ship = new BoatHealth.State
                {
                    health = 850f,
                    pendingDamage = 50f,
                    points = new[] { new BoatHealth.PointState { progress = 30f } }
                }
            };
        }
    }
}
