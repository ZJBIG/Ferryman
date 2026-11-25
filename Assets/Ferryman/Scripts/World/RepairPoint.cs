using System.Collections.Generic;
using Ferryman.Voyage;
using UnityEngine;
using UnityEngine.Events;

namespace Ferryman.World
{
    public sealed class RepairPoint : MonoBehaviour
    {
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Sprite[] appearances;
        [SerializeField] private GameObject progressBar;
        [SerializeField] private Transform progressFill;
        [SerializeField] private GameObject repairEffect;
        [SerializeField] private GameObject completedEffectPrefab;
        [SerializeField] private UnityEvent repairStarted = new UnityEvent();
        [SerializeField] private UnityEvent repairStopped = new UnityEvent();
        [SerializeField] private UnityEvent repairCompleted = new UnityEvent();

        private readonly HashSet<Component> repairers = new HashSet<Component>();
        private BoatHealth boat;
        private AudioSource workAudio;

        public Vector2Int Cell { get; private set; }
        public float Progress { get; private set; }
        public float RequiredProgress { get; private set; }
        public bool IsComplete { get; private set; }
        public bool IsBeingRepaired => repairers.Count > 0;

        private void Awake()
        {
            if (visual != null && appearances != null && appearances.Length > 0)
                visual.sprite = appearances[Random.Range(0, appearances.Length)];
        }

        internal void Initialize(BoatHealth owner, Vector2Int cell, float requiredProgress)
        {
            boat = owner;
            Cell = cell;
            RequiredProgress = requiredProgress;
            UpdateProgressBar();
        }

        internal void RestoreProgress(float progress)
        {
            Progress = Mathf.Clamp(progress, 0f, RequiredProgress);
            UpdateProgressBar();
        }

        public bool BeginRepair(Component repairer)
        {
            if (repairer == null || !isActiveAndEnabled || IsComplete || boat == null || boat.IsDestroyed)
                return false;

            bool wasIdle = repairers.Count == 0;
            repairers.Add(repairer);
            UpdateProgressBar();
            if (wasIdle)
            {
                if (workAudio == null)
                    workAudio = VoyageAudio.StartLoop(gameObject, Sound.Repair, 0.30f, true);
                else
                    workAudio.Play();
                repairStarted.Invoke();
            }
            return true;
        }

        public void EndRepair(Component repairer)
        {
            if (!repairers.Remove(repairer))
                return;
            UpdateProgressBar();
            if (repairers.Count == 0)
                NotifyRepairStopped();
        }

        public bool AddProgress(Component repairer, float amount)
        {
            if (amount <= 0f || IsComplete || !isActiveAndEnabled || boat == null || boat.IsDestroyed
                || !repairers.Contains(repairer))
                return false;

            Progress = Mathf.Min(RequiredProgress, Progress + amount);
            UpdateProgressBar();
            if (Progress >= RequiredProgress)
            {
                // Claim completion before events or Unity's delayed destruction can re-enter.
                IsComplete = true;
                repairers.Clear();
                UpdateProgressBar();
                boat.CompleteRepair(this);
                NotifyRepairStopped();
                VoyageAudio.Play(Sound.RepairDone, 0.69f, transform.position);
                if (completedEffectPrefab != null)
                    Instantiate(completedEffectPrefab, transform.position, Quaternion.identity);
                repairCompleted.Invoke();
                Destroy(gameObject);
            }
            return true;
        }

        internal void StopAllRepairs()
        {
            bool wasActive = repairers.Count > 0;
            repairers.Clear();
            UpdateProgressBar();
            if (wasActive)
                NotifyRepairStopped();
        }

        private void NotifyRepairStopped()
        {
            if (workAudio != null)
                workAudio.Stop();
            repairStopped.Invoke();
        }

        private void UpdateProgressBar()
        {
            if (repairEffect != null)
                repairEffect.SetActive(IsBeingRepaired);
            if (progressBar != null)
                progressBar.SetActive(IsBeingRepaired);
            if (progressFill != null)
                progressFill.localScale = new Vector3(
                    RequiredProgress > 0f ? Progress / RequiredProgress : 0f, 1f, 1f);
        }

        private void OnDisable()
        {
            StopAllRepairs();
        }
    }
}
