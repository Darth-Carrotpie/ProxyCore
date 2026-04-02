using System;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Graph node representing a <see cref="BaseDefinition"/> that implements
    /// <see cref="IUnlockable"/>. Shows name, type, lock-state badge,
    /// prerequisite mode, and in/out ports for dependency edges.
    /// </summary>
    public sealed class DefinitionNode : Node {
        public BaseDefinition Definition { get; private set; }
        public string AssetGuid { get; private set; }

        public Port InputPort { get; private set; }
        public Port OutputPort { get; private set; }

        /// <summary>Fired when the user changes the definition-type colour via the swatch.</summary>
        public event Action<DefinitionNode, Color> OnTypeColorChanged;

        private Label _subtitleLabel;
        private Label _badgeLabel;
        private Label _conditionModeLabel;
        private VisualElement _colorSwatch;
        private Color _typeColor;

        public DefinitionNode(BaseDefinition definition, string assetGuid, Color? typeColor = null) {
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

            // Type-colour swatch (clickable → colour picker for this definition type)
            _typeColor = typeColor ?? new Color(45f / 255f, 100f / 255f, 160f / 255f, 0.85f);
            _colorSwatch = new VisualElement();
            _colorSwatch.AddToClassList("definition-type-color-swatch");
            _colorSwatch.style.backgroundColor = new StyleColor(_typeColor);
            _colorSwatch.RegisterCallback<MouseDownEvent>(OnSwatchClicked);
            titleContainer.Insert(0, _colorSwatch);

            // Condition mode label (if has prerequisites)
            if (definition is IHasPrerequisites hasPrereqs) {
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
            RegisterCallback<MouseDownEvent>(evt => {
                if (evt.clickCount == 2) {
                    EditorGUIUtility.PingObject(Definition);
                    Selection.activeObject = Definition;
                }
            });

            RefreshExpandedState();
            RefreshPorts();

            // Apply initial type colour to title
            if (typeColor.HasValue)
                ApplyTypeColorToTitle(_typeColor);
        }

        public void SetTypeColor(Color color) {
            _typeColor = color;
            _colorSwatch.style.backgroundColor = new StyleColor(color);
            ApplyTypeColorToTitle(color);
        }

        private void ApplyTypeColorToTitle(Color color) {
            var titleElement = this.Q("title");
            if (titleElement != null)
                titleElement.style.backgroundColor = new StyleColor(color);
        }

        // ── Colour picker ────────────────────────────────────────────────

        private void OnSwatchClicked(MouseDownEvent evt) {
            if (evt.button == 0) {
                evt.StopPropagation();
                OpenColorPicker(evt);
            }
        }

        private void OpenColorPicker(MouseDownEvent evt) {
            // Compute screen position of the swatch for picker placement
            var swatchWorldPos = _colorSwatch.LocalToWorld(Vector2.zero);
            var screenPos = new Vector2(swatchWorldPos.x, swatchWorldPos.y);
            // Convert panel coordinates to screen coordinates via the editor window
            if (_colorSwatch.panel?.visualTree != null) {
                var panelPos = _colorSwatch.worldBound;
                screenPos = GUIUtility.GUIToScreenPoint(
                    new Vector2(panelPos.x, panelPos.yMax));
            }

            Action<Color> onColorUpdate = c => {
                SetTypeColor(c);
                OnTypeColorChanged?.Invoke(this, c);
            };
            ColorPickerBridge.Show(onColorUpdate, _typeColor, true, false, screenPos);
        }

        public void RefreshBadge() {
            if (Definition is IUnlockable unlockable) {
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
