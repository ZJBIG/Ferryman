using Ferryman.Characters;
using UnityEngine;

namespace Ferryman.Combat
{
    [RequireComponent(typeof(Rigidbody2D), typeof(CircleCollider2D))]
    public sealed class Projectile : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float flightSpeed = 8f;
        [SerializeField] private Collider2D hitbox;
        [SerializeField] private Character instigator;
        [SerializeField, Min(0.01f)] private float lifetime = 5f;
        [Header("Presentation")]
        [SerializeField] private Transform visualRoot;
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private bool useFactionTint = true;
        [SerializeField] private GameObject impactEffectPrefab;

        private Rigidbody2D body;
        private Vector2 direction;
        private float damage;
        private DamageSystem damageSystem;
        private bool hostile;
        private bool launched;
        private bool consumed;
        private float expiresAt;

        public float FlightSpeed => flightSpeed;
        public Collider2D Hitbox => hitbox;
        public Character Instigator => instigator;

        private void Awake()
        {
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            if (hitbox == null)
                hitbox = GetComponent<Collider2D>();
            hitbox.isTrigger = true;
            if (visual == null)
                visual = GetComponentInChildren<SpriteRenderer>();
            if (visualRoot == null && visual != null)
                visualRoot = visual.transform;
        }

        public void Launch(Vector2 direction, float damage, Character instigator, DamageSystem damageSystem)
        {
            if (direction.sqrMagnitude < 0.0001f || instigator == null || damageSystem == null)
            {
                Consume();
                return;
            }

            this.direction = direction.normalized;
            this.damage = damage;
            this.instigator = instigator;
            this.damageSystem = damageSystem;
            // Keep allegiance after the shooter dies while this projectile is still in flight.
            hostile = instigator.IsHostile;
            launched = true;
            expiresAt = Time.time + lifetime;
            if (instigator.Hitbox != null)
                Physics2D.IgnoreCollision(hitbox, instigator.Hitbox);
            if (visualRoot != null)
                visualRoot.rotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(this.direction.y, this.direction.x) * Mathf.Rad2Deg);
            if (useFactionTint && visual != null)
                visual.color = hostile ? new Color(1f, 0.25f, 0.15f) : new Color(1f, 0.85f, 0.15f);
        }

        private void FixedUpdate()
        {
            if (consumed || !launched)
                return;
            if (Time.time >= expiresAt)
            {
                Consume();
                return;
            }
            body.velocity = direction * flightSpeed;
        }

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (consumed || !launched)
                return;

            var target = other.GetComponentInParent<Character>();
            if (target == null || target == instigator || target.IsHostile == hostile)
                return;

            Consume();
            if (impactEffectPrefab != null)
                Instantiate(impactEffectPrefab, transform.position, Quaternion.identity);
            if (damageSystem != null)
                damageSystem.ApplyDamage(instigator, target, damage);
        }

        private void Consume()
        {
            consumed = true;
            hitbox.enabled = false;
            body.velocity = Vector2.zero;
            Destroy(gameObject);
        }

        private void Reset()
        {
            hitbox = GetComponent<Collider2D>();
        }
    }
}
