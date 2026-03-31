using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Graph node for non-trivial <see cref="UnlockCondition"/> assets
    /// (e.g. <see cref="FlagCondition"/>, custom conditions).
    /// <see cref="DefinitionUnlockedCondition"/> is rendered as a direct
    /// edge instead and does not use this node type.
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

        private Label _detailLabel;

        public ConditionNode(UnlockCondition condition, string assetGuid, string nodeId = null) {
            Condition = condition;
            AssetGuid = assetGuid;
            NodeId = nodeId ?? assetGuid;

            AddToClassList("condition-node");

            title = condition.name;
            tooltip = condition.GetType().Name;

            // Detail label with condition specifics
            _detailLabel = new Label(GetConditionDetail(condition));
            _detailLabel.AddToClassList("node-detail");
            mainContainer.Add(_detailLabel);

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
            _detailLabel.text = GetConditionDetail(Condition);
        }

        private static string GetConditionDetail(UnlockCondition condition) {
            if (condition is FlagCondition flag) {
                var so = new SerializedObject(flag);
                var collection = so.FindProperty("_collection").objectReferenceValue;
                var flagName = so.FindProperty("_flagName").stringValue;
                string collName = collection != null ? collection.name : "<none>";
                return $"Flag: {collName}.{flagName}";
            }

            return condition.GetType().Name;
        }
    }
}
