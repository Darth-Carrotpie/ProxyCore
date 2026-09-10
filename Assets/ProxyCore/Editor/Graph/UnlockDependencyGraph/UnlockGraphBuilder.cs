using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Builds the visual graph from existing ScriptableObject data.
    /// Reads registries via <see cref="IUnlockableCatalog"/>, iterates
    /// prerequisites, and creates nodes + edges in the
    /// <see cref="UnlockGraphView"/>.
    /// </summary>
    public static class UnlockGraphBuilder {
        /// <summary>
        /// Rebuilds the entire graph from the current asset database state.
        /// </summary>
        public static void Build(UnlockGraphView graphView,
            UnlockGraphLayoutData layoutData,
            List<ScriptableObject> registries) {
            graphView.ClearGraph();
            graphView.SetLayoutData(layoutData);

            if (registries == null || registries.Count == 0) return;

            // ── Step 1: Collect all definitions from selected registries ──
            var allDefs = new List<BaseDefinition>();
            foreach (var reg in registries) {
                if (reg is IUnlockableCatalog catalog) {
                    var defs = catalog.GetCatalogDefinitions();
                    if (defs != null) allDefs.AddRange(defs);
                }
            }

            // Deduplicate (a definition could be in multiple registries in theory)
            var uniqueDefs = allDefs
                .Where(d => d != null && d is IUnlockable)
                .GroupBy(d => d.GetInstanceID())
                .Select(g => g.First())
                .ToList();

            // ── Step 2: Create definition nodes ──────────────────────────
            var defGuidMap = new Dictionary<BaseDefinition, string>();
            int autoX = 0;
            int autoY = 0;
            const float refreshInsertDefGapX = 360f;
            const float refreshInsertDefGapY = 160f;
            const float refreshInsertCondOffsetX = -280f;
            const float refreshInsertCondGapY = 70f;

            float refreshInsertBaseX = 0f;
            float refreshInsertBaseY = 0f;

            if (layoutData != null) {
                bool foundSaved = false;
                float maxSavedX = float.MinValue;
                float minSavedY = float.MaxValue;

                foreach (var def in uniqueDefs) {
                    string savedPath = AssetDatabase.GetAssetPath(def);
                    string savedGuid = AssetDatabase.AssetPathToGUID(savedPath);
                    if (string.IsNullOrEmpty(savedGuid)) continue;

                    var savedEntry = layoutData.GetNodeEntry(savedGuid);
                    if (savedEntry == null) continue;

                    foundSaved = true;
                    if (savedEntry.position.x > maxSavedX) maxSavedX = savedEntry.position.x;
                    if (savedEntry.position.y < minSavedY) minSavedY = savedEntry.position.y;
                }

                if (foundSaved) {
                    refreshInsertBaseX = maxSavedX + refreshInsertDefGapX;
                    refreshInsertBaseY = minSavedY;
                }
            }

            foreach (var def in uniqueDefs) {
                string path = AssetDatabase.GetAssetPath(def);
                string guid = AssetDatabase.AssetPathToGUID(path);
                if (string.IsNullOrEmpty(guid)) continue;

                defGuidMap[def] = guid;

                // Determine position — use saved layout or auto-position
                Vector2 pos;
                var savedNode = layoutData?.GetNodeEntry(guid);
                if (savedNode != null) {
                    pos = savedNode.position;
                }
                else {
                    pos = new Vector2(
                        refreshInsertBaseX + autoX * refreshInsertDefGapX,
                        refreshInsertBaseY + autoY * refreshInsertDefGapY);
                    autoX++;
                    if (autoX > 4) { autoX = 0; autoY++; }

                    // Marked auto-placed so "Import New Definitions" can pull it to the
                    // user instead of leaving it stranded off the right edge.
                    layoutData?.SetNodePosition(guid, pos, autoPlaced: true);
                }

                graphView.AddDefinitionNode(def, guid, pos);
            }

            // ── Step 3: Create condition nodes + edges ───────────────────
            // Direct-edge conditions are resolved by registered strategies.
            // Other conditions are rendered as standalone condition nodes.
            var conditionNodeGuids = new HashSet<string>();

            foreach (var def in uniqueDefs) {
                if (def is not IHasPrerequisites hasPrereqs) continue;

                var prereqs = hasPrereqs.Prerequisites;
                if (prereqs == null) continue;

                string targetGuid = defGuidMap.GetValueOrDefault(def);
                var targetNode = graphView.FindDefinitionNode(targetGuid);
                if (targetNode == null) continue;

                foreach (var condition in prereqs) {
                    if (condition == null) continue;

                    if (DefinitionEdgeStrategyRegistry.TryGetDirectEdgeSource(condition, out var sourceDef)) {
                        // Direct edge: source definition → target definition
                        if (sourceDef != null && defGuidMap.TryGetValue(sourceDef, out string sourceGuid)) {
                            var sourceNode = graphView.FindDefinitionNode(sourceGuid);
                            if (sourceNode != null) {
                                graphView.AddEdge(sourceNode.OutputPort, targetNode.InputPort);
                            }
                        }
                    }
                    else {
                        // Non-trivial condition → create ConditionNode
                        string condPath = AssetDatabase.GetAssetPath(condition);
                        string condGuid = AssetDatabase.AssetPathToGUID(condPath);
                        if (string.IsNullOrEmpty(condGuid)) continue;

                        ConditionNode condNode;
                        if (!conditionNodeGuids.Contains(condGuid)) {
                            Vector2 condPos;
                            var savedCond = layoutData?.GetNodeEntry(condGuid);
                            if (savedCond != null) {
                                condPos = savedCond.position;
                            }
                            else {
                                // Place condition nodes slightly to the left of the target
                                var targetPos = targetNode.GetPosition().position;
                                condPos = targetPos + new Vector2(
                                    refreshInsertCondOffsetX,
                                    refreshInsertCondGapY * conditionNodeGuids.Count);
                                layoutData?.SetNodePosition(condGuid, condPos, autoPlaced: true);
                            }

                            condNode = graphView.AddConditionNode(condition, condGuid, condPos);
                            conditionNodeGuids.Add(condGuid);
                        }
                        else {
                            condNode = graphView.FindConditionNode(condGuid);
                        }

                        if (condNode != null) {
                            graphView.AddEdge(condNode.OutputPort, targetNode.InputPort);
                        }
                    }
                }
            }

            // ── Step 4: Recreate groups from layout data ─────────────────
            if (layoutData != null) {
                foreach (var groupEntry in layoutData.groups) {
                    var memberNodes = new List<Node>();
                    foreach (var memberGuid in groupEntry.memberGuids) {
                        var dn = graphView.FindDefinitionNode(memberGuid);
                        if (dn != null) { memberNodes.Add(dn); continue; }
                        // Try by NodeId first, then by AssetGuid
                        var cn = graphView.FindConditionNodeById(memberGuid)
                              ?? graphView.FindConditionNode(memberGuid);
                        if (cn != null) memberNodes.Add(cn);
                    }

                    if (memberNodes.Count == 0) continue;

                    var group = graphView.CreateGroupFromNodes(
                        groupEntry.groupName, groupEntry.color,
                        memberNodes, groupEntry.groupId);

                    // If the group was collapsed, collapse it now
                    if (groupEntry.collapsed && group != null) {
                        graphView.CollapseGroup(group);
                    }
                }

                EditorUtility.SetDirty(layoutData);
            }
        }

        // ── Auto-layout ─────────────────────────────────────

        private const float LayerGapX = 90f;      // gap between two columns
        private const float NodeGapY = 40f;       // gap between two nodes in a column
        private const float ClusterGapY = 140f;   // gap between unrelated clusters
        private const float FallbackNodeWidth = 220f;
        private const float FallbackNodeHeight = 130f;
        private const int OrderingSweeps = 6;     // alternating barycentre passes

        /// <summary>
        /// Arranges the selected nodes — or every visible node when nothing is
        /// selected — into layered columns, one independent cluster under the next.
        ///
        /// Layering is longest-path so an edge always points forward; nodes inside a
        /// column are ordered by the average row of what feeds them, which keeps edges
        /// from crossing. Columns are sized from the real node rects, so nodes never
        /// land on top of each other the way a fixed row pitch does.
        ///
        /// Positions go through <see cref="Undo"/> and the asset is left un-dirtied:
        /// the arrangement lives in memory until the user saves, and CTRL+Z puts the
        /// old positions back.
        /// </summary>
        public static void AutoLayout(UnlockGraphView graphView,
            UnlockGraphLayoutData layoutData) {
            var targets = CollectLayoutTargets(graphView);
            if (targets.Count == 0) return;

            // Anchor on what is being arranged, so laying out a selection inside a big
            // graph keeps it where the user is looking instead of flinging it to origin.
            Vector2 origin = TopLeftOf(targets);

            var targetSet = new HashSet<Node>(targets);
            var outgoing = new Dictionary<Node, List<Node>>();
            var incoming = new Dictionary<Node, List<Node>>();
            foreach (var n in targets) {
                outgoing[n] = new List<Node>();
                incoming[n] = new List<Node>();
            }

            graphView.edges.ForEach(e => {
                var src = e.output?.node;
                var dst = e.input?.node;
                if (src == null || dst == null || src == dst) return;
                if (!targetSet.Contains(src) || !targetSet.Contains(dst)) return;
                if (outgoing[src].Contains(dst)) return;
                outgoing[src].Add(dst);
                incoming[dst].Add(src);
            });

            if (layoutData != null)
                Undo.RegisterCompleteObjectUndo(layoutData, "Auto-layout nodes");

            float clusterTop = origin.y;
            foreach (var cluster in FindClusters(targets, outgoing, incoming)) {
                float height = LayoutCluster(cluster, outgoing, incoming, layoutData,
                    new Vector2(origin.x, clusterTop));
                clusterTop += height + ClusterGapY;
            }

            // Deliberately no SetDirty: an arrangement the user has not accepted yet
            // must not ride along with the next save. The window flags itself dirty so
            // the Save button stays the thing that commits it.
            graphView.NotifyGraphChanged();
        }

        /// <summary>Selected definition/condition nodes, or every visible one when nothing is selected.</summary>
        private static List<Node> CollectLayoutTargets(UnlockGraphView graphView) {
            var selected = graphView.selection
                .OfType<Node>()
                .Where(IsLayoutable)
                .ToList();

            if (selected.Count > 0) return selected;

            var all = new List<Node>();
            graphView.nodes.ForEach(n => {
                if (IsLayoutable(n)) all.Add(n);
            });
            return all;
        }

        private static bool IsLayoutable(Node n) =>
            n is DefinitionNode or ConditionNode && n.visible;

        /// <summary>
        /// Splits the targets into weakly-connected clusters, so unrelated dependency
        /// trees get their own band with a gap instead of interleaving.
        /// </summary>
        public static List<List<T>> FindClusters<T>(List<T> targets,
            Dictionary<T, List<T>> outgoing,
            Dictionary<T, List<T>> incoming) {
            var clusters = new List<List<T>>();
            var seen = new HashSet<T>();

            foreach (var root in targets) {
                if (!seen.Add(root)) continue;

                var cluster = new List<T> { root };
                var frontier = new Queue<T>();
                frontier.Enqueue(root);

                while (frontier.Count > 0) {
                    var current = frontier.Dequeue();
                    foreach (var neighbour in outgoing[current].Concat(incoming[current])) {
                        if (!seen.Add(neighbour)) continue;
                        cluster.Add(neighbour);
                        frontier.Enqueue(neighbour);
                    }
                }

                clusters.Add(cluster);
            }

            // Biggest tree first — the one the user most likely came to look at.
            // OrderByDescending is stable, so equal-sized clusters keep the order they
            // were discovered in rather than swapping bands between runs.
            return clusters.OrderByDescending(c => c.Count).ToList();
        }

        /// <summary>Places one cluster with its top-left at <paramref name="origin"/>; returns its height.</summary>
        private static float LayoutCluster(List<Node> cluster,
            Dictionary<Node, List<Node>> outgoing,
            Dictionary<Node, List<Node>> incoming,
            UnlockGraphLayoutData layoutData,
            Vector2 origin) {
            var columns = AssignColumns(cluster, outgoing, incoming);

            // Seed row order from where the nodes already sit, so the arrangement stays
            // recognisable, then let connectivity decide the final order.
            for (int c = 0; c < columns.Count; c++)
                columns[c] = columns[c].OrderBy(n => n.GetPosition().position.y).ToList();

            OrderRows(columns, outgoing, incoming);

            // Column heights first, so shorter columns can be centred against the tallest.
            var columnHeights = columns
                .Select(c => c.Sum(n => SizeOf(n).y) + NodeGapY * Mathf.Max(0, c.Count - 1))
                .ToList();
            float tallest = columnHeights.Count > 0 ? columnHeights.Max() : 0f;

            float x = origin.x;
            for (int c = 0; c < columns.Count; c++) {
                float widest = columns[c].Max(n => SizeOf(n).x);
                float y = origin.y + (tallest - columnHeights[c]) * 0.5f;

                foreach (var node in columns[c]) {
                    var size = SizeOf(node);
                    // Centre each node in its column so ports line up down the column.
                    var pos = new Vector2(x + (widest - size.x) * 0.5f, y);
                    node.SetPosition(new Rect(pos, Vector2.zero));

                    string key = LayoutKeyOf(node);
                    if (key != null) layoutData?.SetNodePosition(key, pos);

                    y += size.y + NodeGapY;
                }

                x += widest + LayerGapX;
            }

            return tallest;
        }

        /// <summary>
        /// Longest-path layering: every node sits one column right of its furthest
        /// predecessor. Nodes left over by a dependency cycle are appended rather than
        /// dropped, so a cycle degrades the layout instead of losing nodes.
        /// </summary>
        public static List<List<T>> AssignColumns<T>(List<T> cluster,
            Dictionary<T, List<T>> outgoing,
            Dictionary<T, List<T>> incoming) {
            var remainingInDegree = cluster.ToDictionary(n => n, n => incoming[n].Count);
            var column = cluster.ToDictionary(n => n, _ => 0);

            var ready = new Queue<T>(cluster.Where(n => remainingInDegree[n] == 0));
            var settled = new HashSet<T>();

            while (ready.Count > 0) {
                var node = ready.Dequeue();
                if (!settled.Add(node)) continue;

                foreach (var next in outgoing[node]) {
                    column[next] = Mathf.Max(column[next], column[node] + 1);
                    if (--remainingInDegree[next] == 0)
                        ready.Enqueue(next);
                }
            }

            foreach (var node in cluster) {
                if (settled.Contains(node)) continue;
                // Cycle member: park it right of whatever already-resolved node feeds it.
                int after = incoming[node]
                    .Where(settled.Contains)
                    .Select(p => column[p] + 1)
                    .DefaultIfEmpty(0)
                    .Max();
                column[node] = Mathf.Max(column[node], after);
            }

            int columnCount = column.Values.Max() + 1;
            var columns = new List<List<T>>(columnCount);
            for (int i = 0; i < columnCount; i++) columns.Add(new List<T>());
            foreach (var node in cluster) columns[column[node]].Add(node);

            return columns.Where(c => c.Count > 0).ToList();
        }

        /// <summary>
        /// Orders the nodes inside each column so edges run as straight as possible.
        ///
        /// Sweeps alternate direction: on a left-to-right sweep a node is pulled towards
        /// the average row of what feeds it, on a right-to-left sweep towards the average
        /// row of what it feeds. One direction alone leaves half the graph unconsidered
        /// — notably the first column, whose nodes have nothing feeding them and would
        /// otherwise keep whatever arbitrary order they arrived in, scattering the trees
        /// hanging off them. Each sweep is kept only if it actually reduces crossings, so
        /// this can improve the seed order but never degrade it.
        /// </summary>
        public static void OrderRows<T>(List<List<T>> columns,
            Dictionary<T, List<T>> outgoing,
            Dictionary<T, List<T>> incoming) {
            var best = Clone(columns);
            int bestCrossings = CountCrossings(best, outgoing);

            for (int sweep = 0; sweep < OrderingSweeps && bestCrossings > 0; sweep++) {
                bool leftToRight = sweep % 2 == 0;
                SweepByBarycentre(columns, leftToRight ? incoming : outgoing, leftToRight);

                int crossings = CountCrossings(columns, outgoing);
                if (crossings >= bestCrossings) continue;

                bestCrossings = crossings;
                best = Clone(columns);
            }

            for (int c = 0; c < columns.Count; c++)
                columns[c] = best[c];
        }

        /// <summary>
        /// One pass over the columns, sorting each by the average row of its neighbours in
        /// the columns already visited this pass. Nodes with no such neighbour keep their
        /// current row, so isolated nodes stay put instead of sinking to the bottom.
        /// </summary>
        private static void SweepByBarycentre<T>(List<List<T>> columns,
            Dictionary<T, List<T>> neighbours, bool leftToRight) {
            var rowOf = new Dictionary<T, float>();
            foreach (var column in columns)
                for (int i = 0; i < column.Count; i++)
                    rowOf[column[i]] = i;

            for (int step = 0; step < columns.Count; step++) {
                int c = leftToRight ? step : columns.Count - 1 - step;

                // OrderBy is stable, so nodes that tie keep the order they came in with.
                columns[c] = columns[c]
                    .OrderBy(n => Barycentre(n, neighbours, rowOf))
                    .ToList();

                for (int i = 0; i < columns[c].Count; i++)
                    rowOf[columns[c][i]] = i;
            }
        }

        private static float Barycentre<T>(T node,
            Dictionary<T, List<T>> neighbours,
            Dictionary<T, float> rowOf) {
            float total = 0f;
            int count = 0;
            foreach (var neighbour in neighbours[node]) {
                if (!rowOf.TryGetValue(neighbour, out float row)) continue;
                total += row;
                count++;
            }

            return count == 0 ? rowOf[node] : total / count;
        }

        /// <summary>
        /// Edge pairs that cross: two edges spanning the same two columns cross when their
        /// endpoints are in opposite row order.
        /// </summary>
        // ponytail: counts only edges whose endpoints share a column pair, since the layout
        // has no dummy nodes for edges spanning three or more columns. Add dummy nodes if
        // long edges start looking tangled.
        public static int CountCrossings<T>(List<List<T>> columns,
            Dictionary<T, List<T>> outgoing) {
            var columnOf = new Dictionary<T, int>();
            var rowOf = new Dictionary<T, int>();
            for (int c = 0; c < columns.Count; c++) {
                for (int i = 0; i < columns[c].Count; i++) {
                    columnOf[columns[c][i]] = c;
                    rowOf[columns[c][i]] = i;
                }
            }

            var edges = new List<(T src, T dst)>();
            foreach (var column in columns)
                foreach (var node in column)
                    foreach (var target in outgoing[node])
                        if (columnOf.ContainsKey(target))
                            edges.Add((node, target));

            int crossings = 0;
            for (int a = 0; a < edges.Count; a++) {
                for (int b = a + 1; b < edges.Count; b++) {
                    if (columnOf[edges[a].src] != columnOf[edges[b].src]) continue;
                    if (columnOf[edges[a].dst] != columnOf[edges[b].dst]) continue;

                    int bySource = rowOf[edges[a].src].CompareTo(rowOf[edges[b].src]);
                    int byTarget = rowOf[edges[a].dst].CompareTo(rowOf[edges[b].dst]);
                    if (bySource * byTarget < 0) crossings++;
                }
            }

            return crossings;
        }

        private static List<List<T>> Clone<T>(List<List<T>> columns) =>
            columns.Select(c => new List<T>(c)).ToList();

        /// <summary>Real on-screen node size, falling back to a typical one before layout has resolved.</summary>
        private static Vector2 SizeOf(Node node) {
            var rect = node.GetPosition();
            float w = float.IsNaN(rect.width) || rect.width < 1f ? FallbackNodeWidth : rect.width;
            float h = float.IsNaN(rect.height) || rect.height < 1f ? FallbackNodeHeight : rect.height;
            return new Vector2(w, h);
        }

        private static Vector2 TopLeftOf(List<Node> nodes) {
            float minX = float.MaxValue, minY = float.MaxValue;
            foreach (var n in nodes) {
                var p = n.GetPosition().position;
                if (float.IsNaN(p.x) || float.IsNaN(p.y)) continue;
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
            }
            return minX == float.MaxValue ? Vector2.zero : new Vector2(minX, minY);
        }

        internal static string LayoutKeyOf(Node node) => node switch {
            DefinitionNode dn => dn.AssetGuid,
            ConditionNode cn => cn.NodeId,
            _ => null,
        };
    }
}
