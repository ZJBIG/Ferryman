using Ferryman.Combat;
using UnityEngine;
using UnityEngine.Events;

namespace Ferryman.Characters
{
    public abstract class CombatCharacter : Character
    {
        [SerializeField, Min(0f)] private float moveSpeed = 4f;
        [SerializeField, Min(0f)] private float attackPower = 10f;
        [SerializeField] private Projectile projectilePrefab;
        [SerializeField, Min(0.01f)] private float fireInterval = 0.2f;
        [SerializeField, Min(0f)] private float muzzleDistance = 0.75f;
        [SerializeField] private Transform aimVisual;
        [SerializeField] private DamageSystem damageSystem;
        [Header("Weapon Presentation")]
        [SerializeField] private Transform muzzle;
        [SerializeField] private GameObject shotEffectPrefab;
        [SerializeField] private UnityEvent shotFired = new UnityEvent();

        private float nextFireAt;

        public float MoveSpeed => moveSpeed;
        public float AttackPower => attackPower;
        public Projectile ProjectilePrefab => projectilePrefab;
        public DamageSystem DamageSystem => damageSystem;
        public float FireInterval => fireInterval;
        protected Vector2 AimDirection { get; private set; } = Vector2.right;
        protected Quaternion AimRotation { get; private set; } = Quaternion.identity;

        public void ConfigureCombat(DamageSystem system, Projectile projectile, float power, float interval)
        {
            damageSystem = system;
            projectilePrefab = projectile;
            attackPower = Mathf.Max(0f, power);
            fireInterval = Mathf.Max(0.01f, interval);
        }

        protected void Face(Vector2 direction)
        {
            if (direction.sqrMagnitude < 0.0001f)
                return;

            AimDirection = direction.normalized;
            AimRotation = Quaternion.Euler(0f, 0f, Mathf.Atan2(AimDirection.y, AimDirection.x) * Mathf.Rad2Deg);
            if (aimVisual != null)
                aimVisual.rotation = AimRotation;
        }

        public virtual bool TryFire(Vector2 direction)
        {
            if (!CanAct || Time.time < nextFireAt || direction.sqrMagnitude < 0.0001f
                || projectilePrefab == null || damageSystem == null)
                return false;

            Face(direction);
            nextFireAt = Time.time + fireInterval;
            Vector2 origin = muzzle != null ? (Vector2)muzzle.position
                : (Vector2)transform.position + AimDirection * muzzleDistance;
            var projectile = Instantiate(projectilePrefab, origin, Quaternion.identity);
            projectile.Launch(AimDirection, AttackPower, this, damageSystem);
            if (shotEffectPrefab != null)
                Instantiate(shotEffectPrefab, origin, AimRotation);
            shotFired.Invoke();
            return true;
        }
    }
}
