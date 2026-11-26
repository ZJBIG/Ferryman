using System.Collections;
using Ferryman.Combat;
using Ferryman.Voyage;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Characters
{
    public enum ColossusPhase
    {
        OuterShell,
        Crossfire,
        LooseCore
    }

    public sealed partial class EnemyCharacter : CombatCharacter
    {
        private const float CrossfireInterval = 3.8f;
        private const float LooseCoreInterval = 5f;
        private const float CoreExposureSeconds = 2.8f;

        public enum AttackKind
        {
            Projectile,
            Bomb,
            BoardingMelee
        }
        public enum TargetPreference
        {
            Nearest,
            CrewFirst,
            ShipFirst
        }

        [System.Serializable]
        private sealed class BombPose
        {
            public Sprite heldPose;
            public Sprite[] heldFrames;
            public Sprite projectile;
            public Vector2 launchOffset;
            public Vector2[] bodyOutline = System.Array.Empty<Vector2>();
        }

        [SerializeField] private PlayerCharacter target;
        [SerializeField] private GameObject hitEffectPrefab;
        [SerializeField] private GameObject deathEffectPrefab;
        [SerializeField] private DestructibleObstacle deathObstaclePrefab;
        [SerializeField] private AttackKind attackKind;
        [SerializeField] private TargetPreference targetPreference;
        [SerializeField] private string displayName;
        [SerializeField] private bool isBoss;
        [SerializeField] private TimedBomb bombPrefab;
        [SerializeField, Min(0.01f)] private float bombInterval = 3f;
        [SerializeField] private BombPose[] bombPoses;
        [SerializeField] private Sprite[] attackFrames;
        [SerializeField, Min(0f)] private float projectileSlowSeconds;
        [SerializeField, Min(0f)] private float projectilePushSpeed;
        [SerializeField, Min(0f)] private float attackStunDuration;

        private BoatHealth boat;
        private float nextBombAt;
        private BombPose pendingBomb;
        private bool bossWindingUp;
        private ColossusPhase bossThrowPhase;
        private float coreExposedUntil;
        private Sprite idleSprite;
        private Vector2[] idleBodyOutline;
        private Vector3 idleBodyAnchor;
        private float hitFeedbackUntil;
        private Sprite hitFeedbackSprite;
        private Vector2 hitFeedbackLocalPoint;
        private bool hasHitFeedbackPoint;
        private float BombWindup => Mathf.Min(0.5f, bombInterval * 0.5f);

        public override bool IsHostile => true;
        public bool IsBoss => isBoss;
        public ColossusPhase BossPhase => !IsBoss || Health > MaxHealth * 2f / 3f
            ? ColossusPhase.OuterShell
            : Health > MaxHealth / 3f ? ColossusPhase.Crossfire : ColossusPhase.LooseCore;
        public bool IsCoreExposed => IsBoss && Health > 0f && Time.time < coreExposedUntil;
        public AttackKind AttackType => attackKind;
        public TargetPreference PreferredTarget => targetPreference;
        public TimedBomb BombPrefab => bombPrefab;
        internal float AttackStunDuration => attackStunDuration;
        internal DestructibleObstacle DeathObstaclePrefab => deathObstaclePrefab;
        internal BoatHealth ProjectileHullTarget => targetPreference == TargetPreference.ShipFirst ? boat : null;
        internal float ProjectileSlowSeconds => projectileSlowSeconds;
        internal float ProjectilePushSpeed => projectilePushSpeed;
        internal bool IsPreparingThrow => attackKind == AttackKind.Bomb && (bossWindingUp || pendingBomb != null);
        internal SpecialCompanion CompanionSpotter { get; set; }
        internal Bounds SpawnBounds
        {
            get
            {
                Bounds bounds = VisualBounds;
                void Include(Sprite sprite)
                {
                    if (sprite == null || Visual == null)
                        return;
                    Bounds frame = sprite.bounds;
                    bounds.Encapsulate(Visual.transform.TransformPoint(frame.min));
                    bounds.Encapsulate(Visual.transform.TransformPoint(frame.max));
                }
                foreach (Sprite frame in attackFrames ?? System.Array.Empty<Sprite>())
                    Include(frame);
                foreach (BombPose pose in bombPoses ?? System.Array.Empty<BombPose>())
                {
                    Include(pose.heldPose);
                    foreach (Sprite frame in pose.heldFrames ?? System.Array.Empty<Sprite>())
                        Include(frame);
                }
                if (Hitbox != null)
                    bounds.Encapsulate(Hitbox.bounds);
                return bounds;
            }
        }
        internal Vector3 HealthBarAnchor
        {
            get
            {
                if (pendingBomb != null && idleBodyOutline != null)
                    return transform.TransformPoint(idleBodyAnchor);
                Bounds bounds = Hitbox != null ? Hitbox.bounds : VisualBounds;
                return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            }
        }

        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name.Replace("(Clone)", "") : displayName;

        public void InitializeEnemy(BoatHealth targetBoat, PlayerCharacter player, DamageSystem system)
        {
            boat = targetBoat;
            target = player;
            ConfigureCombat(system, ProjectilePrefab, AttackPower,
                attackKind == AttackKind.Bomb ? bombInterval : FireInterval);
            if (attackKind != AttackKind.BoardingMelee && boat != null && Visual != null)
            {
                bool mirrored = transform.position.x > boat.Deck.GetComponent<EdgeCollider2D>().bounds.max.x;
                if (Visual.flipX != mirrored && Hitbox is PolygonCollider2D outline)
                {
                    for (int path = 0; path < outline.pathCount; path++)
                    {
                        Vector2[] points = outline.GetPath(path);
                        for (int i = 0; i < points.Length; i++)
                            points[i].x = -points[i].x;
                        outline.SetPath(path, points);
                    }
                    outline.offset = new Vector2(-outline.offset.x, outline.offset.y);
                }
                Visual.flipX = mirrored;
            }
            if (IsBoss)
                nextBombAt = Time.time + 1.2f;
            else if (attackKind == AttackKind.Bomb && bombPoses != null && bombPoses.Length > 0)
                nextBombAt = Time.time + BombWindup;
        }

        private void Update()
        {
            if (boat == null && target != null)
                boat = target.BoatHealth;
            if (!CanAct || target == null || (boat != null && boat.IsDestroyed))
                return;

            if (attackKind == AttackKind.Bomb)
            {
                UpdateBombThrow();
                return;
            }
            if (attackKind == AttackKind.BoardingMelee)
                return;
            if (targetPreference == TargetPreference.ShipFirst)
            {
                if (boat != null)
                    TryFire(boat.Deck.transform.position - transform.position);
                return;
            }

            Character aimedTarget = SelectTarget();
            if (aimedTarget == null)
                return;
            Face(aimedTarget.Hitbox.bounds.center - transform.position);
            TryFire(AimDirection);
        }

        public override bool TryFire(Vector2 direction)
        {
            if (!base.TryFire(direction))
                return false;
            PlayVisualFrames(attackFrames, Mathf.Min(FireInterval, 0.48f));
            return true;
        }

        internal IEnumerator DiveTo(Vector2 destination)
        {
            if (Visual == null || Hitbox == null || deathEffectPrefab == null)
                yield break;

            StopVisualFrames();
            Bounds body = VisualBounds;
            Vector3 splash = new Vector3(body.center.x, body.min.y + body.size.y * 0.23f, body.center.z);
            Vector2 travel = destination - (Vector2)transform.position;
            SetActionsAllowed(false);
            Hitbox.enabled = false;
            Visual.enabled = false;
            Instantiate(deathEffectPrefab, splash, Quaternion.identity);
            VoyageAudio.Play(Sound.WaterSplash, 0.55f, splash);
            yield return new WaitForSeconds(0.36f);
            if (this == null || Health <= 0f)
                yield break;

            transform.position += (Vector3)travel;
            Instantiate(deathEffectPrefab, splash + (Vector3)travel, Quaternion.identity);
            VoyageAudio.Play(Sound.WaterSplash, 0.45f, transform.position);
            Visual.enabled = true;
            Hitbox.enabled = true;
            SetActionsAllowed(true);
        }

        private void UpdateBombThrow()
        {
            if (boat == null || bombPrefab == null)
                return;
            if (IsBoss)
            {
                UpdateBossThrow();
                return;
            }
            if (pendingBomb == null && bombPoses != null && bombPoses.Length > 0
                && Time.time >= nextBombAt - BombWindup)
            {
                BombPose pose = bombPoses[Random.Range(0, bombPoses.Length)];
                if (pose.bodyOutline != null && pose.bodyOutline.Length >= 3 && Hitbox is PolygonCollider2D outline)
                {
                    idleBodyAnchor = transform.InverseTransformPoint(HealthBarAnchor);
                    idleBodyOutline ??= outline.GetPath(0);
                    Vector2[] points = (Vector2[])pose.bodyOutline.Clone();
                    if (Visual.flipX)
                    {
                        for (int i = 0; i < points.Length; i++)
                            points[i].x = -points[i].x;
                    }
                    outline.SetPath(0, points);
                }
                pendingBomb = pose;
                idleSprite = Visual.sprite;
                Visual.sprite = pendingBomb.heldPose;
                PlayVisualFrames(pose.heldFrames, BombWindup, loop: true);
                nextBombAt = Mathf.Max(nextBombAt, Time.time + BombWindup);
            }
            if (Time.time < nextBombAt)
                return;

            nextBombAt = Time.time + bombInterval;
            TryThrowBomb(pendingBomb);
            if (pendingBomb != null)
            {
                StopVisualFrames();
                Visual.sprite = idleSprite;
                if (idleBodyOutline != null && Hitbox is PolygonCollider2D outline)
                    outline.SetPath(0, idleBodyOutline);
                pendingBomb = null;
            }
        }

        private void UpdateBossThrow()
        {
            // A hit may change phase during the windup. Keep the volley already shown to the player.
            ColossusPhase phase = bossWindingUp ? bossThrowPhase : BossPhase;
            float windup = phase switch
            {
                ColossusPhase.Crossfire => 0.75f,
                ColossusPhase.LooseCore => 1f,
                _ => BombWindup
            };
            if (!bossWindingUp && Time.time >= nextBombAt - windup)
            {
                bossWindingUp = true;
                bossThrowPhase = phase;
                PlayVisualFrames(attackFrames, windup * 2f);
                if (phase != ColossusPhase.OuterShell)
                    VoyageAudio.Play(Sound.TrashGather, 0.55f, transform.position);
            }
            if (Time.time < nextBombAt)
                return;

            var (count, interval) = phase switch
            {
                ColossusPhase.Crossfire => (2, CrossfireInterval),
                ColossusPhase.LooseCore => (3, LooseCoreInterval),
                _ => (1, bombInterval)
            };
            nextBombAt = Time.time + interval;
            bossWindingUp = false;
            for (int i = 0; i < count; i++)
            {
                if (!TryThrowBomb(null))
                    break;
            }
            if (phase == ColossusPhase.LooseCore)
                coreExposedUntil = Time.time + CoreExposureSeconds;
        }

        private bool TryThrowBomb(BombPose pose)
        {
            if (!TryChooseBombLanding(out Vector2 landing))
                return false;
            Face(landing - (Vector2)transform.position);
            Vector2 launchOffset = pose != null ? pose.launchOffset : Vector2.zero;
            if (Visual != null && Visual.flipX)
                launchOffset.x = -launchOffset.x;
            Vector3 origin = pose != null ? Visual.transform.TransformPoint(launchOffset) : transform.position;
            var bomb = Instantiate(bombPrefab, origin, Quaternion.identity);
            if (pose != null)
            {
                // The released artwork includes the edge hidden by the claw in the held pose.
                bomb.GetComponent<DestructibleObstacle>().SetAppearance(pose.projectile);
            }
            bomb.Throw(boat, landing);
            return true;
        }

        private bool TryChooseBombLanding(out Vector2 landing)
        {
            BoatDeck deck = boat.Deck;
            Vector2Int size = deck.GridSize;
            Vector2 footprint = bombPrefab.GetComponent<DestructibleObstacle>().Footprint;
            landing = default;
            int validCount = 0;
            Physics2D.SyncTransforms();
            for (int x = 0; x < size.x; x++)
            {
                for (int y = 0; y < size.y; y++)
                {
                    Vector2 candidate = deck.CellToWorld(new Vector2Int(x, y));
                    if (!deck.CanPlace(candidate, footprint))
                        continue;
                    if (Random.Range(0, ++validCount) == 0)
                        landing = candidate;
                }
            }
            return validCount > 0;
        }

        private Character SelectTarget()
        {
            Character best = null;
            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MaxValue;
            foreach (var character in SceneCharacters)
            {
                if (!CanTarget(character))
                    continue;
                int priority = targetPreference == TargetPreference.CrewFirst && character is PlayerCharacter ? 1 : 0;
                float distance = (character.transform.position - transform.position).sqrMagnitude;
                if (priority < bestPriority || priority == bestPriority && distance < bestDistance)
                {
                    best = character;
                    bestPriority = priority;
                    bestDistance = distance;
                }
            }
            return best;
        }

        private static bool CanTarget(Character character) => character != null && character.gameObject.activeInHierarchy
            && !character.IsHostile && character.ActionsEnabled && !character.IsStunned
            && character.Hitbox != null && character.Hitbox.enabled;

        internal bool TryGetHitFeedbackPosition(out Vector3 position)
        {
            position = default;
            if (Time.time >= hitFeedbackUntil || Visual == null || Visual.sprite == null)
                return false;

            if (Visual.sprite != hitFeedbackSprite)
                SetHitFeedbackPoint(hasHitFeedbackPoint);
            if (!hasHitFeedbackPoint)
                return false;

            var point = new Vector2(Visual.flipX ? -hitFeedbackLocalPoint.x : hitFeedbackLocalPoint.x,
                Visual.flipY ? -hitFeedbackLocalPoint.y : hitFeedbackLocalPoint.y);
            position = Visual.transform.TransformPoint(point);
            return true;
        }

        private void SetHitFeedbackPoint(bool keepVisiblePoint = false)
        {
            hitFeedbackSprite = Visual != null ? Visual.sprite : null;
            hasHitFeedbackPoint = false;
            if (hitFeedbackSprite == null || !hitFeedbackSprite.texture.isReadable)
                return;

            Sprite sprite = hitFeedbackSprite;
            Rect rect = sprite.rect;
            Texture2D texture = sprite.texture;
            var origin = new Vector2Int((int)rect.x, (int)rect.y);
            Vector2Int size = new Vector2Int((int)rect.width, (int)rect.height);
            if (keepVisiblePoint)
            {
                Vector2 pixel = hitFeedbackLocalPoint * sprite.pixelsPerUnit + sprite.pivot + rect.position;
                if (rect.Contains(pixel) && texture.GetPixel(Mathf.FloorToInt(pixel.x), Mathf.FloorToInt(pixel.y)).a >= 0.5f)
                {
                    hasHitFeedbackPoint = true;
                    return;
                }
            }
            int count = size.x * size.y;
            // Try random pixels first, then finish with a bounded scan for sparse sprites.
            for (int attempt = 0; attempt < 32 + count; attempt++)
            {
                int index = attempt < 32 ? Random.Range(0, count) : attempt - 32;
                var pixel = new Vector2Int(index % size.x, index / size.x);
                if (texture.GetPixel(origin.x + pixel.x, origin.y + pixel.y).a < 0.5f)
                    continue;
                hasHitFeedbackPoint = true;
                hitFeedbackLocalPoint = ((Vector2)pixel + Vector2.one * 0.5f - sprite.pivot) / sprite.pixelsPerUnit;
                return;
            }
        }

        public override bool TryReceiveDamage(float damage, float attackStunDuration = 0f) => TryReceiveDamage(damage, critical: false);

        internal bool TryReceiveDamage(float damage, bool critical)
        {
            if (damage <= 0f || Health <= 0f)
                return false;

            float actualDamage = Mathf.Min(IsCoreExposed ? damage * 2f : damage, Health);
            Health -= actualDamage;
            hitFeedbackUntil = Time.time + 0.18f;
            SetHitFeedbackPoint();
            Bounds bounds = VisualBounds;
            Vector3 impactPoint = TryGetHitFeedbackPosition(out Vector3 visiblePoint) ? visiblePoint : bounds.center;
            if (hitEffectPrefab != null)
                Instantiate(hitEffectPrefab, impactPoint, Quaternion.Euler(0f, 0f, Random.Range(-35f, 35f)));
            if (Visual != null)
                EnemyDamagePopup.Play(actualDamage, bounds, impactPoint, RestingVisualMaterial, critical);
            NotifyHit();

            if (Health <= 0f)
            {
                bool leftRemains = LeaveBoardingRemains();
                foreach (Collider2D collider in GetComponentsInChildren<Collider2D>())
                    collider.enabled = false;
                SetActionsAllowed(false);
                if (TryGetComponent(out Rigidbody2D body))
                    body.simulated = false;
                if (leftRemains || Visual == null)
                {
                    Destroy(gameObject);
                    return true;
                }
                if (deathEffectPrefab != null)
                {
                    var waterline = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * 0.23f, bounds.center.z);
                    var splash = Instantiate(deathEffectPrefab, waterline, Quaternion.identity);
                    splash.transform.localScale *= IsBoss ? 2f : 1f;
                }
                StartCoroutine(Sink(bounds.size.y));
            }
            return true;
        }

        private IEnumerator Sink(float height)
        {
            const float duration = 0.52f;
            float started = Time.time;
            Transform sprite = Visual.transform;
            Vector3 origin = sprite.localPosition;
            Color color = Visual.color;
            while (Time.time - started < duration)
            {
                float elapsed = Time.time - started;
                float depth = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((elapsed - 0.07f) / (duration - 0.07f)));
                sprite.localPosition = origin + Vector3.down * (height * 0.55f * depth);
                Visual.color = Color.Lerp(color, new Color(0.37f, 0.68f, 0.76f, 0f), depth);
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
