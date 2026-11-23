using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Ferryman.World
{
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class DestructibleObstacle : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Sprite[] appearances;
        [SerializeField] private GameObject clearedEffectPrefab;

        private BoatDeck landingDeck;
        private BoxCollider2D hitbox;
        private Vector3 visualScale;
        private Vector2 flightOrigin;
        private Vector2 landingPosition;
        private float launchedAt;
        private float flightSeconds;
        private float arcHeight;
        private Action landed;

        public bool IsCleared { get; private set; }
        public bool IsInFlight { get; private set; }

        private void Awake()
        {
            hitbox = GetComponent<BoxCollider2D>();
            if (visual != null)
                visualScale = visual.transform.localScale;
            SetAppearance(ChooseAppearance());
        }

        private Sprite ChooseAppearance() => appearances != null && appearances.Length > 0
            ? appearances[Random.Range(0, appearances.Length)] : visual != null ? visual.sprite : null;

        internal void SetAppearance(Sprite appearance, float scale = 1f)
        {
            if (visual == null)
                return;
            visual.sprite = appearance;
            visual.transform.localScale = visualScale * scale;
        }

        internal void FlyTo(BoatDeck deck, Vector2 destination, float seconds, float arc, Action onLanded = null)
        {
            IsInFlight = true;
            hitbox.enabled = false;
            landingDeck = deck;
            Physics2D.SyncTransforms();
            if (!deck.TryFindPlacement(destination, hitbox.size, hitbox, out landingPosition))
            {
                Destroy(gameObject);
                return;
            }
            flightOrigin = transform.position;
            launchedAt = Time.time;
            flightSeconds = seconds;
            arcHeight = arc;
            landed = onLanded;
        }

        private void LateUpdate()
        {
            if (!IsInFlight)
                return;
            if (landingDeck == null)
            {
                Destroy(gameObject);
                return;
            }

            float t = Mathf.Clamp01((Time.time - launchedAt) / flightSeconds);
            float travel = arcHeight > 0f ? t : t * t;
            transform.position = Vector2.Lerp(flightOrigin, landingPosition, travel)
                + Vector2.up * (Mathf.Sin(t * Mathf.PI) * arcHeight);
            if (t < 1f)
                return;
            Physics2D.SyncTransforms();
            if (!landingDeck.TryFindPlacement(landingPosition, hitbox.size, hitbox, out Vector2 touchdown))
            {
                Destroy(gameObject);
                return;
            }
            if (touchdown != landingPosition)
            {
                // Finish the landing visibly if a character stepped into the original spot.
                flightOrigin = landingPosition;
                landingPosition = touchdown;
                launchedAt = Time.time;
                flightSeconds = 0.12f;
                arcHeight = 0.2f;
                return;
            }
            transform.position = touchdown;
            IsInFlight = false;
            hitbox.enabled = true;
            Physics2D.SyncTransforms();
            landed?.Invoke();
            landed = null;
        }

        public void Clear()
        {
            Remove(true);
        }

        internal void Kick(Vector2 direction, Camera camera)
        {
            Remove(true, direction, camera);
        }

        internal bool Remove(bool showClearedEffect, Vector2 kickDirection = default, Camera camera = null)
        {
            if (hitbox == null)
                hitbox = GetComponent<BoxCollider2D>();
            if (IsCleared || !hitbox.enabled)
                return false;
            IsCleared = true;
            hitbox.enabled = false;
            if (showClearedEffect && clearedEffectPrefab != null)
                Instantiate(clearedEffectPrefab, transform.position, Quaternion.identity);
            if (camera != null && visual != null && visual.sprite != null
                && visual.transform != transform && visual.transform.IsChildOf(transform))
            {
                visual.transform.SetParent(null, true);
                visual.gameObject.AddComponent<KickedItemFlight>().Launch(visual, kickDirection, camera);
            }
            Destroy(gameObject);
            return true;
        }
    }
}
