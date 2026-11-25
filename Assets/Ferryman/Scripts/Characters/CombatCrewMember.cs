using System;
using System.Collections.Generic;
using Ferryman.Combat;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Characters
{
    [RequireComponent(typeof(Rigidbody2D), typeof(BoxCollider2D))]
    public sealed class CombatCrewMember : CombatCharacter
    {
        private const float AvoidanceDistance = 2.5f;
        private BoatHealth boat;
        private Rigidbody2D body;
        private bool hasTarget;
        private bool hasDeckSprite;
        private float wanderSpeed;
        private Character avoidedCrew;
        private float relationAttackMultiplier = 1f;
        private float relationFireRateMultiplier = 1f;

        public override float AttackPower => base.AttackPower * relationAttackMultiplier;
        public override float FireInterval => base.FireInterval / relationFireRateMultiplier;

        public bool IsAvoidingCrew => avoidedCrew != null && avoidedCrew.isActiveAndEnabled
            && ((Vector2)avoidedCrew.transform.position - body.position).sqrMagnitude < AvoidanceDistance * AvoidanceDistance;

        protected override bool CanAct => base.CanAct && boat != null && !boat.IsDestroyed;

        protected override void Awake()
        {
            base.Awake();
            body = GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            body.freezeRotation = true;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        }

        public void InitializeCrew(BoatHealth owner, DamageSystem system, CrewDefinition definition)
        {
            if (owner == null || system == null || ProjectilePrefab == null || definition == null || definition.role != CrewRole.Combat)
                throw new ArgumentException("Combat crew needs its boat, weapon, damage system and fixed combat definition.");
            if (boat != null)
                throw new InvalidOperationException("Combat crew is already assigned to a boat.");

            boat = owner;
            hasDeckSprite = definition.deckSprite != null;
            wanderSpeed = Mathf.Max(0.1f, definition.moveSpeed) * 0.5f;
            ConfigureCombat(system, ProjectilePrefab, definition.attackPower, definition.fireInterval);
            boat.Destroyed.AddListener(StopMovement);
        }

        private void Update()
        {
            if (!CanAct || IsAvoidingCrew)
            {
                hasTarget = false;
                StopVisualFrames();
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
            if (!hasTarget)
            {
                StopVisualFrames();
                return;
            }
            Vector2 direction = (Vector2)target.transform.position - body.position;
            Face(direction);
            if (hasDeckSprite && Visual != null && direction.x != 0f)
                Visual.flipX = direction.x < 0f;
            TryFire(direction);
        }

        public override bool TryFire(Vector2 direction)
        {
            if (!base.TryFire(direction))
                return false;
            PlayVisualFrames(SkillFrames, FireInterval);
            return true;
        }

        private void FixedUpdate()
        {
            if (IsAvoidingCrew)
                MoveBody(MoveAwayOnDeck(boat.Deck, avoidedCrew.transform.position, AvoidanceDistance, wanderSpeed * 2f));
            else
                MoveBody(hasTarget ? Vector2.zero : Wander(boat != null ? boat.Deck : null, wanderSpeed));
        }

        internal void SetAvoidedCrew(Character crew) => avoidedCrew = crew;

        internal void RefreshCrewRelations(string profileId, ISet<string> aboardProfiles)
        {
            relationAttackMultiplier = profileId == "cannoneer-boar" && aboardProfiles.Contains("gunner-monkey") ? 1.2f : 1f;
            relationFireRateMultiplier = profileId == "gunner-monkey" && aboardProfiles.Contains("cannoneer-boar") ? 1.2f : 1f;
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
