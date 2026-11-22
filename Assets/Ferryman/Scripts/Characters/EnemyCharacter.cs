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

        [SerializeField, Tooltip("敌人应避开的船体；当前阶段敌人固定在船外。")]
        private Transform excludedBoat;
        [SerializeField] private PlayerCharacter target;
        [SerializeField] private GameObject deathEffectPrefab;
        [SerializeField] private AttackKind attackKind;
        [SerializeField] private TargetPreference targetPreference;
        [SerializeField] private string displayName;
        [SerializeField] private TimedBomb bombPrefab;
        [SerializeField, Min(0.01f)] private float bombInterval = 3f;

        private BoatHealth boat;
        private float nextBombAt;
        private Character aimedTarget;

        public Transform ExcludedBoat => excludedBoat;
        public override bool IsHostile => true;
        public AttackKind AttackType => attackKind;
        public TargetPreference PreferredTarget => targetPreference;
        public TimedBomb BombPrefab => bombPrefab;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name.Replace("(Clone)", "") : displayName;

        public void InitializeEnemy(BoatHealth targetBoat, PlayerCharacter player, DamageSystem system)
        {
            boat = targetBoat;
            target = player;
            excludedBoat = targetBoat.transform;
            ConfigureCombat(system, ProjectilePrefab, AttackPower,
                attackKind == AttackKind.Bomb ? bombInterval : FireInterval);
        }

        private void Update()
        {
            if (boat == null && target != null)
                boat = target.BoatHealth;
            if (!CanAct || target == null || (boat != null && boat.IsDestroyed))
                return;

            if (attackKind == AttackKind.Bomb)
            {
                if (boat == null || bombPrefab == null || Time.time < nextBombAt)
                    return;
                nextBombAt = Time.time + bombInterval;
                Vector2Int size = boat.Deck.GridSize;
                Vector2 landing = boat.Deck.CellToWorld(new Vector2Int(Random.Range(0, size.x), Random.Range(0, size.y)));
                Face(landing - (Vector2)transform.position);
                var bomb = Instantiate(bombPrefab, transform.position, Quaternion.identity);
                bomb.Throw(boat, landing);
                return;
            }

            aimedTarget = SelectTarget();
            if (aimedTarget == null)
                return;
            Face(aimedTarget.transform.position - transform.position);
            TryFire(AimDirection);
        }

        private Character SelectTarget()
        {
            Character best = null;
            float bestDistance = float.PositiveInfinity;
            int bestPriority = int.MaxValue;
            foreach (var character in FindObjectsOfType<Character>())
            {
                if (character.IsHostile || !character.ActionsEnabled)
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
