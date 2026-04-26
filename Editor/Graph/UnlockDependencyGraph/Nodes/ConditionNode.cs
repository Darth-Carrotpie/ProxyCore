using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Graph node for non-trivial <see cref="UnlockCondition"/> assets
    /// (e.g. <see cref="FlagCondition"/>, custom conditions).
    /// Conditions recognized as direct edges by definition-edge strategies
    /// are rendered as direct links instead and do not use this node type.
    ///
    /// For <see cref="FlagCondition"/>, the node displays the collection
    /// and flag name on separate color-coded rows with inline dropdowns
    /// for editing directly from the graph.
    /// </summary>
    public sealed class ConditionNode : Node {
        public UnlockCondition Condition { get; private set; }
        public string AssetGuid { get; private set; }

        /// <summary>
        /// Unique identifier for this visual node instance.
        /// Differs from <see cref="AssetGuid"/> when duplicates of the
        /// same condition asset exist on the graph.
        /// </summary>
        public string NodeId { get; private set; }

        public Port OutputPort { get; private set; }

        // Generic fallback detail label (non-FlagCondition types)
        private Label _detailLabel;

        // FlagCondition structured UI
        private VisualElement _flagDetailContainer;
        private PopupField<string> _collectionDropdown;
        private PopupField<string> _flagDropdown;
        private Label _collectionValueLabel;
        private Label _flagValueLabel;

        public ConditionNode(UnlockCondition condition, string assetGuid, string nodeId = null) {
            Condition = condition;
            AssetGuid = assetGuid;
            NodeId = nodeId ?? assetGuid;

            AddToClassList("condition-node");

            title = condition.name;
            tooltip = condition.GetType().Name;
            ConfigureTitleWrapping();

            // Build the detail area — structured for FlagCondition, plain label otherwise
            BuildDetailUI(condition);

            // Output port — connects to a DefinitionNode's input
            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output,
                Port.Capacity.Multi, typeof(UnlockCondition));
            OutputPort.portName = "Gates";
            outputContainer.Add(OutputPort);

            // Double-click → ping asset
            RegisterCallback<MouseDownEvent>(evt => {
                if (evt.clickCount == 2) {
                    EditorGUIUtility.PingObject(Condition);
                    Selection.activeObject = Condition;
                }
            });

            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshDetail() {
            if (_flagDetailContainer != null) {
                _flagDetailContainer.RemoveFromHierarchy();
                _flagDetailContainer = null;
            }
            if (_detailLabel != null) {
                _detailLabel.RemoveFromHierarchy();
                _detailLabel = null;
            }
            BuildDetailUI(Condition);
        }

        // ── Detail UI construction ───────────────────────────────────

        private void BuildDetailUI(UnlockCondition condition) {
            if (condition is FlagCondition)
                BuildFlagConditionUI(condition as FlagCondition);
            else
                BuildGenericDetailUI(condition);
        }

        private void BuildGenericDetailUI(UnlockCondition condition) {
            _detailLabel = new Label(condition.GetType().Name);
            _detailLabel.AddToClassList("node-detail");
            mainContainer.Add(_detailLabel);
        }

        private void BuildFlagConditionUI(FlagCondition flag) {
            var so = new SerializedObject(flag);
            var collectionRef = so.FindProperty("_collection").objectReferenceValue as GameFlagCollection;
            var flagName = so.FindProperty("_flagName").stringValue;

            _flagDetailContainer = new VisualElement();
            _flagDetailContainer.AddToClassList("flag-detail-container");

            // ── Collection row ───────────────────────────────────────
            var collRow = new VisualElement();
            collRow.AddToClassList("flag-detail-row");

            var collKey = new Label("Collection:");
            collKey.AddToClassList("flag-detail-key");
            collRow.Add(collKey);

            var collections = FindAllCollections();
            if (collections.Count > 0) {
                var collNames = collections.Select(c => c.name).ToList();
                string currentCollName = collectionRef != null ? collectionRef.name : collNames[0];
                if (!collNames.Contains(currentCollName))
                    currentCollName = collNames[0];

                _collectionDropdown = new PopupField<string>(collNames, currentCollName);
                _collectionDropdown.AddToClassList("flag-dropdown");
                _collectionDropdown.RegisterValueChangedCallback(OnCollectionChanged);
                collRow.Add(_collectionDropdown);
            }
            else {
                var noneLabel = new Label("<no collections>");
                noneLabel.AddToClassList("flag-none-label");
                collRow.Add(noneLabel);
            }

            _flagDetailContainer.Add(collRow);

            // ── Flag row ─────────────────────────────────────────────
            var flagRow = new VisualElement();
            flagRow.AddToClassList("flag-detail-row");

            var flagKey = new Label("Flag:");
            flagKey.AddToClassList("flag-detail-key");
            flagRow.Add(flagKey);

            if (collectionRef != null) {
                var flags = collectionRef.GetDefinedFlagsArray();
                if (flags.Length > 0) {
                    var flagList = flags.ToList();
                    string currentFlag = !string.IsNullOrEmpty(flagName) && flagList.Contains(flagName)
                        ? flagName : flagList[0];
                    _flagDropdown = new PopupField<string>(flagList, currentFlag);
                    _flagDropdown.AddToClassList("flag-dropdown");
                    _flagDropdown.RegisterValueChangedCallback(OnFlagChanged);
                    flagRow.Add(_flagDropdown);
                }
                else {
                    var noneLabel = new Label("<no flags defined>");
                    noneLabel.AddToClassList("flag-none-label");
                    flagRow.Add(noneLabel);
                }
            }
            else {
                var noneLabel = new Label("<select collection>");
                noneLabel.AddToClassList("flag-none-label");
                flagRow.Add(noneLabel);
            }

            _flagDetailContainer.Add(flagRow);
            mainContainer.Add(_flagDetailContainer);
        }

        // ── Dropdown callbacks ───────────────────────────────────────

        private void OnCollectionChanged(ChangeEvent<string> evt) {
            var collections = FindAllCollections();
            var selected = collections.FirstOrDefault(c => c.name == evt.newValue);
            if (selected == null) return;

            var so = new SerializedObject(Condition);
            Undo.RecordObject(Condition, "Change FlagCondition Collection");
            so.FindProperty("_collection").objectReferenceValue = selected;

            // Reset flag to the first available in the new collection
            var flags = selected.GetDefinedFlagsArray();
            so.FindProperty("_flagName").stringValue = flags.Length > 0 ? flags[0] : "";
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(Condition);

            RefreshDetail();
        }

        private void OnFlagChanged(ChangeEvent<string> evt) {
            var so = new SerializedObject(Condition);
            Undo.RecordObject(Condition, "Change FlagCondition Flag");
            so.FindProperty("_flagName").stringValue = evt.newValue;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(Condition);
        }

        // ── Helpers ──────────────────────────────────────────────────

        private static List<GameFlagCollection> FindAllCollections() {
            var guids = AssetDatabase.FindAssets("t:GameFlagCollection");
            var result = new List<GameFlagCollection>(guids.Length);
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var coll = AssetDatabase.LoadAssetAtPath<GameFlagCollection>(path);
                if (coll != null) result.Add(coll);
            }
            return result;
        }

        private void ConfigureTitleWrapping() {
            var titleLabel = this.Q<Label>("title-label");
            if (titleLabel == null) return;

            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
        }
    }
}
