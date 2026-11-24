using System;
using UnityEngine;

namespace Ferryman.Voyage
{
    public static class VoyageExpeditionUI
    {
        private static readonly Color Ink = new Color(0.20f, 0.13f, 0.08f);
        private static readonly Color Paper = new Color(0.98f, 0.89f, 0.70f);
        private static readonly Color Explored = new Color(0.12f, 0.32f, 0.24f);
        private static VoyageRouteMap previewMap;
        private static int previewMapSeed;

        public static void Draw(VoyageController c, VoyageUIArt art, int previewSeed, bool preview,
            GUIStyle title, GUIStyle text, GUIStyle button)
        {
            if (!preview && !c.IslandResolved && (c.CurrentIsland == VoyageIslandKind.Shipyard
                || c.CurrentIsland == VoyageIslandKind.Battle || c.CurrentIsland == VoyageIslandKind.Elite))
                Act(c, () => c.ResolveIslandOption(0));
            if (!preview && c.UpgradeChoices != null && c.UpgradeChoices.Length > 0)
            {
                DrawUpgradeChoices(c, art, text);
                return;
            }
            if (!preview && !c.IslandResolved && c.CurrentIsland == VoyageIslandKind.Market)
            {
                DrawMarket(c, art, text);
                return;
            }
            DrawPaper(art);
            var heading = new GUIStyle(title) { fontSize = 27 };
            var body = new GUIStyle(text) { fontSize = 18 };
            var centered = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };
            heading.normal.textColor = Ink;
            body.normal.textColor = Ink;
            centered.normal.textColor = Ink;
            GUI.Label(new Rect(240f, 116f, 800f, 42f), preview ? "群岛航图" : "航海日志", heading);
            if (preview)
            {
                GUI.Label(new Rect(240f, 161f, 800f, 34f), "每次靠港选择下一座岛；不同航线带来不同事件和成长。", centered);
                if (previewMap == null || previewMapSeed != previewSeed)
                {
                    previewMap = VoyageRouteMap.Generate(previewSeed);
                    previewMapSeed = previewSeed;
                }
                DrawMap(previewMap, c, art, true, centered);
                return;
            }

            GUI.Label(new Rect(240f, 161f, 800f, 34f),
                $"船体 {c.Vessel.Boat.CurrentHealth:0} / {c.Vessel.Boat.MaxHealth:0}   ·   本轮 {c.AvailableCoins} 金币   ·   {c.AvailableSpecialItems} 特殊道具", centered);
            if (!c.IslandResolved)
                DrawIsland(c, art, heading, body, button);
            else
                DrawMap(c.RouteMap, c, art, false, centered);
        }

        private static void DrawMap(VoyageRouteMap map, VoyageController c, VoyageUIArt art, bool preview,
            GUIStyle text)
        {
            if (map == null)
                return;
            foreach (var node in map.Nodes)
            {
                Vector2 start = Position(node.depth, node.lane);
                foreach (int nextId in node.next)
                {
                    foreach (var next in map.Nodes)
                    {
                        if (next.id != nextId)
                            continue;
                        bool walked = !preview && map.IsExploredEdge(node.id, next.id, c.RoutePath);
                        DrawCurve(start, Position(next.depth, next.lane), walked);
                        break;
                    }
                }
            }

            var label = new GUIStyle(text) { fontSize = 15, alignment = TextAnchor.MiddleCenter };
            foreach (var node in map.Nodes)
            {
                Vector2 point = Position(node.depth, node.lane);
                bool current = !preview && node.id == c.CurrentNodeId;
                bool reachable = !preview && c.AwaitingRouteChoice && map.CanTravel(c.CurrentNodeId, node.id);
                var iconArea = new Rect(point.x - 25f, point.y - 30f, 50f, 56f);
                if (current || reachable)
                    Fill(new Rect(point.x - 39f, point.y - 35f, 78f, 97f), current
                        ? new Color(0.18f, 0.42f, 0.32f, 0.22f) : new Color(1f, 1f, 1f, 0.5f));
                Texture2D icon = NodeIcon(art, node.kind);
                if (icon != null)
                    GUI.DrawTexture(iconArea, icon, ScaleMode.ScaleToFit);
                else
                    Fill(new Rect(point.x - 7f, point.y - 7f, 14f, 14f), Ink);
                GUI.Label(new Rect(point.x - 51f, point.y + 27f, 102f, 40f), node.Name, label);
                GUI.Label(new Rect(point.x - 39f, point.y - 35f, 78f, 97f),
                    new GUIContent(string.Empty, node.Description), GUIStyle.none);
                if (reachable && GUI.Button(new Rect(point.x - 39f, point.y - 35f, 78f, 97f),
                    new GUIContent(string.Empty, node.Description), GUIStyle.none))
                    Act(c, () => c.SelectDestination(node.id));
            }

            GUI.Label(new Rect(245f, 537f, 790f, 36f), preview
                ? "虚线 · 未探索航路   |   实线 · 已走过航路"
                : "点击高亮岛屿启航 · 虚线为未探索，实线为已经走过", text);
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                Fill(new Rect(245f, 537f, 790f, 36f), Paper);
                GUI.Label(new Rect(245f, 537f, 790f, 36f), GUI.tooltip, new GUIStyle(text) { fontSize = 16 });
            }
            if (!preview)
                GUI.Label(new Rect(250f, 577f, 780f, 36f), DescribeRunUpgrades(c), new GUIStyle(text) { fontSize = 15 });
        }

        private static Vector2 Position(int depth, int lane)
        {
            return new Vector2(255f + depth * (765f / VoyageRouteMap.FinalDepth), 235f + lane * 117f);
        }

        private static void DrawCurve(Vector2 start, Vector2 end, bool explored)
        {
            Vector2 first = start + Vector2.right * 40f;
            Vector2 second = end - Vector2.right * 40f;
            if (Mathf.Abs(start.y - end.y) < 0.01f)
            {
                first.y -= 18f;
                second.y -= 18f;
            }
            Vector2 previous = start;
            for (int segment = 1; segment <= 32; segment++)
            {
                float t = segment / 32f;
                float inverse = 1f - t;
                Vector2 point = inverse * inverse * inverse * start + 3f * inverse * inverse * t * first
                    + 3f * inverse * t * t * second + t * t * t * end;
                if (explored || ((segment - 1) / 3) % 2 == 0)
                    DrawLine(previous, point, explored ? Explored : new Color(Ink.r, Ink.g, Ink.b, 0.70f), explored ? 3f : 2f);
                previous = point;
            }
        }

        private static void DrawLine(Vector2 start, Vector2 end, Color color, float thickness)
        {
            Matrix4x4 previous = GUI.matrix;
            Color previousColor = GUI.color;
            Vector2 delta = end - start;
            GUIUtility.RotateAroundPivot(Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg, start);
            GUI.color = color;
            GUI.DrawTexture(new Rect(start.x, start.y - thickness * 0.5f, delta.magnitude, thickness), Texture2D.whiteTexture);
            GUI.matrix = previous;
            GUI.color = previousColor;
        }

        private static Texture2D NodeIcon(VoyageUIArt art, VoyageIslandKind kind)
        {
            switch (kind)
            {
                case VoyageIslandKind.Departure:
                    return art.departure;
                case VoyageIslandKind.Battle:
                case VoyageIslandKind.Elite:
                    return art.combat;
                case VoyageIslandKind.Destination:
                    return art.destination;
                case VoyageIslandKind.Mystery:
                case VoyageIslandKind.Treasure:
                    return art.special;
                default:
                    return art.waypoint;
            }
        }

        private static void DrawUpgradeChoices(VoyageController c, VoyageUIArt art, GUIStyle text)
        {
            VoyageUpgradeKind[] choices = c.UpgradeChoices;
            for (int i = 0; i < choices.Length; i++)
            {
                VoyageUpgradeKind kind = choices[i];
                if (DrawCard(CardArea(i, choices.Length, art.upgradeCard), art, kind, text))
                    Act(c, () => c.ChooseUpgrade(kind));
            }
        }

        private static void DrawIsland(VoyageController c, VoyageUIArt art, GUIStyle title, GUIStyle text,
            GUIStyle button)
        {
            string name = "靠港休整";
            string description = "选择岛上行动，随后继续探索。";
            string[] options;
            switch (c.CurrentIsland)
            {
                case VoyageIslandKind.Rest:
                    name = "宁静港湾";
                    description = "在平静的海湾修整船只，或收集下一程所需的补给。";
                    options = new[] { "免费修复 300 船体", "收集补给 · 获得 40 金币" };
                    break;
                case VoyageIslandKind.Treasure:
                    name = "遗落宝藏";
                    description = "打开岛上的宝箱，挑选一件航行装备。";
                    options = new[] { "开启宝箱" };
                    break;
                case VoyageIslandKind.Rescue:
                    name = "求援灯塔";
                    description = "救起一位旅客。他将随船出航，并在岗位上帮助你。";
                    options = new[] { "招募战斗乘客", "招募维护乘客" };
                    break;
                case VoyageIslandKind.Mystery:
                    name = "迷雾奇遇";
                    description = "海雾中有一艘搁浅的货船。搜寻物资需要付出代价。";
                    options = new[] { "冒险打捞 · 消耗 100 船体\n获得 80 金币和 1 特殊道具", "挑选一项免费强化", "直接离港" };
                    break;
                default:
                    options = new[] { "准备启航" };
                    break;
            }

            GUI.Label(new Rect(260f, 216f, 760f, 50f), name, title);
            GUI.Label(new Rect(295f, 280f, 690f, 76f), description, text);
            for (int i = 0; i < options.Length; i++)
            {
                int option = i;
                bool previous = GUI.enabled;
                if (c.CurrentIsland == VoyageIslandKind.Mystery)
                    GUI.enabled = previous && (option != 0 || c.Vessel.Boat.CurrentHealth > 100f);
                if (GUI.Button(new Rect(350f, 370f + i * 71f, 580f, 60f), options[i], button))
                    Act(c, () => c.ResolveIslandOption(option));
                GUI.enabled = previous;
            }
        }

        private static void DrawMarket(VoyageController c, VoyageUIArt art, GUIStyle text)
        {
            var kinds = new[] { VoyageUpgradeKind.Attack, VoyageUpgradeKind.AttackSpeed, VoyageUpgradeKind.RepairSpeed };
            int[] runRanks = c.RunUpgrades;
            VoyageUpgradeKind[] available = Array.FindAll(kinds, kind => runRanks[(int)kind] < VoyageUpgrades.MaxRank);
            if (available.Length == 0)
            {
                Act(c, () => c.ResolveIslandOption(4));
                return;
            }
            for (int i = 0; i < available.Length; i++)
            {
                VoyageUpgradeKind kind = available[i];
                int option = Array.IndexOf(kinds, kind);
                int rank = runRanks[(int)kind];
                int price = VoyageUpgrades.Price(kind, rank);
                bool previous = GUI.enabled;
                GUI.enabled = previous && c.AvailableCoins >= price;
                if (DrawCard(CardArea(i, available.Length, art.upgradeCard), art, kind, text))
                    Act(c, () => c.ResolveIslandOption(option));
                GUI.enabled = previous;
            }
        }

        public static bool DrawPermanent(VoyageController c, VoyageUIArt art, GUIStyle text)
        {
            int[] permanentRanks = c.PermanentUpgrades;
            var available = Array.FindAll((VoyageUpgradeKind[])Enum.GetValues(typeof(VoyageUpgradeKind)),
                kind => permanentRanks[(int)kind] < VoyageUpgrades.MaxRank);
            if (available.Length == 0)
                return true;
            for (int i = 0; i < available.Length; i++)
            {
                VoyageUpgradeKind kind = available[i];
                bool previous = GUI.enabled;
                GUI.enabled = previous && !c.HasCheckpoint;
                bool acquired = false;
                if (DrawCard(CardArea(i, available.Length, art.upgradeCard), art, kind, text))
                    Act(c, () => acquired = c.BuyPermanentUpgrade(kind));
                GUI.enabled = previous;
                if (acquired)
                    return true;
            }
            return false;
        }

        private static Rect CardArea(int index, int count, Texture2D card)
        {
            const float gap = 28f;
            float width = Mathf.Min(290f, (1200f - (count - 1) * gap) / count);
            float height = width * card.height / card.width;
            float totalWidth = count * width + (count - 1) * gap;
            return new Rect((1280f - totalWidth) * 0.5f + index * (width + gap), (720f - height) * 0.5f, width, height);
        }

        private static bool DrawCard(Rect area, VoyageUIArt art, VoyageUpgradeKind kind, GUIStyle text)
        {
            GUI.DrawTexture(area, art.upgradeCard, ScaleMode.StretchToFill);
            Texture2D icon = UpgradeIcon(art, kind);
            if (icon != null)
                GUI.DrawTexture(new Rect(area.x + area.width * 0.265f, area.y + area.height * 0.185f,
                    area.width * 0.475f, area.height * 0.375f), icon, ScaleMode.ScaleToFit);
            var effect = new GUIStyle(text)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = Mathf.Clamp(Mathf.RoundToInt(area.width * 0.068f), 12, 20),
                padding = new RectOffset(),
                wordWrap = true
            };
            effect.normal.textColor = Ink;
            GUI.Label(new Rect(area.x + area.width * 0.18f, area.y + area.height * 0.625f,
                area.width * 0.64f, area.height * 0.18f), VoyageUpgrades.Description(kind), effect);
            return GUI.Button(area, GUIContent.none, GUIStyle.none);
        }

        private static Texture2D UpgradeIcon(VoyageUIArt art, VoyageUpgradeKind kind)
        {
            switch (kind)
            {
                case VoyageUpgradeKind.Attack: return art.upgradeAttack;
                case VoyageUpgradeKind.AttackSpeed: return art.upgradeAttackSpeed;
                case VoyageUpgradeKind.Durability: return art.upgradeDurability;
                case VoyageUpgradeKind.RepairSpeed: return art.upgradeRepairSpeed;
                default: return art.upgradeRepairCount;
            }
        }

        private static string DescribeRunUpgrades(VoyageController c)
        {
            string result = "本轮强化：";
            bool any = false;
            int[] ranks = c.RunUpgrades;
            for (int i = 0; i < ranks.Length; i++)
            {
                if (ranks[i] <= 0)
                    continue;
                result += (any ? "  ·  " : string.Empty) + VoyageUpgrades.Name((VoyageUpgradeKind)i) + " ×" + ranks[i];
                any = true;
            }
            return any ? result : result + "尚未获得";
        }

        private static void DrawPaper(VoyageUIArt art)
        {
            Fill(new Rect(0f, 0f, 1280f, 720f), new Color(0.025f, 0.075f, 0.09f, 0.74f));
            if (art.parchment != null)
                GUI.DrawTexture(new Rect(150f, 55f, 980f, 605f), art.parchment);
            else
                Fill(new Rect(150f, 55f, 980f, 605f), Paper);
        }

        private static void Fill(Rect area, Color color)
        {
            Color previous = GUI.color;
            GUI.color = color;
            GUI.DrawTexture(area, Texture2D.whiteTexture);
            GUI.color = previous;
        }

        private static void Act(VoyageController c, Action action)
        {
            try
            {
                action();
            }
            catch (Exception error)
            {
                c.ReportError(error);
            }
        }
    }
}
