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
        private VoyageController controller;

        public float CameraSize => cameraSize;

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
