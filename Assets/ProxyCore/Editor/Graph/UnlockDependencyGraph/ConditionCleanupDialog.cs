using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Modal dialog that displays used vs unused <see cref="UnlockCondition"/>
    /// assets in two columns. Unused conditions can be selected and deleted
    /// individually or all at once, with a confirmation prompt before deletion.
    ///
    /// A third "Mismatched" column lists referenced conditions whose type does not
    /// match the strategy registered for their source definition. These can be
    /// deleted here; the user then redraws the edge in the graph to recreate the
    /// correct condition type.
    /// </summary>
    internal sealed class ConditionCleanupDialog : EditorWindow {
        // ── Data ─────────────────────────────────────────────────────────
        private List<ConditionEntry> _usedConditions = new();
        private List<ConditionEntry> _unusedConditions = new();
        private List<ConditionEntry> _wrongTypeConditions = new();
        private bool _selectAllUnused;
        private bool _selectAllWrongType;

        // ── Scroll positions ─────────────────────────────────────────────
        private Vector2 _usedScroll;
        private Vector2 _unusedScroll;
        private Vector2 _wrongTypeScroll;

        // ── Result ───────────────────────────────────────────────────────
        public bool DeletedAny { get; private set; }

        private class ConditionEntry {
            public UnlockCondition Condition;
            public string AssetPath;
            public string TypeName;
            public bool Selected;
            public string MismatchReason;
        }

        // ════════════════════════════════════════════════════════════════
        // Show
        // ════════════════════════════════════════════════════════════════

        public static ConditionCleanupDialog Show() {
            var dlg = CreateInstance<ConditionCleanupDialog>();
            dlg.titleContent = new GUIContent("Condition Cleanup");
            dlg.minSize = new Vector2(940, 400);
            dlg.Init();
            dlg.ShowModal();
            return dlg;
        }

        // ════════════════════════════════════════════════════════════════
        // Initialisation — scan project for used / unused / wrong-type conditions
        // ════════════════════════════════════════════════════════════════

        private void Init() {
            RefreshLists();
        }

        private void RefreshLists() {
            _usedConditions.Clear();
            _unusedConditions.Clear();
            _wrongTypeConditions.Clear();
            _selectAllUnused = false;
            _selectAllWrongType = false;

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

            // 3. Partition into used and unused
            var usedEntries = new List<ConditionEntry>();
            foreach (var kvp in allConditions) {
                var entry = new ConditionEntry {
                    Condition = kvp.Value.cond,
                    AssetPath = kvp.Value.path,
                    TypeName = kvp.Value.cond.GetType().Name,
                };

                if (usedIds.Contains(kvp.Key))
                    usedEntries.Add(entry);
                else
                    _unusedConditions.Add(entry);
            }

            // 4. Among used conditions, detect strategy-type mismatches
            foreach (var entry in usedEntries) {
                if (DefinitionEdgeStrategyRegistry.TryGetDirectEdgeSource(entry.Condition, out var sourceDef)) {
                    var expectedStrategy = DefinitionEdgeStrategyRegistry.GetStrategy(sourceDef.GetType());
                    if (!expectedStrategy.OwnsCondition(entry.Condition)) {
                        entry.MismatchReason =
                            $"Expected type from '{expectedStrategy.GetType().Name}' for source '{sourceDef.name}'";
                        _wrongTypeConditions.Add(entry);
                        continue;
                    }
                }

                _usedConditions.Add(entry);
            }

            _usedConditions = _usedConditions.OrderBy(e => e.Condition.name).ToList();
            _unusedConditions = _unusedConditions.OrderBy(e => e.Condition.name).ToList();
            _wrongTypeConditions = _wrongTypeConditions.OrderBy(e => e.Condition.name).ToList();
        }

        // ════════════════════════════════════════════════════════════════
        // GUI
        // ════════════════════════════════════════════════════════════════

        private void OnGUI() {
            EditorGUILayout.Space(4);

            // Header counts
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                $"Used: {_usedConditions.Count}   |   Mismatched: {_wrongTypeConditions.Count}   |   Unused: {_unusedConditions.Count}",
                EditorStyles.boldLabel);
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            // ── Three columns ─────────────────────────────────────────
            EditorGUILayout.BeginHorizontal();

            DrawUsedColumn();
            GUILayout.Space(4);
            DrawWrongTypeColumn();
            GUILayout.Space(4);
            DrawUnusedColumn();

            EditorGUILayout.EndHorizontal();

            GUILayout.FlexibleSpace();

            DrawActionButtons();

            EditorGUILayout.Space(4);

            if (Event.current.type == EventType.KeyDown &&
                Event.current.keyCode == KeyCode.Escape) {
                Close();
            }
        }

        // ── Left column: Used Conditions ─────────────────────────────

        private void DrawUsedColumn() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(260));

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

        // ── Middle column: Wrong-Type Conditions ──────────────────────

        private void DrawWrongTypeColumn() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(300));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Mismatched Type", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (_wrongTypeConditions.Count > 0) {
                bool newSelectAll = GUILayout.Toggle(_selectAllWrongType, "Select All",
                    GUILayout.Width(80));
                if (newSelectAll != _selectAllWrongType) {
                    _selectAllWrongType = newSelectAll;
                    foreach (var entry in _wrongTypeConditions)
                        entry.Selected = _selectAllWrongType;
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(2);

            _wrongTypeScroll = EditorGUILayout.BeginScrollView(_wrongTypeScroll);

            if (_wrongTypeConditions.Count == 0) {
                EditorGUILayout.LabelField("No type mismatches found.",
                    EditorStyles.centeredGreyMiniLabel);
            }
            else {
                foreach (var entry in _wrongTypeConditions)
                    DrawSelectableRow(entry, showReason: true);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ── Right column: Unused Conditions ──────────────────────────

        private void DrawUnusedColumn() {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.MinWidth(260));

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Unused Conditions", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (_unusedConditions.Count > 0) {
                bool newSelectAll = GUILayout.Toggle(_selectAllUnused, "Select All",
                    GUILayout.Width(80));
                if (newSelectAll != _selectAllUnused) {
                    _selectAllUnused = newSelectAll;
                    foreach (var entry in _unusedConditions)
                        entry.Selected = _selectAllUnused;
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
                    DrawSelectableRow(entry, showReason: false);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        // ── Row renderers ────────────────────────────────────────────

        private static void DrawReadOnlyRow(ConditionEntry entry) {
            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(entry.Condition.name, EditorStyles.linkLabel))
                EditorGUIUtility.PingObject(entry.Condition);

            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField(entry.TypeName, EditorStyles.miniLabel,
                GUILayout.Width(160));

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSelectableRow(ConditionEntry entry, bool showReason) {
            EditorGUILayout.BeginHorizontal();

            entry.Selected = EditorGUILayout.Toggle(entry.Selected, GUILayout.Width(18));

            if (GUILayout.Button(entry.Condition.name, EditorStyles.linkLabel))
                EditorGUIUtility.PingObject(entry.Condition);

            GUILayout.FlexibleSpace();

            if (showReason && !string.IsNullOrEmpty(entry.MismatchReason))
                EditorGUILayout.LabelField(entry.MismatchReason, EditorStyles.miniLabel,
                    GUILayout.Width(200));
            else
                EditorGUILayout.LabelField(entry.TypeName, EditorStyles.miniLabel,
                    GUILayout.Width(160));

            EditorGUILayout.EndHorizontal();
        }

        // ── Action buttons ───────────────────────────────────────────

        private void DrawActionButtons() {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            int selectedWrong = _wrongTypeConditions.Count(e => e.Selected);
            int selectedUnused = _unusedConditions.Count(e => e.Selected);

            // Delete selected wrong-type
            EditorGUI.BeginDisabledGroup(selectedWrong == 0);
            if (GUILayout.Button($"Delete Mismatched ({selectedWrong})", GUILayout.Width(180))) {
                var toDelete = _wrongTypeConditions.Where(e => e.Selected).ToList();
                if (ConfirmDeletion(toDelete, "Mismatched Conditions"))
                    DeleteConditions(toDelete);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(8);

            // Delete selected unused
            EditorGUI.BeginDisabledGroup(selectedUnused == 0);
            if (GUILayout.Button($"Clean Selected ({selectedUnused})", GUILayout.Width(160))) {
                var toDelete = _unusedConditions.Where(e => e.Selected).ToList();
                if (ConfirmDeletion(toDelete, "Unused Conditions"))
                    DeleteConditions(toDelete);
            }
            EditorGUI.EndDisabledGroup();

            GUILayout.Space(8);

            // Delete all unused
            EditorGUI.BeginDisabledGroup(_unusedConditions.Count == 0);
            if (GUILayout.Button("Delete All Unused", GUILayout.Width(140))) {
                if (ConfirmDeletion(_unusedConditions, "Unused Conditions"))
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

        private static bool ConfirmDeletion(List<ConditionEntry> entries, string category) {
            if (entries.Count == 0) return false;

            string names = string.Join("\n", entries.Select(e =>
                $"  • {e.Condition.name}  ({e.TypeName})"));

            return EditorUtility.DisplayDialog(
                $"Delete {category}",
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
