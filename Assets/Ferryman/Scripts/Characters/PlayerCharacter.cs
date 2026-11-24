using System.Collections.Generic;
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
        [SerializeField] private LineRenderer meleeOutline;
        [SerializeField] private GameObject meleeEffectPrefab;
        [SerializeField] private GameObject meleeImpactEffectPrefab;
        [SerializeField] private UnityEvent meleePerformed = new UnityEvent();
        [Header("Repair")]
        [SerializeField] private BoatHealth boatHealth;
        [SerializeField, Min(0f)] private float repairSpeed = 50f;
        [SerializeField, Min(0)] private int repairRange = 1;
        [SerializeField, Min(1)] private int simultaneousRepairs = 1;

        public BoatDeck MovementDeck => movementDeck;
        public BoatHealth BoatHealth => boatHealth;
        public bool IsRepairing => repairTargets.Count > 0;
        protected override bool CanAct => base.CanAct && (boatHealth == null || !boatHealth.IsDestroyed);

        private readonly List<RepairPoint> repairTargets = new List<RepairPoint>();
        private Rigidbody2D body;
        private Vector2 moveInput;
        private Camera aimCamera;
        private float nextMeleeAt;
        private float meleeVisibleUntil;

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
            moveInput = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical"));
            if (meleeOutline != null)
                meleeOutline.enabled = Time.time < meleeVisibleUntil;
            if (!CanAct)
            {
                StopRepairing();
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

            bool meleeRequested = Input.GetMouseButtonDown(1);
            bool fireRequested = Input.GetMouseButton(0);
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
            return base.TryFire(direction);
        }

        public bool TryMelee(Vector2 direction)
        {
            StopRepairing();
            if (!CanAct || Time.time < nextMeleeAt || direction.sqrMagnitude < 0.0001f
                || DamageSystem == null)
                return false;

            Face(direction);
            nextMeleeAt = Time.time + meleeCooldown;
            Vector2 origin = transform.position;
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
                var obstacle = hit.GetComponentInParent<DestructibleObstacle>();
                if (character == this || hit.isTrigger && character == null && obstacle == null)
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
                        character.TryKnockBack((away.sqrMagnitude > 0.0001f ? away.normalized : AimDirection) * 8f);
                    }
                    else
                        DamageSystem.ApplyDamage(this, character, AttackPower);
                }
                if (meleeImpactEffectPrefab != null && (character == null || !character.IsHostile))
                    Instantiate(meleeImpactEffectPrefab, contact, AimRotation);
            }

            if (meleeEffectPrefab != null)
                Instantiate(meleeEffectPrefab, origin + AimDirection * (meleeRange * 0.65f), AimRotation);
            else
                ShowMeleeOutline(origin);
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
                point.AddProgress(this, repairSpeed * deltaTime);
            repairTargets.RemoveAll(point => point == null || point.IsComplete);
            return true;
        }

        public override void SetActionsAllowed(bool allowed)
        {
            base.SetActionsAllowed(allowed);
            if (!allowed)
            {
                StopRepairing();
                moveInput = Vector2.zero;
                if (meleeOutline != null)
                    meleeOutline.enabled = false;
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

        public override bool TryReceiveDamage(float damage)
        {
            if (!base.TryReceiveDamage(damage))
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
            if (meleeOutline != null)
                meleeOutline.enabled = false;
        }

        private void ShowMeleeOutline(Vector2 origin)
        {
            if (meleeOutline == null)
                return;

            const int segments = 16;
            float facing = Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg;
            meleeOutline.positionCount = segments + 2;
            meleeOutline.SetPosition(0, origin);
            for (int i = 0; i <= segments; i++)
            {
                float angle = (facing + Mathf.Lerp(-meleeAngle * 0.5f, meleeAngle * 0.5f,
                    (float)i / segments)) * Mathf.Deg2Rad;
                meleeOutline.SetPosition(i + 1,
                    origin + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * meleeRange);
            }
            meleeOutline.enabled = true;
            meleeVisibleUntil = Time.time + 0.12f;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopRepairing();
            moveInput = Vector2.zero;
            if (body != null)
                body.velocity = Vector2.zero;
            if (meleeOutline != null)
                meleeOutline.enabled = false;
        }

        private void OnDestroy()
        {
            if (boatHealth != null)
                boatHealth.Destroyed.RemoveListener(StopForDestroyedBoat);
        }
    }
}
