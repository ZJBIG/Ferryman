using System;
using System.Collections.Generic;
using System.IO;

namespace Ferryman.Voyage
{
    public enum VoyageIslandKind
    {
        Departure,
        Battle,
        Elite,
        Rest,
        Shipyard,
        Market,
        Rescue,
        Treasure,
        Mystery,
        Destination
    }

    [Serializable]
    public sealed class VoyageRouteNode
    {
        public int id;
        public int depth;
        public int lane;
        public VoyageIslandKind kind;
        public int[] next;

        public string Name
        {
            get
            {
                switch (kind)
                {
                    case VoyageIslandKind.Departure: return "渡口";
                    case VoyageIslandKind.Battle: return "海盗据点";
                    case VoyageIslandKind.Elite: return "精英堡垒";
                    case VoyageIslandKind.Rest: return "宁静港湾";
                    case VoyageIslandKind.Shipyard: return "修船坞";
                    case VoyageIslandKind.Market: return "海上集市";
                    case VoyageIslandKind.Rescue: return "求救海岛";
                    case VoyageIslandKind.Treasure: return "藏宝岛";
                    case VoyageIslandKind.Mystery: return "迷雾岛";
                    case VoyageIslandKind.Destination: return "终点港";
                    default: return "未知岛屿";
                }
            }
        }

        public string Description
        {
            get
            {
                switch (kind)
                {
                    case VoyageIslandKind.Departure: return "准备出航，选择下一座岛屿。";
                    case VoyageIslandKind.Battle: return "击退海盗，获得航程奖励与升级。";
                    case VoyageIslandKind.Elite: return "挑战更强的敌人，争取更丰厚的奖励。";
                    case VoyageIslandKind.Rest: return "停靠休整，修复船只或领取补给金币。";
                    case VoyageIslandKind.Shipyard: return "获得一次免费的三选一强化。";
                    case VoyageIslandKind.Market: return "花费本轮金币购买补给与升级。";
                    case VoyageIslandKind.Rescue: return "营救受困乘客，为船员队伍增添帮手。";
                    case VoyageIslandKind.Treasure: return "寻找宝藏，获得金币与航程升级。";
                    case VoyageIslandKind.Mystery: return "遭遇未知事件，作出选择并承担结果。";
                    case VoyageIslandKind.Destination: return "完成最后一段航路，抵达终点并结算。";
                    default: return "探索岛屿。";
                }
            }
        }
    }

    public sealed class VoyageRouteMap
    {
        public const int FinalDepth = 7;

        public VoyageRouteNode[] Nodes { get; }

        private VoyageRouteMap(VoyageRouteNode[] nodes)
        {
            Nodes = nodes;
        }

        public static VoyageRouteMap Generate(int seed, IReadOnlyList<int> savedPath = null)
        {
            var random = new Random(seed);
            var directions = new int[FinalDepth, 2];
            for (int depth = 1; depth < FinalDepth - 1; depth++)
            {
                for (int lane = 0; lane < 2; lane++)
                    directions[depth, lane] = (depth + lane) % 2 == 0 ? 1 : -1;
            }
            // Keep a previously travelled diagonal when restoring older maps.
            for (int i = 1; savedPath != null && i < savedPath.Count; i++)
            {
                int from = savedPath[i - 1];
                int to = savedPath[i];
                if (from < 1 || from > 18 || to < 1 || to > 18)
                    continue;
                int depth = (from - 1) / 3 + 1;
                int lane = (from - 1) % 3;
                int nextLane = (to - 1) % 3;
                if ((to - 1) / 3 + 1 == depth + 1 && Math.Abs(nextLane - lane) == 1)
                    directions[depth, Math.Min(lane, nextLane)] = nextLane - lane;
            }
            var nodes = new VoyageRouteNode[20];
            nodes[0] = new VoyageRouteNode
            {
                id = 0, depth = 0, lane = 1, kind = VoyageIslandKind.Departure,
                next = new[] { 1, 2, 3 }
            };
            var activities = new[]
            {
                VoyageIslandKind.Rest, VoyageIslandKind.Shipyard, VoyageIslandKind.Market,
                VoyageIslandKind.Rescue, VoyageIslandKind.Treasure, VoyageIslandKind.Mystery
            };
            var alternatives = new[]
            {
                VoyageIslandKind.Battle, VoyageIslandKind.Treasure, VoyageIslandKind.Mystery
            };
            for (int depth = 1; depth < FinalDepth; depth++)
            {
                var kinds = new[]
                {
                    random.Next(2) == 0 || depth <= 2 ? VoyageIslandKind.Battle : VoyageIslandKind.Elite,
                    activities[depth - 1],
                    alternatives[(depth - 1) % alternatives.Length]
                };
                for (int i = kinds.Length - 1; i > 0; i--)
                {
                    int other = random.Next(i + 1);
                    VoyageIslandKind previous = kinds[i];
                    kinds[i] = kinds[other];
                    kinds[other] = previous;
                }

                for (int lane = 0; lane < 3; lane++)
                {
                    int id = 1 + (depth - 1) * 3 + lane;
                    var next = new List<int>();
                    if (depth == FinalDepth - 1)
                        next.Add(nodes.Length - 1);
                    else
                    {
                        for (int nextLane = Math.Max(0, lane - 1); nextLane <= Math.Min(2, lane + 1); nextLane++)
                        {
                            if (nextLane == lane || directions[depth, Math.Min(lane, nextLane)] == nextLane - lane)
                                next.Add(1 + depth * 3 + nextLane);
                        }
                    }
                    nodes[id] = new VoyageRouteNode
                    {
                        id = id, depth = depth, lane = lane, kind = kinds[lane], next = next.ToArray()
                    };
                }
            }
            nodes[19] = new VoyageRouteNode
            {
                id = 19, depth = FinalDepth, lane = 1, kind = VoyageIslandKind.Destination,
                next = Array.Empty<int>()
            };
            return new VoyageRouteMap(nodes);
        }

        public bool CanTravel(int from, int to)
        {
            if (from < 0 || from >= Nodes.Length || to < 0 || to >= Nodes.Length)
                return false;
            foreach (int destination in Nodes[from].next)
            {
                if (destination == to)
                    return true;
            }
            return false;
        }

        public bool IsExploredEdge(int from, int to, IReadOnlyList<int> path)
        {
            if (path == null || !CanTravel(from, to))
                return false;
            for (int i = 1; i < path.Count; i++)
            {
                if (path[i - 1] == from && path[i] == to)
                    return true;
            }
            return false;
        }

        public void ValidatePath(int[] path)
        {
            if (path == null || path.Length == 0 || path[0] != 0)
                throw new InvalidDataException("Voyage route must start at the departure island.");
            for (int i = 1; i < path.Length; i++)
            {
                if (!CanTravel(path[i - 1], path[i]))
                    throw new InvalidDataException("Voyage route contains an invalid island connection.");
            }
        }
    }
}
