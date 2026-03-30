using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph
{
    /// <summary>
    /// Graph node representing a <see cref="BaseDefinition"/> that implements
    /// <see cref="IUnlockable"/>. Shows name, type, lock-state badge,
    /// prerequisite mode, and in/out ports for dependency edges.
    /// </summary>
    public sealed class DefinitionNode : Node
    {
        public BaseDefinition Definition { get; private set; }
        public string AssetGuid { get; private set; }

        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        private Label _subtitleLabel;
        private Label _badgeLabel;
        private Label _conditionModeLabel;

        public DefinitionNode(BaseDefinition definition, string assetGuid)
        {
            Definition = definition;
            AssetGuid = assetGuid;

            AddToClassList("definition-node");

            title = definition.name;
            tooltip = $"{definition.GetType().Name}  (ID: {definition.ID})";

            // Subtitle — type name
            _subtitleLabel = new Label(definition.GetType().Name);
            _subtitleLabel.AddToClassList("node-subtitle");
            titleContainer.Add(_subtitleLabel);

            // Badge — lock state indicator
            _badgeLabel = new Label();
            _badgeLabel.AddToClassList("node-badge");
            titleContainer.Insert(0, _badgeLabel);

            // Condition mode label (if has prerequisites)
            if (definition is IHasPrerequisites hasPrereqs)
            {
                _conditionModeLabel = new Label(
                    hasPrereqs.PrerequisiteMode == ConditionMode.All ? "mode: ALL" : "mode: ANY");
                _conditionModeLabel.AddToClassList("condition-mode-label");
                mainContainer.Add(_conditionModeLabel);
            }

            // Unlocked-by-default visual class
            if (definition is IUnlockable unlockable && unlockable.IsUnlockedByDefault)
                AddToClassList("unlocked-by-default");

            RefreshBadge();

            // Ports
            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input,
                Port.Capacity.Multi, typeof(BaseDefinition));
            InputPort.portName = "Prerequisites";
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output,
                Port.Capacity.Multi, typeof(BaseDefinition));
            OutputPort.portName = "Dependents";
            outputContainer.Add(OutputPort);

            // Double-click → ping asset in Project window
            RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.clickCount == 2)
                {
                    EditorGUIUtility.PingObject(Definition);
                    Selection.activeObject = Definition;
                }
            });

            RefreshExpandedState();
            RefreshPorts();
        }

        public void RefreshBadge()
        {
            if (Definition is IUnlockable unlockable)
            {
                if (unlockable.IsUnlockedByDefault)
                    _badgeLabel.text = "\u2699"; // ⚙
                else if (Application.isPlaying && UnlockManager.IsUnlocked(unlockable))
                    _badgeLabel.text = "\uD83D\uDD13"; // 🔓
                else
                    _badgeLabel.text = "\uD83D\uDD12"; // 🔒
            }
        }
    }
}
