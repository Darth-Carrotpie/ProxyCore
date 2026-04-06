using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Modal dialog that displays used vs unused <see cref="UnlockCondition"/>
    /// assets in two columns. Unused conditions can be selected and deleted
    /// individually or all at once, with a confirmation prompt before deletion.
    /// </summary>
    internal sealed class ConditionCleanupDialog : EditorWindow {
        // ── Data ─────────────────────────────────────────────────────────
        private List<ConditionEntry> _usedConditions = new();
        private List<ConditionEntry> _unusedConditions = new();
        private bool _selectAll;

        // ── Scroll positions ─────────────────────────────────────────────
        private Vector2 _usedScroll;
        private Vector2 _unusedScroll;

        // ── Result ───────────────────────────────────────────────────────
        public bool DeletedAny { get; private set; }

        private class ConditionEntry {
            public UnlockCondition Condition;
            public string AssetPath;
            public string TypeName;
            public bool Selected;
        }

        // ════════════════════════════════════════════════════════════════
        // Show
        // ════════════════════════════════════════════════════════════════

        public static ConditionCleanupDialog Show() {
            var dlg = CreateInstance<ConditionCleanupDialog>();
            dlg.titleContent = new GUIContent("Condition Cleanup");
            dlg.minSize = new Vector2(620, 400);
            dlg.Init();
            dlg.ShowModal();
            return dlg;
        }

        // ════════════════════════════════════════════════════════════════
        // Initialisation — scan project for used / unused conditions
        // ════════════════════════════════════════════════════════════════

        private void Init() {
            RefreshLists();
        }

        private void RefreshLists() {
            _usedConditions.Clear();
            _unusedConditions.Clear();
            _selectAll = false;

            // 1. Collect all UnlockCondition assets
            var allConditions = new Dictionary<int, (UnlockCondition cond, string path)>();
            foreach (string guid in AssetDatabase.FindAssets("t:UnlockCondition")) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var cond = AssetDatabase.LoadAssetAtPath<UnlockCondition>(path);
                if (cond == null) continue;
                allConditions[cond.GetInstanceID()] = (cond, path);
            }

            // 2. Collect every condition referenced by any definition's _prerequisites
            var usedIds = new HashSet<int>();
            foreach (string guid in AssetDatabase.FindAssets("t:ScriptableObject")) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so is not BaseDefinition || so is not IHasPrerequisites hasPrereqs)
                    continue;

                var prereqs = hasPrereqs.Prerequisites;
                if (prereqs == null) continue;

                foreach (var cond in prereqs) {
                    if (cond != null)
                        usedIds.Add(cond.GetInstanceID());
                }
            }

            // 3. Partition
            foreach (var kvp in allConditions) {
                var entry = new ConditionEntry {
                    Condition = kvp.Value.cond,
                    AssetPath = kvp.Value.path,
                    TypeName = kvp.Value.cond.GetType().Name,
                };

                if (usedIds.Contains(kvp.Key))
                    _usedConditions.Add(entry);
                else
                    _unusedConditions.Add(entry);
            }

            _usedConditions = _usedConditions.OrderBy(e => e.Condition.name).ToList();
            _unusedConditions = _unusedConditions.OrderBy(e => e.Condition.name).ToList();
        }

        // ════════════════════════════════════════════════════════════════
        // GUI
        // ════════════════════════════════════════════════════════════════

        private void OnGUI() {
            EditorGUILayout.Space(4);

            // Header counts
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"Used: {_usedConditions.Count}   |   Unused: {_unusedConditions.Count}",
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // ── Two columns ──────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();

            // Left column — Used
            DrawUsedColumn();

            // Separator
            GUILayout.Space(4);

            // Right column — Unused
            DrawUnusedColumn();

            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            // ── Action buttons ───────────────────────────────────────
            DrawActionButtons();

            EditorGUILayout.Space(4);

            // Escape to close
            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape) {
                Close();
            }
        }

        // ── Left column: Used Conditions ─────────────────────────────

        private void DrawUsedColumn() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(280));

            EditorGUILayout.LabelField("Used Conditions", EditorStyles.boldLabel);
            EditorGUILayout.Space(2);

            _usedScroll = EditorGUILayout.BeginScrollView(_usedScroll);

            if (_usedConditions.Count == 0) {
                EditorGUILayout.LabelField("No used conditions found.",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else {
                foreach (var entry in _usedConditions)
                    DrawReadOnlyRow(entry);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ── Right column: Unused Conditions ──────────────────────────

        private void DrawUnusedColumn() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(280));

            // Header with Select All toggle
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Unused Conditions", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (_unusedConditions.Count > 0) {
                bool newSelectAll = GUILayout.Toggle(_selectAll, "Select All",
                    GUILayout.Width(80));
                if (newSelectAll != _selectAll) {
                    _selectAll = newSelectAll;
                    foreach (var entry in _unusedConditions)
                        entry.Selected = _selectAll;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            _unusedScroll = EditorGUILayout.BeginScrollView(_unusedScroll);

            if (_unusedConditions.Count == 0) {
                EditorGUILayout.LabelField("No unused conditions found.",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else {
                foreach (var entry in _unusedConditions)
                    DrawSelectableRow(entry);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ── Row renderers ────────────────────────────────────────────

        private static void DrawReadOnlyRow(ConditionEntry entry) {
            EditorGUILayout.BeginHorizontal();

            // Click to ping
            if (GUILayout.Button(entry.Condition.name, EditorStyles.linkLabel))
                EditorGUIUtility.PingObject(entry.Condition);

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(entry.TypeName, EditorStyles.miniLabel,
                GUILayout.Width(160));

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSelectableRow(ConditionEntry entry) {
            EditorGUILayout.BeginHorizontal();

            entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(18));

            // Click to ping
            if (GUILayout.Button(entry.Condition.name, EditorStyles.linkLabel))
                EditorGUIUtility.PingObject(entry.Condition);

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(entry.TypeName, EditorStyles.miniLabel,
                GUILayout.Width(160));

            EditorGUILayout.EndHorizontal();
        }

        // ── Action buttons ───────────────────────────────────────────

        private void DrawActionButtons() {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            int selectedCount = _unusedConditions.Count(e => e.Selected);

            // Clean Selected
            EditorGUI.BeginDisabledGroup(selectedCount == 0);
            if (GUILayout.Button($"Clean Selected ({selectedCount})",
                    GUILayout.Width(160))) {
                var toDelete = _unusedConditions.Where(e => e.Selected).ToList();
                if (ConfirmDeletion(toDelete))
                    DeleteConditions(toDelete);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(8);

            // Delete All Unused
            EditorGUI.BeginDisabledGroup(_unusedConditions.Count == 0);
            if (GUILayout.Button("Delete All Unused", GUILayout.Width(140))) {
                if (ConfirmDeletion(_unusedConditions))
                    DeleteConditions(_unusedConditions);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(8);

            if (GUILayout.Button("Close", GUILayout.Width(80)))
                Close();

            EditorGUILayout.EndHorizontal();
        }

        // ════════════════════════════════════════════════════════════════
        // Confirmation & deletion
        // ════════════════════════════════════════════════════════════════

        private static bool ConfirmDeletion(List<ConditionEntry> entries) {
            if (entries.Count == 0) return false;

            string names = string.Join("\n", entries.Select(e =>
                $"  • {e.Condition.name}  ({e.TypeName})"));

            return EditorUtility.DisplayDialog(
                "Delete Unused Conditions",
                $"Are you sure you want to permanently delete {entries.Count} condition asset(s)?\n\n{names}",
                "Delete",
                "Cancel");
        }

        private void DeleteConditions(List<ConditionEntry> entries) {
            foreach (var entry in entries) {
                if (!string.IsNullOrEmpty(entry.AssetPath))
                    AssetDatabase.DeleteAsset(entry.AssetPath);
            }

            AssetDatabase.SaveAssets();
            DeletedAny = true;
            RefreshLists();
            Repaint();
        }
    }
}
