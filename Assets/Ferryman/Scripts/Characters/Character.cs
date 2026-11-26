using System.Collections;
using System.Collections.Generic;
using Ferryman.Voyage;
using Ferryman.World;
using UnityEngine;
using UnityEngine.Events;

namespace Ferryman.Characters
{
    public abstract class Character : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float health = 100f;
        [SerializeField] private Collider2D hitbox;
        [SerializeField, Min(0f)] private float stunDuration = 1f;
        [Header("Presentation")]
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Material hitFlashMaterial;
        [SerializeField] private UnityEvent hitReceived = new UnityEvent();
        [SerializeField] private UnityEvent stunRecovered = new UnityEvent();

        private float stunnedUntil;
        private float activeStunDuration;
        private float slowStartedAt;
        private float slowUntil;
        private float lostActionTime;
        private bool showingSlow;
        private const float SlowedSpeed = 0.65f;
        private Color originalColor;
        private Quaternion originalVisualRotation;
        private Coroutine stunPose;
        private Coroutine hitPose;
        private SpriteRenderer[] visualParts;
        private Material[] restingMaterials;
        private Vector3 restingVisualScale;
        private bool actionsEnabled = true;
        private Rigidbody2D movementBody;
        private Vector2 knockbackVelocity;
        private float knockbackUntil;
        private const float KnockbackSeconds = 0.25f;
        private Vector2 wanderTarget;
        private bool wandering;
        private Vector2 lastStepPosition;
        private float stepDistance;
        private Sprite[] playingFrames;
        private Sprite[] walkFrames;
        private Sprite staticSprite;
        private float animationTime;
        private float animationDuration;
        private bool animationLoops;
        private float walkTime;
        private readonly List<RaycastHit2D> wanderHits = new List<RaycastHit2D>();
        private static Character[] sceneCharacters;
        private static int sceneCharactersFrame = -1;

        public float Health
        {
            get => health;
            protected set => health = value;
        }

        public Collider2D Hitbox => hitbox;
        protected SpriteRenderer Visual => visual;
        protected Material RestingVisualMaterial => restingMaterials?[0];
        protected Sprite[] SkillFrames { get; private set; }
        internal Bounds VisualBounds => visual != null ? visual.bounds : hitbox.bounds;
        public float MaxHealth { get; private set; }
        public bool IsStunned => Time.time < stunnedUntil;
        public bool IsSlowed => Time.time < slowUntil;
        public float ActionSpeed => IsSlowed ? SlowedSpeed : 1f;
        protected float ActionTime => Time.time - lostActionTime
            - (Mathf.Min(Time.time, slowUntil) - slowStartedAt) * (1f - SlowedSpeed);
        public float StunRemainingRatio => activeStunDuration > 0f
            ? Mathf.Clamp01((stunnedUntil - Time.time) / activeStunDuration)
            : 0f;
        public bool ActionsEnabled => actionsEnabled;
        public virtual bool IsHostile => false;
        protected virtual bool CanAct => Time.timeScale > 0f && actionsEnabled && !IsStunned && (!IsHostile || Health > 0f);
        protected bool IsKnockedBack => Time.time < knockbackUntil;

        public virtual bool TryKnockBack(Vector2 velocity)
        {
            if (!ActionsEnabled || movementBody == null || !movementBody.simulated || velocity.sqrMagnitude <= 0f)
                return false;
            knockbackVelocity = velocity;
            knockbackUntil = Time.time + KnockbackSeconds;
            wandering = false;
            return true;
        }

        protected void MoveBody(Vector2 velocity)
        {
            if (!ActionsEnabled)
            {
                movementBody.velocity = Vector2.zero;
                return;
            }
            Vector2 push = knockbackVelocity * Mathf.Clamp01((knockbackUntil - Time.time) / KnockbackSeconds);
            movementBody.velocity = (CanAct ? velocity * ActionSpeed : Vector2.zero) + push;
        }

        internal void ApplySlow(float seconds)
        {
            if (!IsSlowed)
            {
                lostActionTime += (slowUntil - slowStartedAt) * (1f - SlowedSpeed);
                slowStartedAt = Time.time;
            }
            slowUntil = Mathf.Max(slowUntil, Time.time + seconds);
            PlayHitReaction();
        }

        protected Vector2 Wander(BoatDeck deck, float speed)
        {
            if (!CanAct || IsKnockedBack || deck == null)
                return Vector2.zero;
            Vector2 offset = wanderTarget - movementBody.position;
            if (wandering && (offset.sqrMagnitude <= 0.08f * 0.08f
                || hitbox.Cast(offset.normalized, new ContactFilter2D { useTriggers = false }, wanderHits, offset.magnitude) > 0))
                wandering = false;
            if (!wandering)
            {
                Vector2Int grid = deck.GridSize;
                int count = grid.x * grid.y;
                int start = Random.Range(0, count);
                for (int i = 0; i < count; i++)
                {
                    int index = (start + i) % count;
                    var cell = new Vector2Int(index % grid.x, index / grid.x);
                    Vector2 candidate = deck.CellToWorld(cell);
                    offset = candidate - movementBody.position;
                    if (offset.magnitude < 0.25f || !CanWalkTo(deck, candidate, offset))
                        continue;
                    wanderTarget = candidate;
                    wandering = true;
                    break;
                }
            }
            return wandering ? offset.normalized * Mathf.Min(speed, offset.magnitude / Time.fixedDeltaTime) : Vector2.zero;
        }

        protected Vector2 MoveAwayOnDeck(BoatDeck deck, Vector2 threat, float clearance, float speed)
        {
            if (!CanAct || IsKnockedBack || deck == null)
                return Vector2.zero;

            Vector2 position = movementBody.position;
            float clearanceSquared = clearance * clearance;
            float bestClearance = (position - threat).sqrMagnitude;
            Vector2 bestStep = Vector2.zero;
            Vector2Int grid = deck.GridSize;
            for (int x = 0; x < grid.x; x++)
            {
                for (int y = 0; y < grid.y; y++)
                {
                    Vector2 candidate = deck.CellToWorld(new Vector2Int(x, y));
                    Vector2 step = candidate - position;
                    float candidateClearance = (candidate - threat).sqrMagnitude;
                    bool better = bestClearance >= clearanceSquared
                        ? candidateClearance >= clearanceSquared && step.sqrMagnitude < bestStep.sqrMagnitude
                        : candidateClearance > bestClearance;
                    if (better && CanWalkTo(deck, candidate, step))
                    {
                        bestClearance = candidateClearance;
                        bestStep = step;
                    }
                }
            }
            wandering = false;
            return bestStep.normalized * Mathf.Min(speed, bestStep.magnitude / Time.fixedDeltaTime);
        }

        protected bool CanWalkTo(BoatDeck deck, Vector2 candidate, Vector2 step)
        {
            return deck.CanPlace(candidate, (Vector2)hitbox.bounds.size + Vector2.one * 0.04f, hitbox)
                && hitbox.Cast(step.normalized, new ContactFilter2D { useTriggers = false }, wanderHits, step.magnitude) == 0;
        }

        protected static Character[] SceneCharacters
        {
            get
            {
                // Share one search per frame; newly spawned actors enter the snapshot next frame.
                if (sceneCharactersFrame != Time.frameCount)
                {
                    sceneCharacters = FindObjectsByType<Character>(FindObjectsSortMode.None);
                    sceneCharactersFrame = Time.frameCount;
                }
                return sceneCharacters;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetSceneCharacters()
        {
            sceneCharacters = null;
            sceneCharactersFrame = -1;
        }

        public virtual void SetActionsAllowed(bool allowed)
        {
            actionsEnabled = allowed;
            if (!allowed)
            {
                knockbackUntil = 0f;
                wandering = false;
                var body = GetComponent<Rigidbody2D>();
                if (body != null)
                    body.velocity = Vector2.zero;
            }
        }

        internal void ApplyCrewAppearance(CrewDefinition profile)
        {
            ApplyCrewSprite(profile.deckSprite);
            walkFrames = profile.walkFrames;
            SkillFrames = profile.skillFrames;
            if (profile.deckSprite != null && visual != null && profile.deckSpriteScale > 0f)
            {
                Vector3 size = visual.transform.localScale;
                visual.transform.localScale = new Vector3(size.x * profile.deckSpriteScale, size.y * profile.deckSpriteScale, size.z);
            }
            if (visual != null)
                restingVisualScale = visual.transform.localScale;
        }

        protected void PlayVisualFrames(Sprite[] frames, float duration, bool loop = false)
        {
            if (visual == null || frames == null || frames.Length == 0 || loop && playingFrames == frames)
                return;
            playingFrames = frames;
            animationTime = 0f;
            animationDuration = duration;
            animationLoops = loop;
            visual.sprite = frames[0];
        }

        protected void StopVisualFrames()
        {
            if (playingFrames == null)
                return;
            playingFrames = null;
            if (visual != null)
                visual.sprite = staticSprite;
        }

        internal void ApplyCrewSprite(Sprite sprite)
        {
            if (sprite == null || visual == null)
                return;

            // The authored body already includes the weapon or tool.
            foreach (SpriteRenderer part in visual.transform.parent.GetComponentsInChildren<SpriteRenderer>())
            {
                if (part != visual)
                    part.enabled = false;
            }

            visual.transform.SetParent(transform, false);
            visual.transform.localScale = new Vector3(2.028f, 2.028f, 1f);
            originalVisualRotation = visual.transform.localRotation;
            restingVisualScale = visual.transform.localScale;
            visual.sprite = sprite;
            staticSprite = sprite;
            originalColor = Color.white;
            visual.color = originalColor;
        }

        protected virtual void Awake()
        {
            MaxHealth = health;
            movementBody = GetComponent<Rigidbody2D>();
            lastStepPosition = transform.localPosition;
            if (hitbox == null)
                hitbox = GetComponent<Collider2D>();
            if (visual == null)
                visual = GetComponentInChildren<SpriteRenderer>();
            if (visual != null)
            {
                originalColor = visual.color;
                originalVisualRotation = visual.transform.localRotation;
                restingVisualScale = visual.transform.localScale;
                staticSprite = visual.sprite;
                visualParts = visual.GetComponentsInChildren<SpriteRenderer>(true);
                restingMaterials = new Material[visualParts.Length];
                for (int i = 0; i < visualParts.Length; i++)
                    restingMaterials[i] = visualParts[i].sharedMaterial;
            }
        }

        private void LateUpdate()
        {
            if (visual != null && showingSlow != IsSlowed)
            {
                showingSlow = IsSlowed;
                visual.color = RestingColor;
            }
            if (CanAct && visual != null)
            {
                if (playingFrames != null)
                {
                    if (animationTime >= animationDuration)
                    {
                        if (animationLoops)
                            animationTime %= animationDuration;
                        else
                            StopVisualFrames();
                    }
                }
                if (playingFrames != null)
                {
                    int index = (int)(animationTime / animationDuration * playingFrames.Length);
                    visual.sprite = playingFrames[Mathf.Min(index, playingFrames.Length - 1)];
                    animationTime += Time.deltaTime * ActionSpeed;
                }
                else if (walkFrames != null && walkFrames.Length > 0 && !IsKnockedBack
                    && movementBody != null && movementBody.simulated && movementBody.velocity.sqrMagnitude > 0.0025f)
                {
                    if (Mathf.Abs(movementBody.velocity.x) > 0.05f)
                        visual.flipX = movementBody.velocity.x < 0f;
                    walkTime = (walkTime + Time.deltaTime * 8f * ActionSpeed) % walkFrames.Length;
                    visual.sprite = walkFrames[(int)walkTime];
                }
            }
            if (IsHostile || movementBody == null)
                return;

            Vector2 position = transform.localPosition;
            float distance = Vector2.Distance(lastStepPosition, position);
            lastStepPosition = position;
            if (!CanAct || IsKnockedBack || !movementBody.simulated || distance > 1.5f)
            {
                stepDistance = 0f;
                return;
            }

            if (movementBody.velocity.sqrMagnitude < 0.04f)
                return;
            stepDistance += distance;
            if (stepDistance >= 0.9f)
            {
                stepDistance %= 0.9f;
                VoyageAudio.Footstep(transform.position, this is PlayerCharacter);
            }
        }

        public virtual bool TryReceiveDamage(float damage, float attackStunDuration = 0f)
        {
            if (damage <= 0f || IsStunned)
                return false;

            activeStunDuration = attackStunDuration > 0f ? attackStunDuration : stunDuration;
            stunnedUntil = Time.time + activeStunDuration;
            knockbackUntil = 0f;
            var body = GetComponent<Rigidbody2D>();
            if (body != null)
                body.velocity = Vector2.zero;
            ShowStunFeedback();
            return true;
        }

        public virtual bool TryReceivePlayerKick(Vector2 velocity)
        {
            if (!ActionsEnabled || IsHostile || this is PlayerCharacter)
                return false;
            TryKnockBack(velocity);
            if (!IsStunned)
                activeStunDuration = stunDuration;
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + stunDuration / 3f);
            if (movementBody != null)
                movementBody.velocity = Vector2.zero;
            ShowStunFeedback();
            return true;
        }

        private void ShowStunFeedback()
        {
            if (stunPose == null && IsStunned)
                stunPose = StartCoroutine(HoldStunPose());
            NotifyHit();
        }

        protected void NotifyHit()
        {
            PlayHitReaction();
            hitReceived.Invoke();
        }

        private IEnumerator HoldStunPose()
        {
            if (visual != null)
            {
                StopVisualFrames();
                visual.sprite = staticSprite;
                visual.transform.localRotation = originalVisualRotation * Quaternion.Euler(0f, 0f, 90f);
            }
            while (IsStunned)
                yield return null;
            if (visual != null)
                visual.transform.localRotation = originalVisualRotation;
            stunPose = null;
            stunRecovered.Invoke();
        }

        internal void PlayHitReaction()
        {
            if (visual == null || !gameObject.activeInHierarchy)
                return;
            if (hitPose != null)
                StopCoroutine(hitPose);
            hitPose = StartCoroutine(FlashAndSquash());
        }

        private IEnumerator FlashAndSquash()
        {
            const float duration = 0.2f;
            float start = Time.time;
            bool flashing = true;
            SetFlash(true);
            while (Time.time - start < duration)
            {
                float elapsed = Time.time - start;
                if (flashing && elapsed >= 0.075f)
                {
                    SetFlash(false);
                    flashing = false;
                }
                float progress = elapsed / duration;
                float pulse = Mathf.Cos(progress * Mathf.PI * 2f) * (1f - progress) * 0.16f;
                visual.transform.localScale = Vector3.Scale(restingVisualScale, new Vector3(1f + pulse, 1f - pulse, 1f));
                yield return null;
            }
            RestoreHitPose();
            hitPose = null;
        }

        private void SetFlash(bool flash)
        {
            if (hitFlashMaterial == null || visualParts == null)
                return;
            for (int i = 0; i < visualParts.Length; i++)
                visualParts[i].sharedMaterial = flash ? hitFlashMaterial : restingMaterials[i];
        }

        private void RestoreHitPose()
        {
            if (visual != null)
                visual.transform.localScale = restingVisualScale;
            SetFlash(false);
        }

        private Color RestingColor => IsSlowed ? Color.Lerp(originalColor, new Color(0.47f, 0.79f, 1f, originalColor.a), 0.47f)
            : originalColor;

        protected virtual void OnDisable()
        {
            knockbackUntil = 0f;
            wandering = false;
            if (stunPose != null)
                StopCoroutine(stunPose);
            if (hitPose != null)
                StopCoroutine(hitPose);
            stunPose = hitPose = null;
            RestoreHitPose();
            if (visual != null)
            {
                if (!IsHostile)
                    visual.transform.localRotation = originalVisualRotation;
                if (!IsHostile || Health > 0f)
                    visual.color = originalColor;
            }
            showingSlow = false;
        }

        protected virtual void Reset()
        {
            hitbox = GetComponent<Collider2D>();
            visual = GetComponentInChildren<SpriteRenderer>();
        }
    }
}
