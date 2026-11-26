using System;
using UnityEngine;

namespace Ferryman.Voyage
{
    public sealed class VoyageScenePresentation : MonoBehaviour
    {
        [SerializeField] private Transform seaMotion;
        [SerializeField] private GameObject sailingWake;
        [SerializeField] private float scrollSpeed = 1.2f;
        [SerializeField, Min(0.1f)] private float scrollLoopWidth = 6f;
        [SerializeField, Min(0.1f)] private float cameraSize = 6f;
        [Header("Complete Voyage Scene")]
        [SerializeField] private Transform islandStops;
        [SerializeField, Min(0.1f)] private float islandCameraSize = 8.6f;
        [SerializeField] private float islandCameraHeight = 3.3f;
        private VoyageController controller;

        public bool IsUnifiedScene => islandStops != null;
        public float GetCameraHeight(float viewSize) => IsUnifiedScene && islandStops.gameObject.activeSelf
            ? islandCameraHeight + islandCameraSize - viewSize
            : 0f;

        public float GetCameraSize(Camera camera)
        {
            if (!IsUnifiedScene)
                return cameraSize;
            float size = islandStops.gameObject.activeSelf ? islandCameraSize : cameraSize;
            // Show the entire hull and its surrounding water even in a narrow Game view.
            return Mathf.Max(size, 15.5f / Mathf.Max(0.1f, camera.aspect));
        }

        private void Awake()
        {
            if (!IsUnifiedScene || seaMotion == null)
                return;
            // Mirror the existing upper row below the ship for wider and narrow-aspect framing.
            foreach (SpriteRenderer tile in seaMotion.GetComponentsInChildren<SpriteRenderer>())
            {
                Vector3 position = tile.transform.localPosition;
                if (position.y <= 0f)
                    continue;
                SpriteRenderer lower = Instantiate(tile, seaMotion);
                lower.transform.localPosition = new Vector3(position.x, -position.y, position.z);
            }
        }

        public bool CanPresentStop(VoyageStop stop)
        {
            return IsUnifiedScene && stop != null && (stop.kind == VoyageStopKind.Sea
                ? seaMotion != null : islandStops.Find(stop.sceneName) != null);
        }

        public void PresentStop(VoyageStop stop)
        {
            if (!CanPresentStop(stop))
                throw new InvalidOperationException("主场景缺少航程区域的显示配置：" + stop?.sceneName);

            bool island = stop.kind == VoyageStopKind.Island;
            foreach (Transform visual in islandStops)
                visual.gameObject.SetActive(island && visual.name == stop.sceneName);
            islandStops.gameObject.SetActive(island);
            if (seaMotion != null)
            {
                Vector3 position = seaMotion.localPosition;
                position.x = 0f;
                seaMotion.localPosition = position;
            }
            if (sailingWake != null)
                sailingWake.SetActive(false);

            Camera camera = GetComponent<Camera>();
            float size = GetCameraSize(camera);
            Vector3 cameraPosition = camera.transform.position;
            cameraPosition.y = GetCameraHeight(size);
            camera.transform.position = cameraPosition;
            camera.orthographicSize = size;
        }

        private void Update()
        {
            if (controller == null)
                controller = FindObjectOfType<VoyageController>();
            bool sailing = controller != null && (controller.Phase == VoyagePhase.Sailing
                || controller.Phase == VoyagePhase.Event);
            if (sailingWake != null)
                sailingWake.SetActive(sailing);
            if (seaMotion != null && sailing)
            {
                Vector3 position = seaMotion.localPosition;
                position.x = -Mathf.Repeat(-position.x + scrollSpeed * Time.deltaTime, scrollLoopWidth);
                seaMotion.localPosition = position;
            }
        }
    }
}
