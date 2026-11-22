using UnityEngine;

namespace Ferryman.World
{
    internal sealed class KickedItemFlight : MonoBehaviour
    {
        private const float Duration = 0.55f;
        private const float ArcHeight = 1.15f;
        private const float SpinDegrees = 540f;

        private Camera view;
        private Vector3 origin;
        private Quaternion initialRotation;
        private Vector2 direction;
        private float padding;
        private float startedAt;

        internal void Launch(SpriteRenderer sprite, Vector2 launchDirection, Camera camera)
        {
            view = camera;
            origin = transform.position;
            initialRotation = transform.rotation;
            direction = launchDirection;
            Bounds bounds = sprite.bounds;
            // Use the radius around the pivot so every rotation clears the camera edge.
            padding = (bounds.center - origin).magnitude + bounds.extents.magnitude + 0.75f;
            startedAt = Time.time;
        }

        private void LateUpdate()
        {
            if (view == null)
            {
                Destroy(gameObject);
                return;
            }

            float t = Mathf.Clamp01((Time.time - startedAt) / Duration);
            Vector3 position = origin + (Vector3)direction * (DistanceToExit() * t)
                + Vector3.up * (Mathf.Sin(t * Mathf.PI) * ArcHeight);
            float spin = (direction.x < 0f ? SpinDegrees : -SpinDegrees) * t;
            transform.SetPositionAndRotation(position, Quaternion.Euler(0f, 0f, spin) * initialRotation);
            if (t >= 1f)
                Destroy(gameObject);
        }

        private float DistanceToExit()
        {
            Vector3 start = view.WorldToViewportPoint(origin);
            Vector3 step = view.WorldToViewportPoint(origin + (Vector3)direction) - start;
            float marginY = padding / (2f * view.orthographicSize);
            float marginX = marginY / view.aspect;
            float exitX = DistanceToEdge(start.x, step.x, marginX);
            float exitY = DistanceToEdge(start.y, step.y, marginY);
            return Mathf.Max(padding, Mathf.Min(exitX, exitY));
        }

        private static float DistanceToEdge(float start, float step, float margin)
        {
            if (Mathf.Abs(step) < 0.000001f)
                return float.PositiveInfinity;
            float edge = step > 0f ? 1f + margin : -margin;
            return (edge - start) / step;
        }
    }
}
