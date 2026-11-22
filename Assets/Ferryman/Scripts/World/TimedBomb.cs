using UnityEngine;

namespace Ferryman.World
{
    [RequireComponent(typeof(DestructibleObstacle))]
    public sealed class TimedBomb : MonoBehaviour
    {
        [SerializeField] private BoatHealth targetBoat;
        [SerializeField, Min(0.01f)] private float fuseSeconds = 3f;
        [SerializeField, Min(0f)] private float damage = 100f;
        [SerializeField] private Transform fuseFill;
        [SerializeField] private GameObject explosionEffectPrefab;
        [SerializeField, Min(0.01f)] private float throwSeconds = 0.6f;
        [SerializeField, Min(0f)] private float throwArcHeight = 0.7f;

        private DestructibleObstacle obstacle;
        private float detonateAt;
        private float thrownAt;
        private Vector2 throwOrigin;
        private Vector2 landingPosition;
        private bool inFlight;

        public BoatHealth TargetBoat => targetBoat;
        public bool IsInFlight => inFlight;

        private void Awake()
        {
            obstacle = GetComponent<DestructibleObstacle>();
        }

        private void OnEnable()
        {
            detonateAt = Time.time + fuseSeconds;
        }

        public void BindBoat(BoatHealth boat)
        {
            targetBoat = boat;
        }

        public void Throw(BoatHealth boat, Vector2 destination)
        {
            targetBoat = boat;
            throwOrigin = transform.position;
            var hitbox = GetComponent<BoxCollider2D>();
            Physics2D.SyncTransforms();
            if (!boat.Deck.TryFindPlacement(destination, hitbox.size, hitbox, out landingPosition))
            {
                Destroy(gameObject);
                return;
            }
            thrownAt = Time.time;
            inFlight = true;
            hitbox.enabled = false;
            if (fuseFill != null)
                fuseFill.parent.gameObject.SetActive(false);
        }

        private void LateUpdate()
        {
            if (inFlight)
            {
                float t = Mathf.Clamp01((Time.time - thrownAt) / throwSeconds);
                transform.position = Vector2.Lerp(throwOrigin, landingPosition, t)
                    + Vector2.up * (Mathf.Sin(t * Mathf.PI) * throwArcHeight);
                if (t < 1f)
                    return;
                var hitbox = GetComponent<BoxCollider2D>();
                Physics2D.SyncTransforms();
                if (!targetBoat.Deck.TryFindPlacement(landingPosition, hitbox.size, hitbox, out Vector2 touchdown))
                {
                    Destroy(gameObject);
                    return;
                }
                transform.position = touchdown;
                inFlight = false;
                hitbox.enabled = true;
                Physics2D.SyncTransforms();
                detonateAt = Time.time + fuseSeconds;
                if (fuseFill != null)
                    fuseFill.parent.gameObject.SetActive(true);
            }
            if (fuseFill != null)
                fuseFill.localScale = new Vector3(Mathf.Clamp01((detonateAt - Time.time) / fuseSeconds), 1f, 1f);
            if (Time.time >= detonateAt)
                Explode();
        }

        public bool Explode()
        {
            if (inFlight || !obstacle.Remove(false))
                return false;

            if (targetBoat != null)
                targetBoat.ApplyDamage(damage, transform.position);
            if (explosionEffectPrefab != null)
                Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            return true;
        }
    }
}
