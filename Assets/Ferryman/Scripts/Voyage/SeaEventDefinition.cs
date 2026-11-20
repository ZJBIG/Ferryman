using UnityEngine;

namespace Ferryman.Voyage
{
    [CreateAssetMenu(fileName = "SeaEventDefinition", menuName = "Ferryman/Sea Event")]
    public sealed class SeaEventDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public GameObject obstaclePrefab;
        [Min(1)] public int obstacleCount = 2;
        public RewardData reward;
    }
}
