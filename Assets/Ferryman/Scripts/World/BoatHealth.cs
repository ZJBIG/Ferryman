using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Events;
using Random = UnityEngine.Random;

namespace Ferryman.World
{
    public sealed class BoatHealth : MonoBehaviour
    {
        [Serializable]
        public sealed class State
        {
            public float health;
            public float pendingDamage;
            public PointState[] points;
        }

        [Serializable]
        public sealed class PointState
        {
            public Vector2Int cell;
            public Vector2 localPosition;
            public float progress;
        }
        [SerializeField, Min(1f)] private float maxHealth = 1000f;
        [SerializeField, Min(1f)] private float repairPointHealth = 100f;
        [SerializeField] private BoatDeck deck;
        [SerializeField] private RepairPoint repairPointPrefab;
        [Header("Presentation")]
        [SerializeField] private UnityEvent<float, float> healthChanged = new UnityEvent<float, float>();
        [SerializeField] private UnityEvent<Vector2, float> damaged = new UnityEvent<Vector2, float>();
        [SerializeField] private UnityEvent destroyed = new UnityEvent();

        private readonly Dictionary<Vector2Int, RepairPoint> repairPoints = new Dictionary<Vector2Int, RepairPoint>();
        private float currentHealth;
        private float pendingDamage;

        public float CurrentHealth => currentHealth;
        public float MaxHealth => maxHealth;
        public float PendingDamage => pendingDamage;
        public bool IsDestroyed => currentHealth <= 0f;
        public BoatDeck Deck => deck;
        public IReadOnlyCollection<RepairPoint> RepairPoints => repairPoints.Values;
        public UnityEvent<float, float> HealthChanged => healthChanged;
        public UnityEvent Destroyed => destroyed;

        public State CaptureState()
        {
            var points = new List<PointState>();
            foreach (var point in repairPoints.Values)
                points.Add(new PointState
                {
                    cell = point.Cell,
                    localPosition = deck.transform.InverseTransformPoint(point.transform.position),
                    progress = point.Progress
                });
            return new State { health = currentHealth, pendingDamage = pendingDamage, points = points.ToArray() };
        }

        public void RestoreState(State state)
        {
            if (state == null || !(state.health > 0f && state.health <= maxHealth)
                || !(state.pendingDamage >= 0f) || float.IsInfinity(state.pendingDamage) || state.points == null)
                throw new InvalidDataException("Invalid ship checkpoint.");
            var occupied = new HashSet<Vector2Int>();
            foreach (var point in state.points)
                if (point == null || !deck.ContainsCell(point.cell) || !occupied.Add(point.cell)
                    || !(point.progress >= 0f && point.progress < repairPointHealth)
                    || !float.IsFinite(point.localPosition.x) || !float.IsFinite(point.localPosition.y)
                    || ((Vector2)deck.transform.TransformPoint(point.localPosition)
                        - deck.ClampToDeck(deck.transform.TransformPoint(point.localPosition))).sqrMagnitude > 0.0001f
                    || deck.WorldToCell(deck.transform.TransformPoint(point.localPosition)) != point.cell)
                    throw new InvalidDataException("Invalid repair checkpoint.");

            foreach (var point in repairPoints.Values)
            {
                point.StopAllRepairs();
                Destroy(point.gameObject);
            }
            repairPoints.Clear();
            currentHealth = state.health;
            pendingDamage = state.pendingDamage;
            foreach (var saved in state.points)
            {
                var point = Instantiate(repairPointPrefab, deck.transform.TransformPoint(saved.localPosition),
                    Quaternion.identity, deck.transform);
                point.Initialize(this, saved.cell, repairPointHealth);
                point.RestoreProgress(saved.progress);
                repairPoints.Add(saved.cell, point);
            }
            healthChanged.Invoke(currentHealth, maxHealth);
        }

        private void Awake()
        {
            currentHealth = maxHealth;
            if (deck == null)
                deck = GetComponentInChildren<BoatDeck>();
        }

        public bool ApplyDamage(float damage, Vector2 hitPosition)
        {
            if (damage <= 0f || IsDestroyed)
                return false;

            float accepted = Mathf.Min(damage, currentHealth);
            currentHealth -= accepted;
            pendingDamage += accepted;
            GenerateRepairPoints(hitPosition);
            healthChanged.Invoke(currentHealth, maxHealth);
            damaged.Invoke(hitPosition, accepted);
            if (IsDestroyed)
            {
                foreach (var point in repairPoints.Values)
                    point.StopAllRepairs();
                destroyed.Invoke();
            }
            return true;
        }

        private void GenerateRepairPoints(Vector2 hitPosition)
        {
            if (deck == null || repairPointPrefab == null)
                return;

            Vector2 firstPosition = deck.ClampToDeck(hitPosition);
            Vector2Int origin = deck.WorldToCell(firstPosition);
            while (pendingDamage >= repairPointHealth)
            {
                if (!TryFindFreeCell(origin, out Vector2Int cell))
                    break;

                Vector2 position = cell == origin ? firstPosition : deck.CellToWorld(cell);
                var point = Instantiate(repairPointPrefab, position, Quaternion.identity, deck.transform);
                repairPoints.Add(cell, point);
                point.Initialize(this, cell, repairPointHealth);
                pendingDamage -= repairPointHealth;
            }
        }

        private bool TryFindFreeCell(Vector2Int origin, out Vector2Int cell)
        {
            if (!repairPoints.ContainsKey(origin))
            {
                cell = origin;
                return true;
            }

            var candidates = new List<Vector2Int>();
            int limit = Mathf.Max(deck.GridSize.x, deck.GridSize.y);
            for (int radius = 1; radius < limit; radius++)
            {
                candidates.Clear();
                for (int x = -radius; x <= radius; x++)
                {
                    AddCandidate(origin + new Vector2Int(x, -radius), candidates);
                    AddCandidate(origin + new Vector2Int(x, radius), candidates);
                }
                for (int y = -radius + 1; y < radius; y++)
                {
                    AddCandidate(origin + new Vector2Int(-radius, y), candidates);
                    AddCandidate(origin + new Vector2Int(radius, y), candidates);
                }
                if (candidates.Count == 0)
                    continue;

                cell = candidates[Random.Range(0, candidates.Count)];
                return true;
            }

            cell = default;
            return false;
        }

        private void AddCandidate(Vector2Int cell, List<Vector2Int> candidates)
        {
            if (deck.ContainsCell(cell) && !repairPoints.ContainsKey(cell))
                candidates.Add(cell);
        }

        public RepairPoint SelectRepairPoint(Vector2Int origin, int range, ICollection<RepairPoint> excluded)
        {
            RepairPoint best = null;
            int bestDistance = int.MaxValue;
            int tied = 0;
            foreach (var point in repairPoints.Values)
            {
                if (point == null || !point.isActiveAndEnabled || point.IsComplete
                    || excluded != null && excluded.Contains(point))
                    continue;
                int distance = BoatDeck.CellDistance(origin, point.Cell);
                if (distance > range)
                    continue;
                if (distance < bestDistance || distance == bestDistance && point.Progress > best.Progress)
                {
                    best = point;
                    bestDistance = distance;
                    tied = 1;
                }
                else if (distance == bestDistance && point.Progress == best.Progress && Random.Range(0, ++tied) == 0)
                    best = point;
            }
            return best;
        }

        internal void CompleteRepair(RepairPoint point)
        {
            if (IsDestroyed || !repairPoints.TryGetValue(point.Cell, out RepairPoint registered)
                || registered != point || !point.IsComplete)
                return;

            repairPoints.Remove(point.Cell);
            currentHealth = Mathf.Min(maxHealth, currentHealth + point.RequiredProgress);
            healthChanged.Invoke(currentHealth, maxHealth);
        }
    }
}
