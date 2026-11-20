using UnityEngine;

namespace Ferryman.World
{
    [RequireComponent(typeof(EdgeCollider2D), typeof(LineRenderer))]
    public sealed class BoatDeck : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float width = 12f;
        [SerializeField, Min(0.1f)] private float height = 3f;
        [SerializeField, Min(0.1f)] private float cellSize = 1f;

        public Vector2Int GridSize => new Vector2Int(
            Mathf.Max(1, Mathf.FloorToInt(width / cellSize)),
            Mathf.Max(1, Mathf.FloorToInt(height / cellSize)));

        public Vector2Int WorldToCell(Vector2 position)
        {
            Vector2 local = transform.InverseTransformPoint(position);
            Vector2Int size = GridSize;
            return new Vector2Int(
                Mathf.Clamp(Mathf.FloorToInt(local.x / cellSize + size.x * 0.5f), 0, size.x - 1),
                Mathf.Clamp(Mathf.FloorToInt(local.y / cellSize + size.y * 0.5f), 0, size.y - 1));
        }

        public Vector2 CellToWorld(Vector2Int cell)
        {
            Vector2Int size = GridSize;
            return transform.TransformPoint(new Vector3(
                (cell.x + 0.5f - size.x * 0.5f) * cellSize,
                (cell.y + 0.5f - size.y * 0.5f) * cellSize));
        }

        public Vector2 ClampToDeck(Vector2 position)
        {
            Vector2 local = transform.InverseTransformPoint(position);
            local.x = Mathf.Clamp(local.x, -width * 0.5f, width * 0.5f);
            local.y = Mathf.Clamp(local.y, -height * 0.5f, height * 0.5f);
            return transform.TransformPoint(local);
        }

        public bool ContainsCell(Vector2Int cell)
        {
            Vector2Int size = GridSize;
            return cell.x >= 0 && cell.x < size.x && cell.y >= 0 && cell.y < size.y;
        }

        public static int CellDistance(Vector2Int a, Vector2Int b)
        {
            return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
        }

        private void Awake()
        {
            UpdateBoundary();
        }

        private void OnValidate()
        {
            width = Mathf.Max(0.1f, width);
            height = Mathf.Max(0.1f, height);
            cellSize = Mathf.Max(0.1f, cellSize);
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
