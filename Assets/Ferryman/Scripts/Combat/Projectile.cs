using Ferryman.Characters;
using Ferryman.Voyage;
using Ferryman.World;
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
        private BoatHealth hullTarget;
        private float slowSeconds;
        private float stunSeconds;
        private float pushSpeed;
        private bool hostile;
        private bool launched;
        private bool consumed;
        private bool critical;
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

        public void Launch(Vector2 direction, float damage, Character instigator, DamageSystem damageSystem, bool critical = false)
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
            this.critical = critical;
            // Keep allegiance after the shooter dies while this projectile is still in flight.
            hostile = instigator.IsHostile;
            if (instigator is EnemyCharacter enemy)
            {
                hullTarget = enemy.ProjectileHullTarget;
                slowSeconds = enemy.ProjectileSlowSeconds;
                stunSeconds = enemy.AttackStunDuration;
                pushSpeed = enemy.ProjectilePushSpeed;
            }
            launched = true;
            expiresAt = Time.time + lifetime;
            if (instigator.Hitbox != null)
                Physics2D.IgnoreCollision(hitbox, instigator.Hitbox);
            if (visualRoot != null)
            {
                visualRoot.rotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(this.direction.y, this.direction.x) * Mathf.Rad2Deg);
                if (hostile)
                    visualRoot.localScale *= 2f;
            }
            if (useFactionTint && visual != null)
            {
                visual.color = !hostile ? new Color(1f, 0.85f, 0.15f)
                    : slowSeconds > 0f ? new Color(0.52f, 0.84f, 1f)
                    : hullTarget != null ? new Color(0.95f, 0.55f, 0.28f)
                    : pushSpeed > 0f ? new Color(0.85f, 1f, 0.36f)
                    : stunSeconds > 1f ? new Color(1f, 0.51f, 0.72f)
                    : new Color(1f, 0.25f, 0.15f);
            }
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

            if (hullTarget != null)
            {
                if (other is EdgeCollider2D && other.transform == hullTarget.Deck.transform)
                {
                    Vector2 contact = other.ClosestPoint(transform.position);
                    Consume();
                    if (hullTarget.ApplyDamage(damage, contact, randomizeRepairPosition: true) && impactEffectPrefab != null)
                        Instantiate(impactEffectPrefab, contact, Quaternion.identity);
                }
                return;
            }

            var target = other.GetComponentInParent<Character>();
            if (target == null || target == instigator || target.IsHostile == hostile || target.Health <= 0f)
                return;
            if (target.IsStunned && !target.IsHostile && !(target is PlayerCharacter))
                return;

            Consume();
            if (target.IsStunned)
                return;
            if (slowSeconds > 0f)
            {
                target.ApplySlow(slowSeconds);
                VoyageAudio.Play(Sound.WaterBubbles, 0.34f, transform.position);
                return;
            }
            if (pushSpeed > 0f)
            {
                if (target.TryKnockBack(direction * pushSpeed))
                    target.PlayHitReaction();
                return;
            }
            if (damageSystem == null || hostile && stunSeconds <= 0f)
                return;

            EnemyCharacter enemy = target as EnemyCharacter;
            SpecialCompanion marker = enemy != null ? enemy.CompanionSpotter : null;
            float markBonus = damage > 0f && marker != null && instigator is PlayerCharacter captain
                ? marker.GetMarkBonus(enemy, captain) : 0f;
            if (!damageSystem.ApplyDamage(instigator, target, damage + markBonus, stunSeconds, critical))
                return;
            if (markBonus > 0f && marker != null)
                marker.ConsumeMark(enemy);
            if (enemy != null && instigator is SpecialCompanion spotter && spotter != null)
                spotter.OnProjectileHit(enemy);
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
