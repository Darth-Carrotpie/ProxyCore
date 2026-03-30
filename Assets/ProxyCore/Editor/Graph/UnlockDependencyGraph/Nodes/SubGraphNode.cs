using System.Collections.Generic;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph
{
    /// <summary>
    /// Represents a collapsed <see cref="UnlockGraphGroup"/>.
    /// Aggregates input/output ports of all hidden member nodes so that
    /// cross-group edges still render correctly.
    /// Double-click expands the group back to its full view.
    /// </summary>
    public sealed class SubGraphNode : Node
    {
        public string GroupId { get; private set; }
        public string GroupName { get; private set; }
        public int MemberCount { get; private set; }

        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        /// <summary>Asset GUIDs of the nodes hidden inside this collapsed group.</summary>
        public List<string> HiddenMemberGuids { get; private set; }

        private Label _memberCountLabel;

        public SubGraphNode(string groupId, string groupName, Color groupColor,
            List<string> hiddenMemberGuids)
        {
            GroupId = groupId;
            GroupName = groupName;
            HiddenMemberGuids = hiddenMemberGuids ?? new List<string>();
            MemberCount = HiddenMemberGuids.Count;

            AddToClassList("subgraph-node");

            title = groupName;
            tooltip = $"Sub-graph: {groupName} ({MemberCount} nodes)";

            // Tint the title bar with the group colour
            var titleEl = titleContainer.Q("title-label");
            if (titleEl != null)
                titleEl.style.backgroundColor = new StyleColor(groupColor);

            _memberCountLabel = new Label($"{MemberCount} node{(MemberCount == 1 ? "" : "s")}");
            _memberCountLabel.AddToClassList("member-count-label");
            mainContainer.Add(_memberCountLabel);

            // Aggregated ports
            InputPort = InstantiatePort(Orientation.Horizontal, Direction.Input,
                Port.Capacity.Multi, typeof(BaseDefinition));
            InputPort.portName = "In";
            inputContainer.Add(InputPort);

            OutputPort = InstantiatePort(Orientation.Horizontal, Direction.Output,
                Port.Capacity.Multi, typeof(BaseDefinition));
            OutputPort.portName = "Out";
            outputContainer.Add(OutputPort);

            RefreshExpandedState();
            RefreshPorts();
        }
    }
}
