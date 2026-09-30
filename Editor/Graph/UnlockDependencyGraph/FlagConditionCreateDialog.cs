using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Modal dialog for creating a <see cref="FlagCondition"/> asset with
    /// an assigned <see cref="GameFlagCollection"/> and flag name — all in
    /// one step, directly from the Unlock Dependency Graph.
    /// </summary>
    internal sealed class FlagConditionCreateDialog : EditorWindow {
        private string _assetName = "FlagCondition";
        private GameFlagCollection[] _collections;
        private string[] _collectionNames;
        private int _selectedCollection;
        private string[] _flags;
        private int _selectedFlag;
        private bool _confirmed;
        private bool _focused;

        // Result fields (read after ShowModal returns)
        public bool Confirmed => _confirmed;
        public string AssetName => _assetName;
        public GameFlagCollection SelectedCollection =>
            _collections != null && _selectedCollection >= 0 && _selectedCollection < _collections.Length
                ? _collections[_selectedCollection] : null;
        public string SelectedFlagName =>
            _flags != null && _selectedFlag >= 0 && _selectedFlag < _flags.Length
                ? _flags[_selectedFlag] : "";

        public static FlagConditionCreateDialog Show() {
            var dlg = CreateInstance<FlagConditionCreateDialog>();
            dlg.titleContent = new GUIContent("Create Flag Condition");
            dlg.minSize = new Vector2(360, 160);
            dlg.maxSize = new Vector2(360, 160);
            dlg.Init();
            dlg.ShowModal();
            return dlg;
        }

        private void Init() {
            var guids = AssetDatabase.FindAssets("t:GameFlagCollection");
            _collections = guids
                .Select(g => AssetDatabase.LoadAssetAtPath<GameFlagCollection>(
                    AssetDatabase.GUIDToAssetPath(g)))
                .Where(c => c != null)
                .ToArray();
            _collectionNames = _collections.Select(c => c.name).ToArray();
            _selectedCollection = 0;
            RefreshFlags();
        }

        private void RefreshFlags() {
            if (_collections != null && _selectedCollection >= 0 && _selectedCollection < _collections.Length) {
                _flags = _collections[_selectedCollection].GetDefinedFlagsArray();
                _selectedFlag = 0;
            }
            else {
                _flags = new string[0];
                _selectedFlag = -1;
            }
        }

        private void OnGUI() {
            EditorGUILayout.Space(8);

            // Asset name
            GUI.SetNextControlName("NameField");
            _assetName = EditorGUILayout.TextField("Asset Name", _assetName);
            if (!_focused) {
                EditorGUI.FocusTextInControl("NameField");
                _focused = true;
            }

            EditorGUILayout.Space(4);

            // Collection picker
            if (_collectionNames.Length > 0) {
                int prev = _selectedCollection;
                _selectedCollection = EditorGUILayout.Popup("Collection", _selectedCollection, _collectionNames);
                if (_selectedCollection != prev) RefreshFlags();
            }
            else {
                EditorGUILayout.HelpBox(
                    "No GameFlagCollection assets found in the project.\n" +
                    "Create one first (Create → Flags → Game Flag Collection).",
                    MessageType.Warning);
            }

            // Flag picker
            if (_flags != null && _flags.Length > 0)
                _selectedFlag = EditorGUILayout.Popup("Flag", _selectedFlag, _flags);
            else if (_collectionNames.Length > 0)
                EditorGUILayout.HelpBox("Selected collection has no declared flags.", MessageType.Info);

            EditorGUILayout.Space(4);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            bool canCreate = !string.IsNullOrWhiteSpace(_assetName) &&
                             _collectionNames.Length > 0 &&
                             _flags != null && _flags.Length > 0;

            EditorGUI.BeginDisabledGroup(!canCreate);
            if (GUILayout.Button("Create", GUILayout.Width(80)) ||
                (canCreate && Event.current.type == EventType.KeyDown &&
                 Event.current.keyCode == KeyCode.Return)) {
                _confirmed = true;
                Close();
            }
            EditorGUI.EndDisabledGroup();

            if (GUILayout.Button("Cancel", GUILayout.Width(80)) ||
                (Event.current.type == EventType.KeyDown &&
                 Event.current.keyCode == KeyCode.Escape)) {
                Close();
            }

            EditorGUILayout.EndHorizontal();
        }
    }
}
