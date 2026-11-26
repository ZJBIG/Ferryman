using System.Collections;
using System.Linq;
using Ferryman.Characters;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Voyage
{
    public sealed partial class VoyageController
    {
        public const int FinalCargoCount = 3;
        private const float RecoveryReach = 2.15f;
        private FinalRecoveryPresentation finalRecoveryArt;
        private AudioSource towAudio;
        private Vector2 recoveryStationLocal;
        private bool recoveryActionRequested;
        private bool recoveryActionInProgress;

        public bool AwaitingFinalRecovery { get; private set; }
        public bool TowingFinalCargo { get; private set; }
        public int FinalCargoCollected { get; private set; }
        public Vector2 FinalRecoveryStation => vessel != null
            ? (Vector2)vessel.transform.TransformPoint(recoveryStationLocal) : Vector2.zero;
        public bool NearFinalRecoveryStation => vessel != null
            && ((Vector2)vessel.Player.transform.position - FinalRecoveryStation).sqrMagnitude <= RecoveryReach * RecoveryReach;
        public bool CanRecoverFinalCargo => ActiveRun && AwaitingFinalRecovery && !AwaitingStoryDialogue
            && Time.timeScale > 0f && !recoveryActionRequested && !recoveryActionInProgress
            && NearFinalRecoveryStation && vessel.Player.ActionsEnabled && !vessel.Player.IsStunned;

        public bool TryRecoverFinalCargo()
        {
            if (!CanRecoverFinalCargo)
                return false;
            recoveryActionRequested = true;
            return true;
        }

        private IEnumerator FightFinalBoss(EncounterDefinition encounter)
        {
            waveIndex = 0;
            Message = encounter.displayName;
            yield return SpawnWave(encounter.waves[0]);
            if (!ActiveRun)
                yield break;

            EnemyCharacter boss = enemies.Single();
            boss.BossReinforcementsPhase = ColossusPhase.OuterShell;
            Vector2 half = WaveFootprint(encounter.waves[0], out _);
            Bounds clearance = BossSummonClearance(boss.transform.position, half);
            for (int wave = 1; wave < encounter.waves.Length; wave++)
            {
                var phase = (ColossusPhase)(wave - 1);
                while (ActiveRun && (boss.BossPhase < phase || enemies.Any(enemy => enemy != null && !enemy.IsBoss && enemy.Health > 0f)))
                    yield return null;
                if (!ActiveRun)
                    yield break;

                waveIndex = wave;
                yield return SpawnWave(encounter.waves[wave], clearance);
                if (!ActiveRun)
                    yield break;
                boss.BossReinforcementsPhase = phase;
            }

            while (ActiveRun && enemies.Any(enemy => enemy != null && enemy.Health > 0f))
                yield return null;
        }

        private static Bounds BossSummonClearance(Vector2 center, Vector2 half) =>
            new Bounds(center, half * 2f + Vector2.one * 1.2f);

        private IEnumerator RecoverFinalWreckage()
        {
            BoatDeck deck = vessel.Boat.Deck;
            Bounds rail = deck.GetComponent<EdgeCollider2D>().bounds;
            Vector2 stern = new Vector2(rail.min.x + 0.15f, rail.center.y);
            Vector2 station = FindRecoveryStation(deck, stern);
            recoveryStationLocal = vessel.transform.InverseTransformPoint(station);
            finalRecoveryArt = FinalRecoveryPresentation.Create(vessel.transform, stern,
                definition.obstacleCarrier, definition.obstacleCarrierFrames);

            VoyageAudio.Play(Sound.Gull, 0.55f, stern);
            yield return finalRecoveryArt.DropBag();
            if (!ActiveRun)
                yield break;
            VoyageAudio.Play(Sound.BagOpen, 0.58f, stern);
            vessel.Player.BlockMouseAttacks = false;
            AwaitingFinalRecovery = true;

            while (ActiveRun && FinalCargoCollected < FinalCargoCount)
            {
                while (ActiveRun && !recoveryActionRequested)
                    yield return null;
                if (!ActiveRun)
                    yield break;
                recoveryActionRequested = false;
                recoveryActionInProgress = true;
                yield return finalRecoveryArt.CollectNext();
                if (!ActiveRun)
                    yield break;
                FinalCargoCollected++;
                VoyageAudio.Play(Sound.BagFill, 0.62f, stern);
                recoveryActionInProgress = false;
            }

            while (ActiveRun && !recoveryActionRequested)
                yield return null;
            if (!ActiveRun)
                yield break;
            recoveryActionRequested = false;
            AwaitingFinalRecovery = false;
            finalRecoveryArt.BeginTow();
            VoyageAudio.Play(Sound.BagTie, 0.62f, station);
            vessel.Player.BlockMouseAttacks = true;
        }

        private Vector2 FindRecoveryStation(BoatDeck deck, Vector2 stern)
        {
            Vector2 station = vessel.Player.transform.position;
            var hitbox = vessel.Player.GetComponent<BoxCollider2D>();
            float best = float.PositiveInfinity;
            Vector2Int grid = deck.GridSize;
            Physics2D.SyncTransforms();
            for (int x = 0; x < grid.x; x++)
            {
                for (int y = 0; y < grid.y; y++)
                {
                    Vector2 cell = deck.CellToWorld(new Vector2Int(x, y));
                    float distance = (cell - stern).sqrMagnitude;
                    if (distance >= best || !deck.CanPlace(cell + hitbox.offset, hitbox.size, hitbox))
                        continue;
                    best = distance;
                    station = cell;
                }
            }
            return station;
        }

        private void StartFinalTow()
        {
            TowingFinalCargo = true;
            towAudio = VoyageAudio.StartLoop(finalRecoveryArt.gameObject, Sound.TowWater, 0.42f, true);
        }

        private void DockFinalTow()
        {
            if (towAudio != null)
                towAudio.Stop();
            finalRecoveryArt.FinishTow();
            ResetFinalRecovery();
        }

        private void ResetFinalRecovery()
        {
            finalRecoveryArt = null;
            towAudio = null;
            AwaitingFinalRecovery = false;
            TowingFinalCargo = false;
            FinalCargoCollected = 0;
            recoveryActionRequested = false;
            recoveryActionInProgress = false;
        }
    }
}
