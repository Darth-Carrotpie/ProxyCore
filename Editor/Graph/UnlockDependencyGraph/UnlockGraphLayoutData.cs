using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProxyCore.Editor {
    /// <summary>
    /// One unlock graph: node positions, group assignments, group colours, collapsed
    /// state, which registries it shows, and the save slots it maps to at runtime.
    /// Editor-only metadata — no runtime impact. Keyed by Unity asset GUID so entries
    /// survive renames.
    ///
    /// A project may hold several of these; each one is a separate graph (typically one
    /// per game level), picked from the Unlock Dependency Graph window's Graph dropdown.
    /// </summary>
    [CreateAssetMenu(fileName = "UnlockGraphLayoutData", menuName = "ProxyCore/Unlock Graph Layout Data")]
    public class UnlockGraphLayoutData : ScriptableObject {
        public List<NodeLayoutEntry> nodes = new List<NodeLayoutEntry>();
        public List<GroupLayoutEntry> groups = new List<GroupLayoutEntry>();

        // ────────────────────────────────────────────────────────────────────────
        // Graph identity & save slots
        // ────────────────────────────────────────────────────────────────────────

        [Tooltip("Profile segment identifying this graph on disk. Auto-generated; set it to an id " +
                 "the game also uses (a level or scenario id) so this graph previews the game's " +
                 "save profile. Clearing it mints a fresh id.")]
        [SerializeField] private string _graphId;

        [Tooltip("Name shown in the graph picker. Falls back to the asset name when empty.")]
        public string displayName;

        [Tooltip("Save slots for this graph. Each maps to its own runtime save file.")]
        public List<string> saveSlots = new List<string> { "Default" };

        [Tooltip("Index into saveSlots of the slot currently selected in the graph window.")]
        public int activeSlotIndex;

        [Tooltip("Registry asset names hidden in this graph. This is what scopes a graph to a level.")]
        public List<string> disabledRegistryNames = new List<string>();

        /// <summary>
        /// Stable id used to build this graph's runtime save-profile names.
        /// Generated once and kept across renames and moves; bind it to a game-side id
        /// (a level or scenario id) to preview that game's save profile.
        /// </summary>
        public string GraphId => _graphId;

        // Minted here rather than from the GraphId getter, so reading a profile name never
        // marks the asset dirty.
        private void OnEnable() => EnsureGraphId();

        private void OnValidate() => EnsureGraphId();

        private void EnsureGraphId() {
            if (!string.IsNullOrEmpty(_graphId)) return;
            _graphId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            UnityEditor.EditorUtility.SetDirty(this);
        }

        /// <summary>Label for the graph picker: <see cref="displayName"/> or the asset name.</summary>
        public string DisplayLabel =>
            string.IsNullOrWhiteSpace(displayName) ? name : displayName;

        /// <summary>Currently selected save slot name, or "Default" when the list is empty.</summary>
        public string ActiveSlot =>
            saveSlots != null && activeSlotIndex >= 0 && activeSlotIndex < saveSlots.Count
                ? saveSlots[activeSlotIndex]
                : "Default";

        /// <summary>
        /// Save-profile id for the active slot — pass to UnlockManager.SetSaveProfile so the
        /// graph reads and writes its own save file.
        ///
        /// Composed the same way the game composes one, so a graph whose <see cref="GraphId"/>
        /// is bound to a game-side id previews exactly the profile the game selects with
        /// <c>SaveProfile.Id(thatId, slot)</c> — for any slot name, encoding included.
        /// </summary>
        public string ActiveSaveProfile => SaveProfile.Id(GraphId, ActiveSlot);

        /// <summary>Mints a fresh id so a duplicated asset becomes its own graph.</summary>
        public void ResetGraphId() {
            _graphId = null;
            EnsureGraphId();
        }

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
