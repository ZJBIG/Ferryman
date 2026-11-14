using UnityEngine;

namespace Ferryman.World
{
    [RequireComponent(typeof(EdgeCollider2D), typeof(LineRenderer))]
    public sealed class BoatDeck : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float width = 12f;
        [SerializeField, Min(0.1f)] private float height = 3f;

        private void Awake()
        {
            UpdateBoundary();
        }

        private void OnValidate()
        {
            width = Mathf.Max(0.1f, width);
            height = Mathf.Max(0.1f, height);
            UpdateBoundary();
        }

        private void UpdateBoundary()
        {
            float halfWidth = width * 0.5f;
            float halfHeight = height * 0.5f;
            var points = new[]
            {
                new Vector2(-halfWidth, -halfHeight),
                new Vector2(-halfWidth, halfHeight),
                new Vector2(halfWidth, halfHeight),
                new Vector2(halfWidth, -halfHeight),
                new Vector2(-halfWidth, -halfHeight)
            };

            // A closed edge blocks the perimeter while leaving the deck interior walkable.
            GetComponent<EdgeCollider2D>().points = points;

            var outline = GetComponent<LineRenderer>();
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.positionCount = 4;
            for (int i = 0; i < 4; i++)
                outline.SetPosition(i, points[i]);
        }
    }
}
