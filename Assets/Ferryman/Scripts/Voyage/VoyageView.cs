using System;
using Ferryman.Characters;
using UnityEngine;

namespace Ferryman.Voyage
{
    [RequireComponent(typeof(VoyageController))]
    public sealed class VoyageView : MonoBehaviour
    {
        private enum DeparturePage
        {
            Title,
            ConfirmRestart,
            Route,
            Passengers,
            Upgrades
        }

        [SerializeField] private Font font;
        private VoyageUIArt art;
        private VoyageController controller;
        private DeparturePage page;
        private PlannedVoyageEvent[] preview;
        private int seed;
        private GUIStyle text;
        private GUIStyle title;
        private GUIStyle ink;
        private GUIStyle inkTitle;
        private GUIStyle centeredInk;
        private GUIStyle button;
        private GUIStyle oarText;
        private const int StunFillSteps = 64;
        private readonly Texture2D[] stunFills = new Texture2D[StunFillSteps + 1];
        private Color32[] ringColors;
        private Color32[] ringPixels;
        private float[] ringAngles;
        private Vector2 passengerScroll;
        private static readonly Color InkColor = new Color(0.20f, 0.13f, 0.08f);
        private static readonly Color Shade = new Color(0.025f, 0.075f, 0.09f, 0.74f);

        public void Initialize(VoyageUIArt uiArt) => art = uiArt;

        private void Awake()
        {
            controller = GetComponent<VoyageController>();
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        }

        private void OnGUI()
        {
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 21, wordWrap = true };
                text.normal.textColor = Color.white;
                title = new GUIStyle(text) { fontSize = 32, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
                ink = new GUIStyle(text);
                ink.normal.textColor = InkColor;
                inkTitle = new GUIStyle(title);
                inkTitle.normal.textColor = InkColor;
                centeredInk = new GUIStyle(ink) { alignment = TextAnchor.MiddleCenter };
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 18, wordWrap = true };
                oarText = new GUIStyle(text) { fontSize = 30, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            }

            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            Vector2 offset = (new Vector2(Screen.width, Screen.height) - new Vector2(1280f, 720f) * scale) * 0.5f;
            GUI.matrix = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * scale);
            var screen = new Rect(-offset.x / scale, -offset.y / scale, Screen.width / scale, Screen.height / scale);

            if (controller.Phase == VoyagePhase.Error || art == null)
                DrawError(screen);
            else if (controller.Phase == VoyagePhase.Departure)
                DrawDeparture(screen);
            else if (controller.Phase == VoyagePhase.Success || controller.Phase == VoyagePhase.Failure)
                DrawSettlement(screen);
            else if (controller.Vessel != null)
            {
                bool upgradeCards = controller.IsExpedition && controller.Phase == VoyagePhase.Island
                    && controller.BoardingProfile == null && (controller.UpgradeChoices.Length > 0
                        || !controller.IslandResolved && (controller.CurrentIsland == VoyageIslandKind.Market
                            || controller.CurrentIsland == VoyageIslandKind.Shipyard
                            || controller.CurrentIsland == VoyageIslandKind.Battle
                            || controller.CurrentIsland == VoyageIslandKind.Elite));
                if (!upgradeCards)
                    DrawCombat(screen, scale, offset);
                if (controller.Phase == VoyagePhase.Island && controller.BoardingProfile != null)
                    DrawDialogue();
                else if (controller.IsExpedition && controller.Phase == VoyagePhase.Island)
                    VoyageExpeditionUI.Draw(controller, art, 0, false, inkTitle, ink, button);
            }

            if (controller.Fade > 0f)
                Fill(screen, new Color(0.03f, 0.08f, 0.11f, controller.Fade));
            GUI.matrix = previous;
        }

        private void DrawDeparture(Rect screen)
        {
            if (controller.Definition == null)
                return;
            switch (page)
            {
                case DeparturePage.Title:
                    DrawTitle();
                    break;
                case DeparturePage.ConfirmRestart:
                    DrawRestartConfirmation(screen);
                    break;
                case DeparturePage.Route:
                    DrawRoute(screen);
                    break;
                case DeparturePage.Passengers:
                    DrawPassengers(screen);
                    break;
                case DeparturePage.Upgrades:
                    if (VoyageExpeditionUI.DrawPermanent(controller, art, ink))
                        page = DeparturePage.Title;
                    break;
            }
        }

        private void DrawTitle()
        {
            // The live departure island supplies the sea and pier. The title ship is presentation only.
            GUI.DrawTexture(new Rect(-17f, 270f, 1024f, 576f), art.hull, ScaleMode.ScaleToFit);
            GUI.DrawTexture(new Rect(686f, 109f, 201f, 207f), art.titleBai, ScaleMode.ScaleToFit);
            GUI.DrawTexture(new Rect(878f, 91f, 207f, 207f), art.titleDu, ScaleMode.ScaleToFit);
            GUI.DrawTexture(new Rect(1064f, 137f, 192f, 207f), art.titleRen, ScaleMode.ScaleToFit);
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && !controller.HasCheckpoint;
            if (controller.Definition.branchingRoutes && GUI.Button(new Rect(931f, 329f, 264f, 43f), "港口永久整备", button))
                page = DeparturePage.Upgrades;
            GUI.enabled = enabled;
            if (Oar(new Rect(725f, 378f, 516f, 99f), "新游戏", true))
            {
                if (controller.HasCheckpoint)
                    page = DeparturePage.ConfirmRestart;
                else
                    Act(OpenRoute);
            }
            if (Oar(new Rect(725f, 483f, 516f, 99f), "继续游戏", controller.HasCheckpoint))
            {
                Act(() =>
                {
                    if (controller.ResumeVoyage())
                        page = DeparturePage.Title;
                });
            }
            if (Oar(new Rect(725f, 588f, 516f, 99f), "退出", true))
                Act(controller.QuitGame);
        }

        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.Escape) && controller.IsExpedition && controller.ActiveRun
                && controller.Phase == VoyagePhase.Island && !controller.IslandResolved
                && controller.CurrentIsland == VoyageIslandKind.Market && controller.BoardingProfile == null
                && controller.UpgradeChoices.Length == 0)
                Act(() => controller.ResolveIslandOption(4));
        }

        private bool Oar(Rect area, string label, bool enabled)
        {
            Color previous = GUI.color;
            bool inside = area.Contains(Event.current.mousePosition);
            GUI.color = enabled ? (inside ? new Color(1f, 0.88f, 0.65f) : Color.white) : new Color(1f, 1f, 1f, 0.48f);
            GUI.DrawTexture(area, art.oar, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(area.x + 38f, area.y + 1f, 218f, 52f), label, oarText);
            GUI.color = previous;
            return enabled && GUI.Button(area, GUIContent.none, GUIStyle.none);
        }

        private void DrawRestartConfirmation(Rect screen)
        {
            Fill(screen, Shade);
            Fill(new Rect(366f, 216f, 548f, 278f), Shade);
            GUI.Label(new Rect(396f, 245f, 488f, 52f), "有一段未结束的航程", title);
            GUI.Label(new Rect(416f, 322f, 448f, 68f), "开始新游戏会丢弃上次航程的检查点和本轮收益。", text);
            if (GUI.Button(new Rect(416f, 418f, 208f, 44f), "返回", button))
                page = DeparturePage.Title;
            if (GUI.Button(new Rect(648f, 418f, 216f, 44f), "丢弃并开始新游戏", button))
            {
                Act(() =>
                {
                    controller.AbandonVoyage();
                    OpenRoute();
                });
            }
        }

        private void OpenRoute()
        {
            seed = Environment.TickCount;
            preview = VoyageEventPlanner.Build(controller.Definition, seed);
            page = DeparturePage.Route;
        }

        private void DrawRoute(Rect screen)
        {
            if (controller.Definition.branchingRoutes)
            {
                VoyageExpeditionUI.Draw(controller, art, seed, true, inkTitle, ink, button);
                if (GUI.Button(new Rect(337f, 628f, 190f, 45f), "返回", button))
                    page = DeparturePage.Title;
                if (GUI.Button(new Rect(753f, 628f, 190f, 45f), "选择乘客", button))
                {
                    passengerScroll = Vector2.zero;
                    page = DeparturePage.Passengers;
                }
                return;
            }
            Fill(screen, Shade);
            GUI.DrawTexture(new Rect(148f, 65f, 984f, 590f), art.parchment);
            GUI.Label(new Rect(272f, 163f, 736f, 56f), controller.Definition.displayName, inkTitle);

            VoyageStop[] stops = controller.Definition.stops;
            float gap = 728f / Mathf.Max(1, stops.Length - 1);
            float first = 640f - gap * (stops.Length - 1) * 0.5f;
            float iconWidth = Mathf.Min(84f, gap * 0.64f);
            for (int i = 0; i < stops.Length; i++)
            {
                float x = first + i * gap;
                if (i + 1 < stops.Length)
                {
                    for (float dash = x + iconWidth * 0.5f + 6f; dash < x + gap - iconWidth * 0.5f - 6f; dash += 12f)
                        Fill(new Rect(dash, 331f, 6f, 2f), InkColor);
                }

                Texture2D icon;
                if (stops[i].kind == VoyageStopKind.Island)
                    icon = i == 0 ? art.departure : i == stops.Length - 1 ? art.destination : art.waypoint;
                else
                {
                    icon = preview[i].kind switch
                    {
                        VoyageEventKind.Combat => art.combat,
                        VoyageEventKind.Special => art.special,
                        _ => null
                    };
                }
                if (icon != null)
                    GUI.DrawTexture(new Rect(x - iconWidth * 0.5f, 276f, iconWidth, 80f), icon, ScaleMode.ScaleToFit);
                else
                    Fill(new Rect(x - 5f, 327f, 10f, 10f), InkColor);

                GUI.Label(new Rect(x - gap * 0.5f, 369f, gap, 62f), stops[i].displayName, centeredInk);
            }

            if (GUI.Button(new Rect(337f, 496f, 190f, 45f), "返回", button))
                page = DeparturePage.Title;
            if (GUI.Button(new Rect(753f, 496f, 190f, 45f), "选择乘客", button))
            {
                passengerScroll = Vector2.zero;
                page = DeparturePage.Passengers;
            }
        }

        private void DrawPassengers(Rect screen)
        {
            Fill(screen, Shade);
            GUI.DrawTexture(new Rect(213f, 42f, 854f, 610f), art.parchment);
            GUI.Label(new Rect(352f, 121f, 576f, 50f), "选择首位乘客", inkTitle);
            GUI.Label(new Rect(350f, 171f, 580f, 40f),
                "目的地 · " + controller.Definition.stops[controller.Definition.stops.Length - 1].displayName, centeredInk);

            CrewDefinition[] profiles = controller.Definition.initialPassengers;
            passengerScroll = GUI.BeginScrollView(new Rect(333f, 214f, 617f, 292f), passengerScroll,
                new Rect(0f, 0f, 600f, profiles.Length * 72f));
            for (int i = 0; i < profiles.Length; i++)
            {
                CrewDefinition profile = profiles[i];
                float y = i * 72f;
                Fill(new Rect(0f, y, 600f, 66f), new Color(1f, 0.92f, 0.73f, 0.41f));
                DrawPortrait(profile, new Rect(8f, y + 4f, 49f, 58f));
                GUI.Label(new Rect(70f, y + 2f, 385f, 31f), profile.displayName, ink);
                GUI.Label(new Rect(70f, y + 32f, 385f, 30f), VoyageController.DescribeRole(profile), ink);
                if (GUI.Button(new Rect(468f, y + 12f, 119f, 42f), "护送启航", button))
                {
                    Act(() =>
                    {
                        if (controller.StartVoyage(profile, seed))
                            page = DeparturePage.Title;
                    });
                }
            }
            GUI.EndScrollView();
            if (GUI.Button(new Rect(550f, 523f, 180f, 44f), "返回航线", button))
                page = DeparturePage.Route;
            GUI.Label(new Rect(295f, 577f, 690f, 34f), "WASD 移动 · 左键射击 · 右键踢飞道具 · 按住 E 维修", centeredInk);
        }

        private void DrawCombat(Rect viewport, float scale, Vector2 offset)
        {
            if (Event.current.type != EventType.Repaint)
                return;

            Camera camera = Camera.main;
            if (camera != null)
            {
                float worldScale = camera.pixelHeight / (2f * camera.orthographicSize * scale);
                DrawEnemyFeedback(camera, worldScale, scale, offset);
                DrawStunIndicator(controller.Vessel.Player, camera, viewport, worldScale, scale, offset);
                foreach (Passenger passenger in controller.Passengers)
                    if (passenger != null)
                        DrawStunIndicator(passenger.Actor, camera, viewport, worldScale, scale, offset);
            }

            var boat = controller.Vessel.Boat;
            var ship = new Rect(viewport.xMin + 24f, viewport.yMin + 18f, 310f, 54f);
            // These coordinates align the trimmed fill artwork with the frame's painted opening.
            Rect shipFill = Within(ship, 321f / 1000f, 47f / 174f, 664f / 1000f, 84f / 174f);
            float health = boat.MaxHealth > 0f ? Mathf.Clamp01(boat.CurrentHealth / boat.MaxHealth) : 0f;
            DrawFill(shipFill, art.shipHealthy, 0f, health);
            DrawFill(shipFill, art.shipDamaged, health, 1f);
            GUI.DrawTexture(ship, art.shipFrame);
        }

        private void DrawEnemyFeedback(Camera camera, float worldScale, float scale, Vector2 offset)
        {
            float barWidth = 2.15f * worldScale;
            float barHeight = barWidth * art.enemyFrame.height / art.enemyFrame.width;
            // The frame includes a sword on the left. Center the painted bar under the enemy.
            var opening = new Rect(118f / 480f, 66f / 153f, 352f / 480f, 57f / 153f);
            foreach (EnemyCharacter enemy in controller.Enemies)
            {
                if (enemy == null || enemy.Health <= 0f)
                    continue;
                Vector2 point = WorldToGui(camera, enemy.HealthBarAnchor - Vector3.up * 0.06f, scale, offset);
                var frame = new Rect(point.x - barWidth * opening.center.x, point.y - barHeight * opening.yMin,
                    barWidth, barHeight);
                Rect fill = Within(frame, opening.x, opening.y, opening.width, opening.height);
                float health = enemy.MaxHealth > 0f ? Mathf.Clamp01(enemy.Health / enemy.MaxHealth) : 0f;
                DrawFill(fill, art.enemyFill, 0f, health);
                GUI.DrawTexture(frame, art.enemyFrame);
                if (art.hitIcon != null && enemy.TryGetHitFeedbackPosition(out Vector3 position))
                {
                    Vector2 hitPoint = WorldToGui(camera, position, scale, offset);
                    GUI.DrawTexture(new Rect(hitPoint.x - barHeight * 0.5f, hitPoint.y - barHeight * 0.5f, barHeight, barHeight),
                        art.hitIcon, ScaleMode.ScaleToFit);
                }
            }
        }

        private void DrawStunIndicator(Character actor, Camera camera, Rect viewport, float worldScale, float scale, Vector2 offset)
        {
            if (actor == null || !actor.isActiveAndEnabled)
                return;
            float stun = actor.StunRemainingRatio;
            if (stun <= 0f)
                return;

            Bounds bounds = actor.VisualBounds;
            Vector2 point = WorldToGui(camera, bounds.center, scale, offset);
            if (!viewport.Contains(point))
                return;

            float size = 0.85f * worldScale;
            float side = (bounds.extents.x + 0.52f) * worldScale;
            float x = point.x + side;
            if (x + size > viewport.xMax)
                x = point.x - side - size;
            float y = Mathf.Clamp(point.y - size * 0.5f, viewport.yMin, viewport.yMax - size);
            var ring = new Rect(x, y, size, size);
            DrawStunFill(Within(ring, 17f / 616f, 17f / 616f, 599f / 616f, 599f / 616f), stun);
            GUI.DrawTexture(ring, art.stunFrame);
            GUI.DrawTexture(Within(ring, 0.26f, 0.27f, 0.52f, 0.52f), art.stunIcon, ScaleMode.ScaleToFit);
        }

        private static Vector2 WorldToGui(Camera camera, Vector3 position, float scale, Vector2 offset)
        {
            Vector3 screen = camera.WorldToScreenPoint(position);
            return (new Vector2(screen.x, Screen.height - screen.y) - offset) / scale;
        }

        private void DrawStunFill(Rect area, float fraction)
        {
            int steps = Mathf.CeilToInt(StunFillSteps * fraction);
            Texture2D fill = stunFills[steps];
            if (fill != null)
            {
                GUI.DrawTexture(area, fill);
                return;
            }

            int width = art.stunFill.width;
            int height = art.stunFill.height;
            if (ringColors == null)
            {
                ringColors = art.stunFill.GetPixels32();
                ringPixels = new Color32[ringColors.Length];
                ringAngles = new float[ringColors.Length];
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        float angle = Mathf.Atan2((x + 0.5f) / width - 0.5f, (y + 0.5f) / height - 0.5f);
                        ringAngles[y * width + x] = Mathf.Repeat(angle / (2f * Mathf.PI), 1f);
                    }
                }
            }

            float cutoff = (float)steps / StunFillSteps;
            for (int i = 0; i < ringPixels.Length; i++)
                ringPixels[i] = ringAngles[i] < cutoff ? ringColors[i] : default;
            fill = new Texture2D(width, height, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            fill.SetPixels32(ringPixels);
            fill.Apply(false, true);
            stunFills[steps] = fill;
            GUI.DrawTexture(area, fill);
        }

        private void DrawDialogue()
        {
            CrewDefinition profile = controller.BoardingProfile;
            GUI.DrawTexture(new Rect(90f, 492f, 1138f, 191f), art.dialogue);
            DrawPortrait(profile, new Rect(162f, 422f, 166f, 226f));
            GUI.Label(new Rect(354f, 516f, 616f, 36f), profile.displayName + " · 将上船", ink);
            GUI.Label(new Rect(354f, 560f, 616f, 36f), "目的地 · " + controller.BoardingDestination, ink);
            GUI.Label(new Rect(354f, 600f, 616f, 36f), VoyageController.DescribeRole(profile), ink);
        }

        private void DrawPortrait(CrewDefinition profile, Rect area)
        {
            GUI.DrawTexture(area, art.portrait, ScaleMode.ScaleToFit);
            if (profile.portrait == null)
            {
                GUI.Label(area, profile.role == CrewRole.Combat ? "战" : "修", centeredInk);
                return;
            }
            Sprite portrait = profile.portrait;
            GUI.DrawTextureWithTexCoords(Within(area, 0.10f, 0.09f, 0.80f, 0.80f), portrait.texture,
                new Rect(portrait.rect.x / portrait.texture.width, portrait.rect.y / portrait.texture.height,
                    portrait.rect.width / portrait.texture.width, portrait.rect.height / portrait.texture.height));
        }

        private void DrawSettlement(Rect screen)
        {
            Fill(screen, Shade);
            GUI.DrawTexture(new Rect(383f, 96f, 514f, 521f), art.settlement, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(565f, 98f, 150f, 51f),
                controller.Phase == VoyagePhase.Success ? "航程完成" : "船只沉没", inkTitle);
            GUI.Label(new Rect(470f, 225f, 340f, 40f), $"送达乘客  {controller.DeliveredPassengers} 位", centeredInk);
            GUI.Label(new Rect(470f, 273f, 340f, 40f), $"获得金币  ×{controller.PendingReward.coins}", centeredInk);
            GUI.Label(new Rect(475f, 361f, 330f, 70f), $"本轮物品\n特殊升级道具  ×{controller.PendingReward.specialItems}", centeredInk);
            if (controller.Progress != null)
                GUI.Label(new Rect(475f, 440f, 330f, 42f),
                    $"累计  {controller.Progress.coins} 金币 · {controller.Progress.specialItems} 特殊道具", centeredInk);
            if (GUI.Button(new Rect(517f, 632f, 246f, 46f), "返回出发岛", button))
                Act(controller.ReturnHome);
        }

        private void DrawError(Rect screen)
        {
            Fill(screen, Shade);
            GUI.Label(new Rect(304f, 188f, 672f, 65f), "航程已停止", title);
            GUI.Label(new Rect(338f, 275f, 604f, 126f), controller.Message ?? "缺少航程界面素材。", text);
            if (controller.CanRetrySettlement && GUI.Button(new Rect(338f, 418f, 280f, 46f), "重试奖励结算", button))
                Act(controller.RetrySettlement);
            if (controller.Progress != null && controller.Definition != null
                && GUI.Button(new Rect(640f, 418f, 300f, 46f), "丢弃本轮并返回出发岛", button))
                Act(controller.AbandonVoyage);
        }

        private void Act(Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                controller.ReportError(error);
            }
        }

        private static Rect Within(Rect area, float x, float y, float width, float height)
        {
            return new Rect(area.x + x * area.width, area.y + y * area.height, width * area.width, height * area.height);
        }

        private static void DrawFill(Rect area, Texture2D texture, float from, float to)
        {
            float fraction = to - from;
            if (fraction <= 0f)
                return;
            area.x += area.width * from;
            area.width *= fraction;
            GUI.DrawTextureWithTexCoords(area, texture, new Rect(from, 0f, fraction, 1f));
        }

        private static void Fill(Rect area, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private void OnDestroy()
        {
            foreach (Texture2D fill in stunFills)
                if (fill != null)
                    Destroy(fill);
        }
    }
}
