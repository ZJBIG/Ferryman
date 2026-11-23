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
        public float CameraSize => IsUnifiedScene && islandStops.gameObject.activeSelf ? islandCameraSize : cameraSize;
        public float CameraHeight => IsUnifiedScene && islandStops.gameObject.activeSelf ? islandCameraHeight : 0f;

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
            Vector3 cameraPosition = camera.transform.position;
            cameraPosition.y = CameraHeight;
            camera.transform.position = cameraPosition;
            camera.orthographicSize = CameraSize;
        }

        private void Update()
        {
            if (controller == null)
                controller = FindObjectOfType<VoyageController>();
            bool sailing = controller != null && controller.Phase == VoyagePhase.Sailing;
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
