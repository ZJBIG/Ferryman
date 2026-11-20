using UnityEngine;

namespace Ferryman.Voyage
{
    public sealed class VoyageScenePresentation : MonoBehaviour
    {
        [SerializeField] private Transform seaMotion;
        [SerializeField] private GameObject sailingWake;
        [SerializeField] private float scrollSpeed = 1.2f;
        private VoyageController controller;

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
                position.x = Mathf.Repeat(position.x - scrollSpeed * Time.deltaTime + 3f, 3f) - 3f;
                seaMotion.localPosition = position;
            }
        }
    }
}
