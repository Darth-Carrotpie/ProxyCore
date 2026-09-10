using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProxyCore.Editor.Graph;

namespace ProxyCore.Editor.Tests {
    /// <summary>
    /// Covers the shape auto-layout gives a graph: a node must sit right of everything
    /// that unlocks it, unrelated trees must not interleave, and a dependency cycle must
    /// degrade the arrangement rather than lose nodes.
    /// </summary>
    [TestFixture]
    public class UnlockGraphAutoLayoutTests {
        /// <summary>Builds the adjacency pair the layout helpers take, from "a>b" edges.</summary>
        private static (Dictionary<string, List<string>> outgoing,
                        Dictionary<string, List<string>> incoming)
            Graph(IEnumerable<string> nodes, params string[] edges) {
            var outgoing = nodes.ToDictionary(n => n, _ => new List<string>());
            var incoming = outgoing.Keys.ToDictionary(n => n, _ => new List<string>());

            foreach (var edge in edges) {
                var parts = edge.Split('>');
                outgoing[parts[0]].Add(parts[1]);
                incoming[parts[1]].Add(parts[0]);
            }

            return (outgoing, incoming);
        }

        /// <summary>Seeds a column layout from "a,b" strings, one per column.</summary>
        private static List<List<string>> Columns(params string[] columns) =>
            columns.Select(c => c.Split(',').ToList()).ToList();

        private static int ColumnOf(List<List<string>> columns, string node) =>
            columns.FindIndex(c => c.Contains(node));

        [Test]
        public void AssignColumns_PlacesNodeRightOfItsFurthestPrerequisite() {
            // a feeds b and c, both feed d, and a also feeds d directly. The direct a>d
            // edge must not drag d left into column 1 on top of b and c.
            var nodes = new List<string> { "a", "b", "c", "d" };
            var (outgoing, incoming) = Graph(nodes, "a>b", "a>c", "b>d", "c>d", "a>d");

            var columns = UnlockGraphBuilder.AssignColumns(nodes, outgoing, incoming);

            Assert.AreEqual(3, columns.Count);
            Assert.AreEqual(0, ColumnOf(columns, "a"));
            Assert.AreEqual(1, ColumnOf(columns, "b"));
            Assert.AreEqual(1, ColumnOf(columns, "c"));
            Assert.AreEqual(2, ColumnOf(columns, "d"));
        }

        [Test]
        public void AssignColumns_KeepsCycleMembers() {
            var nodes = new List<string> { "a", "b", "c" };
            var (outgoing, incoming) = Graph(nodes, "a>b", "b>c", "c>b");

            var columns = UnlockGraphBuilder.AssignColumns(nodes, outgoing, incoming);

            CollectionAssert.AreEquivalent(nodes, columns.SelectMany(c => c).ToList());
        }

        [Test]
        public void AssignColumns_LeavesUnconnectedNodesInOneColumn() {
            var nodes = new List<string> { "a", "b", "c" };
            var (outgoing, incoming) = Graph(nodes);

            var columns = UnlockGraphBuilder.AssignColumns(nodes, outgoing, incoming);

            Assert.AreEqual(1, columns.Count);
            Assert.AreEqual(3, columns[0].Count);
        }

        [Test]
        public void OrderRows_RemovesCrossingsFromATangledSeed() {
            var nodes = new List<string> { "r1", "r2", "m1", "m2", "t1", "t2" };
            var (outgoing, incoming) = Graph(nodes, "r1>m2", "r2>m1", "m1>t1", "m2>t2");
            var columns = Columns("r1,r2", "m1,m2", "t1,t2");

            Assert.Greater(UnlockGraphBuilder.CountCrossings(columns, outgoing), 0,
                "seed should start tangled, otherwise this proves nothing");

            UnlockGraphBuilder.OrderRows(columns, outgoing, incoming);

            Assert.AreEqual(0, UnlockGraphBuilder.CountCrossings(columns, outgoing));
        }

        [Test]
        public void OrderRows_LeavesATidySeedAlone() {
            var nodes = new List<string> { "a", "b", "c", "d" };
            var (outgoing, incoming) = Graph(nodes, "a>c", "b>d");
            var columns = Columns("a,b", "c,d");

            UnlockGraphBuilder.OrderRows(columns, outgoing, incoming);

            Assert.AreEqual(0, UnlockGraphBuilder.CountCrossings(columns, outgoing));
            CollectionAssert.AreEqual(new[] { "a", "b" }, columns[0]);
        }

        [Test]
        public void OrderRows_KeepsAnIsolatedNodeWhereItWasSeeded() {
            // A node with no edges has no barycentre. Treating that as "infinitely far
            // down" sank every such node to the bottom of its column.
            var nodes = new List<string> { "x", "a", "b" };
            var (outgoing, incoming) = Graph(nodes, "a>b");
            var columns = Columns("x,a", "b");

            UnlockGraphBuilder.OrderRows(columns, outgoing, incoming);

            Assert.AreEqual("x", columns[0][0]);
        }

        [Test]
        public void FindClusters_SeparatesUnrelatedTreesBiggestFirst() {
            var nodes = new List<string> { "a", "b", "c", "x", "y" };
            var (outgoing, incoming) = Graph(nodes, "a>b", "b>c", "x>y");

            var clusters = UnlockGraphBuilder.FindClusters(nodes, outgoing, incoming);

            Assert.AreEqual(2, clusters.Count);
            CollectionAssert.AreEquivalent(new[] { "a", "b", "c" }, clusters[0]);
            CollectionAssert.AreEquivalent(new[] { "x", "y" }, clusters[1]);
        }

        [Test]
        public void FindClusters_FollowsEdgesBackwardsToo() {
            // Reached only by walking an incoming edge: "b" is the seed's predecessor.
            var nodes = new List<string> { "a", "b" };
            var (outgoing, incoming) = Graph(nodes, "b>a");

            var clusters = UnlockGraphBuilder.FindClusters(nodes, outgoing, incoming);

            Assert.AreEqual(1, clusters.Count);
        }
    }
}
