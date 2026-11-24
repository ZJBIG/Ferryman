using System.Collections;
using System.Collections.Generic;
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
        [SerializeField] private bool usePlaceholderFeedback = true;
        [SerializeField] private UnityEvent hitReceived = new UnityEvent();
        [SerializeField] private UnityEvent stunRecovered = new UnityEvent();

        private float stunnedUntil;
        private Color originalColor;
        private Coroutine feedback;
        private bool actionsEnabled = true;
        private Rigidbody2D movementBody;
        private Vector2 knockbackVelocity;
        private float knockbackUntil;
        private const float KnockbackSeconds = 0.25f;
        private Vector2 wanderTarget;
        private bool wandering;
        private float nextWanderAt;
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
        internal Bounds VisualBounds => visual != null ? visual.bounds : hitbox.bounds;
        public float MaxHealth { get; private set; }
        public bool IsStunned => Time.time < stunnedUntil;
        public float StunRemainingRatio => stunDuration > 0f
            ? Mathf.Clamp01((stunnedUntil - Time.time) / stunDuration)
            : 0f;
        public bool ActionsEnabled => actionsEnabled;
        public virtual bool IsHostile => false;
        protected virtual bool CanAct => actionsEnabled && !IsStunned && (!IsHostile || Health > 0f);
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
            movementBody.velocity = (CanAct ? velocity : Vector2.zero) + push;
        }

        protected Vector2 Wander(BoatDeck deck, float speed)
        {
            if (!CanAct || IsKnockedBack || deck == null || Time.time < nextWanderAt)
                return Vector2.zero;
            if (!wandering)
            {
                Vector2Int grid = deck.GridSize;
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    Vector2 candidate = deck.CellToWorld(new Vector2Int(Random.Range(0, grid.x), Random.Range(0, grid.y)));
                    Vector2 delta = candidate - movementBody.position;
                    if (delta.magnitude < 0.5f || !deck.CanPlace(candidate, (Vector2)hitbox.bounds.size + Vector2.one * 0.04f, hitbox)
                        || hitbox.Cast(delta.normalized, new ContactFilter2D { useTriggers = false }, wanderHits, delta.magnitude) > 0)
                        continue;
                    wanderTarget = candidate;
                    wandering = true;
                    break;
                }
                if (!wandering)
                {
                    nextWanderAt = Time.time + 0.5f;
                    return Vector2.zero;
                }
            }
            Vector2 offset = wanderTarget - movementBody.position;
            if (offset.magnitude <= 0.08f
                || hitbox.Cast(offset.normalized, new ContactFilter2D { useTriggers = false }, wanderHits, offset.magnitude) > 0)
            {
                wandering = false;
                nextWanderAt = Time.time + Random.Range(0.5f, 1.5f);
                return Vector2.zero;
            }
            return offset.normalized * Mathf.Min(speed, offset.magnitude / Time.fixedDeltaTime);
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

        protected virtual void Awake()
        {
            MaxHealth = health;
            movementBody = GetComponent<Rigidbody2D>();
            if (hitbox == null)
                hitbox = GetComponent<Collider2D>();
            if (visual == null)
                visual = GetComponentInChildren<SpriteRenderer>();
            if (visual != null)
                originalColor = visual.color;
        }

        public virtual bool TryReceiveDamage(float damage)
        {
            if (damage <= 0f || IsStunned)
                return false;

            stunnedUntil = Time.time + stunDuration;
            knockbackUntil = 0f;
            var body = GetComponent<Rigidbody2D>();
            if (body != null)
                body.velocity = Vector2.zero;
            ShowFeedback(new Color(0.65f, 0.65f, 0.65f), stunDuration);
            return true;
        }

        public virtual bool TryReceivePlayerKick(Vector2 velocity)
        {
            if (!ActionsEnabled || IsHostile || this is PlayerCharacter)
                return false;
            TryKnockBack(velocity);
            stunnedUntil = Mathf.Max(stunnedUntil, Time.time + stunDuration / 3f);
            if (movementBody != null)
                movementBody.velocity = Vector2.zero;
            ShowFeedback(new Color(0.65f, 0.65f, 0.65f), Mathf.Max(0f, stunnedUntil - Time.time));
            return true;
        }

        protected void ShowFeedback(Color color, float duration)
        {
            if (feedback != null)
                StopCoroutine(feedback);
            feedback = StartCoroutine(RestoreColor(color, duration));
            hitReceived.Invoke();
        }

        private IEnumerator RestoreColor(Color color, float duration)
        {
            if (usePlaceholderFeedback && visual != null)
                visual.color = color;
            yield return new WaitForSeconds(duration);
            if (usePlaceholderFeedback && visual != null)
                visual.color = originalColor;
            feedback = null;
            if (!IsHostile)
                stunRecovered.Invoke();
        }

        protected virtual void OnDisable()
        {
            knockbackUntil = 0f;
            wandering = false;
            if (feedback != null)
                StopCoroutine(feedback);
            feedback = null;
            if (usePlaceholderFeedback && visual != null)
                visual.color = originalColor;
        }

        protected virtual void Reset()
        {
            hitbox = GetComponent<Collider2D>();
            visual = GetComponentInChildren<SpriteRenderer>();
        }
    }
}
