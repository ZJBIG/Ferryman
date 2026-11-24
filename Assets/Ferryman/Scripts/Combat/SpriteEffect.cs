using UnityEngine;

namespace Ferryman.Combat
{
    public sealed class SpriteEffect : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Sprite[] appearances;
        [SerializeField, Min(0f)] private float lifetime;

        private void Awake()
        {
            if (visual != null && appearances != null && appearances.Length > 1)
                visual.sprite = appearances[Random.Range(0, appearances.Length)];
            if (lifetime > 0f)
                Destroy(gameObject, lifetime);
        }
    }
}
