using System;
using System.IO;
using System.Text;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Voyage
{
    [Serializable]
    public sealed class PermanentProgress
    {
        public int version;
        public int coins;
        public int specialItems;
        public string lastSettledRunId;
        public int[] upgrades;
    }

    [Serializable]
    public sealed class PassengerState
    {
        public string profileId;
        public int destinationStopIndex;
        public Vector2 localPosition;
    }

    [Serializable]
    public sealed class VoyageCheckpoint
    {
        public int version;
        public string voyageId;
        public string runId;
        public int seed;
        public int stopIndex;
        public int pendingCoins;
        public int pendingSpecialItems;
        public int deliveredPassengers;
        public Vector2 playerLocalPosition;
        public PassengerState[] passengers;
        public BoatHealth.State ship;
        public int[] routePath;
        public int[] runUpgrades;
        public bool islandResolved;
        public VoyageUpgradeKind[] upgradeChoices;
    }

    public sealed class VoyageSaveStore
    {
        private readonly string progressPath;
        private readonly string checkpointPath;

        public PermanentProgress Progress { get; private set; }
        public bool HasCheckpoint => File.Exists(checkpointPath);

        public VoyageSaveStore(string directory)
        {
            Directory.CreateDirectory(directory);
            progressPath = Path.Combine(directory, "progress.json");
            checkpointPath = Path.Combine(directory, "checkpoint.json");
            Progress = File.Exists(progressPath) ? Read<PermanentProgress>(progressPath)
                : new PermanentProgress { version = 1 };
            if (Progress.version != 1 || Progress.coins < 0 || Progress.specialItems < 0)
                throw new InvalidDataException("Permanent progress is invalid or uses an unsupported version.");
            Progress.upgrades = VoyageUpgrades.ReadRanks(Progress.upgrades);
        }

        public void SaveCheckpoint(VoyageCheckpoint checkpoint)
        {
            ValidateCheckpoint(checkpoint);
            WriteAtomically(checkpointPath, checkpoint);
        }

        public VoyageCheckpoint LoadCheckpoint(string voyageId)
        {
            if (!HasCheckpoint)
                return null;
            var checkpoint = Read<VoyageCheckpoint>(checkpointPath);
            ValidateCheckpoint(checkpoint);
            if (checkpoint.voyageId != voyageId)
                throw new InvalidDataException("Checkpoint belongs to a different voyage configuration.");
            // A crash between the progress write and checkpoint removal must not pay twice.
            if (checkpoint.runId == Progress.lastSettledRunId)
            {
                AbandonCheckpoint();
                return null;
            }
            return checkpoint;
        }

        public bool Settle(string runId, RewardData reward)
        {
            if (string.IsNullOrWhiteSpace(runId) || reward.coins < 0 || reward.specialItems < 0)
                throw new ArgumentException("Invalid voyage settlement.");
            if (Progress.lastSettledRunId == runId)
            {
                AbandonCheckpoint();
                return false;
            }
            var next = new PermanentProgress
            {
                version = 1,
                coins = checked(Progress.coins + reward.coins),
                specialItems = checked(Progress.specialItems + reward.specialItems),
                lastSettledRunId = runId,
                upgrades = VoyageUpgrades.ReadRanks(Progress.upgrades)
            };
            WriteAtomically(progressPath, next);
            Progress = next;
            AbandonCheckpoint();
            return true;
        }

        public bool TryBuyUpgrade(VoyageUpgradeKind kind)
        {
            if (!VoyageUpgrades.IsValid(kind))
                return false;
            int[] ranks = VoyageUpgrades.ReadRanks(Progress.upgrades);
            int rank = ranks[(int)kind];
            if (rank >= VoyageUpgrades.MaxRank)
                return false;
            int price = VoyageUpgrades.Price(kind, rank);
            bool usesItems = VoyageUpgrades.UsesSpecialItems(kind);
            if ((usesItems ? Progress.specialItems : Progress.coins) < price)
                return false;
            ranks[(int)kind]++;
            var next = new PermanentProgress
            {
                version = 1,
                coins = Progress.coins - (usesItems ? 0 : price),
                specialItems = Progress.specialItems - (usesItems ? price : 0),
                lastSettledRunId = Progress.lastSettledRunId,
                upgrades = ranks
            };
            WriteAtomically(progressPath, next);
            Progress = next;
            return true;
        }

        public void AbandonCheckpoint()
        {
            if (HasCheckpoint)
                File.Delete(checkpointPath);
        }

        private static void ValidateCheckpoint(VoyageCheckpoint checkpoint)
        {
            if (checkpoint == null || checkpoint.version != 1 || string.IsNullOrWhiteSpace(checkpoint.voyageId)
                || string.IsNullOrWhiteSpace(checkpoint.runId) || checkpoint.stopIndex < 0
                || checkpoint.pendingCoins < 0 || checkpoint.pendingSpecialItems < 0 || checkpoint.deliveredPassengers < 0
                || checkpoint.passengers == null || checkpoint.ship == null)
                throw new InvalidDataException("Voyage checkpoint is invalid or uses an unsupported version.");
            int[] ranks = VoyageUpgrades.ReadRanks(checkpoint.runUpgrades);
            if (checkpoint.routePath != null && checkpoint.routePath.Length > 0)
            {
                if (checkpoint.stopIndex >= VoyageRouteMap.FinalDepth * 2 + 1)
                    throw new InvalidDataException("Branching voyage checkpoint has an invalid stop index.");
                VoyageRouteMap.Generate(checkpoint.seed).ValidatePath(checkpoint.routePath);
                int expectedLength = (checkpoint.stopIndex + 1) / 2 + 1;
                if (checkpoint.routePath.Length != expectedLength)
                    throw new InvalidDataException("Branching voyage path does not match the checkpoint stop.");
            }
            if (checkpoint.upgradeChoices == null)
                return;
            if (checkpoint.upgradeChoices.Length > 3)
                throw new InvalidDataException("Voyage checkpoint has too many pending upgrades.");
            var seen = new bool[VoyageUpgrades.Count];
            foreach (VoyageUpgradeKind kind in checkpoint.upgradeChoices)
            {
                if (!VoyageUpgrades.IsValid(kind) || seen[(int)kind] || ranks[(int)kind] >= VoyageUpgrades.MaxRank)
                    throw new InvalidDataException("Voyage checkpoint has an invalid pending upgrade.");
                seen[(int)kind] = true;
            }
        }

        private static T Read<T>(string path) where T : class
        {
            try
            {
                T value = JsonUtility.FromJson<T>(File.ReadAllText(path));
                if (value == null)
                    throw new InvalidDataException("Empty save: " + path);
                return value;
            }
            catch (ArgumentException error)
            {
                throw new InvalidDataException("Save cannot be read: " + path, error);
            }
        }

        private static void WriteAtomically<T>(string path, T value)
        {
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(value, true), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, path + ".bak");
            else
                File.Move(temporary, path);
        }
    }
}
