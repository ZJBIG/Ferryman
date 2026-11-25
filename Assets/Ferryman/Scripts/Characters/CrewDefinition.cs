using UnityEngine;

namespace Ferryman.Characters
{
    public enum CrewRole
    {
        Combat,
        Maintenance
    }

    [CreateAssetMenu(menuName = "Ferryman/Crew Definition")]
    public sealed class CrewDefinition : ScriptableObject
    {
        public string id;
        public string displayName;
        public CrewRole role;
        public Character prefab;
        public Sprite portrait;
        public Sprite deckSprite;
        public Sprite[] walkFrames;
        public Sprite[] skillFrames;
        [Min(0.1f)] public float deckSpriteScale = 1f;
        [Min(0.1f)] public float moveSpeed = 2f;
        [Min(0f)] public float repairSpeed = 25f;
        [Min(0)] public int repairRange = 1;
        [Min(1)] public int simultaneousRepairs = 1;
        [Min(0f)] public float attackPower = 10f;
        [Min(0.01f)] public float fireInterval = 0.5f;
        [Min(0)] public int coinsReward = 20;
        [Min(0)] public int specialItemsReward = 1;
    }
}
