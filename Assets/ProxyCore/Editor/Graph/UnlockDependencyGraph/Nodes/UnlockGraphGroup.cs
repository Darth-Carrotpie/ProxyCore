using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Extended <see cref="Group"/> with a colour swatch, a minimize button,
    /// and context-menu actions for the unlock dependency graph.
    /// </summary>
    public sealed class UnlockGraphGroup : Group {
        public string GroupId { get; private set; }
        public Color GroupColor { get; private set; }

        /// <summary>Fired when the user clicks the minimize button.</summary>
        public event Action<UnlockGraphGroup> OnMinimizeRequested;

        /// <summary>Fired when the user changes the group colour.</summary>
        public event Action<UnlockGraphGroup, Color> OnColorChanged;

        private VisualElement _colorSwatch;

        public UnlockGraphGroup(string groupId, string groupName, Color color) {
            GroupId = groupId;
            GroupColor = color;

            AddToClassList("unlock-graph-group");

            title = groupName;
            style.borderBottomColor = new StyleColor(color);
            style.borderTopColor = new StyleColor(color);
            style.borderLeftColor = new StyleColor(color);
            style.borderRightColor = new StyleColor(color);

            // Colour swatch (clickable → colour picker)
            _colorSwatch = new VisualElement();
            _colorSwatch.AddToClassList("group-color-swatch");
            _colorSwatch.style.backgroundColor = new StyleColor(color);
            _colorSwatch.RegisterCallback<MouseDownEvent>(OnSwatchClicked);

            // Minimize button
            var minimizeBtn = new Button(() => OnMinimizeRequested?.Invoke(this));
            minimizeBtn.text = "\u25BC"; // ▼
            minimizeBtn.tooltip = "Collapse group to a single node";
            minimizeBtn.AddToClassList("group-minimize-btn");

            // Insert swatch and button into the header
            var header = headerContainer;
            header.Insert(0, _colorSwatch);
            header.Add(minimizeBtn);

            // Context menu via manipulator (Group has no virtual BuildContextualMenu)
            this.AddManipulator(new ContextualMenuManipulator(BuildGroupContextMenu));
        }

        private void BuildGroupContextMenu(ContextualMenuPopulateEvent evt) {
            evt.menu.AppendAction("Change Color…", _ => OpenColorPicker());
            evt.menu.AppendAction("Minimize Group", _ => OnMinimizeRequested?.Invoke(this));
        }

        /// <summary>
        /// Returns the asset GUIDs of all <see cref="DefinitionNode"/> and
        /// <see cref="ConditionNode"/> members currently inside this group.
        /// </summary>
        public List<string> GetMemberGuids() {
            var guids = new List<string>();
            foreach (var element in containedElements) {
                if (element is DefinitionNode dn)
                    guids.Add(dn.AssetGuid);
                else if (element is ConditionNode cn)
                    guids.Add(cn.NodeId);
            }
            return guids;
        }

        public void SetColor(Color color) {
            GroupColor = color;
            _colorSwatch.style.backgroundColor = new StyleColor(color);
            style.borderBottomColor = new StyleColor(color);
            style.borderTopColor = new StyleColor(color);
            style.borderLeftColor = new StyleColor(color);
            style.borderRightColor = new StyleColor(color);
        }

        // ── Colour picker ────────────────────────────────────────────────

        private void OnSwatchClicked(MouseDownEvent evt) {
            if (evt.button == 0)
                OpenColorPicker();
        }

        private void OpenColorPicker() {
            // Use EditorWindow-based colour picker so we get an undo-able operation
            Action<Color> onColorUpdate = c => {
                SetColor(c);
                OnColorChanged?.Invoke(this, c);
            };

            // Show the built-in colour picker
            ColorPickerBridge.Show(onColorUpdate, GroupColor, true, false);
        }
    }

    // ── Colour-picker bridge ─────────────────────────────────────────────
    // Unity doesn't expose a public colour picker API for UIElements.
    // This helper tries multiple internal signatures via reflection,
    // falling back to a small EditorWindow with a ColorField.

    internal static class ColorPickerBridge {
        public static void Show(Action<Color> onChanged, Color initial,
            bool showAlpha, bool hdr) {
            var cpType = typeof(UnityEditor.Editor).Assembly
                .GetType("UnityEditor.ColorPicker");

            if (cpType != null) {
                // Unity 6 signature: Show(Action<Color>, Color, bool, bool)
                var show4 = cpType.GetMethod("Show",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public,
                    null,
                    new[] { typeof(Action<Color>), typeof(Color),
                            typeof(bool), typeof(bool) },
                    null);

                if (show4 != null) {
                    show4.Invoke(null, new object[] { onChanged, initial, showAlpha, hdr });
                    return;
                }

                // Older Unity: Show(GUIView, Action<Color>, Color, bool, bool)
                var guiViewType = typeof(UnityEditor.Editor).Assembly
                    .GetType("UnityEditor.GUIView");
                if (guiViewType != null) {
                    var show5 = cpType.GetMethod("Show",
                        System.Reflection.BindingFlags.Static |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public,
                        null,
                        new[] { guiViewType, typeof(Action<Color>), typeof(Color),
                                typeof(bool), typeof(bool) },
                        null);
                    if (show5 != null) {
                        show5.Invoke(null, new object[] { null, onChanged, initial, showAlpha, hdr });
                        return;
                    }
                }
            }

            // Fallback — tiny EditorWindow hosting a ColorField
            ColorPickerFallbackWindow.Open(onChanged, initial, showAlpha);
        }
    }

    /// <summary>
    /// Minimal EditorWindow fallback when the internal colour picker
    /// cannot be opened via reflection.
    /// </summary>
    internal sealed class ColorPickerFallbackWindow : EditorWindow {
        private Action<Color> _onChanged;
        private Color _color;
        private bool _showAlpha;

        public static void Open(Action<Color> onChanged, Color initial, bool showAlpha) {
            var w = GetWindow<ColorPickerFallbackWindow>(true, "Pick Color");
            w._onChanged = onChanged;
            w._color = initial;
            w._showAlpha = showAlpha;
            w.minSize = new Vector2(230, 60);
            w.maxSize = new Vector2(230, 60);
            w.ShowUtility();
        }

        private void OnGUI() {
            EditorGUI.BeginChangeCheck();
            _color = EditorGUILayout.ColorField(
                new GUIContent("Color"), _color, true, _showAlpha, false);
            if (EditorGUI.EndChangeCheck())
                _onChanged?.Invoke(_color);

            if (GUILayout.Button("Done"))
                Close();
        }
    }
}
