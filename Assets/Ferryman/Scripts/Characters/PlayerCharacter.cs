using System.Collections.Generic;
using Ferryman.Voyage;
using Ferryman.World;
using UnityEngine;
using UnityEngine.Events;

namespace Ferryman.Characters
{
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class PlayerCharacter : CombatCharacter
    {
        [SerializeField, Tooltip("玩家允许移动的甲板区域。")]
        private BoatDeck movementDeck;
        [SerializeField, Min(0f)] private float meleeRange = 1.5f;
        [SerializeField, Range(0f, 360f)] private float meleeAngle = 90f;
        [SerializeField, Min(0.01f)] private float meleeCooldown = 0.5f;
        [SerializeField] private GameObject meleeEffectPrefab;
        [SerializeField] private GameObject meleeImpactEffectPrefab;
        [SerializeField] private UnityEvent meleePerformed = new UnityEvent();
        [Header("Captain Presentation")]
        [SerializeField] private Sprite[] captainWalkFrames;
        [SerializeField] private Sprite[] captainShootFrames;
        [SerializeField] private Sprite[] captainMeleeFrames;
        [SerializeField] private SpriteRenderer cornGun;
        [Header("Repair")]
        [SerializeField] private BoatHealth boatHealth;
        [SerializeField, Min(0f)] private float repairSpeed = 30f;
        [SerializeField, Min(0)] private int repairRange = 1;
        [SerializeField, Min(1)] private int simultaneousRepairs = 1;

        public BoatDeck MovementDeck => movementDeck;
        public BoatHealth BoatHealth => boatHealth;
        public bool IsRepairing => repairTargets.Count > 0;
        public float RepairSpeed => repairSpeed;
        public int RepairRange => repairRange;
        public int SimultaneousRepairs => simultaneousRepairs;
        public bool BlockMouseAttacks { get; set; }
        public void ConfigureRepair(float speed, int range, int parallel)
        {
            StopRepairing();
            repairSpeed = Mathf.Max(0f, speed);
            repairRange = Mathf.Max(0, range);
            simultaneousRepairs = Mathf.Max(1, parallel);
        }
        protected override bool CanAct => base.CanAct && (boatHealth == null || !boatHealth.IsDestroyed);

        private readonly List<RepairPoint> repairTargets = new List<RepairPoint>();
        private Rigidbody2D body;
        private Vector2 moveInput;
        private Camera aimCamera;
        private float nextMeleeAt;
        private float shotStartedAt = float.NegativeInfinity;
        private float meleePoseUntil;
        private float shotPoseUntil;

        protected override void Awake()
        {
            base.Awake();
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            aimCamera = Camera.main;
            if (boatHealth == null && movementDeck != null)
                boatHealth = movementDeck.GetComponentInParent<BoatHealth>();
            if (boatHealth != null)
                boatHealth.Destroyed.AddListener(StopForDestroyedBoat);
        }

        private void Update()
        {
            if (Time.timeScale == 0f)
            {
                moveInput = Vector2.zero;
                return;
            }
            moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (!CanAct)
            {
                StopRepairing();
                ResetCaptainPose();
                return;
            }

            if (aimCamera == null)
                aimCamera = Camera.main;
            Vector3 mouse = Input.mousePosition;
            // A hidden or resized Game view can report an invalid mouse position.
            if (aimCamera != null && float.IsFinite(mouse.x) && float.IsFinite(mouse.y))
            {
                mouse.z = Mathf.Abs(transform.position.z - aimCamera.transform.position.z);
                Face(aimCamera.ScreenToWorldPoint(mouse) - transform.position);
            }

            bool meleeRequested = !BlockMouseAttacks && Input.GetMouseButtonDown(1);
            bool fireRequested = !BlockMouseAttacks && Input.GetMouseButton(0);
            // Attack intent interrupts repair even while the weapon is cooling down.
            if (moveInput.sqrMagnitude > 0f || meleeRequested || fireRequested)
                StopRepairing();

            if (meleeRequested)
                TryMelee(AimDirection);
            else if (fireRequested)
                TryFire(AimDirection);
            else if (moveInput.sqrMagnitude == 0f && Input.GetKey(KeyCode.E))
                TryRepair(Time.deltaTime);
            else
                StopRepairing();

            UpdateCaptainPose();
        }

        private void FixedUpdate()
        {
            Move(moveInput);
        }

        // Apply movement during a physics step; collisions are resolved by Unity.
        public void Move(Vector2 direction)
        {
            if (direction.sqrMagnitude > 0f)
                StopRepairing();
            MoveBody(Vector2.ClampMagnitude(direction, 1f) * MoveSpeed);
        }

        public override bool TryFire(Vector2 direction)
        {
            StopRepairing();
            if (!base.TryFire(direction))
                return false;

            shotStartedAt = ActionTime;
            float duration = Mathf.Min(FireInterval, 0.22f);
            shotPoseUntil = shotStartedAt + duration;
            meleePoseUntil = 0f;
            PlayVisualFrames(captainShootFrames, duration);
            UpdateCaptainPose();
            return true;
        }

        private void UpdateCaptainPose()
        {
            if (cornGun == null || Visual == null)
                return;

            bool swinging = ActionTime < meleePoseUntil;
            bool shooting = ActionTime < shotPoseUntil;
            bool walking = !swinging && !shooting && body.simulated && !IsKnockedBack
                && body.velocity.sqrMagnitude > 0.01f;
            if (walking)
            {
                if (Mathf.Abs(body.velocity.x) > 0.05f)
                    Visual.flipX = body.velocity.x < 0f;
                PlayVisualFrames(captainWalkFrames, 0.5f, loop: true);
            }
            else if (!swinging)
            {
                Visual.flipX = AimDirection.x < 0f;
                if (!shooting)
                    StopVisualFrames();
            }

            cornGun.enabled = !walking && !swinging;
            cornGun.flipY = AimDirection.x < 0f;
            cornGun.sortingOrder = AimDirection.y > 0.7f ? Visual.sortingOrder - 1 : Visual.sortingOrder + 1;
            float recoil = 1f - Mathf.Clamp01((ActionTime - shotStartedAt) / 0.14f);
            cornGun.transform.localPosition = Vector3.left * (0.13f * recoil * recoil);
            cornGun.transform.localRotation = Quaternion.Euler(0f, 0f, (cornGun.flipY ? -1f : 1f) * 10f * recoil);
        }

        private void ResetCaptainPose()
        {
            StopVisualFrames();
            shotStartedAt = float.NegativeInfinity;
            shotPoseUntil = meleePoseUntil = 0f;
            if (cornGun == null)
                return;
            cornGun.enabled = true;
            cornGun.transform.localPosition = Vector3.zero;
            cornGun.transform.localRotation = Quaternion.identity;
        }

        public bool TryMelee(Vector2 direction)
        {
            StopRepairing();
            if (!CanAct || ActionTime < nextMeleeAt || direction.sqrMagnitude < 0.0001f
                || DamageSystem == null)
                return false;

            Face(direction);
            nextMeleeAt = ActionTime + meleeCooldown;
            const float meleePoseDuration = 0.30f;
            shotPoseUntil = 0f;
            meleePoseUntil = ActionTime + meleePoseDuration;
            if (Visual != null)
                Visual.flipX = AimDirection.x < 0f;
            PlayVisualFrames(captainMeleeFrames, meleePoseDuration);
            UpdateCaptainPose();
            Vector2 origin = body.position;
            VoyageAudio.Play(Sound.KickSwing, 0.66f, origin);
            var hits = new HashSet<Collider2D>();
            foreach (var hit in Physics2D.OverlapCircleAll(origin, meleeRange))
            {
                Vector2 offset = hit.ClosestPoint(origin) - origin;
                if (Vector2.Angle(AimDirection, offset) <= meleeAngle * 0.5f)
                    hits.Add(hit);
            }

            // A hitbox can cross a sector edge while its nearest point is outside the angle.
            for (int edge = -1; edge <= 1; edge += 2)
            {
                Vector2 boundary = Quaternion.Euler(0f, 0f, edge * meleeAngle * 0.5f) * AimDirection;
                foreach (var contact in Physics2D.RaycastAll(origin, boundary, meleeRange))
                    hits.Add(contact.collider);
            }

            var struck = new HashSet<Object>();
            Camera kickCamera = aimCamera != null ? aimCamera : Camera.main;
            foreach (var hit in hits)
            {
                var character = hit.GetComponentInParent<Character>();
                if (character == this || character is EnemyCharacter { IsBoss: true })
                    continue;
                var obstacle = hit.GetComponentInParent<DestructibleObstacle>();
                if (hit.isTrigger && character == null && obstacle == null)
                    continue;
                Object entity = obstacle != null ? (Object)obstacle : character != null ? character : hit.gameObject;
                if (!struck.Add(entity))
                    continue;
                Vector2 contact = hit.ClosestPoint(origin);
                if (obstacle != null)
                {
                    Vector2 away = (Vector2)obstacle.transform.position - origin;
                    obstacle.Kick(away.sqrMagnitude > 0.0001f ? away.normalized : AimDirection, kickCamera);
                }
                else if (character != null)
                {
                    if (!character.IsHostile)
                    {
                        Vector2 away = (Vector2)character.transform.position - origin;
                        if (character.TryReceivePlayerKick((away.sqrMagnitude > 0.0001f ? away.normalized : AimDirection) * 8f))
                            VoyageAudio.Play(Sound.KickFlesh, position: contact);
                    }
                    else
                        DamageSystem.ApplyDamage(this, character, AttackPower);
                }
                else
                    VoyageAudio.Play(Sound.KickWood, position: contact);
                if (meleeImpactEffectPrefab != null && character == null)
                    Instantiate(meleeImpactEffectPrefab, contact, AimRotation);
            }

            foreach (var bomb in FindObjectsOfType<TimedBomb>())
            {
                var obstacle = bomb.GetComponent<DestructibleObstacle>();
                if (obstacle.IsInFlight || obstacle.IsCleared || struck.Contains(obstacle))
                    continue;
                Bounds bounds = obstacle.VisualBounds;
                Vector2 contact = bounds.ClosestPoint(origin);
                Vector2 offset = contact - origin;
                bool reached = offset.sqrMagnitude <= meleeRange * meleeRange
                    && Vector2.Angle(AimDirection, offset) <= meleeAngle * 0.5f;
                for (int edge = -1; !reached && edge <= 1; edge += 2)
                {
                    Vector2 boundary = Quaternion.Euler(0f, 0f, edge * meleeAngle * 0.5f) * AimDirection;
                    if (bounds.IntersectRay(new Ray(origin, boundary), out float distance) && distance <= meleeRange)
                    {
                        reached = true;
                        contact = origin + boundary * distance;
                    }
                }
                if (!reached)
                    continue;
                Vector2 away = (Vector2)obstacle.transform.position - origin;
                obstacle.Kick(away.sqrMagnitude > 0.0001f ? away.normalized : AimDirection, kickCamera);
                if (meleeImpactEffectPrefab != null)
                    Instantiate(meleeImpactEffectPrefab, contact, AimRotation);
            }

            if (meleeEffectPrefab != null)
                Instantiate(meleeEffectPrefab, origin + AimDirection * (meleeRange * 0.65f), AimRotation);
            meleePerformed.Invoke();
            return true;
        }

        public bool TryRepair(float deltaTime)
        {
            if (!CanAct || IsKnockedBack || boatHealth == null || boatHealth.Deck == null || repairSpeed <= 0f || deltaTime < 0f)
            {
                StopRepairing();
                return false;
            }

            Vector2Int origin = boatHealth.Deck.WorldToCell(body.position);
            for (int i = repairTargets.Count - 1; i >= 0; i--)
            {
                RepairPoint point = repairTargets[i];
                if (point != null && BoatDeck.CellDistance(origin, point.Cell) <= repairRange
                    && point.BeginRepair(this))
                    continue;
                repairTargets.RemoveAt(i);
                if (point != null)
                    point.EndRepair(this);
            }

            while (repairTargets.Count < simultaneousRepairs)
            {
                RepairPoint point = boatHealth.SelectRepairPoint(origin, repairRange, repairTargets);
                if (point == null || !point.BeginRepair(this))
                    break;
                repairTargets.Add(point);
            }

            if (repairTargets.Count == 0)
                return false;

            body.velocity = Vector2.zero;
            foreach (var point in repairTargets)
                point.AddProgress(this, repairSpeed * deltaTime * ActionSpeed);
            repairTargets.RemoveAll(point => point == null || point.IsComplete);
            return true;
        }

        public override void SetActionsAllowed(bool allowed)
        {
            base.SetActionsAllowed(allowed);
            if (!allowed)
            {
                StopRepairing();
                ResetCaptainPose();
                moveInput = Vector2.zero;
            }
        }

        public void StopRepairing()
        {
            while (repairTargets.Count > 0)
            {
                int last = repairTargets.Count - 1;
                RepairPoint point = repairTargets[last];
                repairTargets.RemoveAt(last);
                if (point != null)
                    point.EndRepair(this);
            }
        }

        public override bool TryReceiveDamage(float damage, float attackStunDuration = 0f)
        {
            if (!base.TryReceiveDamage(damage, attackStunDuration))
                return false;
            StopRepairing();
            return true;
        }

        public override bool TryKnockBack(Vector2 velocity)
        {
            if (!CanAct || !base.TryKnockBack(velocity))
                return false;
            StopRepairing();
            return true;
        }

        private void StopForDestroyedBoat()
        {
            StopRepairing();
            moveInput = Vector2.zero;
            body.velocity = Vector2.zero;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopRepairing();
            ResetCaptainPose();
            moveInput = Vector2.zero;
            if (body != null)
                body.velocity = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (boatHealth != null)
                boatHealth.Destroyed.RemoveListener(StopForDestroyedBoat);
        }
    }
}
