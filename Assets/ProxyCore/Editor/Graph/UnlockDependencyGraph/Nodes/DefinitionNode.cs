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

        /// <summary>Fired after the corner button locks or unlocks this definition.</summary>
        public event Action<DefinitionNode> OnUnlockStateToggled;

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
        private Button _toggleLockButton;
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

            // Quick lock/unlock — corner button in the node's title bar.
            if (definition is IUnlockable) {
                _toggleLockButton = new Button(ToggleUnlockState);
                _toggleLockButton.AddToClassList("definition-toggle-lock-btn");
                // Swallow the mouse-down so the node's double-click ping and the
                // SelectionDragger don't also react to a click on the button.
                _toggleLockButton.RegisterCallback<MouseDownEvent>(evt => evt.StopPropagation());
                titleButtonContainer.Insert(0, _toggleLockButton);
            }

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

            // Sets the badge, the toggle button, and the unlocked-by-default class.
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

        // ── Lock state ───────────────────────────────────────────────────

        private const string ICON_UNLOCKED = "🔓";
        private const string ICON_LOCKED = "🔒";

        private void ToggleUnlockState() {
            if (Definition is not IUnlockable unlockable) return;

            if (UnlockManager.IsUnlocked(unlockable))
                UnlockManager.Lock(unlockable);
            else
                UnlockManager.Unlock(unlockable);

            RefreshBadge();
            // An unlock can cascade through auto-unlock chains, so the view refreshes
            // every other node too.
            OnUnlockStateToggled?.Invoke(this);
        }

        /// <summary>
        /// Re-reads live unlock state onto the badge and the toggle button.
        /// Works in Edit Mode as well as Play Mode — SingletonSO resolves the
        /// UnlockManager asset through AssetDatabase when not playing.
        /// </summary>
        public void RefreshBadge() {
            if (Definition is not IUnlockable unlockable) return;

            bool unlocked = UnlockManager.IsUnlocked(unlockable);
            _badgeLabel.text = unlocked ? ICON_UNLOCKED : ICON_LOCKED;
            _badgeLabel.tooltip = unlockable.IsUnlockedByDefault
                ? "Unlocked by default" + (unlocked ? "" : " — overridden by an explicit Lock()")
                : null;

            // The class still marks "unlocked by default"; an explicit lock does not change that.
            EnableInClassList("unlocked-by-default", unlockable.IsUnlockedByDefault);

            if (_toggleLockButton != null) {
                _toggleLockButton.text = unlocked ? ICON_UNLOCKED : ICON_LOCKED;
                _toggleLockButton.tooltip = unlocked
                    ? "Lock this definition"
                    : "Unlock this definition";
            }
        }
    }
}
