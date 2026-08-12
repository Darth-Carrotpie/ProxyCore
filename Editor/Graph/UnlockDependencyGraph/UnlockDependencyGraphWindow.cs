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
        private const string PREF_DEFINITION_TYPE_PATH_PREFIX = "ProxyCore_UnlockGraph_DefinitionTypePath_";
        private const string PREF_DEFINITION_TYPE_EXTRAS_PREFIX = "ProxyCore_UnlockGraph_DefinitionTypeExtraPaths_";
        private const string PREF_CONDITIONS_EXTRAS = "ProxyCore_UnlockGraph_ConditionsExtraPaths";
        private const string PREF_LAYOUT_DATA_PATH = "ProxyCore_UnlockGraph_LayoutDataPath";
        private const string PREF_LAYOUT_DATA_EXTRAS = "ProxyCore_UnlockGraph_LayoutDataExtraPaths";
        // Which graph this machine last had open. The graph's own contents (registry
        // filter, save slots) live on the asset so they are shared with the team.
        private const string PREF_ACTIVE_GRAPH_GUID = "ProxyCore_UnlockGraph_ActiveGraphGuid";

        private const string DEFAULT_DEFINITIONS_PATH = "Assets/Data/Unlockables/Definitions";
        private const string DEFAULT_CONDITIONS_PATH = "Assets/Data/Unlockables/Conditions";
        private const string DEFAULT_LAYOUT_DATA_PATH = "Assets/Data/Unlockables/Layout";

        // ── State ────────────────────────────────────────────────────────
        private UnlockGraphView _graphView;
        private UnlockGraphLayoutData _layoutData;
        private List<RegistryCatalogEntry> _catalogEntries = new();
        private List<UnlockGraphLayoutData> _availableGraphs = new();

        // Path management (EventManagerWindow pattern)
        private List<string> _defKnownPaths = new();
        private int _defSelectedPathIdx;
        private List<string> _condKnownPaths = new();
        private int _condSelectedPathIdx;
        private List<string> _layoutKnownPaths = new();
        private int _layoutSelectedPathIdx;
        private readonly List<DefinitionTypePathEntry> _definitionTypePathEntries = new();

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

        private sealed class DefinitionTypePathEntry {
            public Type DefinitionType;
            public string TypePrefsKey;
            public string ExtrasPrefsKey;
            public List<string> KnownPaths = new();
            public int SelectedPathIdx;
            public bool AddingNewPath;
            public string NewPathInput = "";
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
            ApplyActiveSaveProfile();

            // Discover registries
            RefreshCatalogEntries();
            RefreshKnownPaths();

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;

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

        private void OnDisable() {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        }

        private void OnFocus() {
            // Refresh when the window is focused
            if (_graphView != null) {
                RefreshCatalogEntries();
                RefreshKnownPaths();
                _graphView.RefreshAllBadges();
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state) {
            // Statics reset on domain reload, so the graph's profile must be re-applied
            // once play mode has actually started.
            if (state == PlayModeStateChange.EnteredPlayMode
                || state == PlayModeStateChange.EnteredEditMode) {
                ApplyActiveSaveProfile();
                _graphView?.RefreshAllBadges();
                Repaint();
            }
        }

        /// <summary>
        /// Points UnlockManager at the active graph's selected save slot, so unlock state
        /// read and written from this window lands in that save's own file.
        ///
        /// Does nothing in Play mode: the running game owns the active profile there, and
        /// save correctness must not depend on whether this window happens to be open.
        /// </summary>
        private void ApplyActiveSaveProfile() {
            if (_layoutData == null || Application.isPlaying) return;
            UnlockManager.SetSaveProfile(_layoutData.ActiveSaveProfile);
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
            GUILayout.Label("Unlock Graph", EditorStyles.boldLabel,
                GUILayout.Width(90));

            // In Play mode the running game owns the active profile — the pickers become a
            // read-out of what the game selected rather than a control over it.
            bool playing = Application.isPlaying;

            // Graph picker — each graph is a separate progression tree (typically a level)
            using (new EditorGUI.DisabledScope(playing)) {
                string graphLabel = _layoutData != null ? _layoutData.DisplayLabel : "<none>";
                if (GUILayout.Button(new GUIContent($"{graphLabel} ▾", "Switch, create, or delete unlock graphs"),
                        EditorStyles.toolbarDropDown, GUILayout.Width(140))) {
                    ShowGraphMenu();
                }
            }

            // Save slot picker — each slot is its own runtime save file
            using (new EditorGUI.DisabledScope(playing || _layoutData == null)) {
                string slotLabel = playing
                    ? (string.IsNullOrEmpty(SaveProfile.Active) ? "<default>" : SaveProfile.Active)
                    : _layoutData != null ? _layoutData.ActiveSlot : "—";
                string slotTooltip = playing
                    ? "Live save profile selected by the running game. The graph's own slot is " +
                      "restored on exiting Play mode."
                    : "Switch, create, or delete save slots for this graph";
                if (GUILayout.Button(new GUIContent($"💾 {slotLabel} ▾", slotTooltip),
                        EditorStyles.toolbarDropDown, GUILayout.Width(120))) {
                    ShowSaveSlotMenu();
                }
            }

            GUILayout.Space(4);

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

            // ── Definitions paths (per concrete definition type) ─────────
            EditorGUILayout.LabelField("Definitions (Per Type)", EditorStyles.miniBoldLabel);
            if (_definitionTypePathEntries.Count == 0) {
                EditorGUILayout.HelpBox(
                    "No unlockable definition types discovered via BaseRegistry<T>.",
                    MessageType.Info);
            }
            else {
                foreach (var entry in _definitionTypePathEntries)
                    DrawDefinitionTypePathRow(entry);
            }

            EditorGUILayout.Space(6);

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

        private void DrawDefinitionTypePathRow(DefinitionTypePathEntry entry) {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(entry.DefinitionType.Name, GUILayout.Width(125));

            var displayNames = entry.KnownPaths
                .Select(p => p.Replace("/", " \u203A "))
                .Concat(new[] { "+ New Path…", "↻ Refresh Paths" })
                .ToArray();

            int newIdx = EditorGUILayout.Popup(entry.SelectedPathIdx, displayNames);

            if (newIdx == displayNames.Length - 1) {
                RefreshKnownPaths();
                newIdx = entry.SelectedPathIdx;
            }
            else if (newIdx == displayNames.Length - 2) {
                entry.AddingNewPath = true;
                newIdx = entry.SelectedPathIdx;
            }
            else if (newIdx != entry.SelectedPathIdx && newIdx >= 0 && newIdx < entry.KnownPaths.Count) {
                entry.SelectedPathIdx = newIdx;
                EditorPrefs.SetString(entry.TypePrefsKey, entry.KnownPaths[entry.SelectedPathIdx]);
                UpdateGraphViewPaths();
            }

            EditorGUILayout.EndHorizontal();

            if (entry.AddingNewPath) {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(129);
                entry.NewPathInput = EditorGUILayout.TextField(entry.NewPathInput);

                if (GUILayout.Button("Add", GUILayout.Width(40))) {
                    if (!string.IsNullOrWhiteSpace(entry.NewPathInput)) {
                        string path = entry.NewPathInput.Trim();
                        if (!path.StartsWith("Assets")) path = "Assets/" + path;

                        string extras = EditorPrefs.GetString(entry.ExtrasPrefsKey, "");
                        extras = string.IsNullOrEmpty(extras) ? path : extras + ";" + path;
                        EditorPrefs.SetString(entry.ExtrasPrefsKey, extras);

                        RefreshKnownPaths();

                        var refreshed = _definitionTypePathEntries
                            .FirstOrDefault(e => e.DefinitionType == entry.DefinitionType);
                        if (refreshed != null) {
                            refreshed.SelectedPathIdx = refreshed.KnownPaths.FindIndex(p =>
                                string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                            if (refreshed.SelectedPathIdx < 0) refreshed.SelectedPathIdx = 0;

                            if (refreshed.KnownPaths.Count > 0) {
                                EditorPrefs.SetString(refreshed.TypePrefsKey,
                                    refreshed.KnownPaths[refreshed.SelectedPathIdx]);
                            }
                            UpdateGraphViewPaths();
                        }
                    }

                    entry.AddingNewPath = false;
                    entry.NewPathInput = "";
                }

                if (GUILayout.Button("Cancel", GUILayout.Width(55))) {
                    entry.AddingNewPath = false;
                    entry.NewPathInput = "";
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
        // Graph picker — create / switch / duplicate / delete
        // ════════════════════════════════════════════════════════════════

        private void ShowGraphMenu() {
            RefreshAvailableGraphs();

            var menu = new GenericMenu();
            foreach (var graph in _availableGraphs) {
                var g = graph; // capture
                menu.AddItem(new GUIContent(g.DisplayLabel), g == _layoutData, () => SwitchToGraph(g));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New Graph…"), false, CreateNewGraph);

            if (_layoutData != null) {
                menu.AddItem(new GUIContent("Duplicate Graph"), false, DuplicateActiveGraph);
                menu.AddItem(new GUIContent("Rename Graph…"), false, RenameActiveGraph);
                if (_availableGraphs.Count > 1)
                    menu.AddItem(new GUIContent("Delete Graph…"), false, DeleteActiveGraph);
                else
                    menu.AddDisabledItem(new GUIContent("Delete Graph…"));
            }

            menu.ShowAsContext();
        }

        private void RefreshAvailableGraphs() {
            _availableGraphs.Clear();
            foreach (string guid in AssetDatabase.FindAssets("t:UnlockGraphLayoutData")) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<UnlockGraphLayoutData>(path);
                if (asset != null) _availableGraphs.Add(asset);
            }
            _availableGraphs = _availableGraphs.OrderBy(g => g.DisplayLabel).ToList();
        }

        private void SwitchToGraph(UnlockGraphLayoutData graph) {
            if (graph == null || graph == _layoutData) return;

            _layoutData = graph;
            EditorPrefs.SetString(PREF_ACTIVE_GRAPH_GUID, GetAssetGuid(graph));
            ApplyActiveSaveProfile();
            RefreshCatalogEntries();
            RebuildGraph();
            Repaint();
        }

        private void CreateNewGraph() {
            string graphName = EditorInputDialog.Show("New Unlock Graph",
                "Name for the new graph (typically a level name):", "Level");
            if (string.IsNullOrWhiteSpace(graphName)) return;

            var data = CreateInstance<UnlockGraphLayoutData>();
            data.displayName = graphName.Trim();
            SwitchToGraph(CreateGraphAsset(data, graphName.Trim()));
        }

        private void DuplicateActiveGraph() {
            if (_layoutData == null) return;

            string sourcePath = AssetDatabase.GetAssetPath(_layoutData);
            var copy = Instantiate(_layoutData);
            // A duplicate is a new graph: it needs its own id so it gets its own save files.
            copy.ResetGraphId();
            copy.displayName = _layoutData.DisplayLabel + " Copy";

            string dir = string.IsNullOrEmpty(sourcePath)
                ? GetSelectedLayoutDataPath()
                : System.IO.Path.GetDirectoryName(sourcePath)?.Replace('\\', '/');
            SwitchToGraph(CreateGraphAsset(copy, copy.displayName, dir));
        }

        private void RenameActiveGraph() {
            if (_layoutData == null) return;

            string newName = EditorInputDialog.Show("Rename Unlock Graph",
                "New display name:", _layoutData.DisplayLabel);
            if (string.IsNullOrWhiteSpace(newName)) return;

            Undo.RecordObject(_layoutData, "Rename unlock graph");
            _layoutData.displayName = newName.Trim();
            EditorUtility.SetDirty(_layoutData);
            MarkDirty();
            Repaint();
        }

        private void DeleteActiveGraph() {
            if (_layoutData == null || _availableGraphs.Count <= 1) return;

            string path = AssetDatabase.GetAssetPath(_layoutData);
            if (!EditorUtility.DisplayDialog("Delete Unlock Graph",
                    $"Delete '{_layoutData.DisplayLabel}'?\n\n{path}\n\n" +
                    "This removes the graph's layout, groups, and save-slot list. " +
                    "Definitions, conditions, and save files on disk are not touched.",
                    "Delete", "Cancel"))
                return;

            var replacement = _availableGraphs.FirstOrDefault(g => g != _layoutData);
            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();

            _layoutData = null;
            SwitchToGraph(replacement);
        }

        private UnlockGraphLayoutData CreateGraphAsset(UnlockGraphLayoutData data,
            string fileName, string targetDir = null) {
            targetDir ??= GetSelectedLayoutDataPath();
            UnlockGraphView.EnsureFolderExists(targetDir);

            string safeName = string.Join("_", fileName.Split(System.IO.Path.GetInvalidFileNameChars()));
            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{targetDir}/{safeName}.asset");
            AssetDatabase.CreateAsset(data, assetPath);
            AssetDatabase.SaveAssets();
            return data;
        }

        private static string GetAssetGuid(UnityEngine.Object asset) =>
            asset == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(asset));

        // ════════════════════════════════════════════════════════════════
        // Save slots — one runtime save file per slot, per graph
        // ════════════════════════════════════════════════════════════════

        private void ShowSaveSlotMenu() {
            if (_layoutData == null) return;

            var menu = new GenericMenu();
            for (int i = 0; i < _layoutData.saveSlots.Count; i++) {
                int idx = i; // capture
                menu.AddItem(new GUIContent(_layoutData.saveSlots[i]),
                    idx == _layoutData.activeSlotIndex,
                    () => SelectSaveSlot(idx));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("New Save…"), false, CreateSaveSlot);
            if (_layoutData.saveSlots.Count > 1)
                menu.AddItem(new GUIContent("Delete Save…"), false, DeleteActiveSaveSlot);
            else
                menu.AddDisabledItem(new GUIContent("Delete Save…"));

            menu.ShowAsContext();
        }

        private void SelectSaveSlot(int index) {
            if (_layoutData == null || index == _layoutData.activeSlotIndex) return;

            Undo.RecordObject(_layoutData, "Change save slot");
            _layoutData.activeSlotIndex = index;
            EditorUtility.SetDirty(_layoutData);
            MarkDirty();

            ApplyActiveSaveProfile();
            _graphView?.RefreshAllBadges();
            Repaint();
        }

        private void CreateSaveSlot() {
            if (_layoutData == null) return;

            string slotName = EditorInputDialog.Show("New Save",
                "Name for the new save:", $"Save {_layoutData.saveSlots.Count + 1}");
            if (string.IsNullOrWhiteSpace(slotName)) return;

            slotName = slotName.Trim();
            if (_layoutData.saveSlots.Contains(slotName)) {
                EditorUtility.DisplayDialog("Duplicate Save Name",
                    $"'{slotName}' already exists in this graph.", "OK");
                return;
            }

            Undo.RecordObject(_layoutData, "Add save slot");
            _layoutData.saveSlots.Add(slotName);
            EditorUtility.SetDirty(_layoutData);
            MarkDirty();

            SelectSaveSlot(_layoutData.saveSlots.Count - 1);
        }

        private void DeleteActiveSaveSlot() {
            if (_layoutData == null || _layoutData.saveSlots.Count <= 1) return;

            // The erase below runs against whatever profile is active. In Play mode that is the
            // running game's, not this graph's, so deleting here would wipe the player's save.
            if (Application.isPlaying) {
                EditorUtility.DisplayDialog("Delete Save",
                    "Save slots cannot be deleted while in Play mode — the running game owns the " +
                    "active save profile.", "OK");
                return;
            }

            string slot = _layoutData.ActiveSlot;
            if (!EditorUtility.DisplayDialog("Delete Save",
                    $"Delete save '{slot}' from '{_layoutData.DisplayLabel}'?\n\n" +
                    "Its unlock state on disk is erased.",
                    "Delete", "Cancel"))
                return;

            // Erase the runtime file for this slot while it is still the active profile.
            ApplyActiveSaveProfile();
            UnlockManager.ResetSavedUnlocks();

            Undo.RecordObject(_layoutData, "Delete save slot");
            _layoutData.saveSlots.RemoveAt(_layoutData.activeSlotIndex);
            _layoutData.activeSlotIndex = Mathf.Clamp(_layoutData.activeSlotIndex, 0,
                _layoutData.saveSlots.Count - 1);
            EditorUtility.SetDirty(_layoutData);
            MarkDirty();

            ApplyActiveSaveProfile();
            _graphView?.RefreshAllBadges();
            Repaint();
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

            BuildDefinitionTypePathEntries();

            _condKnownPaths = DiscoverPaths("UnlockCondition", PREF_CONDITIONS_EXTRAS,
                DEFAULT_CONDITIONS_PATH);
            _condSelectedPathIdx = RestorePathSelection(_condKnownPaths,
                PREF_CONDITIONS_PATH, DEFAULT_CONDITIONS_PATH);

            _layoutKnownPaths = DiscoverLayoutDataPaths();
            _layoutSelectedPathIdx = RestorePathSelection(_layoutKnownPaths,
                PREF_LAYOUT_DATA_PATH, DEFAULT_LAYOUT_DATA_PATH);

            UpdateGraphViewPaths();
        }

        private void BuildDefinitionTypePathEntries() {
            _definitionTypePathEntries.Clear();

            string legacySeedPath = _defSelectedPathIdx >= 0 && _defSelectedPathIdx < _defKnownPaths.Count
                ? _defKnownPaths[_defSelectedPathIdx]
                : DEFAULT_DEFINITIONS_PATH;

            var definitionTypes = DiscoverDefinitionTypesFromRegistries();

            foreach (var definitionType in definitionTypes) {
                string typeKey = GetTypePrefsKeySuffix(definitionType);
                string selectedPrefKey = PREF_DEFINITION_TYPE_PATH_PREFIX + typeKey;
                string extrasPrefKey = PREF_DEFINITION_TYPE_EXTRAS_PREFIX + typeKey;

                string defaultPath = EditorPrefs.HasKey(selectedPrefKey)
                    ? DEFAULT_DEFINITIONS_PATH
                    : legacySeedPath;

                var knownPaths = DiscoverPathsForDefinitionType(definitionType, extrasPrefKey, defaultPath);
                int selectedIdx = RestorePathSelection(knownPaths, selectedPrefKey, defaultPath);

                _definitionTypePathEntries.Add(new DefinitionTypePathEntry {
                    DefinitionType = definitionType,
                    TypePrefsKey = selectedPrefKey,
                    ExtrasPrefsKey = extrasPrefKey,
                    KnownPaths = knownPaths,
                    SelectedPathIdx = selectedIdx,
                });
            }
        }

        private static List<Type> DiscoverDefinitionTypesFromRegistries() {
            var result = new HashSet<Type>();
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");

            foreach (string guid in guids) {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (so is not IUnlockableCatalog) continue;

                var definitionType = TryGetBaseRegistryDefinitionType(so.GetType());
                if (definitionType == null) continue;
                if (definitionType.IsAbstract) continue;
                if (!typeof(BaseDefinition).IsAssignableFrom(definitionType)) continue;
                if (!typeof(IUnlockable).IsAssignableFrom(definitionType)) continue;

                result.Add(definitionType);
            }

            return result
                .OrderBy(t => t.Name)
                .ThenBy(t => t.FullName)
                .ToList();
        }

        private static Type TryGetBaseRegistryDefinitionType(Type type) {
            while (type != null && type != typeof(object)) {
                if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(BaseRegistry<>)) {
                    var args = type.GetGenericArguments();
                    if (args.Length == 1) return args[0];
                }

                type = type.BaseType;
            }

            return null;
        }

        private static string GetTypePrefsKeySuffix(Type type) {
            var fullName = type.FullName;
            return string.IsNullOrWhiteSpace(fullName)
                ? type.Name
                : fullName.Replace('+', '.');
        }

        private static List<string> DiscoverPathsForDefinitionType(Type definitionType,
            string prefKeyExtras, string defaultPath) {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (string guid in guids) {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (obj == null || obj.GetType() != definitionType) continue;

                string dir = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir)) paths.Add(dir);
            }

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

            _graphView.DefinitionPathsByType.Clear();
            foreach (var entry in _definitionTypePathEntries) {
                if (entry.SelectedPathIdx < 0 || entry.SelectedPathIdx >= entry.KnownPaths.Count)
                    continue;

                _graphView.DefinitionPathsByType[entry.DefinitionType] =
                    entry.KnownPaths[entry.SelectedPathIdx];
            }

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
            // The filter lives on the graph asset, not EditorPrefs: it is what scopes a
            // graph to its level, so it must travel with the graph and with the team.
            var disabled = _layoutData != null
                ? new HashSet<string>(_layoutData.disabledRegistryNames, StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>();
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

        private void SaveDisabledRegistries() {
            if (_layoutData == null) return;

            Undo.RecordObject(_layoutData, "Change graph registry filter");
            _layoutData.disabledRegistryNames = _catalogEntries
                .Where(e => !e.Enabled)
                .Select(e => e.Name)
                .ToList();
            EditorUtility.SetDirty(_layoutData);
            MarkDirty();
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
            RefreshAvailableGraphs();

            // Prefer the graph this machine last had open.
            string activeGuid = EditorPrefs.GetString(PREF_ACTIVE_GRAPH_GUID, "");
            if (!string.IsNullOrEmpty(activeGuid)) {
                var remembered = AssetDatabase.LoadAssetAtPath<UnlockGraphLayoutData>(
                    AssetDatabase.GUIDToAssetPath(activeGuid));
                if (remembered != null) return remembered;
            }

            if (_availableGraphs.Count > 0) {
                EditorPrefs.SetString(PREF_ACTIVE_GRAPH_GUID, GetAssetGuid(_availableGraphs[0]));
                return _availableGraphs[0];
            }

            // No graph exists yet — create the first one at the configured path.
            var data = CreateGraphAsset(CreateInstance<UnlockGraphLayoutData>(), "UnlockGraphLayoutData");
            EditorPrefs.SetString(PREF_ACTIVE_GRAPH_GUID, GetAssetGuid(data));
            RefreshAvailableGraphs();
            return data;
        }
    }
}
