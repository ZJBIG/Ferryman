using System.Collections.Generic;
using System.IO;
using System.Linq;
using Ferryman.Voyage;
using NUnit.Framework;

namespace Ferryman.Tests
{
    public sealed class VoyageRouteMapTests
    {
        [Test]
        public void SeedRecreatesTheSameRouteAndEachRowKeepsItsActivity()
        {
            VoyageRouteMap first = VoyageRouteMap.Generate(7301);
            VoyageRouteMap restored = VoyageRouteMap.Generate(7301);
            var activities = new[]
            {
                VoyageIslandKind.Rest, VoyageIslandKind.Shipyard, VoyageIslandKind.Market,
                VoyageIslandKind.Rescue, VoyageIslandKind.Treasure, VoyageIslandKind.Mystery
            };
            for (int i = 0; i < first.Nodes.Length; i++)
            {
                Assert.That(restored.Nodes[i].id, Is.EqualTo(first.Nodes[i].id));
                Assert.That(restored.Nodes[i].depth, Is.EqualTo(first.Nodes[i].depth));
                Assert.That(restored.Nodes[i].lane, Is.EqualTo(first.Nodes[i].lane));
                Assert.That(restored.Nodes[i].kind, Is.EqualTo(first.Nodes[i].kind));
                Assert.That(restored.Nodes[i].next, Is.EqualTo(first.Nodes[i].next));
            }
            for (int depth = 1; depth < VoyageRouteMap.FinalDepth; depth++)
            {
                VoyageRouteNode[] row = first.Nodes.Where(node => node.depth == depth).ToArray();
                Assert.That(row.Length, Is.EqualTo(3));
                Assert.That(row.Any(node => node.kind == activities[depth - 1]), Is.True);
                Assert.That(row.Any(node => node.kind == VoyageIslandKind.Battle || node.kind == VoyageIslandKind.Elite), Is.True);
            }
        }

        [TestCase(0)]
        [TestCase(7301)]
        [TestCase(-1)]
        public void EveryIslandIsReachableAndEveryConnectionAdvancesOneDepth(int seed)
        {
            VoyageRouteMap map = VoyageRouteMap.Generate(seed);
            Assert.That(map.Nodes.Length, Is.EqualTo(20));
            Assert.That(map.Nodes[0].kind, Is.EqualTo(VoyageIslandKind.Departure));
            Assert.That(map.Nodes[19].kind, Is.EqualTo(VoyageIslandKind.Destination));
            Assert.That(map.Nodes[19].depth, Is.EqualTo(VoyageRouteMap.FinalDepth));
            Assert.That(map.Nodes[19].next, Is.Empty);
            var reached = new HashSet<int> { 0 };
            var pending = new Queue<int>();
            pending.Enqueue(0);
            while (pending.Count > 0)
            {
                int id = pending.Dequeue();
                VoyageRouteNode node = map.Nodes[id];
                Assert.That(node.id, Is.EqualTo(id));
                if (node.depth < VoyageRouteMap.FinalDepth - 1)
                    Assert.That(node.next.Length, Is.GreaterThanOrEqualTo(1));
                else if (node.depth == VoyageRouteMap.FinalDepth - 1)
                    Assert.That(node.next, Is.EqualTo(new[] { 19 }));
                foreach (int destination in node.next)
                {
                    VoyageRouteNode next = map.Nodes[destination];
                    Assert.That(next.depth, Is.EqualTo(node.depth + 1));
                    if (node.depth > 0 && next.depth < VoyageRouteMap.FinalDepth)
                        Assert.That(System.Math.Abs(next.lane - node.lane), Is.LessThanOrEqualTo(1));
                    if (reached.Add(destination))
                        pending.Enqueue(destination);
                }
            }
            Assert.That(reached.Count, Is.EqualTo(map.Nodes.Length));
        }

        [Test]
        public void PathValidationAcceptsProgressAndRejectsInvalidConnections()
        {
            VoyageRouteMap map = VoyageRouteMap.Generate(12);
            Assert.DoesNotThrow(() => map.ValidatePath(new[] { 0 }));
            Assert.DoesNotThrow(() => map.ValidatePath(new[] { 0, 1, 4, 7 }));
            Assert.DoesNotThrow(() => map.ValidatePath(new[] { 0, 1, 4, 7, 10, 13, 16, 19 }));
            var invalidPaths = new[]
            {
                new int[0], new[] { 1 }, new[] { -1 }, new[] { 0, -1 }, new[] { 0, 20 },
                new[] { 0, 19 }, new[] { 0, 1, 7 }, new[] { 0, 1, 6 },
                new[] { 0, 1, 0 }, new[] { 0, 1, 1 },
                new[] { 0, 1, 4, 7, 10, 13, 16, 19, 19 }
            };
            Assert.Throws<InvalidDataException>(() => map.ValidatePath(null));
            foreach (int[] path in invalidPaths)
                Assert.Throws<InvalidDataException>(() => map.ValidatePath(path));
            Assert.That(map.CanTravel(-1, 1), Is.False);
            Assert.That(map.CanTravel(0, 20), Is.False);
            Assert.That(map.CanTravel(20, 0), Is.False);
            Assert.That(map.CanTravel(19, 16), Is.False);
        }

        [Test]
        public void ExploredConnectionsAreOnlyConsecutiveStepsActuallyTravelled()
        {
            VoyageRouteMap map = VoyageRouteMap.Generate(12);
            var path = new[] { 0, 2, 4, 7 };
            Assert.That(map.IsExploredEdge(0, 2, path), Is.True);
            Assert.That(map.IsExploredEdge(2, 4, path), Is.True);
            Assert.That(map.IsExploredEdge(4, 7, path), Is.True);
            Assert.That(map.IsExploredEdge(0, 1, path), Is.False);
            Assert.That(map.IsExploredEdge(7, 10, path), Is.False);
            Assert.That(map.IsExploredEdge(2, 7, path), Is.False);
            Assert.That(map.IsExploredEdge(4, 2, path), Is.False);
            Assert.That(map.IsExploredEdge(0, 2, null), Is.False);
            Assert.That(map.IsExploredEdge(0, 2, new int[0]), Is.False);
            Assert.That(map.IsExploredEdge(0, 19, new[] { 0, 19 }), Is.False);
        }
    }
}
