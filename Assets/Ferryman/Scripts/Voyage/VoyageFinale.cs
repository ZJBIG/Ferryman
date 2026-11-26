using System;
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
        private const float FinalSummonInterval = 10f;
        private const float RecoveryReach = 2.15f;
        private FinalRecoveryPresentation finalRecoveryArt;
        private float finalSummonCameraSize;
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
            float nextSummonAt = Time.time + FinalSummonInterval;
            while (ActiveRun && IsLivingEnemy(boss))
            {
                if (Time.time < nextSummonAt)
                {
                    yield return null;
                    continue;
                }

                waveIndex = (int)boss.BossPhase + 1;
                nextSummonAt += FinalSummonInterval;
                yield return SummonFinalWave(encounter.waves[waveIndex], boss);
            }

            while (ActiveRun && enemies.Any(IsLivingEnemy))
                yield return null;
        }

        private IEnumerator SummonFinalWave(WaveDefinition wave, EnemyCharacter boss)
        {
            Camera camera = Camera.main;
            if (camera == null)
                throw new InvalidOperationException("海怪生成需要主相机。");

            enemies.RemoveAll(enemy => enemy == null);
            Bounds deck = vessel.Boat.Deck.GetComponent<EdgeCollider2D>().bounds;
            Vector2 half = WaveFootprint(wave, out int count);
            while (ActiveRun && IsLivingEnemy(boss))
            {
                Bounds[] occupied = LivingEnemyBounds();
                Vector2 viewCenter = camera.transform.position;
                float size = camera.orthographicSize;
                Vector2[] positions;
                while (true)
                {
                    positions = FindWavePositions(deck, half, viewCenter, new Vector2(size * camera.aspect, size))
                        .Where(position => HasSummonClearance(position, half, occupied))
                        .OrderBy(position => (position - (Vector2)deck.center).sqrMagnitude)
                        .Take(count).ToArray();
                    if (positions.Length == count)
                        break;
                    size += 0.5f;
                }

                if (size > camera.orthographicSize)
                {
                    finalSummonCameraSize = size;
                    while (ActiveRun && IsLivingEnemy(boss) && camera.orthographicSize < size)
                        yield return null;
                    continue;
                }

                yield return WarnWave(positions, count, boss);
                if (!ActiveRun || !IsLivingEnemy(boss))
                    yield break;
                occupied = LivingEnemyBounds();
                // A boarding crab can move into a warned spot before the wave surfaces.
                if (!positions.All(position => HasSummonClearance(position, half, occupied)))
                    continue;
                SpawnWaveAt(wave, positions);
                yield break;
            }
        }

        private Bounds[] LivingEnemyBounds()
        {
            Physics2D.SyncTransforms();
            return enemies.Where(IsLivingEnemy).Select(enemy =>
            {
                Vector2 origin = enemy.transform.position;
                Bounds bounds = enemy.SpawnBounds;
                Vector2 half = Vector2.Max(origin - (Vector2)bounds.min, (Vector2)bounds.max - origin);
                return SummonClearance(origin, half);
            }).ToArray();
        }

        private static bool HasSummonClearance(Vector2 position, Vector2 half, Bounds[] occupied)
        {
            var bounds = new Bounds(position, half * 2f);
            return !occupied.Any(other => other.Intersects(bounds));
        }

        private static bool IsLivingEnemy(EnemyCharacter enemy) => enemy != null && enemy.Health > 0f;

        private static Bounds SummonClearance(Vector2 center, Vector2 half) =>
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
            finalSummonCameraSize = 0f;
            towAudio = null;
            AwaitingFinalRecovery = false;
            TowingFinalCargo = false;
            FinalCargoCollected = 0;
            recoveryActionRequested = false;
            recoveryActionInProgress = false;
        }
    }
}
