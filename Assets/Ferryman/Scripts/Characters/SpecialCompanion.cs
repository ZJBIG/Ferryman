using System;
using System.Collections.Generic;
using Ferryman.Combat;
using Ferryman.Voyage;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Characters
{
    public enum SpecialCompanionSkill
    {
        Kick,
        Mark,
        Throw,
        Repair
    }

    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class SpecialCompanion : CombatCharacter
    {
        private const float KickRange = 1.45f;
        private const float KickApproachRange = 3.4f;
        private const float KickSeconds = 0.44f;
        private const float KickImpactSeconds = 0.22f;
        private const float KickCooldown = 4f;
        private const float MarkSeconds = 2f;
        private const float MarkCooldown = 4f;
        private const float MarkBonus = 12f;
        private const float HeavyAimSeconds = 0.26f;
        private const float BurstSeconds = 2f;
        private const float BurstCooldown = 8f;

        [SerializeField] private GameObject kickEffectPrefab;
        [SerializeField] private GameObject kickImpactEffectPrefab;
        [SerializeField] private Sprite markSprite;

        private BoatHealth boat;
        private PlayerCharacter captain;
        private Rigidbody2D body;
        private float moveSpeed;
        private Sprite[] kickFrames;
        private Vector2? approachPosition;
        private bool holdingPosition;
        private Component kickTarget;
        private float nextKickAt;
        private float kickImpactAt;
        private float kickEndsAt;
        private EnemyCharacter markedEnemy;
        private SpriteRenderer markRenderer;
        private float markUntil;
        private float nextMarkAt;
        private EnemyCharacter aimedEnemy;
        private float aimReadyAt;
        private bool captainWasBusy;
        private float burstUntil;
        private float nextBurstAt;
        private readonly List<RaycastHit2D> kickLineHits = new List<RaycastHit2D>();

        public SpecialCompanionSkill Skill { get; private set; }
        public override float FireInterval => Skill == SpecialCompanionSkill.Repair && ActionTime < burstUntil
            ? base.FireInterval * 0.38f : base.FireInterval;
        protected override bool CanAct => base.CanAct && boat != null && !boat.IsDestroyed;

        protected override void Awake()
        {
            base.Awake();
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public void InitializeCompanion(BoatHealth owner, DamageSystem system, PlayerCharacter player,
            CrewDefinition profile, SpecialCompanionSkill skill, Sprite[] kickFrames = null)
        {
            if (owner == null || owner.Deck == null || system == null || player == null || profile == null
                || ProjectilePrefab == null)
                throw new ArgumentException("A companion needs its boat, captain, damage system, profile and projectile.");
            if (boat != null)
                throw new InvalidOperationException("The companion is already assigned to a boat.");

            boat = owner;
            captain = player;
            Skill = skill;
            this.kickFrames = kickFrames;
            moveSpeed = Mathf.Max(0.1f, profile.moveSpeed);
            ApplyCrewAppearance(profile);
            ConfigureCombat(system, ProjectilePrefab, profile.attackPower, profile.fireInterval);
        }

        private void Update()
        {
            if (Time.timeScale <= 0f)
                return;
            UpdateMark();
            approachPosition = null;
            holdingPosition = false;
            if (!CanAct || IsKnockedBack)
            {
                CancelAction();
                return;
            }

            if (Skill == SpecialCompanionSkill.Kick && UpdateKick())
                return;

            EnemyCharacter target = SelectEnemy();
            if (Skill == SpecialCompanionSkill.Repair)
                UpdateCoverBurst(target != null);
            if (target == null)
            {
                aimedEnemy = null;
                return;
            }

            holdingPosition = true;
            Vector2 direction = (Vector2)target.Hitbox.bounds.center - body.position;
            Aim(direction);
            if (Skill == SpecialCompanionSkill.Throw)
            {
                AimHeavyShot(target, direction);
                return;
            }
            if (TryFire(direction))
                PlayVisualFrames(SkillFrames, Mathf.Min(0.32f, FireInterval));
        }

        private void FixedUpdate()
        {
            if (!CanAct || holdingPosition)
            {
                MoveBody(Vector2.zero);
                return;
            }

            if (approachPosition.HasValue)
            {
                Vector2 step = approachPosition.Value - body.position;
                Aim(step);
                MoveBody(step.normalized * Mathf.Min(moveSpeed, step.magnitude / Time.fixedDeltaTime));
                return;
            }
            MoveBody(Wander(boat.Deck, moveSpeed * 0.5f));
        }

        private void Aim(Vector2 direction)
        {
            Face(direction);
            if (Visual != null && Mathf.Abs(direction.x) > 0.05f)
                Visual.flipX = direction.x < 0f;
        }

        private EnemyCharacter SelectEnemy()
        {
            bool preferThrower = Skill == SpecialCompanionSkill.Throw;
            EnemyCharacter aim = preferThrower && IsAvailable(aimedEnemy) ? aimedEnemy : null;
            if (aim != null && aim.IsPreparingThrow)
                return aim;

            EnemyCharacter target = aim;
            int priority = target == null ? -1 : 0;
            float nearest = float.PositiveInfinity;
            foreach (Character candidate in SceneCharacters)
            {
                if (candidate is not EnemyCharacter enemy || !IsAvailable(enemy)
                    || Skill == SpecialCompanionSkill.Kick && enemy.IsOnDeck)
                    continue;
                int score = preferThrower && enemy.IsPreparingThrow ? 1 : 0;
                float distance = (enemy.Hitbox.ClosestPoint(body.position) - body.position).sqrMagnitude;
                if (score < priority || score == priority && (target == aim && aim != null || distance >= nearest))
                    continue;
                target = enemy;
                priority = score;
                nearest = distance;
            }
            return target;
        }

        private static bool IsAvailable(EnemyCharacter enemy) => enemy != null && enemy.isActiveAndEnabled
            && enemy.Health > 0f && enemy.ActionsEnabled && enemy.Hitbox != null && enemy.Hitbox.enabled;

        private void AimHeavyShot(EnemyCharacter target, Vector2 direction)
        {
            if (!IsFireReady)
                return;
            if (aimedEnemy != target)
            {
                aimedEnemy = target;
                aimReadyAt = ActionTime + HeavyAimSeconds;
                PlayVisualFrames(SkillFrames, HeavyAimSeconds + 0.21f);
                return;
            }

            if (ActionTime >= aimReadyAt && TryFire(direction))
                aimedEnemy = null;
        }

        private void UpdateCoverBurst(bool hasTarget)
        {
            bool captainBusy = captain.IsRepairing || captain.IsStunned;
            if (hasTarget && captainBusy && !captainWasBusy && ActionTime >= nextBurstAt)
            {
                burstUntil = ActionTime + BurstSeconds;
                nextBurstAt = ActionTime + BurstCooldown;
            }
            captainWasBusy = captainBusy;
        }

        private bool UpdateKick()
        {
            if (ActionTime < kickEndsAt)
            {
                holdingPosition = true;
                if (kickTarget != null && ActionTime >= kickImpactAt)
                {
                    ResolveKick(kickTarget);
                    kickTarget = null;
                }
                return true;
            }
            if (ActionTime < nextKickAt)
                return false;

            Component target = FindKickTarget(out Vector2? approach);
            if (target == null)
                return false;
            if (approach.HasValue)
            {
                approachPosition = approach;
                return true;
            }

            Vector2 attackPoint = KickPoint(target);
            Vector2 direction = attackPoint - body.position;
            if (direction.sqrMagnitude < 0.0001f)
                direction = (Vector2)target.transform.position - body.position;
            Aim(direction);
            holdingPosition = true;
            kickTarget = target;
            kickImpactAt = ActionTime + KickImpactSeconds;
            kickEndsAt = ActionTime + KickSeconds;
            nextKickAt = ActionTime + KickCooldown;
            PlayVisualFrames(kickFrames, KickSeconds);
            VoyageAudio.Play(Sound.KickSwing, 0.47f, body.position);
            if (kickEffectPrefab != null)
                Instantiate(kickEffectPrefab, body.position + AimDirection * (KickRange * 0.65f), AimRotation);
            return true;
        }

        private Component FindKickTarget(out Vector2? approach)
        {
            approach = null;
            Component best = null;
            float distance = KickApproachRange * KickApproachRange;
            foreach (Character candidate in SceneCharacters)
            {
                if (candidate is EnemyCharacter enemy && enemy.IsOnDeck && IsAvailable(enemy)
                    && TryKickPosition(enemy, ref distance, out Vector2? step))
                {
                    best = enemy;
                    approach = step;
                }
            }
            if (best != null)
                return best;

            foreach (TimedBomb bomb in FindObjectsByType<TimedBomb>(FindObjectsSortMode.None))
            {
                if (!IsKickableBomb(bomb) || bomb.FuseRemaining > 1.8f)
                    continue;
                float approachTime = Mathf.Max(0f, (KickPoint(bomb) - body.position).magnitude - KickRange) / moveSpeed;
                if (bomb.FuseRemaining <= (approachTime + KickImpactSeconds) / ActionSpeed
                    || !TryKickPosition(bomb, ref distance, out Vector2? step))
                    continue;
                best = bomb;
                approach = step;
            }
            return best;
        }

        private bool TryKickPosition(Component target, ref float closest, out Vector2? approach)
        {
            approach = null;
            Vector2 offset = KickPoint(target) - body.position;
            float distance = offset.sqrMagnitude;
            if (distance >= closest)
                return false;

            if (distance > KickRange * KickRange)
            {
                Vector2 step = offset.normalized * (offset.magnitude - KickRange + 0.12f);
                Vector2 candidate = body.position + step;
                if (!CanWalkTo(boat.Deck, candidate, step))
                    return false;
                approach = candidate;
            }
            else if (IsKickBlocked(body.position + offset))
                return false;
            closest = distance;
            return true;
        }

        private bool IsKickBlocked(Vector2 point)
        {
            Physics2D.Linecast(body.position, point, new ContactFilter2D { useTriggers = false }, kickLineHits);
            foreach (RaycastHit2D hit in kickLineHits)
                if (hit.collider.GetComponentInParent<Character>() == null)
                    return true;
            return false;
        }

        private bool IsKickableBomb(TimedBomb bomb) => bomb != null && bomb.isActiveAndEnabled
            && bomb.TargetBoat == boat && !bomb.IsInFlight && bomb.FuseRemaining > 0f
            && bomb.TryGetComponent(out DestructibleObstacle obstacle) && !obstacle.IsCleared;

        private Vector2 KickPoint(Component target) => target is EnemyCharacter enemy
            ? enemy.Hitbox.ClosestPoint(body.position)
            : (Vector2)target.GetComponent<DestructibleObstacle>().VisualBounds.ClosestPoint(body.position);

        private void ResolveKick(Component target)
        {
            if (target is EnemyCharacter enemy && (!IsAvailable(enemy) || !enemy.IsOnDeck)
                || target is TimedBomb bomb && !IsKickableBomb(bomb))
                return;
            Vector2 point = KickPoint(target);
            Vector2 offset = point - body.position;
            if (offset.sqrMagnitude > KickRange * KickRange
                || offset.sqrMagnitude > 0.0001f && Vector2.Angle(offset, AimDirection) > 40f
                || IsKickBlocked(point))
                return;

            if (target is EnemyCharacter boarder)
            {
                if (DamageSystem.ApplyDamage(this, boarder, AttackPower * 2f) && boarder.Health > 0f)
                    boarder.TryKnockBack(AimDirection * 5f);
            }
            else if (target.GetComponent<DestructibleObstacle>().Remove(true, AimDirection, Camera.main)
                && kickImpactEffectPrefab != null)
            {
                Instantiate(kickImpactEffectPrefab, point, AimRotation);
            }
        }

        internal void OnProjectileHit(EnemyCharacter enemy)
        {
            if (Skill != SpecialCompanionSkill.Mark || !isActiveAndEnabled || !ActionsEnabled || !IsAvailable(enemy)
                || markedEnemy != null || enemy.CompanionSpotter != null || ActionTime < nextMarkAt)
                return;
            markedEnemy = enemy;
            enemy.CompanionSpotter = this;
            markUntil = Time.time + MarkSeconds;
            nextMarkAt = ActionTime + MarkCooldown;
            UpdateMark();
        }

        internal float GetMarkBonus(EnemyCharacter enemy, PlayerCharacter shooter)
        {
            return enemy == markedEnemy && shooter == captain && Time.time < markUntil && IsAvailable(enemy)
                ? MarkBonus : 0f;
        }

        internal void ConsumeMark(EnemyCharacter enemy)
        {
            if (enemy == markedEnemy)
                ClearMark();
        }

        private void UpdateMark()
        {
            if (!IsAvailable(markedEnemy) || Time.time >= markUntil)
            {
                ClearMark();
                return;
            }
            if (markSprite == null)
                return;

            if (markRenderer == null)
            {
                markRenderer = new GameObject("Companion Mark").AddComponent<SpriteRenderer>();
                markRenderer.transform.SetParent(transform, false);
                markRenderer.sprite = markSprite;
                markRenderer.sortingOrder = 15;
            }
            markRenderer.transform.position = markedEnemy.HealthBarAnchor + Vector3.up * 0.27f;
            markRenderer.enabled = true;
        }

        private void ClearMark()
        {
            if (markedEnemy != null && markedEnemy.CompanionSpotter == this)
                markedEnemy.CompanionSpotter = null;
            markedEnemy = null;
            if (markRenderer != null)
                markRenderer.enabled = false;
        }

        private void CancelAction()
        {
            if (kickEndsAt > 0f || aimedEnemy != null)
                StopVisualFrames();
            kickTarget = null;
            aimedEnemy = null;
            kickEndsAt = 0f;
        }

        public override void SetActionsAllowed(bool allowed)
        {
            base.SetActionsAllowed(allowed);
            if (allowed)
                return;
            CancelAction();
            ClearMark();
            burstUntil = 0f;
            captainWasBusy = false;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            CancelAction();
            ClearMark();
        }
    }
}
