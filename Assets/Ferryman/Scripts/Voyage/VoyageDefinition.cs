using System;
using Ferryman.Characters;
using UnityEngine;

namespace Ferryman.Voyage
{
    public enum VoyageStopKind
    {
        Island,
        Sea
    }

    [Serializable]
    public sealed class VoyageStop
    {
        public VoyageStopKind kind;
        public string displayName;
        public string sceneName;
        public EncounterDefinition fixedEncounter;
        public SeaEventDefinition fixedSpecialEvent;
        [Range(0f, 1f)] public float boardingChance = 0.75f;
        [Min(0)] public int maxBoardingPassengers = 1;
    }

    [Serializable]
    public sealed class SpecialEventEntry
    {
        public SeaEventDefinition definition;
        [Min(0)] public int maxAppearances = 1;
    }

    [CreateAssetMenu(fileName = "VoyageDefinition", menuName = "Ferryman/Voyage")]
    public sealed class VoyageDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public GameObject vesselPrefab;
        public bool branchingRoutes;
        public bool storyCampaign;
        public EncounterDefinition finalBossEncounter;
        public VoyageStop[] stops = Array.Empty<VoyageStop>();
        public CrewDefinition[] initialPassengers = Array.Empty<CrewDefinition>();
        public CrewDefinition[] passengerPool = Array.Empty<CrewDefinition>();
        public EncounterDefinition[] randomEncounters = Array.Empty<EncounterDefinition>();
        public SpecialEventEntry[] specialEvents = Array.Empty<SpecialEventEntry>();
        public Sprite obstacleCarrier;
        public Sprite[] obstacleCarrierFrames = Array.Empty<Sprite>();
        [Min(0)] public int enemyEncounterCount = 1;
        [Min(0)] public int specialEventCount = 1;
        [Min(0.01f)] public float sailingSeconds = 10f;
        [Min(0.01f)] public float transitionSeconds = 0.8f;
        [Min(0.01f)] public float boardingNoticeSeconds = 3f;
    }
}
