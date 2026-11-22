using UnityEngine;

namespace Ferryman.World
{
    [RequireComponent(typeof(EdgeCollider2D), typeof(LineRenderer))]
    public sealed class BoatDeck : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float width = 12f;
        [SerializeField, Min(0.1f)] private float height = 3f;
        [SerializeField, Min(0.1f)] private float cellSize = 1f;
        [SerializeField] private Vector2[] walkablePerimeter;
        [SerializeField] private BoxCollider2D[] fixedObstacles;

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

        public bool ContainsFloor(Vector2 position)
        {
            if (walkablePerimeter == null || walkablePerimeter.Length < 3)
                return (ClampToDeck(position) - position).sqrMagnitude <= 0.0001f;

            Vector2 local = transform.InverseTransformPoint(position);
            // The configured perimeter is convex and ordered clockwise.
            for (int i = 0; i < walkablePerimeter.Length; i++)
            {
                Vector2 start = walkablePerimeter[i];
                Vector2 edge = walkablePerimeter[(i + 1) % walkablePerimeter.Length] - start;
                Vector2 offset = local - start;
                if (edge.x * offset.y - edge.y * offset.x > 0.001f)
                    return false;
            }
            return true;
        }

        public bool HasClearFloor(Vector2 position, Vector2 size)
        {
            Vector2 half = size * 0.5f;
            Vector2 horizontal = Vector2.right * half.x;
            Vector2 vertical = Vector2.up * half.y;
            if (!ContainsFloor(position - horizontal - vertical) || !ContainsFloor(position - horizontal + vertical)
                || !ContainsFloor(position + horizontal - vertical) || !ContainsFloor(position + horizontal + vertical))
                return false;
            if (fixedObstacles != null)
            {
                var footprint = new Rect(position - half, size);
                foreach (BoxCollider2D obstacle in fixedObstacles)
                {
                    Bounds bounds = obstacle.bounds;
                    if (footprint.Overlaps(new Rect(bounds.min, bounds.size)))
                        return false;
                }
            }
            return true;
        }

        public bool CanPlace(Vector2 position, Vector2 size, Collider2D ignored = null)
        {
            if (!HasClearFloor(position, size))
                return false;
            foreach (Collider2D hit in Physics2D.OverlapBoxAll(position, size, 0f))
                if (!hit.isTrigger && hit != ignored && hit.gameObject != gameObject)
                    return false;
            return true;
        }

        public bool TryFindPlacement(Vector2 preferred, Vector2 size, Collider2D ignored, out Vector2 position)
        {
            position = preferred;
            if (CanPlace(preferred, size, ignored))
                return true;

            float best = float.PositiveInfinity;
            Vector2Int grid = GridSize;
            for (int x = 0; x < grid.x; x++)
            for (int y = 0; y < grid.y; y++)
            {
                Vector2 candidate = CellToWorld(new Vector2Int(x, y));
                float distance = (candidate - preferred).sqrMagnitude;
                if (distance >= best || !CanPlace(candidate, size, ignored))
                    continue;
                position = candidate;
                best = distance;
            }
            return !float.IsPositiveInfinity(best);
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
            var perimeter = walkablePerimeter != null && walkablePerimeter.Length >= 3 ? walkablePerimeter : new[]
            {
                new Vector2(-halfWidth, -halfHeight),
                new Vector2(-halfWidth, halfHeight),
                new Vector2(halfWidth, halfHeight),
                new Vector2(halfWidth, -halfHeight)
            };
            var points = new Vector2[perimeter.Length + 1];
            perimeter.CopyTo(points, 0);
            points[perimeter.Length] = perimeter[0];

            // A closed edge blocks the perimeter while leaving the deck interior walkable.
            GetComponent<EdgeCollider2D>().points = points;

            var outline = GetComponent<LineRenderer>();
            outline.useWorldSpace = false;
            outline.loop = true;
            outline.positionCount = perimeter.Length;
            for (int i = 0; i < perimeter.Length; i++)
                outline.SetPosition(i, points[i]);
        }
    }
}
