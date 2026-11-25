using System.Collections;
using UnityEngine;

namespace Ferryman.Combat
{
    public sealed class SpriteEffect : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Sprite[] appearances;
        [SerializeField, Min(0f)] private float lifetime;
        [SerializeField] private bool expandAndFade;

        private void Awake()
        {
            if (visual != null && appearances != null && appearances.Length > 1)
                visual.sprite = appearances[Random.Range(0, appearances.Length)];
            if (expandAndFade && visual != null && lifetime > 0f)
                StartCoroutine(ExpandAndFade());
            else if (lifetime > 0f)
                Destroy(gameObject, lifetime);
        }

        private IEnumerator ExpandAndFade()
        {
            float startedAt = Time.time;
            Transform splash = visual.transform;
            Vector3 scale = splash.localScale;
            Color color = visual.color;
            while (Time.time - startedAt < lifetime)
            {
                float progress = (Time.time - startedAt) / lifetime;
                float rise = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(progress / 0.29f));
                splash.localScale = Vector3.Scale(scale, new Vector3(0.47f + rise * 0.53f + progress * 0.16f,
                    (0.22f + rise * 0.78f) * (1f - progress * 0.45f), 1f));
                visual.color = new Color(color.r, color.g, color.b,
                    color.a * (1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((progress - 0.38f) / 0.62f))));
                yield return null;
            }
            Destroy(gameObject);
        }
    }
}
