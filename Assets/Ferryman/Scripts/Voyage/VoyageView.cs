using System;
using System.Linq;
using Ferryman.Characters;
using Ferryman.World;
using UnityEngine;

namespace Ferryman.Voyage
{
    [RequireComponent(typeof(VoyageController))]
    public sealed class VoyageView : MonoBehaviour
    {
        [SerializeField] private Font font;
        private VoyageController controller;
        private GUIStyle text;
        private GUIStyle title;
        private GUIStyle small;
        private GUIStyle button;
        private Vector2 passengerScroll;
        private readonly Color accent = new Color(0.4f, 0.88f, 0.72f);
        private readonly Color muted = new Color(0.68f, 0.78f, 0.8f);

        private void Awake()
        {
            controller = GetComponent<VoyageController>();
            if (font == null)
                font = Font.CreateDynamicFontFromOSFont(new[] { "Microsoft YaHei", "SimHei", "Arial" }, 18);
        }

        private void OnGUI()
        {
            if (controller == null)
                return;
            if (text == null)
            {
                text = new GUIStyle(GUI.skin.label) { font = font, fontSize = 18, wordWrap = true };
                text.normal.textColor = Color.white;
                title = new GUIStyle(text) { fontSize = 30, fontStyle = FontStyle.Bold };
                small = new GUIStyle(text) { fontSize = 15 };
                small.normal.textColor = muted;
                button = new GUIStyle(GUI.skin.button) { font = font, fontSize = 17, wordWrap = true };
            }

            Matrix4x4 previous = GUI.matrix;
            float scale = Mathf.Min(Screen.width / 1280f, Screen.height / 720f);
            Vector2 offset = new Vector2((Screen.width - 1280f * scale) * 0.5f, (Screen.height - 720f * scale) * 0.5f);
            GUI.matrix = Matrix4x4.TRS(offset, Quaternion.identity, Vector3.one * scale);
            if (controller.Phase == VoyagePhase.Departure)
                DrawDeparture();
            else if (controller.Phase == VoyagePhase.Success || controller.Phase == VoyagePhase.Failure)
                DrawSettlement();
            else if (controller.Phase == VoyagePhase.Error)
                DrawError();
            else
                DrawVoyage(scale, offset);

            if (controller.Fade > 0f)
                Fill(new Rect(0f, 0f, 1280f, 720f), new Color(0.03f, 0.08f, 0.11f, controller.Fade));
            GUI.matrix = previous;
        }

        private void DrawDeparture()
        {
            GUI.Label(new Rect(48f, 48f, 750f, 50f), "摆渡人 · 可玩原型", title);
            GUI.Label(new Rect(50f, 102f, 740f, 32f), "护送乘客，守住甲板，抵达下一座岛。", text);
            DrawPermanent(new Rect(910f, 48f, 320f, 80f));
            if (controller.Definition == null)
                return;

            if (controller.HasCheckpoint)
            {
                Panel(new Rect(48f, 174f, 480f, 290f));
                GUI.Label(new Rect(72f, 194f, 432f, 38f), "发现未结束的航程", text);
                GUI.Label(new Rect(72f, 240f, 432f, 65f), "继续上次区域检查点，或丢弃本轮临时奖励重新出发。", small);
                if (GUI.Button(new Rect(72f, 326f, 432f, 44f), "恢复航程", button))
                    Act(() => controller.ResumeVoyage());
                if (GUI.Button(new Rect(72f, 386f, 432f, 44f), "丢弃本轮并重新出发", button))
                    Act(controller.AbandonVoyage);
            }
            else
            {
                GUI.Label(new Rect(50f, 163f, 480f, 30f), "选择首位乘客 · 目的地为最终岛屿", text);
                for (int i = 0; i < controller.Definition.initialPassengers.Length; i++)
                {
                    CrewDefinition profile = controller.Definition.initialPassengers[i];
                    float y = 208f + i * 98f;
                    Panel(new Rect(48f, y, 480f, 88f));
                    if (profile.portrait != null)
                        GUI.DrawTexture(new Rect(61f, y + 14f, 55f, 55f), profile.portrait.texture, ScaleMode.ScaleToFit);
                    float x = profile.portrait != null ? 126f : 66f;
                    GUI.Label(new Rect(x, y + 9f, 280f, 28f), profile.displayName, text);
                    GUI.Label(new Rect(x, y + 39f, 300f, 40f), VoyageController.DescribeRole(profile), small);
                    if (GUI.Button(new Rect(400f, y + 20f, 110f, 48f), "护送启航", button))
                        Act(() => controller.StartVoyage(profile, Environment.TickCount));
                }
            }

            Panel(new Rect(574f, 208f, 656f, 310f));
            GUI.Label(new Rect(596f, 226f, 610f, 40f), controller.Definition.displayName, title);
            GUI.Label(new Rect(596f, 286f, 610f, 92f),
                string.Join("  →  ", controller.Definition.stops.Select(stop => stop.displayName)), text);
            GUI.Label(new Rect(596f, 388f, 608f, 100f),
                "船只已完整修理。船员自动执行固定岗位。\n途中遇到的顺路乘客自动上船，到站领取报酬。\n成功或船毁均结算；主动退出丢弃本轮。", small);
            GUI.Label(new Rect(574f, 544f, 650f, 64f), "WASD 移动 · 鼠标瞄准\n按住左键射击 · 右键近战清障 · 按住 E 维修", text);
            if (GUI.Button(new Rect(1090f, 654f, 140f, 42f), "退出游戏", button))
                Act(controller.QuitGame);
        }

        private void DrawVoyage(float scale, Vector2 offset)
        {
            VoyageVessel vessel = controller.Vessel;
            if (vessel == null)
                return;
            Panel(new Rect(24f, 24f, 310f, 156f));
            GUI.Label(new Rect(42f, 36f, 280f, 29f), $"船只 HP  {vessel.Boat.CurrentHealth:0} / {vessel.Boat.MaxHealth:0}", text);
            Fill(new Rect(42f, 74f, 274f, 10f), new Color(0.12f, 0.2f, 0.24f));
            Fill(new Rect(42f, 74f, 274f * vessel.Boat.CurrentHealth / vessel.Boat.MaxHealth, 10f), accent);
            string state = vessel.Player.IsStunned ? "眩晕中，等待恢复" : vessel.Player.IsRepairing ? "正在维修" : "可以行动";
            GUI.Label(new Rect(42f, 94f, 276f, 27f), state, text);
            int bombs = FindObjectsOfType<TimedBomb>().Length;
            GUI.Label(new Rect(42f, 129f, 276f, 30f), $"维修点 {vessel.Boat.RepairPoints.Count}  ·  炸弹威胁 {bombs}", small);

            Panel(new Rect(352f, 24f, 550f, 120f));
            GUI.Label(new Rect(370f, 34f, 516f, 30f),
                $"{controller.StopIndex + 1}/{controller.Definition.stops.Length} · {controller.Definition.stops[controller.StopIndex].displayName}", text);
            GUI.Label(new Rect(370f, 72f, 516f, 65f), controller.Message, small);
            Panel(new Rect(924f, 24f, 332f, 126f));
            GUI.Label(new Rect(942f, 36f, 300f, 26f), "本轮待结算", small);
            GUI.Label(new Rect(942f, 67f, 300f, 28f),
                $"金币 {controller.PendingReward.coins}  ·  特殊道具 {controller.PendingReward.specialItems}", text);
            GUI.Label(new Rect(942f, 108f, 300f, 27f), $"已送达乘客 {controller.DeliveredPassengers} 人", small);

            Panel(new Rect(24f, 558f, 748f, 138f));
            GUI.Label(new Rect(42f, 568f, 706f, 28f), "船上乘客 · 固定岗位自动工作", text);
            passengerScroll = GUI.BeginScrollView(new Rect(42f, 604f, 714f, 80f), passengerScroll,
                new Rect(0f, 0f, 690f, Mathf.Max(74f, controller.Passengers.Count * 25f)));
            for (int i = 0; i < controller.Passengers.Count; i++)
            {
                Passenger passenger = controller.Passengers[i];
                string role = passenger.Definition.role == CrewRole.Combat ? "战斗" : "维护";
                string status = passenger.Actor.IsStunned ? "眩晕" : "在岗";
                GUI.Label(new Rect(0f, i * 25f, 690f, 25f),
                    $"{passenger.Definition.displayName} · {role} / {status} → {controller.Definition.stops[passenger.DestinationStopIndex].displayName}", small);
            }
            GUI.EndScrollView();
            GUI.Label(new Rect(800f, 570f, 455f, 62f),
                "WASD 移动 · 左键连射 · 右键清障\n按住 E 维修；移动和攻击会打断维修", text);
            if (GUI.Button(new Rect(1090f, 654f, 166f, 42f), "放弃本轮并返航", button))
                Act(controller.AbandonVoyage);

            Camera camera = Camera.main;
            if (camera == null)
                return;
            foreach (EnemyCharacter enemy in controller.Enemies)
            {
                if (enemy == null || enemy.Health <= 0f)
                    continue;
                float below = enemy.Hitbox.bounds.extents.y + 0.28f;
                Vector3 screen = camera.WorldToScreenPoint(enemy.transform.position + Vector3.down * below);
                Vector2 position = (new Vector2(screen.x, Screen.height - screen.y) - offset) / scale;
                GUI.Label(new Rect(position.x - 78f, position.y - 20f, 160f, 28f), $"{enemy.DisplayName}  {enemy.Health:0} HP", small);
            }
        }

        private void DrawSettlement()
        {
            Panel(new Rect(354f, 158f, 572f, 418f));
            GUI.Label(new Rect(390f, 182f, 510f, 48f), controller.Message, title);
            GUI.Label(new Rect(390f, 252f, 506f, 42f), $"成功送达 {controller.DeliveredPassengers} 位乘客", text);
            GUI.Label(new Rect(390f, 314f, 506f, 60f),
                $"本轮获得  {controller.PendingReward.coins} 金币\n特殊升级道具  {controller.PendingReward.specialItems}", text);
            DrawPermanent(new Rect(390f, 408f, 506f, 72f));
            if (GUI.Button(new Rect(390f, 500f, 500f, 46f), "返回出发岛 · 修船并开始新航程", button))
                Act(controller.ReturnHome);
        }

        private void DrawError()
        {
            Panel(new Rect(304f, 172f, 672f, 360f));
            GUI.Label(new Rect(338f, 192f, 600f, 46f), "航程已停止", title);
            GUI.Label(new Rect(338f, 254f, 602f, 140f), controller.Message, text);
            if (controller.CanRetrySettlement && GUI.Button(new Rect(338f, 418f, 280f, 46f), "重试奖励结算", button))
                Act(controller.RetrySettlement);
            if (controller.Progress != null && controller.Definition != null
                && GUI.Button(new Rect(640f, 418f, 300f, 46f), "丢弃本轮并返回出发岛", button))
                Act(controller.AbandonVoyage);
        }

        private void DrawPermanent(Rect area)
        {
            if (controller.Progress == null)
                return;
            GUI.Label(area, $"累计金币  {controller.Progress.coins}\n累计特殊道具  {controller.Progress.specialItems}", small);
        }

        private void Act(Action action)
        {
            try { action(); }
            catch (Exception error) { controller.ReportError(error); }
        }

        private static void Panel(Rect area)
        {
            Fill(area, new Color(0.035f, 0.1f, 0.13f, 0.93f));
        }

        private static void Fill(Rect area, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }
    }
}
