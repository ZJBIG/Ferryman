using System;
using Ferryman.Combat;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Characters
{
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class CombatCrewMember : CombatCharacter
    {
        private BoatHealth boat;
        private Rigidbody2D body;
        private bool hasTarget;
        private float wanderSpeed;

        protected override bool CanAct => base.CanAct && boat != null && !boat.IsDestroyed;

        protected override void Awake()
        {
            base.Awake();
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public void InitializeCrew(BoatHealth owner, DamageSystem system, Projectile projectile, CrewDefinition definition)
        {
            if (owner == null || system == null || projectile == null || definition == null || definition.role != CrewRole.Combat)
                throw new ArgumentException("Combat crew needs its boat, weapon, damage system and fixed combat definition.");
            if (boat != null)
                throw new InvalidOperationException("Combat crew is already assigned to a boat.");

            boat = owner;
            wanderSpeed = Mathf.Max(0.1f, definition.moveSpeed) * 0.5f;
            ConfigureCombat(system, projectile, definition.attackPower, definition.fireInterval);
            boat.Destroyed.AddListener(StopMovement);
        }

        private void Update()
        {
            if (!CanAct)
            {
                hasTarget = false;
                return;
            }

            Character target = null;
            float nearest = float.PositiveInfinity;
            foreach (var candidate in SceneCharacters)
            {
                if (candidate == null || !candidate.gameObject.activeInHierarchy
                    || !candidate.IsHostile || candidate.Health <= 0f || candidate.Hitbox == null || !candidate.Hitbox.enabled)
                    continue;
                float distance = ((Vector2)candidate.transform.position - body.position).sqrMagnitude;
                if (distance >= nearest)
                    continue;
                target = candidate;
                nearest = distance;
            }
            hasTarget = target != null;
            if (hasTarget)
            {
                Face((Vector2)target.transform.position - body.position);
                TryFire((Vector2)target.transform.position - body.position);
            }
        }

        private void FixedUpdate()
        {
            MoveBody(hasTarget ? Vector2.zero : Wander(boat != null ? boat.Deck : null, wanderSpeed));
        }

        private void StopMovement()
        {
            if (body != null)
                body.velocity = Vector2.zero;
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            StopMovement();
        }

        private void OnDestroy()
        {
            if (boat != null)
                boat.Destroyed.RemoveListener(StopMovement);
        }
    }
}
