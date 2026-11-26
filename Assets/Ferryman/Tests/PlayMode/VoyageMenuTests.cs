#if UNITY_EDITOR
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Ferryman.Combat;
using Ferryman.Voyage;
using Ferryman.World;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace Ferryman.Tests
{
    public sealed class VoyageMenuTests
    {
        private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
        private VoyageController controller;
        private VoyageView view;
        private VoyageDefinition definition;
        private VoyageSaveStore store;
        private string directory;
        private float originalTimeScale;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            originalTimeScale = Time.timeScale;
            Time.timeScale = 1f;
            foreach (var existing in Object.FindObjectsOfType<VoyageController>(true))
                Object.Destroy(existing.gameObject);
            yield return null;
            definition = Object.Instantiate(Resources.Load<VoyageDefinition>("Voyage/DefaultVoyage"));
            definition.transitionSeconds = 0.05f;
            directory = Path.Combine(Path.GetTempPath(), "FerrymanMenuTests", Guid.NewGuid().ToString("N"));
            store = new VoyageSaveStore(directory);
            controller = new GameObject("Menu regression").AddComponent<VoyageController>();
            controller.Initialize(definition, store);
            // Keep Bootstrap from opening the real player save store.
            yield return SceneManager.LoadSceneAsync("main");
            view = controller.gameObject.AddComponent<VoyageView>();
            view.Initialize(AssetDatabase.LoadAssetAtPath<VoyageUIArt>("Assets/Ferryman/Art/UI/VoyageUI.asset"));
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (controller != null)
                Object.Destroy(controller.gameObject);
            yield return null;
            Time.timeScale = originalTimeScale;
            if (definition != null)
                Object.DestroyImmediate(definition);
            if (directory != null && Directory.Exists(directory))
            {
                string root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "FerrymanMenuTests"))
                    + Path.DirectorySeparatorChar;
                Assert.That(Path.GetFullPath(directory).StartsWith(root, StringComparison.OrdinalIgnoreCase), Is.True);
                Directory.Delete(directory, true);
            }
        }

        [UnityTest]
        public IEnumerator ShortEscapeOpensMenuWithoutSkippingAndHoldSkipsOnlyOnce()
        {
            Assert.That(controller.StartVoyage(7), Is.True);
            yield return WaitFor(() => controller.AwaitingStoryDialogue);
            string line = controller.StoryDialogueLine;
            Escape(true, false);
            Assert.That(IsOpen, Is.False);
            yield return new WaitForSecondsRealtime(0.05f);
            Escape(false, true);
            Assert.That(IsOpen, Is.True);
            Assert.That(Time.timeScale, Is.Zero);
            yield return new WaitForSecondsRealtime(0.1f);
            Assert.That(controller.StoryDialogueLine, Is.EqualTo(line));
            Invoke("HandleDialogueKeys", new Event { type = EventType.KeyDown, keyCode = KeyCode.Space });
            Assert.That(controller.StoryDialogueLine, Is.EqualTo(line));
            Escape(true, false);
            Escape(false, true);
            Assert.That(IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));

            Escape(true, false);
            yield return new WaitForSecondsRealtime(0.85f);
            Assert.That(controller.AwaitingStoryDialogue, Is.False);
            Escape(false, true);
            Assert.That(IsOpen, Is.False, "Releasing a consumed long press opened the menu.");
        }

        [UnityTest]
        public IEnumerator MenuFreezesGameplayAndRestoresCinematicAndTimeScaleState()
        {
            Time.timeScale = 0.6f;
            TapEscape();
            Assert.That(IsOpen, Is.True, "The title screen did not open the menu.");
            TapEscape();
            Assert.That(Time.timeScale, Is.EqualTo(0.6f));
            Assert.That(controller.StartVoyage(7), Is.True);
            bool simulated = controller.Vessel.Player.GetComponent<Rigidbody2D>().simulated;
            VoyagePhase phase = controller.Phase;
            TapEscape();
            yield return new WaitForSecondsRealtime(0.15f);
            Assert.That(controller.Phase, Is.EqualTo(phase));
            Assert.That(controller.Vessel.Player.GetComponent<Rigidbody2D>().simulated, Is.EqualTo(simulated));
            TapEscape();
            yield return WaitFor(() => controller.AwaitingStoryDialogue);

            var boat = controller.Vessel.Boat;
            var bomb = Object.Instantiate(Resources.Load<SeaEventDefinition>("Voyage/Events/DriftingBombs")
                .obstaclePrefab, controller.Vessel.transform).GetComponent<TimedBomb>();
            bomb.BindBoat(boat);
            float health = boat.CurrentHealth;
            float detonateAt = Time.time;
            typeof(TimedBomb).GetField("detonateAt", Private).SetValue(bomb, detonateAt);
            int shots = Object.FindObjectsOfType<Projectile>().Length;
            TapEscape();
            float pausedTime = Time.time;
            Assert.That(controller.Vessel.Player.TryFire(Vector2.up), Is.False);
            Assert.That(controller.Vessel.Player.TryMelee(Vector2.up), Is.False);
            yield return new WaitForSecondsRealtime(0.25f);
            Assert.That(Time.time, Is.EqualTo(pausedTime));
            Assert.That(boat.CurrentHealth, Is.EqualTo(health));
            Assert.That(Object.FindObjectsOfType<Projectile>().Length, Is.EqualTo(shots));
            Assert.That((float)typeof(TimedBomb).GetField("detonateAt", Private).GetValue(bomb), Is.EqualTo(detonateAt));
            view.enabled = false;
            Assert.That(Time.timeScale, Is.EqualTo(0.6f), "Disabling the menu left the world paused.");
        }

        [UnityTest]
        public IEnumerator RestartDiscardsOnlyThisRunAndStartsAfterReturningHome()
        {
            Assert.That(controller.StartVoyage(7), Is.True);
            yield return WaitFor(() => controller.AwaitingStoryDialogue);
            VoyageCheckpoint before = store.LoadCheckpoint(definition.id);
            TapEscape();
            Invoke("RestartFromMenu");
            yield return WaitFor(() => controller.ActiveRun);
            yield return WaitFor(() => controller.AwaitingStoryDialogue);
            Assert.That(store.LoadCheckpoint(definition.id).runId, Is.Not.EqualTo(before.runId));
            Assert.That(controller.Phase, Is.EqualTo(VoyagePhase.Island));
            Assert.That(controller.PendingReward.coins, Is.Zero);
            Assert.That(IsOpen, Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator MenuAudioRoutesMusicAndExistingLoopsThroughIndependentVolumes()
        {
            Type audio = typeof(VoyageController).Assembly.GetType("Ferryman.Voyage.VoyageAudio", true);
            controller.gameObject.AddComponent(audio);
            yield return null;
            AudioMixer mixer = Resources.Load<AudioMixer>("Audio/Voyage");
            Assert.That(mixer, Is.Not.Null);
            AudioMixerGroup music = mixer.FindMatchingGroups("Music").Single();
            AudioMixerGroup effects = mixer.FindMatchingGroups("Effects").Single();
            var sources = controller.GetComponents<AudioSource>();
            Assert.That(sources.Count(source => source.outputAudioMixerGroup == music), Is.EqualTo(2));
            Assert.That(sources.Count(source => source.outputAudioMixerGroup == effects), Is.EqualTo(sources.Length - 2));
            MethodInfo startLoop = audio.GetMethod("StartLoop", BindingFlags.Static | BindingFlags.NonPublic);
            Type sound = audio.Assembly.GetType("Ferryman.Voyage.Sound");
            var loop = (AudioSource)startLoop.Invoke(null, new object[]
            {
                controller.gameObject, Enum.Parse(sound, "Repair"), 0.3f, false
            });
            Assert.That(loop.outputAudioMixerGroup, Is.EqualTo(effects));
            Assert.That(mixer.GetFloat("MusicVolume", out float musicVolume), Is.True);
            Assert.That(mixer.GetFloat("EffectsVolume", out float effectsVolume), Is.True);
            try
            {
                Assert.That(mixer.SetFloat("EffectsVolume", -80f), Is.True);
                yield return null;
                Assert.That(mixer.GetFloat("MusicVolume", out float unchanged), Is.True);
                Assert.That(unchanged, Is.EqualTo(musicVolume));
                Assert.That(loop.volume, Is.EqualTo(0.3f), "The channel volume overwrote the authored loop gain.");
            }
            finally
            {
                mixer.SetFloat("MusicVolume", musicVolume);
                mixer.SetFloat("EffectsVolume", effectsVolume);
            }
        }

        private bool IsOpen => (bool)typeof(VoyageView).GetField("menuOpen", Private).GetValue(view);
        private void Escape(bool pressed, bool released) => Invoke("HandleEscapeInput", pressed, released);
        private void TapEscape()
        {
            Escape(true, false);
            Escape(false, true);
        }

        private void Invoke(string method, params object[] args) => typeof(VoyageView).GetMethod(method, Private).Invoke(view, args);

        private static IEnumerator WaitFor(Func<bool> ready)
        {
            float until = Time.realtimeSinceStartup + 10f;
            while (!ready() && Time.realtimeSinceStartup < until)
                yield return null;
            Assert.That(ready(), Is.True, "Menu regression did not reach its expected voyage state.");
        }
    }
}
#endif
