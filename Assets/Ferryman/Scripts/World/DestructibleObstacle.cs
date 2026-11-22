using UnityEngine;

namespace Ferryman.World
{
    [RequireComponent(typeof(BoxCollider2D))]
    public sealed class DestructibleObstacle : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Sprite[] appearances;
        [SerializeField] private GameObject clearedEffectPrefab;

        public bool IsCleared { get; private set; }

        private void Awake()
        {
            if (visual != null && appearances != null && appearances.Length > 0)
                visual.sprite = appearances[Random.Range(0, appearances.Length)];
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
            var hitbox = GetComponent<Collider2D>();
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
