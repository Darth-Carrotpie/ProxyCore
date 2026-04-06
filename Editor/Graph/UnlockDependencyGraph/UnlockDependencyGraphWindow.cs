using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Host <see cref="EditorWindow"/> for the Unlock Dependency Graph.
    /// Provides a toolbar with registry filter, refresh, auto-layout,
    /// path settings, search, and the full <see cref="UnlockGraphView"/>.
    /// </summary>
    public sealed class UnlockDependencyGraphWindow : EditorWindow {
        // ── Prefs keys ───────────────────────────────────────────────────
        private const string PREF_DEFINITIONS_PATH = "ProxyCore_UnlockGraph_DefinitionsPath";
        private const string PREF_CONDITIONS_PATH = "ProxyCore_UnlockGraph_ConditionsPath";
        private const string PREF_DEFINITIONS_EXTRAS = "ProxyCore_UnlockGraph_DefinitionsExtraPaths";
        private const string PREF_CONDITIONS_EXTRAS = "ProxyCore_UnlockGraph_ConditionsExtraPaths";
        private const string PREF_LAYOUT_DATA_PATH = "ProxyCore_UnlockGraph_LayoutDataPath";
        private const string PREF_LAYOUT_DATA_EXTRAS = "ProxyCore_UnlockGraph_LayoutDataExtraPaths";
        private const string PREF_DISABLED_REGISTRIES = "ProxyCore_UnlockGraph_DisabledRegistries";

        private const string DEFAULT_DEFINITIONS_PATH = "Assets/Data/Unlockables/Definitions";
        private const string DEFAULT_CONDITIONS_PATH = "Assets/Data/Unlockables/Conditions";
        private const string DEFAULT_LAYOUT_DATA_PATH = "Assets/Data/Unlockables/Layout";

        // ── State ────────────────────────────────────────────────────────
        private UnlockGraphView _graphView;
        private UnlockGraphLayoutData _layoutData;
        private List<RegistryCatalogEntry> _catalogEntries = new();

        // Path management (EventManagerWindow pattern)
        private List<string> _defKnownPaths = new();
        private int _defSelectedPathIdx;
        private List<string> _condKnownPaths = new();
        private int _condSelectedPathIdx;
        private List<string> _layoutKnownPaths = new();
        private int _layoutSelectedPathIdx;

        // Settings panel
        private bool _settingsPanelOpen;
        private bool _addingNewDefPath;
        private string _newDefPathInput = "";
        private bool _addingNewCondPath;
        private string _newCondPathInput = "";
        private bool _addingNewLayoutPath;
        private string _newLayoutPathInput = "";

        // Search
        private string _searchFilter = "";

        // Dirty tracking
        private bool _isDirty;

        // ── Catalog entry ────────────────────────────────────────────────
        private class RegistryCatalogEntry {
            public ScriptableObject Registry;
            public string Name;
            public bool Enabled;
        }

        // ════════════════════════════════════════════════════════════════
        // Open
        // ════════════════════════════════════════════════════════════════

        [MenuItem("ProxyCore/Unlock Dependency Graph")]
        public static void ShowWindow() {
            var w = GetWindow<UnlockDependencyGraphWindow>();
            w.titleContent = new GUIContent("Unlock Graph", EditorGUIUtility.IconContent("d_SceneViewFx").image);
            w.minSize = new Vector2(800, 500);
            w.Show();
        }

        // ════════════════════════════════════════════════════════════════
        // Lifecycle
        // ════════════════════════════════════════════════════════════════

        private void CreateGUI() {
            // Load or create layout data
            _layoutData = FindOrCreateLayoutData();

            // Discover registries
            RefreshCatalogEntries();
            RefreshKnownPaths();

            // Build UI
            var root = rootVisualElement;

            // Toolbar (IMGUI for ease of EditorGUI controls)
            var toolbarContainer = new IMGUIContainer(DrawToolbar);
            toolbarContainer.AddToClassList("graph-toolbar");
            root.Add(toolbarContainer);

            // Settings panel (IMGUI, conditionally shown)
            var settingsContainer = new IMGUIContainer(DrawSettingsPanel);
            root.Add(settingsContainer);

            // GraphView
            _graphView = new UnlockGraphView();
            _graphView.StretchToParentSize();
            _graphView.OnGraphChanged += OnGraphChanged;
            _graphView.HostWindow = this;

            var graphContainer = new VisualElement();
            graphContainer.style.flexGrow = 1;
            graphContainer.Add(_graphView);
            root.Add(graphContainer);

            // Initial build
            RebuildGraph();
        }

        private void OnGUI() {
            // Catch SPACE key in the IMGUI event loop — UIElements
            // KeyDownEvent is unreliable with IMGUI-hosted toolbars.
            var e = Event.current;
            if (e != null && e.type == EventType.KeyDown
                && e.keyCode == KeyCode.Space
                && !e.control && !e.alt && !e.command
                && !_settingsPanelOpen
                && _graphView != null) {
                var screenPos = GUIUtility.GUIToScreenPoint(e.mousePosition);
                _graphView.OpenSearchWindow(screenPos);
                e.Use();
            }
        }

        private void OnFocus() {
            // Refresh when the window is focused
            if (_graphView != null) {
                RefreshCatalogEntries();
                RefreshKnownPaths();
            }
        }

        private void OnGraphChanged() {
            MarkDirty();
            Repaint();
        }

        private void MarkDirty() {
            if (_isDirty) return;
            _isDirty = true;
            UpdateTitle();
        }

        private void ClearDirty() {
            _isDirty = false;
            UpdateTitle();
        }

        private void UpdateTitle() {
            string baseName = "Unlock Graph";
            titleContent = new GUIContent(
                _isDirty ? baseName + " *" : baseName,
                EditorGUIUtility.IconContent("d_SceneViewFx").image);
        }

        private void SaveAll() {
            if (_layoutData != null)
                EditorUtility.SetDirty(_layoutData);
            AssetDatabase.SaveAssets();
            ClearDirty();
        }

        // ════════════════════════════════════════════════════════════════
        // Toolbar (IMGUI)
        // ════════════════════════════════════════════════════════════════

        private void DrawToolbar() {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Title
            GUILayout.Label("Unlock Dependency Graph", EditorStyles.boldLabel,
                GUILayout.Width(180));

            // Registry filter
            if (GUILayout.Button("Registries ▾", EditorStyles.toolbarDropDown,
                    GUILayout.Width(100))) {
                ShowRegistryFilterMenu();
            }

            GUILayout.Space(4);

            // Refresh
            if (GUILayout.Button("↻ Refresh", EditorStyles.toolbarButton,
                    GUILayout.Width(70))) {
                RefreshCatalogEntries();
                RebuildGraph();
            }

            // Save
            EditorGUI.BeginDisabledGroup(!_isDirty);
            if (GUILayout.Button("💾 Save", EditorStyles.toolbarButton,
                    GUILayout.Width(60))) {
                SaveAll();
            }
            EditorGUI.EndDisabledGroup();

            // Auto-Layout
            if (GUILayout.Button("Auto-Layout", EditorStyles.toolbarButton,
                    GUILayout.Width(80))) {
                UnlockGraphBuilder.AutoLayout(_graphView, _layoutData);
            }

            // Group Selected
            if (GUILayout.Button("Group Selected", EditorStyles.toolbarButton,
                    GUILayout.Width(100))) {
                _graphView.GroupSelectedNodes();
            }

            // Condition Cleanup
            if (GUILayout.Button("Cleanup", EditorStyles.toolbarButton,
                    GUILayout.Width(60))) {
                var dlg = ConditionCleanupDialog.Show();
                if (dlg.DeletedAny)
                    RebuildGraph();
            }

            GUILayout.Space(4);

            // Ping / Select SO — target icon
            var pingIcon = EditorGUIUtility.IconContent("d_Search Icon");
            if (pingIcon == null || pingIcon.image == null)
                pingIcon = new GUIContent("⊙");
            pingIcon.tooltip = "Select underlying asset(s) in Inspector";
            if (GUILayout.Button(pingIcon, EditorStyles.toolbarButton,
                    GUILayout.Width(28))) {
                SelectUnderlyingAssets();
            }

            // Filter duplicates — fills search bar with selected condition name
            var filterIcon = EditorGUIUtility.IconContent("d_FilterByType");
            if (filterIcon == null || filterIcon.image == null)
                filterIcon = new GUIContent("⧫");
            filterIcon.tooltip = "Filter duplicate condition nodes";
            if (GUILayout.Button(filterIcon, EditorStyles.toolbarButton,
                    GUILayout.Width(28))) {
                FilterDuplicateConditions();
            }

            GUILayout.FlexibleSpace();

            // Search
            _searchFilter = EditorGUILayout.TextField(_searchFilter,
                EditorStyles.toolbarSearchField, GUILayout.Width(180));
            if (GUILayout.Button("", GUI.skin.FindStyle("ToolbarSearchCancelButton") ?? EditorStyles.toolbarButton)) {
                _searchFilter = "";
                GUI.FocusControl(null);
            }

            GUILayout.Space(4);

            // Settings toggle
            bool wasOpen = _settingsPanelOpen;
            _settingsPanelOpen = GUILayout.Toggle(_settingsPanelOpen, "⚙",
                EditorStyles.toolbarButton, GUILayout.Width(28));
            if (wasOpen != _settingsPanelOpen) Repaint();

            EditorGUILayout.EndHorizontal();

            // Apply search filter
            ApplySearchFilter();
        }

        // ════════════════════════════════════════════════════════════════
        // Select underlying assets
        // ════════════════════════════════════════════════════════════════

        private void SelectUnderlyingAssets() {
            if (_graphView == null) return;

            var objects = _graphView.GetSelectedObjects();
            if (objects.Count == 0) return;

            // Set Inspector selection
            Selection.objects = objects.ToArray();

            // If single object, also ping it in the Project window
            if (objects.Count == 1)
                EditorGUIUtility.PingObject(objects[0]);
        }

        // ════════════════════════════════════════════════════════════════
        // Settings panel (IMGUI)
        // ════════════════════════════════════════════════════════════════

        private void DrawSettingsPanel() {
            if (!_settingsPanelOpen) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Path Settings", EditorStyles.boldLabel);

            // ── Definitions path ─────────────────────────────────
            DrawPathRow("Definitions", _defKnownPaths, ref _defSelectedPathIdx,
                ref _addingNewDefPath, ref _newDefPathInput,
                PREF_DEFINITIONS_PATH, PREF_DEFINITIONS_EXTRAS,
                DEFAULT_DEFINITIONS_PATH, isDefinitions: true);

            // ── Conditions path ──────────────────────────────────
            DrawPathRow("Conditions", _condKnownPaths, ref _condSelectedPathIdx,
                ref _addingNewCondPath, ref _newCondPathInput,
                PREF_CONDITIONS_PATH, PREF_CONDITIONS_EXTRAS,
                DEFAULT_CONDITIONS_PATH, isDefinitions: false);

            // ── Layout data path ─────────────────────────────────
            DrawPathRow("Layout Data", _layoutKnownPaths, ref _layoutSelectedPathIdx,
                ref _addingNewLayoutPath, ref _newLayoutPathInput,
                PREF_LAYOUT_DATA_PATH, PREF_LAYOUT_DATA_EXTRAS,
                DEFAULT_LAYOUT_DATA_PATH, isDefinitions: false);

            EditorGUILayout.EndVertical();
        }

        private void DrawPathRow(string label, List<string> knownPaths,
            ref int selectedIdx, ref bool addingNew, ref string newInput,
            string prefKeySelected, string prefKeyExtras, string defaultPath,
            bool isDefinitions) {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(90));

            // Replace "/" with " › " for display
            var displayNames = knownPaths
                .Select(p => p.Replace("/", " \u203A "))
                .Concat(new[] { "+ New Path…", "↻ Refresh Paths" })
                .ToArray();

            int newIdx = EditorGUILayout.Popup(selectedIdx, displayNames);

            if (newIdx == displayNames.Length - 1) {
                // Refresh
                RefreshKnownPaths();
                newIdx = selectedIdx;
            }
            else if (newIdx == displayNames.Length - 2) {
                // New path
                addingNew = true;
                newIdx = selectedIdx;
            }
            else if (newIdx != selectedIdx && newIdx >= 0 && newIdx < knownPaths.Count) {
                selectedIdx = newIdx;
                EditorPrefs.SetString(prefKeySelected, knownPaths[selectedIdx]);
                UpdateGraphViewPaths();
            }

            EditorGUILayout.EndHorizontal();

            // New path input row
            if (addingNew) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(94);
                newInput = EditorGUILayout.TextField(newInput);

                if (GUILayout.Button("Add", GUILayout.Width(40))) {
                    if (!string.IsNullOrWhiteSpace(newInput)) {
                        string path = newInput.Trim();
                        if (!path.StartsWith("Assets")) path = "Assets/" + path;

                        string extras = EditorPrefs.GetString(prefKeyExtras, "");
                        extras = string.IsNullOrEmpty(extras) ? path : extras + ";" + path;
                        EditorPrefs.SetString(prefKeyExtras, extras);

                        RefreshKnownPaths();

                        selectedIdx = knownPaths.FindIndex(p =>
                            string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                        if (selectedIdx < 0) selectedIdx = 0;
                        EditorPrefs.SetString(prefKeySelected, knownPaths[selectedIdx]);
                        UpdateGraphViewPaths();
                    }
                    addingNew = false;
                    newInput = "";
                }

                if (GUILayout.Button("Cancel", GUILayout.Width(55))) {
                    addingNew = false;
                    newInput = "";
                }
                EditorGUILayout.EndHorizontal();
            }
        }

        // ════════════════════════════════════════════════════════════════
        // Filter duplicate condition nodes
        // ════════════════════════════════════════════════════════════════

        private void FilterDuplicateConditions() {
            if (_graphView == null) return;

            // If a condition node is selected, filter by its name
            var selected = _graphView.selection
                .OfType<ConditionNode>()
                .FirstOrDefault();

            if (selected != null) {
                _searchFilter = selected.Condition.name;
                GUI.FocusControl(null);
                Repaint();
                return;
            }

            // No selection: toggle — if a filter is active, clear it;
            // otherwise show all conditions that appear more than once
            if (!string.IsNullOrWhiteSpace(_searchFilter)) {
                _searchFilter = "";
                GUI.FocusControl(null);
                Repaint();
                return;
            }

            // Find conditions with multiple nodes
            var condGrouped = _graphView.nodes.ToList()
                .OfType<ConditionNode>()
                .GroupBy(cn => cn.AssetGuid)
                .Where(g => g.Count() > 1)
                .ToList();

            if (condGrouped.Count == 0) {
                Debug.Log("[Unlock Graph] No duplicate condition nodes found.");
                return;
            }

            // Select all duplicate nodes so they're easy to spot
            _graphView.ClearSelection();
            foreach (var group in condGrouped) {
                foreach (var cn in group)
                    _graphView.AddToSelection(cn);
            }

            var firstName = condGrouped[0].First().Condition.name;
            _searchFilter = condGrouped.Count == 1 ? firstName : "";
            GUI.FocusControl(null);
            Repaint();
        }

        // ════════════════════════════════════════════════════════════════
        // Registry filter
        // ════════════════════════════════════════════════════════════════

        private void ShowRegistryFilterMenu() {
            var menu = new GenericMenu();
            foreach (var entry in _catalogEntries) {
                var e = entry; // capture
                menu.AddItem(new GUIContent(e.Name), e.Enabled, () => {
                    e.Enabled = !e.Enabled;
                    SaveDisabledRegistries();
                    RebuildGraph();
                });
            }
            menu.ShowAsContext();
        }

        // ════════════════════════════════════════════════════════════════
        // Search filter
        // ════════════════════════════════════════════════════════════════

        private void ApplySearchFilter() {
            if (_graphView == null) return;
            bool hasFilter = !string.IsNullOrWhiteSpace(_searchFilter);

            _graphView.nodes.ForEach(node => {
                if (!hasFilter) {
                    node.visible = true;
                    node.style.display = DisplayStyle.Flex;
                    return;
                }

                bool match = node.title != null &&
                    node.title.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase);
                node.visible = match;
                node.style.display = match ? DisplayStyle.Flex : DisplayStyle.None;
            });
        }

        // ════════════════════════════════════════════════════════════════
        // Graph rebuild
        // ════════════════════════════════════════════════════════════════

        private void RebuildGraph() {
            if (_graphView == null) return;

            var enabledRegistries = _catalogEntries
                .Where(e => e.Enabled)
                .Select(e => e.Registry)
                .ToList();

            UnlockGraphBuilder.Build(_graphView, _layoutData, enabledRegistries);
            UpdateGraphViewPaths();
        }

        // ════════════════════════════════════════════════════════════════
        // Path discovery (EventManagerWindow pattern)
        // ════════════════════════════════════════════════════════════════

        private void RefreshKnownPaths() {
            _defKnownPaths = DiscoverPaths("BaseDefinition", PREF_DEFINITIONS_EXTRAS,
                DEFAULT_DEFINITIONS_PATH);
            _defSelectedPathIdx = RestorePathSelection(_defKnownPaths,
                PREF_DEFINITIONS_PATH, DEFAULT_DEFINITIONS_PATH);

            _condKnownPaths = DiscoverPaths("UnlockCondition", PREF_CONDITIONS_EXTRAS,
                DEFAULT_CONDITIONS_PATH);
            _condSelectedPathIdx = RestorePathSelection(_condKnownPaths,
                PREF_CONDITIONS_PATH, DEFAULT_CONDITIONS_PATH);

            _layoutKnownPaths = DiscoverLayoutDataPaths();
            _layoutSelectedPathIdx = RestorePathSelection(_layoutKnownPaths,
                PREF_LAYOUT_DATA_PATH, DEFAULT_LAYOUT_DATA_PATH);

            UpdateGraphViewPaths();
        }

        private static List<string> DiscoverPaths(string baseTypeName,
            string prefKeyExtras, string defaultPath) {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Scan existing assets
            string[] guids = AssetDatabase.FindAssets($"t:ScriptableObject");
            var baseType = baseTypeName == "BaseDefinition"
                ? typeof(BaseDefinition) : typeof(UnlockCondition);

            foreach (string guid in guids) {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (obj == null || !baseType.IsAssignableFrom(obj.GetType())) continue;

                string dir = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir)) paths.Add(dir);
            }

            // Merge manually pinned paths
            string extras = EditorPrefs.GetString(prefKeyExtras, "");
            if (!string.IsNullOrEmpty(extras)) {
                foreach (string p in extras.Split(';')) {
                    string trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) paths.Add(trimmed);
                }
            }

            if (paths.Count == 0 || AssetDatabase.IsValidFolder(defaultPath))
                paths.Add(defaultPath);

            return paths.OrderBy(p => p).ToList();
        }

        private static int RestorePathSelection(List<string> paths,
            string prefKey, string defaultPath) {
            string saved = EditorPrefs.GetString(prefKey, defaultPath);
            int idx = paths.FindIndex(p =>
                string.Equals(p, saved, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 ? idx : 0;
        }

        private void UpdateGraphViewPaths() {
            if (_graphView == null) return;
            _graphView.DefinitionsPath = _defSelectedPathIdx >= 0 &&
                _defSelectedPathIdx < _defKnownPaths.Count
                ? _defKnownPaths[_defSelectedPathIdx]
                : DEFAULT_DEFINITIONS_PATH;
            _graphView.ConditionsPath = _condSelectedPathIdx >= 0 &&
                _condSelectedPathIdx < _condKnownPaths.Count
                ? _condKnownPaths[_condSelectedPathIdx]
                : DEFAULT_CONDITIONS_PATH;
        }

        // ════════════════════════════════════════════════════════════════
        // Registry discovery
        // ════════════════════════════════════════════════════════════════

        private void RefreshCatalogEntries() {
            var disabled = LoadDisabledRegistries();
            _catalogEntries.Clear();

            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (so is not IUnlockableCatalog) continue;

                _catalogEntries.Add(new RegistryCatalogEntry {
                    Registry = so,
                    Name = so.name,
                    Enabled = !disabled.Contains(so.name),
                });
            }

            _catalogEntries = _catalogEntries.OrderBy(e => e.Name).ToList();
        }

        private HashSet<string> LoadDisabledRegistries() {
            string raw = EditorPrefs.GetString(PREF_DISABLED_REGISTRIES, "");
            if (string.IsNullOrEmpty(raw)) return new HashSet<string>();
            return new HashSet<string>(raw.Split(';'), StringComparer.OrdinalIgnoreCase);
        }

        private void SaveDisabledRegistries() {
            var disabled = _catalogEntries
                .Where(e => !e.Enabled)
                .Select(e => e.Name);
            EditorPrefs.SetString(PREF_DISABLED_REGISTRIES, string.Join(";", disabled));
        }

        // ════════════════════════════════════════════════════════════════
        // Layout data path discovery
        // ════════════════════════════════════════════════════════════════

        private List<string> DiscoverLayoutDataPaths() {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Scan existing layout data assets
            string[] guids = AssetDatabase.FindAssets("t:UnlockGraphLayoutData");
            foreach (string guid in guids) {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                string dir = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir)) paths.Add(dir);
            }

            // Merge manually pinned paths
            string extras = EditorPrefs.GetString(PREF_LAYOUT_DATA_EXTRAS, "");
            if (!string.IsNullOrEmpty(extras)) {
                foreach (string p in extras.Split(';')) {
                    string trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed)) paths.Add(trimmed);
                }
            }

            if (paths.Count == 0 || AssetDatabase.IsValidFolder(DEFAULT_LAYOUT_DATA_PATH))
                paths.Add(DEFAULT_LAYOUT_DATA_PATH);

            return paths.OrderBy(p => p).ToList();
        }

        private string GetSelectedLayoutDataPath() {
            if (_layoutKnownPaths != null
                && _layoutSelectedPathIdx >= 0
                && _layoutSelectedPathIdx < _layoutKnownPaths.Count)
                return _layoutKnownPaths[_layoutSelectedPathIdx];
            return EditorPrefs.GetString(PREF_LAYOUT_DATA_PATH, DEFAULT_LAYOUT_DATA_PATH);
        }

        // ════════════════════════════════════════════════════════════════
        // Layout data management
        // ════════════════════════════════════════════════════════════════

        private UnlockGraphLayoutData FindOrCreateLayoutData() {
            string targetDir = GetSelectedLayoutDataPath();

            // Look for existing layout data in the target directory
            string[] guids = AssetDatabase.FindAssets("t:UnlockGraphLayoutData");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var candidate = AssetDatabase.LoadAssetAtPath<UnlockGraphLayoutData>(path);
                if (candidate != null) return candidate;
            }

            // Create new layout data at the configured path
            var data = CreateInstance<UnlockGraphLayoutData>();
            UnlockGraphView.EnsureFolderExists(targetDir);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{targetDir}/UnlockGraphLayoutData.asset");
            AssetDatabase.CreateAsset(data, assetPath);
            AssetDatabase.SaveAssets();
            return data;
        }
    }
}
