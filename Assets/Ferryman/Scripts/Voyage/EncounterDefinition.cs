using System;
using Ferryman.Characters;
using UnityEngine;

namespace Ferryman.Voyage
{
    [Serializable]
    public struct RewardData
    {
        [Min(0)] public int coins;
        [Min(0)] public int specialItems;
    }

    [Serializable]
    public sealed class EnemySpawn
    {
        public EnemyCharacter prefab;
        [Min(1)] public int count = 1;
        public Vector2 spawnPosition;
        [Min(1)] public int firstStoryLeg = 1;
    }

    [Serializable]
    public sealed class WaveDefinition
    {
        public EnemySpawn[] enemies = Array.Empty<EnemySpawn>();
    }

    [CreateAssetMenu(fileName = "EncounterDefinition", menuName = "Ferryman/Encounter")]
    public sealed class EncounterDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public WaveDefinition[] waves = Array.Empty<WaveDefinition>();
        [Min(0.1f), Tooltip("The first obstacle flyby starts after half this duration. Waves advance when cleared.")]
        public float waveInterval = 8f;
        public RewardData reward;
        [Min(1f)] public float cameraSize = 6f;
        public float cameraHeight;
    }
}
