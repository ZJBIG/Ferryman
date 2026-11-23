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

        private Vector2Int BaseGridSize => new Vector2Int(
            Mathf.Max(1, Mathf.FloorToInt(width / cellSize)),
            Mathf.Max(1, Mathf.FloorToInt(height / cellSize)));

        public Vector2Int GridSize
        {
            get
            {
                Vector2Int size = BaseGridSize;
                if (!HasPerimeter)
                    return size;

                float right = width * 0.5f;
                foreach (Vector2 point in walkablePerimeter)
                    right = Mathf.Max(right, point.x);
                if (right > width * 0.5f)
                    size.x += Mathf.CeilToInt((right - size.x * 0.5f * cellSize) / cellSize);
                return size;
            }
        }

        private bool HasPerimeter => walkablePerimeter != null && walkablePerimeter.Length >= 3;

        public Vector2Int WorldToCell(Vector2 position) => LocalToCell(transform.InverseTransformPoint(position));

        internal Vector2Int LocalToCell(Vector2 local)
        {
            Vector2Int size = BaseGridSize;
            int x = Mathf.FloorToInt(local.x / cellSize + size.x * 0.5f);
            // The right edge of the old grid belonged to its last column.
            if (x == size.x && Mathf.Approximately(local.x, size.x * 0.5f * cellSize))
                x--;
            return new Vector2Int(
                Mathf.Clamp(x, 0, GridSize.x - 1),
                Mathf.Clamp(Mathf.FloorToInt(local.y / cellSize + size.y * 0.5f), 0, size.y - 1));
        }

        public Vector2 CellToWorld(Vector2Int cell)
        {
            Vector2Int size = BaseGridSize;
            return transform.TransformPoint(new Vector3(
                (cell.x + 0.5f - size.x * 0.5f) * cellSize,
                (cell.y + 0.5f - size.y * 0.5f) * cellSize));
        }

        public Vector2 ClampToDeck(Vector2 position)
        {
            Vector2 local = transform.InverseTransformPoint(position);
            if (!HasPerimeter)
                return transform.TransformPoint(ClampToBaseRectangle(local));
            if (ContainsLocalFloor(local))
                return position;

            Vector2 closest = walkablePerimeter[0];
            float bestDistance = float.PositiveInfinity;
            for (int i = 0; i < walkablePerimeter.Length; i++)
            {
                Vector2 start = walkablePerimeter[i];
                Vector2 edge = walkablePerimeter[(i + 1) % walkablePerimeter.Length] - start;
                if (edge.sqrMagnitude == 0f)
                    continue;
                Vector2 candidate = start + edge * Mathf.Clamp01(Vector2.Dot(local - start, edge) / edge.sqrMagnitude);
                float distance = (local - candidate).sqrMagnitude;
                if (distance < bestDistance)
                {
                    closest = candidate;
                    bestDistance = distance;
                }
            }
            return transform.TransformPoint(closest);
        }

        internal bool ContainsRepairCheckpointPosition(Vector2 local)
        {
            // Older saves allowed the rectangular area even where the artwork needs to be relocated.
            return (ClampToBaseRectangle(local) - local).sqrMagnitude <= 0.0001f
                || HasPerimeter && ContainsLocalFloor(local);
        }

        private Vector2 ClampToBaseRectangle(Vector2 local) => new Vector2(
            Mathf.Clamp(local.x, -width * 0.5f, width * 0.5f),
            Mathf.Clamp(local.y, -height * 0.5f, height * 0.5f));

        public bool ContainsCell(Vector2Int cell)
        {
            Vector2Int size = GridSize;
            return cell.x >= 0 && cell.x < size.x && cell.y >= 0 && cell.y < size.y;
        }

        public bool ContainsFloor(Vector2 position)
        {
            if (!HasPerimeter)
                return (ClampToDeck(position) - position).sqrMagnitude <= 0.0001f;

            return ContainsLocalFloor(transform.InverseTransformPoint(position));
        }

        private bool ContainsLocalFloor(Vector2 local)
        {
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
            {
                for (int y = 0; y < grid.y; y++)
                {
                    Vector2 candidate = CellToWorld(new Vector2Int(x, y));
                    float distance = (candidate - preferred).sqrMagnitude;
                    if (distance >= best || !CanPlace(candidate, size, ignored))
                        continue;
                    position = candidate;
                    best = distance;
                }
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
            var perimeter = HasPerimeter ? walkablePerimeter : new[]
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
