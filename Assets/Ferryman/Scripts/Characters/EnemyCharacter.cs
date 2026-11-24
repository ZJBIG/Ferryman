using Ferryman.Combat;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Characters
{
    public sealed class EnemyCharacter : CombatCharacter
    {
        public enum AttackKind
        {
            Projectile,
            Bomb
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
            public Sprite projectile;
            public Vector2 launchOffset;
            public Vector2[] bodyOutline = System.Array.Empty<Vector2>();
        }

        [SerializeField] private PlayerCharacter target;
        [SerializeField] private GameObject deathEffectPrefab;
        [SerializeField] private AttackKind attackKind;
        [SerializeField] private TargetPreference targetPreference;
        [SerializeField] private string displayName;
        [SerializeField] private TimedBomb bombPrefab;
        [SerializeField, Min(0.01f)] private float bombInterval = 3f;
        [SerializeField] private BombPose[] bombPoses;

        private BoatHealth boat;
        private float nextBombAt;
        private BombPose pendingBomb;
        private Sprite idleSprite;
        private Vector2[] idleBodyOutline;
        private Vector3 idleBodyAnchor;
        private float hitFeedbackUntil;
        private Sprite hitFeedbackSprite;
        private Vector2 hitFeedbackLocalPoint;
        private bool hasHitFeedbackPoint;
        private float BombWindup => Mathf.Min(0.5f, bombInterval * 0.5f);

        public override bool IsHostile => true;
        public AttackKind AttackType => attackKind;
        public TargetPreference PreferredTarget => targetPreference;
        public TimedBomb BombPrefab => bombPrefab;
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
            if (attackKind == AttackKind.Bomb && bombPoses != null && bombPoses.Length > 0)
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

            Character aimedTarget = SelectTarget();
            if (aimedTarget == null)
                return;
            Face(aimedTarget.Hitbox.bounds.center - transform.position);
            TryFire(AimDirection);
        }

        private void UpdateBombThrow()
        {
            if (boat == null || bombPrefab == null)
                return;
            if (pendingBomb == null && bombPoses != null && bombPoses.Length > 0
                && Time.time >= nextBombAt - BombWindup)
            {
                BombPose pose = bombPoses[Random.Range(0, bombPoses.Length)];
                if (pose.bodyOutline != null && pose.bodyOutline.Length >= 3 && Hitbox is PolygonCollider2D outline)
                {
                    idleBodyAnchor = transform.InverseTransformPoint(HealthBarAnchor);
                    idleBodyOutline ??= outline.GetPath(0);
                    outline.SetPath(0, pose.bodyOutline);
                }
                pendingBomb = pose;
                idleSprite = Visual.sprite;
                Visual.sprite = pendingBomb.heldPose;
                nextBombAt = Mathf.Max(nextBombAt, Time.time + BombWindup);
            }
            if (Time.time < nextBombAt)
                return;

            nextBombAt = Time.time + bombInterval;
            if (TryChooseBombLanding(out Vector2 landing))
            {
                Face(landing - (Vector2)transform.position);
                Vector3 origin = pendingBomb != null ? Visual.transform.TransformPoint(pendingBomb.launchOffset) : transform.position;
                var bomb = Instantiate(bombPrefab, origin, Quaternion.identity);
                if (pendingBomb != null)
                {
                    // The released artwork includes the edge hidden by the claw in the held pose.
                    bomb.GetComponent<DestructibleObstacle>().SetAppearance(pendingBomb.projectile, 0.7f);
                }
                bomb.Throw(boat, landing);
            }
            if (pendingBomb != null)
            {
                Visual.sprite = idleSprite;
                if (idleBodyOutline != null && Hitbox is PolygonCollider2D outline)
                    outline.SetPath(0, idleBodyOutline);
                pendingBomb = null;
            }
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
                if (character == null || !character.gameObject.activeInHierarchy || character.IsHostile
                    || !character.ActionsEnabled || character.IsStunned || character.Hitbox == null || !character.Hitbox.enabled)
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

        public override bool TryReceiveDamage(float damage)
        {
            if (damage <= 0f || Health <= 0f)
                return false;

            Health = Mathf.Max(0f, Health - damage);
            hitFeedbackUntil = Time.time + 0.18f;
            SetHitFeedbackPoint();
            ShowFeedback(Color.white, 0.1f);
            if (Health <= 0f)
            {
                Hitbox.enabled = false;
                enabled = false;
                if (deathEffectPrefab != null)
                    Instantiate(deathEffectPrefab, transform.position, Quaternion.identity);
                Destroy(gameObject);
            }
            return true;
        }
    }
}
