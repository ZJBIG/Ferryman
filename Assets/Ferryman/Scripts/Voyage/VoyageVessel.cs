using Ferryman.Characters;
using Ferryman.Combat;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Voyage
{
    public sealed class VoyageVessel : MonoBehaviour
    {
        [SerializeField] private BoatHealth boat;
        [SerializeField] private PlayerCharacter player;
        [SerializeField] private DamageSystem damageSystem;

        public BoatHealth Boat => boat;
        public PlayerCharacter Player => player;
        public DamageSystem DamageSystem => damageSystem;

        public void SetActionsAllowed(bool allowed, bool cinematic = false)
        {
            foreach (var character in GetComponentsInChildren<Character>())
            {
                character.SetActionsAllowed(allowed);
                var body = character.GetComponent<Rigidbody2D>();
                if (body == null)
                    continue;
                body.velocity = Vector2.zero;
                body.simulated = !cinematic;
                if (!cinematic)
                    body.position = character.transform.position;
            }
            Physics2D.SyncTransforms();
        }
    }
}
