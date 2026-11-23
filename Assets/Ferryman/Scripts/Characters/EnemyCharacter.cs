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
            PlayerFirst,
            CrewFirst,
            ShipFirst
        }

        [System.Serializable]
        private sealed class BombPose
        {
            public Sprite heldPose;
            public Sprite projectile;
            public Vector2 launchOffset;
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
        private float hitFeedbackUntil;
        private float BombWindup => Mathf.Min(0.5f, bombInterval * 0.5f);

        public override bool IsHostile => true;
        public AttackKind AttackType => attackKind;
        public TargetPreference PreferredTarget => targetPreference;
        public TimedBomb BombPrefab => bombPrefab;
        internal bool ShowsHitFeedback => Time.time < hitFeedbackUntil;
        internal Vector3 HealthBarAnchor
        {
            get
            {
                bool holdingBomb = pendingBomb != null && idleSprite != null;
                Bounds bounds = holdingBomb ? idleSprite.bounds : VisualBounds;
                var bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                return holdingBomb ? Visual.transform.TransformPoint(bottomCenter) : bottomCenter;
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
            Face(aimedTarget.transform.position - transform.position);
            TryFire(AimDirection);
        }

        private void UpdateBombThrow()
        {
            if (boat == null || bombPrefab == null)
                return;
            if (pendingBomb == null && bombPoses != null && bombPoses.Length > 0
                && Time.time >= nextBombAt - BombWindup)
            {
                pendingBomb = bombPoses[Random.Range(0, bombPoses.Length)];
                idleSprite = Visual.sprite;
                Visual.sprite = pendingBomb.heldPose;
                nextBombAt = Mathf.Max(nextBombAt, Time.time + BombWindup);
            }
            if (Time.time < nextBombAt)
                return;

            nextBombAt = Time.time + bombInterval;
            Vector2Int size = boat.Deck.GridSize;
            Vector2 landing = boat.Deck.CellToWorld(new Vector2Int(Random.Range(0, size.x), Random.Range(0, size.y)));
            Face(landing - (Vector2)transform.position);
            Vector3 origin = pendingBomb != null ? transform.TransformPoint(pendingBomb.launchOffset) : transform.position;
            var bomb = Instantiate(bombPrefab, origin, Quaternion.identity);
            if (pendingBomb != null)
            {
                // The released artwork includes the edge hidden by the claw in the held pose.
                bomb.GetComponent<DestructibleObstacle>().SetAppearance(pendingBomb.projectile, 0.7f);
                Visual.sprite = idleSprite;
                pendingBomb = null;
            }
            bomb.Throw(boat, landing);
        }

        private Character SelectTarget()
        {
            Character best = null;
            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MaxValue;
            foreach (var character in SceneCharacters)
            {
                if (character == null || !character.gameObject.activeInHierarchy
                    || character.IsHostile || !character.ActionsEnabled)
                    continue;
                bool isPlayer = character is PlayerCharacter;
                int priority = targetPreference == TargetPreference.CrewFirst ? (isPlayer ? 1 : 0) : (isPlayer ? 0 : 1);
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

        public override bool TryReceiveDamage(float damage)
        {
            if (damage <= 0f || Health <= 0f)
                return false;

            Health = Mathf.Max(0f, Health - damage);
            hitFeedbackUntil = Time.time + 0.18f;
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
