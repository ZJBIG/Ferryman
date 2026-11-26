using System;
using System.Collections;
using UnityEngine;

namespace Ferryman.Voyage
{
    public sealed partial class VoyageView
    {
        private enum MenuConfirmation { None, Restart, Quit }

        private const float EscapeHoldSeconds = 0.8f;
        private bool menuOpen;
        private MenuConfirmation menuConfirmation;
        private float previousTimeScale;
        private bool escapeHeld;
        private bool escapeHandled;
        private float escapeStartedAt;
        private GUIStyle resumeButton;
        private GUIStyle restartButton;
        private GUIStyle quitButton;
        private GUIStyle confirmationButton;
        private GUIStyle confirmationText;
        private GUIStyle volumeTrack;
        private GUIStyle volumeHandle;

        private void Update()
        {
            HandleEscapeInput(Input.GetKeyDown(KeyCode.Escape), Input.GetKeyUp(KeyCode.Escape));
        }

        private void HandleEscapeInput(bool pressed, bool released)
        {
            if (pressed && !escapeHeld)
            {
                escapeHeld = true;
                escapeStartedAt = Time.unscaledTime;
                escapeHandled = menuOpen || !controller.AwaitingStoryDialogue;
                if (escapeHandled)
                    ToggleMenu();
            }
            if (!escapeHeld)
                return;
            if (!escapeHandled && controller.AwaitingStoryDialogue
                && Time.unscaledTime - escapeStartedAt >= EscapeHoldSeconds)
            {
                escapeHandled = true;
                Act(() => controller.SkipStoryDialogue());
            }
            if (released)
            {
                escapeHeld = false;
                if (!escapeHandled)
                    ToggleMenu();
            }
        }

        private void ToggleMenu()
        {
            if (menuOpen)
            {
                if (menuConfirmation != MenuConfirmation.None)
                    menuConfirmation = MenuConfirmation.None;
                else
                    CloseMenu();
                return;
            }
            if (art == null || controller.Definition == null)
                return;
            previousTimeScale = Time.timeScale;
            menuOpen = true;
            Time.timeScale = 0f;
            VoyageAudio.Play(Sound.Paper, 0.49f);
        }

        private void CloseMenu()
        {
            if (!menuOpen)
                return;
            menuOpen = false;
            menuConfirmation = MenuConfirmation.None;
            Time.timeScale = previousTimeScale;
            PlayerPrefs.Save();
        }

        private void DrawPauseMenu(Rect screen)
        {
            if (resumeButton == null)
            {
                resumeButton = MenuButton(art.menuResume, art.menuResumeHover);
                restartButton = MenuButton(art.menuRestart, art.menuRestartHover);
                quitButton = MenuButton(art.menuQuit, art.menuQuitHover);
                confirmationButton = MenuButton(art.menuButton, art.menuButtonHover);
                confirmationButton.font = font;
                confirmationButton.fontSize = 30;
                confirmationButton.alignment = TextAnchor.MiddleCenter;
                confirmationText = new GUIStyle(centeredInk) { fontSize = 34 };
                volumeTrack = new GUIStyle(GUIStyle.none) { fixedHeight = 142f };
                volumeHandle = MenuButton(art.menuVolumeHandle, art.menuVolumeHandle);
                volumeHandle.fixedWidth = 144f;
                volumeHandle.fixedHeight = 142f;
            }

            Fill(screen, Shade);
            Matrix4x4 previous = GUI.matrix;
            // Match the supplied 1920 x 1080 layout using the original transparent canvases.
            GUI.matrix = previous * Matrix4x4.Scale(Vector3.one * (2f / 3f));
            GUI.DrawTexture(new Rect(-6f, -46f, 1875f, 1216f), art.menuPanel);
            if (menuConfirmation == MenuConfirmation.None)
            {
                GUI.DrawTexture(new Rect(235f, 260f, 647f, 160f), art.menuMusic);
                GUI.DrawTexture(new Rect(235f, 435f, 647f, 160f), art.menuEffects);
                DrawVolumeSlider(271f, true);
                DrawVolumeSlider(421f, false);
                if (GUI.Button(new Rect(770f, 660f, 429f, 141f), GUIContent.none, resumeButton))
                {
                    VoyageAudio.Play(Sound.Confirm, 0.43f);
                    CloseMenu();
                }
                if (GUI.Button(new Rect(421f, 777f, 429f, 141f), GUIContent.none, restartButton))
                    menuConfirmation = MenuConfirmation.Restart;
                if (GUI.Button(new Rect(1115f, 777f, 429f, 141f), GUIContent.none, quitButton))
                {
                    if (controller.ActiveRun)
                        menuConfirmation = MenuConfirmation.Quit;
                    else
                        Act(controller.QuitGame);
                }
            }
            else
            {
                bool restart = menuConfirmation == MenuConfirmation.Restart;
                GUI.Label(new Rect(400f, 315f, 1060f, 210f), restart
                    ? "重新开始航程？\n本轮进度和未结算收益将丢弃，永久整备保留。"
                    : "退出游戏？\n本轮进度和未结算收益将丢弃，永久整备保留。", confirmationText);
                if (GUI.Button(new Rect(530f, 605f, 363f, 121f), "返回", confirmationButton))
                    menuConfirmation = MenuConfirmation.None;
                if (GUI.Button(new Rect(1010f, 605f, 363f, 121f), restart ? "重新开始" : "退出", confirmationButton))
                {
                    if (restart)
                        Act(RestartFromMenu);
                    else
                        Act(controller.QuitGame);
                }
            }
            GUI.matrix = previous;
        }

        private void DrawVolumeSlider(float y, bool music)
        {
            GUI.DrawTexture(new Rect(830f, y, 795f, 237f), art.menuVolumeTrack);
            float value = music ? VoyageAudio.MusicVolume : VoyageAudio.EffectsVolume;
            float next = GUI.HorizontalSlider(new Rect(874f, y + 43f, 683f, 142f),
                value, 0f, 1f, volumeTrack, volumeHandle);
            if (next == value)
                return;
            if (music)
                VoyageAudio.MusicVolume = next;
            else
                VoyageAudio.EffectsVolume = next;
        }

        private static GUIStyle MenuButton(Texture2D normal, Texture2D hover)
        {
            var style = new GUIStyle(GUIStyle.none);
            style.normal.background = normal;
            style.hover.background = style.active.background = style.focused.background = hover;
            style.normal.textColor = style.hover.textColor = style.active.textColor = style.focused.textColor = Color.white;
            return style;
        }

        private void RestartFromMenu()
        {
            CloseMenu();
            page = DeparturePage.Title;
            controller.AbandonVoyage();
            StartCoroutine(StartAfterReturn());
        }

        private IEnumerator StartAfterReturn()
        {
            while (controller.Phase == VoyagePhase.Transition)
                yield return null;
            if (controller.Phase == VoyagePhase.Departure)
                Act(() => controller.StartVoyage(Environment.TickCount));
        }

        private void OnApplicationFocus(bool focused)
        {
            if (!focused)
                escapeHeld = false;
        }

        private void OnDisable()
        {
            CloseMenu();
            escapeHeld = false;
        }
    }
}
