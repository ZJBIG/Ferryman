using UnityEngine;

namespace Ferryman.Combat
{
    public sealed class WaveBubbles : MonoBehaviour
    {
        internal const float Duration = 1.15f;
        private const int BubbleCount = 6;
        private static Sprite bubbleSprite;
        private readonly SpriteRenderer[] bubbles = new SpriteRenderer[BubbleCount];
        private SpriteRenderer ripple;
        private float startedAt;

        internal static WaveBubbles Create(Vector2 position, float scale)
        {
            if (bubbleSprite == null)
                bubbleSprite = Resources.Load<Sprite>("Voyage/WaveBubble");

            var warning = new GameObject("Wave Bubbles").AddComponent<WaveBubbles>();
            warning.transform.SetPositionAndRotation(position, Quaternion.identity);
            warning.transform.localScale = Vector3.one * scale;
            warning.startedAt = Time.time;
            warning.ripple = warning.AddSprite("Surface Ripple");
            for (int i = 0; i < warning.bubbles.Length; i++)
                warning.bubbles[i] = warning.AddSprite("Bubble");
            warning.Update();
            return warning;
        }

        private SpriteRenderer AddSprite(string spriteName)
        {
            var renderer = new GameObject(spriteName).AddComponent<SpriteRenderer>();
            renderer.transform.SetParent(transform, false);
            renderer.sprite = bubbleSprite;
            renderer.sortingOrder = 17;
            return renderer;
        }

        private void Update()
        {
            float progress = Mathf.Clamp01((Time.time - startedAt) / Duration);
            ripple.transform.localScale = new Vector3(1f + progress * 2f, 0.22f + progress * 0.41f, 1f);
            ripple.color = new Color(0.73f, 0.92f, 1f, Mathf.Sin(progress * Mathf.PI) * 0.75f);
            for (int i = 0; i < bubbles.Length; i++)
            {
                float rise = Mathf.Repeat(progress * 1.35f + i * 0.29f, 1f);
                var bubble = bubbles[i];
                float offset = (i - (BubbleCount - 1) * 0.5f) * 0.18f;
                bubble.transform.localPosition = new Vector3(offset + Mathf.Sin(rise * 6f + i) * 0.09f, rise * 0.91f, 0f);
                bubble.transform.localScale = Vector3.one * (0.19f + (i % 3) * 0.10f + rise * 0.10f);
                float fade = Mathf.Sin(rise * Mathf.PI) * (1f - Mathf.SmoothStep(0f, 1f, (progress - 0.76f) / 0.24f));
                bubble.color = new Color(0.91f, 0.98f, 1f, fade);
            }
        }
    }
}
