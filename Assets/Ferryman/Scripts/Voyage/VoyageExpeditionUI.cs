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
            if (c.UpgradeChoices != null && c.UpgradeChoices.Length > 0)
                DrawUpgradeChoices(c, art, heading, body, button);
            else if (!c.IslandResolved)
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

        private static void DrawUpgradeChoices(VoyageController c, VoyageUIArt art, GUIStyle title, GUIStyle text,
            GUIStyle button)
        {
            GUI.Label(new Rect(250f, 190f, 780f, 40f), "航行奖励 · 选择一项本轮强化", title);
            VoyageUpgradeKind[] choices = c.UpgradeChoices;
            for (int i = 0; i < choices.Length; i++)
            {
                VoyageUpgradeKind kind = choices[i];
                var card = new Rect(248f + i * 266f, 246f, 250f, 282f);
                DrawCard(card, art, kind, text, VoyageUpgrades.Description(kind));
                if (GUI.Button(new Rect(card.x + 30f, card.yMax - 55f, 190f, 40f), "获得强化", button))
                    Act(c, () => c.ChooseUpgrade(kind));
            }
            GUI.Label(new Rect(250f, 557f, 780f, 45f), "本轮强化持续到航程结束，可与港口永久升级叠加。", text);
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
                case VoyageIslandKind.Shipyard:
                    name = "漂流船坞";
                    description = "船匠为你准备了一次免费强化。";
                    options = new[] { "挑选船坞强化" };
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
                    options = new[] { "冒险打捞 · 消耗 100 船体\n获得 80 金币和 1 特殊道具", "交换装备 · 消耗 40 金币\n挑选一项强化", "直接离港" };
                    break;
                case VoyageIslandKind.Market:
                    DrawMarket(c, art, title, text, button);
                    return;
                case VoyageIslandKind.Battle:
                case VoyageIslandKind.Elite:
                    name = c.CurrentIsland == VoyageIslandKind.Elite ? "精英战利品" : "战斗战利品";
                    description = "成功穿过危险海域，领取这一程的奖励。";
                    options = new[] { "领取航行强化" };
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
                    GUI.enabled = previous && (option == 0 ? c.Vessel.Boat.CurrentHealth > 100f
                        : option != 1 || c.AvailableCoins >= 40 && Array.Exists(c.RunUpgrades, rank => rank < VoyageUpgrades.MaxRank));
                if (GUI.Button(new Rect(350f, 370f + i * 71f, 580f, 60f), options[i], button))
                    Act(c, () => c.ResolveIslandOption(option));
                GUI.enabled = previous;
            }
        }

        private static void DrawMarket(VoyageController c, VoyageUIArt art, GUIStyle title, GUIStyle text,
            GUIStyle button)
        {
            GUI.Label(new Rect(250f, 187f, 780f, 42f), "海上集市", title);
            var kinds = new[] { VoyageUpgradeKind.Attack, VoyageUpgradeKind.AttackSpeed, VoyageUpgradeKind.RepairSpeed };
            int[] runRanks = c.RunUpgrades;
            for (int i = 0; i < kinds.Length; i++)
            {
                int option = i;
                VoyageUpgradeKind kind = kinds[i];
                int rank = runRanks[(int)kind];
                int price = VoyageUpgrades.Price(kind, rank);
                bool special = VoyageUpgrades.UsesSpecialItems(kind);
                var card = new Rect(248f + i * 266f, 241f, 250f, 265f);
                DrawCard(card, art, kind, text, VoyageUpgrades.Description(kind));
                bool previous = GUI.enabled;
                GUI.enabled = previous && rank < VoyageUpgrades.MaxRank
                    && (special ? c.AvailableSpecialItems : c.AvailableCoins) >= price;
                if (GUI.Button(new Rect(card.x + 30f, card.yMax - 54f, 190f, 39f), rank >= VoyageUpgrades.MaxRank
                    ? "已达最高等级" : $"购买 · {price} {(special ? "道具" : "金币")}", button))
                    Act(c, () => c.ResolveIslandOption(option));
                GUI.enabled = previous;
            }
            bool enabled = GUI.enabled;
            GUI.enabled = enabled && c.AvailableCoins >= 40 && c.Vessel.Boat.CurrentHealth < c.Vessel.Boat.MaxHealth;
            if (GUI.Button(new Rect(316f, 542f, 330f, 48f), "修复 200 船体 · 40 金币", button))
                Act(c, () => c.ResolveIslandOption(3));
            GUI.enabled = enabled;
            if (GUI.Button(new Rect(674f, 542f, 290f, 48f), "离开集市", button))
                Act(c, () => c.ResolveIslandOption(4));
        }

        public static void DrawPermanent(VoyageController c, VoyageUIArt art, GUIStyle title, GUIStyle text,
            GUIStyle button)
        {
            DrawPaper(art);
            var heading = new GUIStyle(title) { fontSize = 27 };
            var body = new GUIStyle(text) { fontSize = 16 };
            heading.normal.textColor = Ink;
            body.normal.textColor = Ink;
            GUI.Label(new Rect(250f, 116f, 780f, 42f), "出发港 · 永久整备", heading);
            GUI.Label(new Rect(295f, 161f, 690f, 32f),
                $"已结算资产：{c.Progress.coins} 金币 · {c.Progress.specialItems} 特殊道具", body);
            int[] permanentRanks = c.PermanentUpgrades;
            for (int i = 0; i < 6; i++)
            {
                var kind = (VoyageUpgradeKind)i;
                int rank = permanentRanks[i];
                int price = VoyageUpgrades.Price(kind, rank);
                bool special = VoyageUpgrades.UsesSpecialItems(kind);
                var area = new Rect(247f + (i % 3) * 267f, 195f + (i / 3) * 198f, 252f, 185f);
                Fill(area, new Color(1f, 0.97f, 0.85f, 0.55f));
                Texture2D icon = UpgradeIcon(art, kind);
                if (icon != null)
                    GUI.DrawTexture(new Rect(area.x + 12f, area.y + 12f, 49f, 49f), icon, ScaleMode.ScaleToFit);
                GUI.Label(new Rect(area.x + 70f, area.y + 12f, 178f, 52f), $"{VoyageUpgrades.Name(kind)}\n等级 {rank}", body);
                GUI.Label(new Rect(area.x + 12f, area.y + 75f, 226f, 48f), VoyageUpgrades.Description(kind), body);
                bool previous = GUI.enabled;
                GUI.enabled = previous && !c.HasCheckpoint && rank < VoyageUpgrades.MaxRank
                    && (special ? c.Progress.specialItems : c.Progress.coins) >= price;
                if (GUI.Button(new Rect(area.x + 12f, area.y + 135f, 226f, 39f),
                    rank >= VoyageUpgrades.MaxRank ? "已达最高等级" : $"升级 · {price} {(special ? "特殊道具" : "金币")}", button))
                    Act(c, () => c.BuyPermanentUpgrade(kind));
                GUI.enabled = previous;
            }
            GUI.Label(new Rect(265f, 586f, 750f, 36f), c.HasCheckpoint
                ? "完成或放弃当前航程后可进行永久整备。" : "永久整备在每次新航程生效；本轮奖励在结算后可用于升级。", body);
        }

        private static void DrawCard(Rect area, VoyageUIArt art, VoyageUpgradeKind kind, GUIStyle text, string description)
        {
            Fill(area, Paper);
            if (art.upgradeCard != null)
                GUI.DrawTexture(area, art.upgradeCard, ScaleMode.StretchToFill);
            var centered = new GUIStyle(text) { alignment = TextAnchor.MiddleCenter, fontSize = 18 };
            GUI.Label(new Rect(area.x + 20f, area.y + 21f, area.width - 40f, 36f), VoyageUpgrades.Name(kind), centered);
            Texture2D icon = UpgradeIcon(art, kind);
            if (icon != null)
                GUI.DrawTexture(new Rect(area.center.x - 40f, area.y + 62f, 80f, 80f), icon, ScaleMode.ScaleToFit);
            GUI.Label(new Rect(area.x + 28f, area.y + 147f, area.width - 56f, area.height - 211f), description, centered);
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
