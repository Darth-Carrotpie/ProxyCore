using System;
using System.Collections.Generic;
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
        public event Action<DefinitionNode, string> OnPassStrategyChanged;
        public event Action<DefinitionNode, ConditionMode> OnPrerequisiteModeChanged;

        public sealed class PassStrategyChoice {
            public string StrategyId;
            public string Label;
        }

        private Label _subtitleLabel;
        private Label _badgeLabel;
        private VisualElement _passStateRow;
        private Label _passStateKeyLabel;
        private Label _passStateValueLabel;
        private VisualElement _conditionModeRow;
        private Label _conditionModeKeyLabel;
        private PopupField<string> _conditionModeDropdown;
        private PopupField<PassStrategyChoice> _passModePopup;
        private VisualElement _colorSwatch;
        private Color _typeColor;
        private string _selectedPassStrategyId;
        private List<PassStrategyChoice> _passStrategyChoices;

        private static readonly List<string> s_ModeChoices = new() { "All", "Any" };

        public DefinitionNode(BaseDefinition definition, string assetGuid, Color? typeColor = null,
            string passStateLabel = null,
            IReadOnlyList<PassStrategyChoice> passStrategyChoices = null,
            string selectedPassStrategyId = null) {
            Definition = definition;
            AssetGuid = assetGuid;

            AddToClassList("definition-node");

            title = definition.name;
            tooltip = $"{definition.GetType().Name}  (ID: {definition.ID})";
            ConfigureTitleWrapping();

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

            // Pass-state behavior can be switched per-node when multiple
            // compatible strategies are registered for this definition type.
            if (passStrategyChoices != null && passStrategyChoices.Count > 0) {
                _passStrategyChoices = new List<PassStrategyChoice>(passStrategyChoices);

                var selectedChoice = ResolveSelectedPassChoice(selectedPassStrategyId)
                    ?? _passStrategyChoices[0];
                _selectedPassStrategyId = selectedChoice.StrategyId;

                _passStateRow = new VisualElement();
                _passStateRow.AddToClassList("definition-mode-row");

                _passStateKeyLabel = new Label("Pass:");
                _passStateKeyLabel.AddToClassList("definition-mode-key");
                _passStateRow.Add(_passStateKeyLabel);

                if (_passStrategyChoices.Count > 1) {
                    _passModePopup = new PopupField<PassStrategyChoice>(
                        _passStrategyChoices,
                        selectedChoice,
                        c => c?.Label ?? string.Empty,
                        c => c?.Label ?? string.Empty);
                    _passModePopup.AddToClassList("definition-mode-dropdown");
                    _passModePopup.AddToClassList("pass-mode-dropdown");
                    _passModePopup.RegisterValueChangedCallback(OnPassModeChanged);
                    _passStateRow.Add(_passModePopup);
                }
                else {
                    _passStateValueLabel = new Label(selectedChoice.Label);
                    _passStateValueLabel.AddToClassList("definition-mode-value");
                    _passStateRow.Add(_passStateValueLabel);
                }

                mainContainer.Add(_passStateRow);
            }
            else if (!string.IsNullOrWhiteSpace(passStateLabel)) {
                _passStateRow = new VisualElement();
                _passStateRow.AddToClassList("definition-mode-row");

                _passStateKeyLabel = new Label("Pass:");
                _passStateKeyLabel.AddToClassList("definition-mode-key");
                _passStateRow.Add(_passStateKeyLabel);

                _passStateValueLabel = new Label(passStateLabel);
                _passStateValueLabel.AddToClassList("definition-mode-value");
                _passStateRow.Add(_passStateValueLabel);

                mainContainer.Add(_passStateRow);
            }

            // Condition mode selector (if has prerequisites)
            if (definition is IHasPrerequisites hasPrereqs) {
                BuildConditionModeUI(hasPrereqs.PrerequisiteMode);
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

        private void ConfigureTitleWrapping() {
            var titleLabel = this.Q<Label>("title-label");
            if (titleLabel == null) return;

            titleLabel.style.whiteSpace = WhiteSpace.Normal;
            titleLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
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

        private PassStrategyChoice ResolveSelectedPassChoice(string strategyId) {
            if (string.IsNullOrWhiteSpace(strategyId) || _passStrategyChoices == null)
                return null;

            for (int i = 0; i < _passStrategyChoices.Count; i++) {
                var choice = _passStrategyChoices[i];
                if (choice != null && choice.StrategyId == strategyId)
                    return choice;
            }

            return null;
        }

        private void OnPassModeChanged(ChangeEvent<PassStrategyChoice> evt) {
            var selected = evt.newValue;
            if (selected == null || string.IsNullOrWhiteSpace(selected.StrategyId))
                return;

            if (_selectedPassStrategyId == selected.StrategyId)
                return;

            _selectedPassStrategyId = selected.StrategyId;
            OnPassStrategyChanged?.Invoke(this, _selectedPassStrategyId);
        }

        private void BuildConditionModeUI(ConditionMode mode) {
            _conditionModeRow = new VisualElement();
            _conditionModeRow.AddToClassList("definition-mode-row");

            _conditionModeKeyLabel = new Label("Mode:");
            _conditionModeKeyLabel.AddToClassList("definition-mode-key");
            _conditionModeRow.Add(_conditionModeKeyLabel);

            string selected = mode == ConditionMode.All ? "All" : "Any";
            _conditionModeDropdown = new PopupField<string>(s_ModeChoices, selected);
            _conditionModeDropdown.AddToClassList("definition-mode-dropdown");
            _conditionModeDropdown.RegisterValueChangedCallback(OnConditionModeChanged);
            _conditionModeRow.Add(_conditionModeDropdown);

            mainContainer.Add(_conditionModeRow);
        }

        private void OnConditionModeChanged(ChangeEvent<string> evt) {
            var newMode = string.Equals(evt.newValue, "Any", StringComparison.OrdinalIgnoreCase)
                ? ConditionMode.Any
                : ConditionMode.All;

            var so = new SerializedObject(Definition);
            var modeProp = so.FindProperty("_prerequisiteMode");
            if (modeProp == null) {
                // Revert visual selection if the backing field is not writable.
                if (_conditionModeDropdown != null)
                    _conditionModeDropdown.SetValueWithoutNotify(evt.previousValue);
                return;
            }

            Undo.RecordObject(Definition, "Change prerequisite mode");
            modeProp.enumValueIndex = (int)newMode;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(Definition);

            OnPrerequisiteModeChanged?.Invoke(this, newMode);
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
