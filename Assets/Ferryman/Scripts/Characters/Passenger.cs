using System;
using UnityEngine;
using UnityEngine.Events;

namespace Ferryman.Characters
{
    [DisallowMultipleComponent]
    public sealed class Passenger : MonoBehaviour
    {
        [SerializeField] private UnityEvent boarded = new UnityEvent();
        [SerializeField] private UnityEvent disembarked = new UnityEvent();
        [SerializeField] private GameObject disembarkEffectPrefab;
        public CrewDefinition Definition { get; private set; }
        public int DestinationStopIndex { get; private set; }
        public Character Actor => GetComponent<Character>();

        public void Initialize(CrewDefinition definition, int destinationStopIndex)
        {
            if (definition == null || destinationStopIndex < 0 || Actor == null)
                throw new ArgumentException("Passenger needs a crew definition and a valid destination.");
            if (Definition != null)
                throw new InvalidOperationException("A passenger's fixed job and destination are already assigned.");

            Definition = definition;
            DestinationStopIndex = destinationStopIndex;
            boarded.Invoke();
        }

        public void Disembark()
        {
            disembarked.Invoke();
            if (disembarkEffectPrefab != null)
                Instantiate(disembarkEffectPrefab, transform.position, Quaternion.identity);
            gameObject.SetActive(false);
            Destroy(gameObject);
        }
    }
}
