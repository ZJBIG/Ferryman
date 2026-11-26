using Ferryman.Characters;
using Ferryman.Voyage;
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

        public bool ApplyDamage(Character instigator, Character target, float damage, float attackStunDuration = 0f, bool critical = false)
        {
            if (target == null || target == instigator || damage <= 0f
                || (instigator != null && instigator.IsHostile == target.IsHostile))
                return false;
            bool accepted = critical && target is EnemyCharacter struckEnemy
                ? struckEnemy.TryReceiveDamage(damage, critical: true)
                : target.TryReceiveDamage(damage, attackStunDuration);
            if (!accepted)
                return false;

            if (target.IsHostile)
            {
                if (target.Health > 0f)
                    VoyageAudio.Play(Sound.EnemyHit, 0.39f, target.transform.position);
                else
                {
                    bool boss = target is EnemyCharacter enemy && enemy.IsBoss;
                    bool onDeck = target is EnemyCharacter boarder && boarder.IsOnDeck;
                    VoyageAudio.Play(onDeck ? Sound.KickFlesh : Sound.WaterSplash, boss ? 0.84f : 0.67f, target.transform.position);
                    if (boss)
                        VoyageAudio.Play(Sound.TrashBreak, 0.79f, target.transform.position);
                }
            }
            else
                VoyageAudio.Play(Sound.Stun, target is PlayerCharacter ? 0.75f : 0.48f, target.transform.position);
            damageReceived.Invoke(instigator, target, damage);
            damageSent.Invoke(instigator, target, damage);
            return true;
        }
    }
}
