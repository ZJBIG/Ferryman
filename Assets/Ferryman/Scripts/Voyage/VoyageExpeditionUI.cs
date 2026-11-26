using System;
using System.Collections.Generic;
using System.Linq;
using Ferryman.Characters;
using UnityEngine;

namespace Ferryman.Voyage
{
    public static class VoyageExpeditionUI
    {
        private static readonly Color Ink = new Color(0.20f, 0.13f, 0.08f);
        private static readonly Color Paper = new Color(0.98f, 0.89f, 0.70f);
        private static readonly Color Explored = new Color(0.12f, 0.32f, 0.24f);
        private static readonly Color Reachable = new Color(0.13f, 0.36f, 0.43f);
        private static readonly Color FaintInk = new Color(0.35f, 0.22f, 0.12f, 0.6f);
        private static readonly Color LabelBackground = new Color(0.04f, 0.10f, 0.13f, 0.90f);
        private static VoyageRouteMap previewMap;
        private static int previewMapSeed;
        private static VoyageRouteMap scrollMap;
        private static Vector2 mapScroll;
        private static int scrollNode = -1;
        private static bool draggingMap;
        private static VoyageRouteMap inkMap;
        private static int inkNode = -1;
        private static bool inkReachable;
        private static Texture2D chartInk;

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
            if (preview)
            {
                if (previewMap == null || previewMapSeed != previewSeed)
                {
                    previewMap = VoyageRouteMap.Generate(previewSeed);
                    previewMapSeed = previewSeed;
                }
                DrawMap(previewMap, c, art, true, text);
                return;
            }
            if (c.IslandResolved)
            {
                DrawMap(c.RouteMap, c, art, false, text);
                return;
            }
            DrawPaper(art);
            var heading = new GUIStyle(title) { fontSize = 27 };
            var body = new GUIStyle(text) { fontSize = 18 };
            var centered = new GUIStyle(body) { alignment = TextAnchor.MiddleCenter };
            heading.normal.textColor = Ink;
            body.normal.textColor = Ink;
            centered.normal.textColor = Ink;
            GUI.Label(new Rect(240f, 116f, 800f, 42f), "航海日志", heading);
            GUI.Label(new Rect(240f, 161f, 800f, 34f),
                $"船体 {c.Vessel.Boat.CurrentHealth:0} / {c.Vessel.Boat.MaxHealth:0}   ·   本轮 {c.AvailableCoins} 金币   ·   {c.AvailableSpecialItems} 特殊道具", centered);
            DrawIsland(c, art, heading, body, button);
        }

        private static void DrawMap(VoyageRouteMap map, VoyageController c, VoyageUIArt art, bool preview,
            GUIStyle text)
        {
            if (map == null)
                return;
            const int mapWidth = 1850;
            const int mapHeight = 555;
            var viewport = new Rect(80f, 45f, 1120f, mapHeight);
            int currentNode = preview ? 0 : c.CurrentNodeId;
            if (scrollMap != map || scrollNode != currentNode)
            {
                scrollMap = map;
                scrollNode = currentNode;
                mapScroll.x = Mathf.Clamp(Position(map.Nodes[currentNode]).x - viewport.width * 0.35f, 0f, mapWidth - viewport.width);
                draggingMap = false;
            }
            Event input = Event.current;
            if (input.type == EventType.ScrollWheel && viewport.Contains(input.mousePosition))
            {
                mapScroll.x += (Mathf.Abs(input.delta.x) > 0f ? input.delta.x : input.delta.y) * 65f;
                input.Use();
            }
            if (input.type == EventType.MouseDown && input.button == 1 && viewport.Contains(input.mousePosition))
            {
                draggingMap = true;
                input.Use();
            }
            if (draggingMap && input.type == EventType.MouseDrag)
            {
                mapScroll.x -= input.delta.x;
                input.Use();
            }
            if (input.type == EventType.MouseUp && input.button == 1)
                draggingMap = false;
            mapScroll.x = Mathf.Clamp(mapScroll.x, 0f, mapWidth - viewport.width);
            mapScroll.y = 0f;
            mapScroll = GUI.BeginScrollView(viewport, mapScroll, new Rect(0f, 0f, mapWidth, viewport.height),
                false, false, GUIStyle.none, GUIStyle.none);
            var paperArea = new Rect(0f, 0f, mapWidth, viewport.height);
            if (art.routeParchment != null)
                GUI.DrawTexture(paperArea, art.routeParchment, ScaleMode.StretchToFill);
            else if (art.parchment != null)
                GUI.DrawTexture(paperArea, art.parchment, ScaleMode.StretchToFill);
            else
                Fill(paperArea, Paper);
            var passengerBadges = preview || c.Passengers.Count == 0 ? null : PassengerBadgesByIsland(map, c);
            if (input.type == EventType.Repaint)
            {
                bool choosing = !preview && c.AwaitingRouteChoice;
                if (chartInk == null || inkMap != map || inkNode != currentNode || inkReachable != choosing)
                    PaintChartInk(map, preview ? null : c.RoutePath, currentNode, choosing, mapWidth, mapHeight);
                GUI.DrawTexture(paperArea, chartInk, ScaleMode.StretchToFill);
            }

            foreach (var node in map.Nodes)
            {
                Vector2 point = Position(node);
                bool current = !preview && node.id == c.CurrentNodeId;
                bool reachable = !preview && c.AwaitingRouteChoice && map.CanTravel(c.CurrentNodeId, node.id);
                var iconArea = new Rect(point.x - 28f, point.y - 28f, 56f, 56f);
                Color previous = GUI.color;
                GUI.color = current ? Explored : preview || reachable ? Color.white : new Color(1f, 1f, 1f, 0.55f);
                Texture2D icon = NodeIcon(art, node.kind);
                if (icon != null)
                    GUI.DrawTexture(iconArea, icon, ScaleMode.ScaleToFit);
                else
                    Fill(new Rect(point.x - 7f, point.y - 7f, 14f, 14f), Ink);
                GUI.color = previous;
                string tooltip = node.Name + " · " + node.Description;
                if (passengerBadges != null && passengerBadges.TryGetValue(node.id, out CrewDefinition[] passengers))
                {
                    tooltip += " · 下船顾客 " + string.Join("、", passengers.Select(passenger => passenger.displayName));
                    DrawPassengerBadges(iconArea, passengers, tooltip, text);
                }
                var hint = new GUIContent(string.Empty, tooltip);
                GUI.Label(iconArea, hint, GUIStyle.none);
                if (reachable && GUI.Button(iconArea,
                    hint, GUIStyle.none))
                    Act(c, () => c.SelectDestination(node.id));
            }
            GUI.EndScrollView();
            if (!string.IsNullOrEmpty(GUI.tooltip))
            {
                var hintArea = new Rect(180f, 584f, 920f, 38f);
                Fill(hintArea, LabelBackground);
                GUI.Label(hintArea, GUI.tooltip,
                    new GUIStyle(text) { fontSize = 16, wordWrap = true, alignment = TextAnchor.MiddleCenter, normal = { textColor = Paper } });
            }
        }

        private static Dictionary<int, CrewDefinition[]> PassengerBadgesByIsland(VoyageRouteMap map, VoyageController controller)
        {
            var badges = new Dictionary<int, List<CrewDefinition>>();
            int current = controller.CurrentNodeId;
            foreach (var group in controller.Passengers.GroupBy(passenger => passenger.DestinationStopIndex))
            {
                var canReach = new bool[map.Nodes.Length];
                canReach[current] = true;
                // Stop each possible branch at its first eligible island after the requested depth.
                foreach (var node in map.Nodes)
                {
                    if (!canReach[node.id])
                        continue;
                    if (node.id != current && node.depth * 2 >= group.Key && VoyageController.CanDisembarkAt(node.kind))
                    {
                        if (!badges.TryGetValue(node.id, out List<CrewDefinition> passengers))
                            badges[node.id] = passengers = new List<CrewDefinition>();
                        passengers.AddRange(group.Select(passenger => passenger.Definition));
                        continue;
                    }
                    foreach (int next in node.next)
                        canReach[next] = true;
                }
            }
            return badges.ToDictionary(entry => entry.Key, entry => entry.Value.ToArray());
        }

        private static void DrawPassengerBadges(Rect iconArea, CrewDefinition[] passengers, string tooltip, GUIStyle text)
        {
            const int visibleSlots = 3;
            const float size = 21f;
            const float gap = 2f;
            var label = new GUIStyle(text) { fontSize = 12, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } };
            var hint = new GUIContent(string.Empty, tooltip);
            for (int i = 0; i < Mathf.Min(passengers.Length, visibleSlots); i++)
            {
                var badge = new Rect(iconArea.xMax - 14f + i * (size + gap), iconArea.yMax - 14f, size, size);
                CrewDefinition profile = passengers[i];
                if (i == visibleSlots - 1 && passengers.Length > visibleSlots)
                    GUI.Label(badge, "+" + (passengers.Length - i), label);
                else if (profile.portrait != null)
                {
                    VoyageView.DrawPortraitImage(profile.portrait, badge);
                }
                else
                    GUI.Label(badge, profile.role == CrewRole.Combat ? "战" : "修", label);
                GUI.Label(badge, hint, GUIStyle.none);
            }
        }

        private static Vector2 Position(VoyageRouteNode node)
        {
            if (node.depth == 0)
                return new Vector2(140f, 345f);
            if (node.depth == VoyageRouteMap.FinalDepth)
                return new Vector2(1680f, 290f);
            return new Vector2(140f + node.depth * 220f,
                130f + node.lane * 145f + (node.id * 53 % 5 - 2) * 13f);
        }

        private static void PaintChartInk(VoyageRouteMap map, IReadOnlyList<int> path, int current, bool choosing,
            int width, int height)
        {
            // Drawing rotated GUI rectangles inside a scaled ScrollView offsets the route strokes.
            // Paint the ink in map coordinates and let the paper and ink share one scroll transform.
            if (chartInk == null)
                chartInk = new Texture2D(width, height, TextureFormat.RGBA32, false)
                {
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Bilinear,
                    wrapMode = TextureWrapMode.Clamp
                };
            var pixels = new Color32[width * height];
            foreach (var node in map.Nodes)
            {
                Vector2 start = Position(node);
                foreach (int next in node.next)
                {
                    bool walked = path != null && map.IsExploredEdge(node.id, next, path);
                    PaintCurve(start, Position(map.Nodes[next]), walked, choosing && node.id == current);
                }
            }
            chartInk.SetPixels32(pixels);
            chartInk.Apply(false);
            inkMap = map;
            inkNode = current;
            inkReachable = choosing;

            void PaintCurve(Vector2 start, Vector2 end, bool explored, bool reachable)
            {
                // A common X and easing at any t keeps routes in lane order between columns.
                Vector2 Point(float t) => new Vector2(Mathf.Lerp(start.x, end.x, t),
                    Mathf.SmoothStep(start.y, end.y, t) - 36f * t * (1f - t));
                float trim = 26f / (end.x - start.x);
                Color color = explored ? Explored : reachable ? Reachable : FaintInk;
                float radius = explored ? 2.1f : reachable ? 1.8f : 1.1f;
                int dashCount = Mathf.Max(1, Mathf.RoundToInt((Vector2.Distance(start, end) - 52f) / 21f));
                int segments = dashCount * 6 - 2;
                Vector2 previous = Point(trim);
                for (int segment = 1; segment <= segments; segment++)
                {
                    Vector2 point = Point(Mathf.Lerp(trim, 1f - trim, (float)segment / segments));
                    if (explored || (segment - 1) % 6 < 4)
                        PaintStroke(previous, point, color, radius);
                    previous = point;
                }
            }

            void PaintStroke(Vector2 start, Vector2 end, Color color, float radius)
            {
                Vector2 delta = end - start;
                float lengthSquared = delta.sqrMagnitude;
                int left = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(start.x, end.x) - radius));
                int right = Mathf.Min(width - 1, Mathf.CeilToInt(Mathf.Max(start.x, end.x) + radius));
                int top = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(start.y, end.y) - radius));
                int bottom = Mathf.Min(height - 1, Mathf.CeilToInt(Mathf.Max(start.y, end.y) + radius));
                for (int y = top; y <= bottom; y++)
                for (int x = left; x <= right; x++)
                {
                    Vector2 sample = new Vector2(x + 0.5f, y + 0.5f);
                    float t = Mathf.Clamp01(Vector2.Dot(sample - start, delta) / lengthSquared);
                    float coverage = Mathf.Clamp01(radius + 0.5f - Vector2.Distance(sample, start + t * delta));
                    int i = (height - y - 1) * width + x;
                    Color32 ink = new Color(color.r, color.g, color.b, color.a * coverage);
                    if (ink.a > pixels[i].a)
                        pixels[i] = ink;
                }
            }
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
                    options = new[] { "招募战斗乘客", "招募维护乘客", "直接离港" };
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
                else if (c.CurrentIsland == VoyageIslandKind.Rescue && option < 2)
                    GUI.enabled = previous && c.CanBoardRole(option == 0 ? CrewRole.Combat : CrewRole.Maintenance);
                if (GUI.Button(new Rect(350f, 370f + i * 71f, 580f, 60f), options[i], button))
                    Act(c, () => c.ResolveIslandOption(option));
                GUI.enabled = previous;
            }
        }

        internal static void DrawMarket(VoyageController c, VoyageUIArt art, GUIStyle text, bool finalPort = false)
        {
            var kinds = finalPort ? VoyageController.FinalPortUpgrades : VoyageController.MarketUpgrades;
            int[] runRanks = c.RunUpgrades;
            VoyageUpgradeKind[] available = finalPort ? kinds
                : Array.FindAll(kinds, kind => runRanks[(int)kind] < VoyageUpgrades.MaxRank);
            if (available.Length == 0)
            {
                Act(c, () => c.ResolveIslandOption(4));
                return;
            }
            var marketLabel = new GUIStyle(text)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 19,
                normal = { textColor = Paper }
            };
            var balance = new Rect(430f, 96f, 420f, 42f);
            Fill(balance, LabelBackground);
            string market = finalPort ? "终点港整备" : "海上集市";
            GUI.Label(balance, $"{market} · 本轮金币 {c.AvailableCoins}", marketLabel);
            var leave = new Rect(528f, 586f, 224f, 35f);
            Fill(leave, LabelBackground);
            if (GUI.Button(leave, finalPort ? "离港迎战" : "离开集市", marketLabel))
            {
                if (finalPort)
                    c.ContinueStoryVoyage();
                else
                    Act(c, () => c.ResolveIslandOption(4));
            }
            for (int i = 0; i < available.Length; i++)
            {
                VoyageUpgradeKind kind = available[i];
                int option = Array.IndexOf(kinds, kind);
                int rank = runRanks[(int)kind];
                long price = VoyageUpgrades.Price(kind, rank);
                bool previous = GUI.enabled;
                bool affordable = c.AvailableCoins >= price;
                GUI.enabled = previous && affordable;
                string purchase = affordable ? $"{price} 金币购买" : $"需 {price} 金币 · 不足";
                if (DrawCard(CardArea(i, available.Length, art.upgradeCard), art, kind, text, purchase))
                {
                    if (finalPort)
                        Act(c, () => c.BuyFinalPortUpgrade(kind));
                    else
                        Act(c, () => c.ResolveIslandOption(option));
                }
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
                GUI.enabled = previous && c.CanChoosePermanentUpgrade;
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

        private static bool DrawCard(Rect area, VoyageUIArt art, VoyageUpgradeKind kind, GUIStyle text, string purchase = null)
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
            bool market = purchase != null;
            GUI.Label(new Rect(area.x + area.width * 0.18f, area.y + area.height * (market ? 0.6f : 0.625f),
                area.width * 0.64f, area.height * (market ? 0.12f : 0.18f)), VoyageUpgrades.Description(kind), effect);
            if (market)
            {
                effect.fontStyle = FontStyle.Bold;
                effect.fontSize = 16;
                effect.normal.textColor = GUI.enabled ? Explored : new Color(0.49f, 0.17f, 0.10f);
                GUI.Label(new Rect(area.x + area.width * 0.18f, area.y + area.height * 0.725f,
                    area.width * 0.64f, area.height * 0.075f), purchase, effect);
            }
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
