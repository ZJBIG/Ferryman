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
        private static VoyageRouteMap scrollMap;
        private static Vector2 mapScroll;
        private static int scrollNode = -1;
        private static bool draggingMap;

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
            const float mapWidth = 1850f;
            var viewport = new Rect(80f, 45f, 1120f, 555f);
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
            DrawMapPaper(art.parchment, mapWidth, viewport.height);
            foreach (var node in map.Nodes)
            {
                Vector2 start = Position(node);
                foreach (int nextId in node.next)
                {
                    foreach (var next in map.Nodes)
                    {
                        if (next.id != nextId)
                            continue;
                        bool walked = !preview && map.IsExploredEdge(node.id, next.id, c.RoutePath);
                        DrawCurve(start, Position(next), walked);
                        break;
                    }
                }
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
                GUI.Label(iconArea, new GUIContent(string.Empty, node.Name + " · " + node.Description), GUIStyle.none);
                if (reachable && GUI.Button(iconArea,
                    new GUIContent(string.Empty, node.Description), GUIStyle.none))
                    Act(c, () => c.SelectDestination(node.id));
            }
            GUI.EndScrollView();
            if (!string.IsNullOrEmpty(GUI.tooltip))
                GUI.Label(new Rect(180f, 602f, 920f, 32f), GUI.tooltip,
                    new GUIStyle(text) { fontSize = 16, alignment = TextAnchor.MiddleCenter, normal = { textColor = Ink } });
        }

        private static Vector2 Position(VoyageRouteNode node)
        {
            if (node.depth == 0)
                return new Vector2(140f, 345f);
            if (node.depth == VoyageRouteMap.FinalDepth)
                return new Vector2(1680f, 290f);
            return new Vector2(140f + node.depth * 220f + ((node.id * 37 + node.depth * 13) % 5 - 2) * 16f,
                130f + node.lane * 145f + (node.id * 53 % 5 - 2) * 13f);
        }

        private static void DrawMapPaper(Texture2D parchment, float width, float height)
        {
            if (parchment == null)
            {
                Fill(new Rect(0f, 0f, width, height), Paper);
                return;
            }
            // Preserve the two taped ends; the middle slice has matching top and bottom edges.
            const float first = 380f / 1690f;
            const float second = 790f / 1690f;
            float scale = height / parchment.height;
            float left = parchment.width * first * scale;
            float right = parchment.width * (1f - second) * scale;
            float middle = width - left - right;
            int tiles = Mathf.CeilToInt(middle / (parchment.width * (second - first) * scale));
            float tileWidth = middle / tiles;
            GUI.DrawTextureWithTexCoords(new Rect(0f, 0f, left, height), parchment, new Rect(0f, 0f, first, 1f));
            for (int i = 0; i < tiles; i++)
                DrawPaperSlice(parchment, new Rect(left + i * tileWidth - 24f, 0f, tileWidth + 24f, height),
                    new Rect(first, 0f, second - first, 1f));
            DrawPaperSlice(parchment, new Rect(width - right - 24f, 0f, right + 24f, height),
                new Rect(second, 0f, 1f - second, 1f));
        }

        private static void DrawPaperSlice(Texture2D parchment, Rect area, Rect uv)
        {
            Color previous = GUI.color;
            // Fade the overlapping edge so the original paper colours do not form a hard seam.
            for (int strip = 0; strip < 12; strip++)
            {
                GUI.color = new Color(previous.r, previous.g, previous.b, previous.a * (strip + 0.5f) / 12f);
                GUI.DrawTextureWithTexCoords(new Rect(area.x + strip * 2f, area.y, 2f, area.height), parchment,
                    new Rect(uv.x + uv.width * strip * 2f / area.width, uv.y, uv.width * 2f / area.width, uv.height));
            }
            GUI.color = previous;
            float fade = 24f / area.width;
            GUI.DrawTextureWithTexCoords(new Rect(area.x + 24f, area.y, area.width - 24f, area.height), parchment,
                new Rect(uv.x + uv.width * fade, uv.y, uv.width * (1f - fade), uv.height));
        }

        private static void DrawCurve(Vector2 start, Vector2 end, bool explored)
        {
            Vector2 direction = (end - start).normalized;
            start += direction * 34f;
            end -= direction * 34f;
            float bend = (end.x - start.x) * 0.45f;
            Vector2 first = start + Vector2.right * bend;
            Vector2 second = end - Vector2.right * bend;
            if (Mathf.Abs(start.y - end.y) < 0.01f)
            {
                first.y -= 18f;
                second.y -= 18f;
            }
            Vector2 previous = start;
            for (int segment = 1; segment <= 64; segment++)
            {
                float t = segment / 64f;
                float inverse = 1f - t;
                Vector2 point = inverse * inverse * inverse * start + 3f * inverse * inverse * t * first
                    + 3f * inverse * t * t * second + t * t * t * end;
                if (explored || ((segment - 1) / 4) % 2 == 0)
                    DrawLine(previous, point, explored ? Explored : new Color(Ink.r, Ink.g, Ink.b, 0.65f), explored ? 2f : 1.3f);
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
