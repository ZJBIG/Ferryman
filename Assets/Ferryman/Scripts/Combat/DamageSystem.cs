using Ferryman.Characters;
using UnityEngine;
using UnityEngine.Events;

namespace Ferryman.Combat
{
    public sealed class DamageSystem : MonoBehaviour
    {
        // Event arguments: instigator, target, accepted damage amount.
        [SerializeField] private UnityEvent<Character, Character, float> damageReceived = new UnityEvent<Character, Character, float>();
        [SerializeField] private UnityEvent<Character, Character, float> damageSent = new UnityEvent<Character, Character, float>();

        public UnityEvent<Character, Character, float> DamageReceived => damageReceived;
        public UnityEvent<Character, Character, float> DamageSent => damageSent;

        public bool ApplyDamage(Character instigator, Character target, float damage)
        {
            if (target == null || target == instigator || damage <= 0f
                || (instigator != null && instigator.IsHostile == target.IsHostile)
                || !target.TryReceiveDamage(damage))
                return false;

            damageReceived.Invoke(instigator, target, damage);
            damageSent.Invoke(instigator, target, damage);
            return true;
        }
    }
}
