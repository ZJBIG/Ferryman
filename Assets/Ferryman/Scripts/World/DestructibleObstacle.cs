using System;
using Ferryman.Voyage;
using UnityEngine;
using Random = UnityEngine.Random;

namespace Ferryman.World
{
    public sealed class DestructibleObstacle : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Sprite[] appearances;
        [SerializeField] private GameObject clearedEffectPrefab;
        [SerializeField] private string voyageEventId;

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
        internal string VoyageEventId { get => voyageEventId; set => voyageEventId = value; }
        internal int AppearanceIndex => appearances != null && visual != null ? Array.IndexOf(appearances, visual.sprite) : -1;
        internal Vector2 WorldLandingPosition => IsInFlight && landingDeck != null
            ? (Vector2)landingDeck.transform.TransformPoint(landingPosition) : (Vector2)transform.position;
        internal Bounds VisualBounds => visual != null ? visual.bounds : new Bounds(transform.position, Footprint);
        internal Vector2 Footprint
        {
            get
            {
                var collider = hitbox != null ? hitbox : GetComponent<BoxCollider2D>();
                if (collider != null)
                    return collider.size;
                if (visual == null || visual.sprite == null)
                    return Vector2.one;
                Vector3 scale = visual.transform.lossyScale;
                return Vector2.Scale(visual.sprite.bounds.size, new Vector2(Mathf.Abs(scale.x), Mathf.Abs(scale.y)));
            }
        }

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

        internal void RestoreAppearance(int index)
        {
            if (appearances != null && index >= 0 && index < appearances.Length)
                SetAppearance(appearances[index]);
        }

        internal void FlyTo(BoatDeck deck, Vector2 destination, float seconds, float arc, Action onLanded = null)
        {
            IsInFlight = true;
            if (hitbox != null)
                hitbox.enabled = false;
            landingDeck = deck;
            Physics2D.SyncTransforms();
            if (!deck.TryFindPlacement(destination, Footprint, hitbox, out landingPosition, this))
            {
                IsCleared = true;
                Destroy(gameObject);
                return;
            }
            landingPosition = deck.transform.InverseTransformPoint(landingPosition);
            flightOrigin = deck.transform.InverseTransformPoint(transform.position);
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
            transform.position = landingDeck.transform.TransformPoint(Vector2.Lerp(flightOrigin, landingPosition, t)
                + Vector2.up * (Mathf.Sin(t * Mathf.PI) * arcHeight));
            if (t < 1f)
                return;
            Physics2D.SyncTransforms();
            Vector2 destination = landingDeck.transform.TransformPoint(landingPosition);
            if (!landingDeck.TryFindPlacement(destination, Footprint, hitbox, out Vector2 touchdown, this))
            {
                IsCleared = true;
                Destroy(gameObject);
                return;
            }
            if (touchdown != destination)
            {
                // Finish the landing visibly if a character stepped into the original spot.
                flightOrigin = landingPosition;
                landingPosition = landingDeck.transform.InverseTransformPoint(touchdown);
                launchedAt = Time.time;
                flightSeconds = 0.12f;
                arcHeight = 0.2f;
                return;
            }
            transform.position = touchdown;
            IsInFlight = false;
            if (hitbox != null)
                hitbox.enabled = true;
            Physics2D.SyncTransforms();
            if (landed != null)
                landed.Invoke();
            else
                VoyageAudio.Play(Sound.StoneLand, 0.75f, touchdown);
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
            if (IsCleared || IsInFlight || hitbox != null && !hitbox.enabled)
                return false;
            IsCleared = true;
            var bomb = GetComponent<TimedBomb>();
            if (bomb != null)
                bomb.StopFuseAudio();
            if (showClearedEffect)
                VoyageAudio.Play(bomb != null ? Sound.BombClear : Sound.KickStone, position: transform.position);
            if (hitbox != null)
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
