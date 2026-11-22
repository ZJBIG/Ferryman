using System;
using System.Collections.Generic;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Characters
{
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class MaintenanceCrewMember : Character
    {
        private BoatHealth boat;
        private Rigidbody2D body;
        private BoxCollider2D footprint;
        private float moveSpeed;
        private float repairSpeed;
        private int repairRange;
        private int simultaneousRepairs;
        private RepairPoint target;
        private bool working;
        private bool contributing;
        private readonly List<RepairPoint> repairTargets = new List<RepairPoint>();
        private readonly List<Vector2Int> path = new List<Vector2Int>();
        private int pathIndex;
        private readonly List<RaycastHit2D> routeHits = new List<RaycastHit2D>();
        private bool routeBlocked;
        private float nextPathAt;

        public RepairPoint CurrentTarget => target;
        public bool IsRepairing => contributing;
        protected override bool CanAct => base.CanAct && boat != null && !boat.IsDestroyed;

        protected override void Awake()
        {
            base.Awake();
            body = GetComponent<Rigidbody2D>();
            footprint = GetComponent<BoxCollider2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public void InitializeCrew(BoatHealth owner, CrewDefinition definition)
        {
            if (owner == null || owner.Deck == null || definition == null || definition.role != CrewRole.Maintenance)
                throw new ArgumentException("Maintenance crew needs its deck and fixed maintenance definition.");
            if (boat != null)
                throw new InvalidOperationException("Maintenance crew is already assigned to a boat.");

            boat = owner;
            moveSpeed = Mathf.Max(0.1f, definition.moveSpeed);
            repairSpeed = Mathf.Max(0f, definition.repairSpeed);
            repairRange = Mathf.Max(0, definition.repairRange);
            simultaneousRepairs = Mathf.Max(1, definition.simultaneousRepairs);
            boat.Destroyed.AddListener(StopWork);
        }

        private void Update()
        {
            if (!CanAct)
            {
                PauseContributions();
                return;
            }
            if (working)
            {
                RepairAtCenter();
                return;
            }
            if ((target == null || target.IsComplete || !target.isActiveAndEnabled || routeBlocked)
                && Time.time >= nextPathAt)
                FindWork();
            if (target == null || target.IsComplete || !target.isActiveAndEnabled)
                return;

            Vector2Int position = boat.Deck.WorldToCell(body.position);
            if (BoatDeck.CellDistance(position, target.Cell) <= repairRange)
            {
                working = true;
                path.Clear();
                body.velocity = Vector2.zero;
                RepairAtCenter();
            }
        }

        private void FixedUpdate()
        {
            if (!CanAct || working || target == null || target.IsComplete || !target.isActiveAndEnabled || path.Count == 0)
            {
                body.velocity = Vector2.zero;
                return;
            }

            while (pathIndex < path.Count && Vector2.Distance(body.position, boat.Deck.CellToWorld(path[pathIndex])) <= 0.08f)
                pathIndex++;
            for (int i = path.Count - 1; i >= pathIndex; i--)
            {
                Vector2 delta = boat.Deck.CellToWorld(path[i]) - body.position;
                if (footprint.Cast(delta.normalized, new ContactFilter2D { useTriggers = false }, routeHits, delta.magnitude) > 0)
                    continue;
                pathIndex = i;
                routeBlocked = false;
                body.velocity = delta.normalized * Mathf.Min(moveSpeed, delta.magnitude / Time.fixedDeltaTime);
                return;
            }

            routeBlocked = true;
            body.velocity = Vector2.zero;
        }

        private void FindWork()
        {
            path.Clear();
            target = null;
            routeBlocked = false;
            nextPathAt = Time.time + 0.25f;
            var unreachable = new List<RepairPoint>();
            Vector2Int origin = boat.Deck.WorldToCell(body.position);
            while (true)
            {
                RepairPoint candidate = boat.SelectRepairPoint(origin, int.MaxValue, unreachable);
                if (candidate == null)
                    return;
                if (BuildPath(candidate.Cell))
                {
                    target = candidate;
                    return;
                }
                unreachable.Add(candidate);
            }
        }

        private void RepairAtCenter()
        {
            Vector2Int repairCenter = boat.Deck.WorldToCell(body.position);
            for (int i = repairTargets.Count - 1; i >= 0; i--)
            {
                RepairPoint point = repairTargets[i];
                if (point != null && BoatDeck.CellDistance(repairCenter, point.Cell) <= repairRange && point.BeginRepair(this))
                    continue;
                repairTargets.RemoveAt(i);
                if (point != null)
                    point.EndRepair(this);
            }
            while (repairTargets.Count < simultaneousRepairs)
            {
                RepairPoint point = boat.SelectRepairPoint(repairCenter, repairRange, repairTargets);
                if (point == null || !point.BeginRepair(this))
                    break;
                repairTargets.Add(point);
            }

            contributing = repairTargets.Count > 0 && repairSpeed > 0f;
            if (repairSpeed <= 0f)
            {
                StopWork();
                return;
            }
            foreach (var point in repairTargets)
                point.AddProgress(this, repairSpeed * Time.deltaTime);
            repairTargets.RemoveAll(point => point == null || point.IsComplete);
            if (repairTargets.Count == 0)
                StopWork();
        }

        private bool BuildPath(Vector2Int targetCell)
        {
            BoatDeck deck = boat.Deck;
            Vector2Int size = deck.GridSize;
            var walkable = new bool[size.x, size.y];
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                    walkable[x, y] = CanStand(deck.CellToWorld(new Vector2Int(x, y)));
            }

            Vector2Int origin = deck.WorldToCell(body.position);
            var frontier = new Queue<Vector2Int>();
            var previous = new Dictionary<Vector2Int, Vector2Int> { [origin] = origin };
            frontier.Enqueue(origin);
            Vector2Int? goal = null;
            while (frontier.Count > 0)
            {
                Vector2Int cell = frontier.Dequeue();
                if (BoatDeck.CellDistance(cell, targetCell) <= repairRange)
                {
                    goal = cell;
                    break;
                }
                for (int dx = -1; dx <= 1; dx++)
                {
                    for (int dy = -1; dy <= 1; dy++)
                    {
                        if (dx == 0 && dy == 0)
                            continue;
                        Vector2Int next = cell + new Vector2Int(dx, dy);
                        if (!deck.ContainsCell(next) || previous.ContainsKey(next) || !walkable[next.x, next.y])
                            continue;
                        if (dx != 0 && dy != 0 && (!walkable[cell.x + dx, cell.y] || !walkable[cell.x, cell.y + dy]))
                            continue;
                        previous.Add(next, cell);
                        frontier.Enqueue(next);
                    }
                }
            }
            path.Clear();
            pathIndex = 0;
            if (!goal.HasValue)
                return false;

            // Keep the current cell center as a fallback when the next segment is blocked.
            Vector2Int step = goal.Value;
            while (step != origin)
            {
                path.Add(step);
                step = previous[step];
            }
            if (walkable[origin.x, origin.y] && goal.Value != origin)
                path.Add(origin);
            path.Reverse();
            return true;
        }

        private bool CanStand(Vector2 position)
        {
            Vector2 size = Vector2.Scale(footprint.size, transform.lossyScale) + Vector2.one * 0.04f;
            Vector2 center = position + Vector2.Scale(footprint.offset, transform.lossyScale);
            return boat.Deck.CanPlace(center, size, footprint);
        }

        private void PauseContributions()
        {
            contributing = false;
            foreach (var point in repairTargets)
                if (point != null)
                    point.EndRepair(this);
            if (body != null)
                body.velocity = Vector2.zero;
        }

        private void StopWork()
        {
            PauseContributions();
            repairTargets.Clear();
            working = false;
            target = null;
            path.Clear();
            routeBlocked = false;
            nextPathAt = 0f;
        }

        public override bool TryReceiveDamage(float damage)
        {
            if (!base.TryReceiveDamage(damage))
                return false;
            PauseContributions();
            return true;
        }

        public override void SetActionsAllowed(bool allowed)
        {
            base.SetActionsAllowed(allowed);
            if (!allowed)
                StopWork();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopWork();
        }

        private void OnDestroy()
        {
            if (boat != null)
                boat.Destroyed.RemoveListener(StopWork);
        }
    }
}
