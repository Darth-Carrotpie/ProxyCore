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

                    layoutData?.SetNodePosition(guid, pos);
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
                                layoutData?.SetNodePosition(condGuid, condPos);
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

        // ── Auto-layout (simple layered / Sugiyama-inspired) ─────────────

        /// <summary>
        /// Runs a basic topological-sort layout on all currently visible
        /// definition nodes. Useful for initial graph arrangement.
        /// </summary>
        public static void AutoLayout(UnlockGraphView graphView,
            UnlockGraphLayoutData layoutData) {
            // Collect all visible definition nodes
            var nodes = new List<DefinitionNode>();
            graphView.nodes.ForEach(n => {
                if (n is DefinitionNode dn && n.visible)
                    nodes.Add(dn);
            });

            if (nodes.Count == 0) return;

            // Build adjacency: who depends on whom
            var inDegree = new Dictionary<string, int>();
            var adj = new Dictionary<string, List<string>>();
            foreach (var n in nodes) {
                inDegree[n.AssetGuid] = 0;
                adj[n.AssetGuid] = new List<string>();
            }

            graphView.edges.ForEach(e => {
                if (e.output?.node is DefinitionNode src &&
                    e.input?.node is DefinitionNode tgt) {
                    adj[src.AssetGuid].Add(tgt.AssetGuid);
                    if (inDegree.ContainsKey(tgt.AssetGuid))
                        inDegree[tgt.AssetGuid]++;
                }
            });

            // Kahn's algorithm for topological sort → layers
            var queue = new Queue<string>();
            foreach (var kvp in inDegree)
                if (kvp.Value == 0) queue.Enqueue(kvp.Key);

            var layers = new List<List<string>>();
            var visited = new HashSet<string>();

            while (queue.Count > 0) {
                var currentLayer = new List<string>();
                int count = queue.Count;
                for (int i = 0; i < count; i++) {
                    var guid = queue.Dequeue();
                    if (!visited.Add(guid)) continue;
                    currentLayer.Add(guid);

                    foreach (var next in adj.GetValueOrDefault(guid, new List<string>())) {
                        inDegree[next]--;
                        if (inDegree[next] == 0)
                            queue.Enqueue(next);
                    }
                }
                if (currentLayer.Count > 0)
                    layers.Add(currentLayer);
            }

            // Place any remaining (cycles) in a final layer
            var remaining = nodes.Where(n => !visited.Contains(n.AssetGuid))
                .Select(n => n.AssetGuid).ToList();
            if (remaining.Count > 0)
                layers.Add(remaining);

            // Position: layers go left-to-right, nodes top-to-bottom
            const float layerSpacing = 320f;
            const float nodeSpacing = 120f;

            for (int layer = 0; layer < layers.Count; layer++) {
                for (int idx = 0; idx < layers[layer].Count; idx++) {
                    var guid = layers[layer][idx];
                    var node = graphView.FindDefinitionNode(guid);
                    if (node == null) continue;

                    var pos = new Vector2(layer * layerSpacing, idx * nodeSpacing);
                    node.SetPosition(new Rect(pos, Vector2.zero));
                    layoutData?.SetNodePosition(guid, pos);
                }
            }

            if (layoutData != null)
                EditorUtility.SetDirty(layoutData);
        }
    }
}
