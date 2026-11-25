using System;
using System.Collections.Generic;
using UnityEngine;

namespace Ferryman.Voyage
{
    internal enum Sound
    {
        Ocean, HarborWater, Footstep1, Footstep2, Footstep3, CornShot, EnemyShot, EnemyHit, Stun, WaterSplash,
        KickSwing, KickFlesh, KickStone, KickWood, BombThrow, BombLand, Fuse, Explosion, BombClear,
        Gull, GullRelease, StoneLand, HullHit, HullBreak, Repair, RepairDone, WaterBubbles, BattleClear, Board,
        Dialogue, Confirm, Paper, Reward, Upgrade, Dock, Failure, TrashGather, TrashBreak, AllyHorn, SeaAlly
    }

    [RequireComponent(typeof(VoyageController))]
    internal sealed class VoyageAudio : MonoBehaviour
    {
        private enum Score { Silence, Harbor, Sailing, Battle, FinalBattle, Victory, Defeat }

        private const float ScoreVolume = 0.43f;
        private const float EffectHeadroom = 0.78f;
        private const float FadeSeconds = 0.65f;
        private static VoyageAudio instance;
        private readonly System.Random random = new System.Random();
        private readonly Voice[] voices = new Voice[18];
        private readonly Dictionary<Sound, AudioClip> sounds = new Dictionary<Sound, AudioClip>();
        private readonly Dictionary<Score, AudioClip> music = new Dictionary<Score, AudioClip>();
        private readonly Dictionary<Score, int> loopPositions = new Dictionary<Score, int>();
        private VoyageController controller;
        private Camera view;
        private AudioSource ocean;
        private AudioSource harborWater;
        private AudioSource foreground;
        private AudioSource outgoing;
        private Score score;
        private Score outgoingScore;

        private struct Voice
        {
            public AudioSource source;
            public int importance;
            public float startedAt;
        }

        private void Awake()
        {
            instance = this;
            controller = GetComponent<VoyageController>();
            foreach (Sound sound in Enum.GetValues(typeof(Sound)))
                sounds[sound] = Resources.Load<AudioClip>("Audio/Sfx/" + sound);
            foreach (Score track in Enum.GetValues(typeof(Score)))
                if (track != Score.Silence)
                    music[track] = Resources.Load<AudioClip>("Audio/Music/" + track);

            foreground = CreateSource(gameObject);
            outgoing = CreateSource(gameObject);
            foreground.priority = outgoing.priority = 16;
            ocean = StartLoop(gameObject, Sound.Ocean, 0f);
            harborWater = StartLoop(gameObject, Sound.HarborWater, 0f);
            for (int i = 0; i < voices.Length; i++)
                voices[i].source = CreateSource(gameObject);
        }

        private void OnDestroy()
        {
            if (instance == this)
                instance = null;
        }

        private void Update()
        {
            VoyagePhase phase = controller.Phase;
            VoyageStop[] stops = controller.ActiveStops;
            int index = controller.StopIndex;
            bool atSea = stops != null && index < stops.Length && stops[index].kind == VoyageStopKind.Sea
                && phase != VoyagePhase.Departure;

            Score next = Score.Harbor;
            if (phase == VoyagePhase.Success)
                next = Score.Victory;
            else if (phase == VoyagePhase.Failure)
                next = Score.Defeat;
            else if (phase == VoyagePhase.Error)
                next = Score.Silence;
            else if (atSea)
            {
                bool combat = phase == VoyagePhase.Event && controller.EventPlan?[index]?.kind == VoyageEventKind.Combat;
                bool finalBattle = controller.IsBossLeg || controller.StoryRound == 0 && index == stops.Length - 2;
                next = combat ? (finalBattle ? Score.FinalBattle : Score.Battle) : Score.Sailing;
            }
            ChangeScore(next);

            float step = Time.unscaledDeltaTime / FadeSeconds;
            foreground.volume = Mathf.MoveTowards(foreground.volume, next == Score.Silence ? 0f : ScoreVolume, step * ScoreVolume);
            outgoing.volume = Mathf.MoveTowards(outgoing.volume, 0f, step * ScoreVolume);
            if (outgoing.volume == 0f && outgoingScore != Score.Silence)
                ReleaseOutgoing();

            bool ambience = phase != VoyagePhase.Error;
            if (ocean != null)
                ocean.volume = Mathf.MoveTowards(ocean.volume, ambience && atSea ? 0.27f : 0f, step * 0.27f);
            if (harborWater != null)
                harborWater.volume = Mathf.MoveTowards(harborWater.volume, ambience && !atSea ? 0.23f : 0f, step * 0.23f);
        }

        private void ChangeScore(Score next)
        {
            if (next == score)
                return;

            if (next == outgoingScore && outgoing.isPlaying)
                (foreground, outgoing) = (outgoing, foreground);
            else
            {
                ReleaseOutgoing();
                (foreground, outgoing) = (outgoing, foreground);
                if (next != Score.Silence && music[next] != null)
                {
                    foreground.clip = music[next];
                    foreground.loop = next != Score.Victory && next != Score.Defeat;
                    if (foreground.loop && loopPositions.TryGetValue(next, out int sample))
                        foreground.timeSamples = sample;
                    foreground.Play();
                }
            }
            outgoingScore = score;
            score = next;
        }

        private void ReleaseOutgoing()
        {
            if (outgoing.loop && outgoing.isPlaying)
                loopPositions[outgoingScore] = outgoing.timeSamples;
            outgoing.Stop();
            outgoing.clip = null;
            outgoing.volume = 0f;
            outgoingScore = Score.Silence;
        }

        internal static void Play(Sound sound, float volume = 0.55f, Vector2? position = null)
        {
            if (instance != null)
                instance.PlayEffect(sound, volume, position);
        }

        internal static void Footstep(Vector2 position, bool captain)
        {
            if (instance == null)
                return;
            var sound = (Sound)((int)Sound.Footstep1 + instance.random.Next(3));
            instance.PlayEffect(sound, captain ? 0.27f : 0.16f, position);
        }

        internal static AudioSource StartLoop(GameObject owner, Sound sound, float volume, bool positionOnDeck = false)
        {
            if (instance == null || instance.sounds[sound] == null)
                return null;
            AudioSource source = CreateSource(owner);
            source.clip = instance.sounds[sound];
            source.loop = true;
            source.volume = volume;
            source.priority = sound == Sound.Fuse ? 24 : 64;
            if (positionOnDeck)
                source.panStereo = instance.Pan(owner.transform.position);
            source.Play();
            return source;
        }

        private void PlayEffect(Sound sound, float volume, Vector2? position)
        {
            AudioClip clip = sounds[sound];
            if (clip == null)
                return;
            int importance = sound switch
            {
                Sound.Explosion or Sound.BombLand or Sound.Gull or Sound.HullBreak or Sound.WaterBubbles
                    or Sound.BattleClear or Sound.Upgrade or Sound.Failure or Sound.TrashGather or Sound.TrashBreak
                    or Sound.AllyHorn or Sound.SeaAlly => 2,
                Sound.Footstep1 or Sound.Footstep2 or Sound.Footstep3 or Sound.CornShot
                    or Sound.EnemyShot or Sound.EnemyHit => 0,
                _ => 1
            };
            int selected = -1;
            for (int i = 0; i < voices.Length; i++)
            {
                if (!voices[i].source.isPlaying)
                {
                    selected = i;
                    break;
                }
                if (voices[i].importance <= importance && (selected < 0
                    || voices[i].importance < voices[selected].importance
                    || voices[i].importance == voices[selected].importance && voices[i].startedAt < voices[selected].startedAt))
                    selected = i;
            }
            if (selected < 0)
                return;

            ref Voice voice = ref voices[selected];
            voice.importance = importance;
            voice.startedAt = Time.unscaledTime;
            AudioSource source = voice.source;
            source.Stop();
            source.clip = clip;
            source.volume = volume * EffectHeadroom;
            source.priority = 128 - 32 * importance;
            source.panStereo = position.HasValue ? Pan(position.Value) : 0f;
            source.pitch = position.HasValue && importance < 2 ? 0.94f + (float)random.NextDouble() * 0.12f : 1f;
            source.Play();
        }

        private float Pan(Vector2 position)
        {
            if (view == null)
                view = Camera.main;
            return view == null ? 0f : Mathf.Clamp((view.WorldToViewportPoint(position).x - 0.5f) * 1.3f, -0.65f, 0.65f);
        }

        private static AudioSource CreateSource(GameObject owner)
        {
            AudioSource source = owner.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }
    }
}
