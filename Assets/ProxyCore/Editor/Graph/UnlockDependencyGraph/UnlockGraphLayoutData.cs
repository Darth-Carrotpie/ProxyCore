using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProxyCore.Editor {
    /// <summary>
    /// Persists graph-only editor metadata: node positions, group assignments,
    /// group colours, and collapsed state. No runtime impact.
    /// Keyed by Unity asset GUID so entries survive renames.
    /// </summary>
    [CreateAssetMenu(fileName = "UnlockGraphLayoutData", menuName = "ProxyCore/Unlock Graph Layout Data")]
    public class UnlockGraphLayoutData : ScriptableObject {
        public List<NodeLayoutEntry> nodes = new List<NodeLayoutEntry>();
        public List<GroupLayoutEntry> groups = new List<GroupLayoutEntry>();

        // ────────────────────────────────────────────────────────────────────────
        // Node positions
        // ────────────────────────────────────────────────────────────────────────

        [Serializable]
        public class NodeLayoutEntry {
            public string assetGuid;
            public Vector2 position;
        }

        public NodeLayoutEntry GetNodeEntry(string guid) {
            foreach (var n in nodes)
                if (n.assetGuid == guid) return n;
            return null;
        }

        public void SetNodePosition(string guid, Vector2 pos) {
            var entry = GetNodeEntry(guid);
            if (entry == null) {
                entry = new NodeLayoutEntry { assetGuid = guid };
                nodes.Add(entry);
            }
            entry.position = pos;
        }

        public void RemoveNodeEntry(string guid) {
            nodes.RemoveAll(n => n.assetGuid == guid);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Group layout
        // ────────────────────────────────────────────────────────────────────────

        [Serializable]
        public class GroupLayoutEntry {
            public string groupId;
            public string groupName;
            public Color color = new Color(0.18f, 0.36f, 0.53f, 0.8f);
            public bool collapsed;
            public Rect rect;
            public List<string> memberGuids = new List<string>();
        }

        public GroupLayoutEntry GetGroupEntry(string groupId) {
            foreach (var g in groups)
                if (g.groupId == groupId) return g;
            return null;
        }

        public GroupLayoutEntry CreateGroup(string name, Color color) {
            var entry = new GroupLayoutEntry {
                groupId = System.Guid.NewGuid().ToString("N"),
                groupName = name,
                color = color,
            };
            groups.Add(entry);
            return entry;
        }

        public void RemoveGroup(string groupId) {
            groups.RemoveAll(g => g.groupId == groupId);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Definition type colours
        // ────────────────────────────────────────────────────────────────────────

        public List<DefinitionTypeColorEntry> definitionTypeColors = new List<DefinitionTypeColorEntry>();
        public List<DefinitionPassStrategyEntry> definitionPassStrategies = new List<DefinitionPassStrategyEntry>();

        [Serializable]
        public class DefinitionTypeColorEntry {
            public string typeName;
            public Color color;
        }

        [Serializable]
        public class DefinitionPassStrategyEntry {
            public string assetGuid;
            public string strategyId;
        }

        private static readonly Color DefaultDefinitionColor = new Color(45f / 255f, 100f / 255f, 160f / 255f, 0.85f);

        public Color GetDefinitionTypeColor(string typeName) {
            foreach (var entry in definitionTypeColors)
                if (entry.typeName == typeName) return entry.color;
            return DefaultDefinitionColor;
        }

        public bool HasDefinitionTypeColor(string typeName) {
            foreach (var entry in definitionTypeColors)
                if (entry.typeName == typeName) return true;
            return false;
        }

        public void SetDefinitionTypeColor(string typeName, Color color) {
            foreach (var entry in definitionTypeColors) {
                if (entry.typeName == typeName) {
                    entry.color = color;
                    return;
                }
            }
            definitionTypeColors.Add(new DefinitionTypeColorEntry {
                typeName = typeName,
                color = color,
            });
        }

        public string GetDefinitionPassStrategyId(string guid) {
            foreach (var entry in definitionPassStrategies)
                if (entry.assetGuid == guid) return entry.strategyId;
            return null;
        }

        public void SetDefinitionPassStrategyId(string guid, string strategyId) {
            foreach (var entry in definitionPassStrategies) {
                if (entry.assetGuid == guid) {
                    entry.strategyId = strategyId;
                    return;
                }
            }

            definitionPassStrategies.Add(new DefinitionPassStrategyEntry {
                assetGuid = guid,
                strategyId = strategyId,
            });
        }

        public void RemoveDefinitionPassStrategyId(string guid) {
            definitionPassStrategies.RemoveAll(e => e.assetGuid == guid);
        }
    }
}
