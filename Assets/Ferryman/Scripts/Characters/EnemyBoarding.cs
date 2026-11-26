using System.Collections.Generic;
using Ferryman.Voyage;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Characters
{
    public sealed partial class EnemyCharacter
    {
        private const float BoardingDuration = 0.65f;
        private const float ClawReach = 0.6f;
        private const float ClawWindup = 0.44f;
        private const float ClawFacingDot = 0.64f;
        private static readonly ContactFilter2D SolidContacts = new ContactFilter2D { useTriggers = false };
        private static readonly Vector2Int[] DeckSteps =
        {
            Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down
        };

        [SerializeField] private Sprite[] boardingWalkFrames;
        [SerializeField] private GameObject clawSwingEffectPrefab;

        private readonly List<RaycastHit2D> boardingHits = new List<RaycastHit2D>();
        private readonly List<Vector2> pursuitPath = new List<Vector2>();
        private Rigidbody2D boardingBody;
        private bool onDeck;
        private bool hopping;
        private bool returningToSea;
        private Vector2 hopOrigin;
        private Vector2 hopLanding;
        private float hopStartedAt;
        private float nextHopAttempt;
        private Character pursuedCharacter;
        private Vector2 plannedTargetPosition;
        private float nextPathAt;
        private Character clawTarget;
        private float clawHitsAt;
        private float clawEndsAt;
        private float nextClawAt;
        internal bool IsOnDeck => onDeck;

        private bool LeaveBoardingRemains()
        {
            if (attackKind != AttackKind.BoardingMelee || !onDeck || boat == null || deathObstaclePrefab == null)
                return false;
            Physics2D.SyncTransforms();
            Vector2 origin = boardingBody.position;
            Vector2 size = deathObstaclePrefab.Footprint;
            if (!boat.Deck.TryFindPlacement(origin, size, Hitbox, out Vector2 position))
            {
                // Crowded decks keep the corpse, while its full hitbox still fits the floor and fixed cargo.
                float closest = boat.Deck.HasClearFloor(origin, size) ? 0f : float.PositiveInfinity;
                Vector2Int grid = boat.Deck.GridSize;
                for (int x = 0; x < grid.x; x++)
                {
                    for (int y = 0; y < grid.y; y++)
                    {
                        Vector2 candidate = boat.Deck.CellToWorld(new Vector2Int(x, y));
                        float distance = (candidate - origin).sqrMagnitude;
                        if (distance >= closest || !boat.Deck.HasClearFloor(candidate, size))
                            continue;
                        closest = distance;
                        position = candidate;
                    }
                }
                if (float.IsPositiveInfinity(closest))
                    return false;
            }
            Instantiate(deathObstaclePrefab, position, Quaternion.identity, boat.transform);
            return true;
        }

        private void FixedUpdate()
        {
            if (attackKind != AttackKind.BoardingMelee)
                return;
            if (!CanAct || boat == null || boat.IsDestroyed)
            {
                if (boardingBody != null)
                    boardingBody.velocity = Vector2.zero;
                return;
            }

            if (boardingBody == null)
                boardingBody = GetComponent<Rigidbody2D>();
            if (!onDeck)
            {
                BoardShip(boat.Deck);
                return;
            }

            if (clawTarget != null && ActionTime >= clawHitsAt)
            {
                Character victim = clawTarget;
                clawTarget = null;
                if (clawSwingEffectPrefab != null)
                    Instantiate(clawSwingEffectPrefab, (Vector2)Hitbox.bounds.center + AimDirection * ClawReach, AimRotation);
                if (CanTarget(victim))
                {
                    Vector2 direction = (Vector2)victim.Hitbox.bounds.center - boardingBody.position;
                    if (Vector2.Dot(AimDirection, direction.normalized) >= ClawFacingDot && CanReachWithClaw(victim))
                        DamageSystem.ApplyDamage(this, victim, AttackPower, AttackStunDuration);
                }
            }
            if (ActionTime < clawEndsAt)
            {
                MoveBody(Vector2.zero);
                return;
            }

            Character victimToChase = SelectTarget();
            if (victimToChase == null)
            {
                MoveBody(Vector2.zero);
                StopVisualFrames();
                return;
            }
            if (pursuedCharacter != victimToChase)
            {
                pursuedCharacter = victimToChase;
                pursuitPath.Clear();
                nextPathAt = 0f;
            }
            if (CanReachWithClaw(victimToChase))
            {
                MoveBody(Vector2.zero);
                StopVisualFrames();
                pursuitPath.Clear();
                FaceClaw(victimToChase.Hitbox.bounds.center - (Vector3)boardingBody.position);
                if (ActionTime >= nextClawAt)
                {
                    clawTarget = victimToChase;
                    clawHitsAt = ActionTime + ClawWindup;
                    clawEndsAt = ActionTime + ClawWindup * 2f;
                    nextClawAt = ActionTime + FireInterval;
                    PlayVisualFrames(attackFrames, ClawWindup * 2f);
                    VoyageAudio.Play(Sound.KickSwing, 0.46f, boardingBody.position);
                }
                return;
            }

            ChaseOnDeck(boat.Deck, victimToChase);
        }

        private void BoardShip(BoatDeck deck)
        {
            Vector2 position = boardingBody.position;
            if (returningToSea)
            {
                if ((position - hopOrigin).sqrMagnitude > 0.001f)
                {
                    boardingBody.MovePosition(hopOrigin);
                    return;
                }
                Hitbox.isTrigger = false;
                returningToSea = false;
                hopping = false;
                nextHopAttempt = Time.time + 0.25f;
                return;
            }
            Vector2 size = (Vector2)Hitbox.bounds.size + Vector2.one * 0.06f;
            if (!hopping)
            {
                if (Time.time < nextHopAttempt)
                    return;
                nextHopAttempt = Time.time + 0.25f;
                if (!deck.TryFindPlacement(deck.ClampToDeck(position), size, Hitbox, out hopLanding))
                    return;
                hopping = true;
                hopOrigin = position;
                hopStartedAt = Time.time;
                Hitbox.isTrigger = true;
                FaceClaw(hopLanding - position);
            }

            float progress = Mathf.Clamp01((Time.time - hopStartedAt) / BoardingDuration);
            Visual.transform.localPosition = Vector3.up * (4f * progress * (1f - progress));
            if (progress < 1f)
            {
                boardingBody.MovePosition(Vector2.Lerp(hopOrigin, hopLanding, progress));
                return;
            }

            if (!deck.TryFindPlacement(hopLanding, size, Hitbox, out Vector2 landing))
            {
                // Return to the water if a character or falling obstacle took the landing space.
                returningToSea = true;
                return;
            }
            hopLanding = landing;
            if ((position - landing).sqrMagnitude > 0.001f)
            {
                boardingBody.MovePosition(landing);
                return;
            }
            Hitbox.isTrigger = false;
            boardingBody.velocity = Vector2.zero;
            onDeck = true;
            nextClawAt = ActionTime + 0.2f;
            VoyageAudio.Play(Sound.Board, 0.53f, landing);
        }

        private void FaceClaw(Vector2 direction)
        {
            Face(direction);
            Visual.flipX = AimDirection.x < 0f;
            Visual.transform.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg + (Visual.flipX ? 180f : 0f));
        }

        private bool CanReachWithClaw(Character victim)
        {
            if (Hitbox.Distance(victim.Hitbox).distance > ClawReach)
                return false;
            Vector2 contact = victim.Hitbox.ClosestPoint(boardingBody.position);
            Physics2D.Linecast(boardingBody.position, contact, SolidContacts, boardingHits);
            foreach (RaycastHit2D hit in boardingHits)
                if (hit.collider != Hitbox && hit.collider != victim.Hitbox)
                    return false;
            return true;
        }

        private void ChaseOnDeck(BoatDeck deck, Character victim)
        {
            Vector2 origin = boardingBody.position;
            Vector2 destination = victim.Hitbox.bounds.center;
            Vector2 step = destination - origin;
            if (HasClearStep(step, victim.Hitbox))
                pursuitPath.Clear();
            else
            {
                while (pursuitPath.Count > 0 && (pursuitPath[0] - origin).sqrMagnitude < 0.01f)
                    pursuitPath.RemoveAt(0);
                if (Time.time >= nextPathAt && (pursuitPath.Count == 0
                    || (plannedTargetPosition - destination).sqrMagnitude > 2.25f
                    || !HasClearStep(pursuitPath[0] - origin)))
                    FindPursuitPath(deck, destination);
                step = pursuitPath.Count > 0 ? pursuitPath[0] - origin : Vector2.zero;
                if (!HasClearStep(step))
                    step = Vector2.zero;
            }
            FaceClaw(step);
            MoveBody(step.normalized * Mathf.Min(MoveSpeed, step.magnitude / Time.fixedDeltaTime));
            if (step.sqrMagnitude > 0.0001f)
                PlayVisualFrames(boardingWalkFrames, 0.48f, loop: true);
            else
                StopVisualFrames();
        }

        private bool HasClearStep(Vector2 step, Collider2D targetHitbox = null)
        {
            if (step.sqrMagnitude < 0.0001f)
                return true;
            Hitbox.Cast(step.normalized, SolidContacts, boardingHits, step.magnitude);
            foreach (RaycastHit2D hit in boardingHits)
                if (hit.collider != targetHitbox)
                    return false;
            return true;
        }

        private void FindPursuitPath(BoatDeck deck, Vector2 destination)
        {
            nextPathAt = Time.time + 0.25f;
            plannedTargetPosition = destination;
            pursuitPath.Clear();
            Vector2 size = Hitbox.bounds.size;
            Vector2Int grid = deck.GridSize;
            var walkable = new bool[grid.x, grid.y];
            var frontier = new Queue<Vector2Int>();
            var previous = new Dictionary<Vector2Int, Vector2Int>();
            for (int x = 0; x < grid.x; x++)
            {
                for (int y = 0; y < grid.y; y++)
                {
                    var cell = new Vector2Int(x, y);
                    Vector2 position = deck.CellToWorld(cell);
                    if (!deck.CanPlace(position, size, Hitbox))
                        continue;
                    walkable[x, y] = true;
                    Vector2 step = position - boardingBody.position;
                    if (step.sqrMagnitude <= 3.24f && HasClearStep(step))
                    {
                        frontier.Enqueue(cell);
                        previous.Add(cell, cell);
                    }
                }
            }

            if (frontier.Count == 0)
                return;
            Vector2Int best = frontier.Peek();
            float bestDistance = float.PositiveInfinity;
            while (frontier.Count > 0)
            {
                Vector2Int cell = frontier.Dequeue();
                Vector2 position = deck.CellToWorld(cell);
                float distance = (position - destination).sqrMagnitude;
                if (distance < bestDistance)
                {
                    best = cell;
                    bestDistance = distance;
                }
                foreach (Vector2Int offset in DeckSteps)
                {
                    Vector2Int next = cell + offset;
                    if (!deck.ContainsCell(next) || !walkable[next.x, next.y] || previous.ContainsKey(next)
                        || !deck.CanPlace((position + deck.CellToWorld(next)) * 0.5f, size, Hitbox))
                        continue;
                    previous.Add(next, cell);
                    frontier.Enqueue(next);
                }
            }
            for (; best != previous[best]; best = previous[best])
                pursuitPath.Add(deck.CellToWorld(best));
            pursuitPath.Add(deck.CellToWorld(best));
            pursuitPath.Reverse();
        }
    }
}
