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

        internal bool Remove(bool showClearedEffect)
        {
            var hitbox = GetComponent<Collider2D>();
            if (IsCleared || !hitbox.enabled)
                return false;
            IsCleared = true;
            hitbox.enabled = false;
            if (showClearedEffect && clearedEffectPrefab != null)
                Instantiate(clearedEffectPrefab, transform.position, Quaternion.identity);
            Destroy(gameObject);
            return true;
        }
    }
}
