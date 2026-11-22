using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace Ferryman.Characters
{
    [RequireComponent(typeof(BoxCollider2D))]
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

        public float Health
        {
            get => health;
            protected set => health = value;
        }

        public Collider2D Hitbox => hitbox;
        public bool IsStunned => Time.time < stunnedUntil;
        public float StunRemainingRatio => stunDuration > 0f
            ? Mathf.Clamp01((stunnedUntil - Time.time) / stunDuration)
            : 0f;
        public bool ActionsEnabled => actionsEnabled;
        public virtual bool IsHostile => false;
        protected virtual bool CanAct => actionsEnabled && !IsStunned && (!IsHostile || Health > 0f);

        public virtual void SetActionsAllowed(bool allowed)
        {
            actionsEnabled = allowed;
            if (!allowed)
            {
                var body = GetComponent<Rigidbody2D>();
                if (body != null)
                    body.velocity = Vector2.zero;
            }
        }

        protected virtual void Awake()
        {
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
            var body = GetComponent<Rigidbody2D>();
            if (body != null)
                body.velocity = Vector2.zero;
            ShowFeedback(new Color(0.65f, 0.65f, 0.65f), stunDuration);
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
