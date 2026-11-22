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

        public BoatHealth TargetBoat => targetBoat;
        public bool IsInFlight => obstacle != null && obstacle.IsInFlight;

        private void Awake()
        {
            obstacle = GetComponent<DestructibleObstacle>();
        }

        private void OnEnable()
        {
            if (!IsInFlight)
                StartFuse();
        }

        public void BindBoat(BoatHealth boat)
        {
            targetBoat = boat;
        }

        public void Throw(BoatHealth boat, Vector2 destination)
        {
            Launch(boat, destination, throwSeconds, throwArcHeight);
        }

        internal void Launch(BoatHealth boat, Vector2 destination, float seconds, float arcHeight)
        {
            targetBoat = boat;
            if (fuseFill != null)
                fuseFill.parent.gameObject.SetActive(false);
            obstacle.FlyTo(boat.Deck, destination, seconds, arcHeight, StartFuse);
        }

        private void StartFuse()
        {
            detonateAt = Time.time + fuseSeconds;
            if (fuseFill != null)
            {
                fuseFill.localScale = Vector3.one;
                fuseFill.parent.gameObject.SetActive(true);
            }
        }

        private void LateUpdate()
        {
            if (IsInFlight)
                return;
            if (fuseFill != null)
                fuseFill.localScale = new Vector3(Mathf.Clamp01((detonateAt - Time.time) / fuseSeconds), 1f, 1f);
            if (Time.time >= detonateAt)
                Explode();
        }

        public bool Explode()
        {
            if (IsInFlight || !obstacle.Remove(false))
                return false;

            if (targetBoat != null)
                targetBoat.ApplyDamage(damage, transform.position);
            if (explosionEffectPrefab != null)
                Instantiate(explosionEffectPrefab, transform.position, Quaternion.identity);
            return true;
        }
    }
}
