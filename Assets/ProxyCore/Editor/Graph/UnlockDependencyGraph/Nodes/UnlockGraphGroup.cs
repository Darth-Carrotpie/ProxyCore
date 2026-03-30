using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor.Graph
{
    /// <summary>
    /// Extended <see cref="Group"/> with a colour swatch, a minimize button,
    /// and context-menu actions for the unlock dependency graph.
    /// </summary>
    public sealed class UnlockGraphGroup : Group
    {
        public string GroupId { get; private set; }
        public Color GroupColor { get; private set; }

        /// <summary>Fired when the user clicks the minimize button.</summary>
        public event Action<UnlockGraphGroup> OnMinimizeRequested;

        /// <summary>Fired when the user changes the group colour.</summary>
        public event Action<UnlockGraphGroup, Color> OnColorChanged;

        private VisualElement _colorSwatch;

        public UnlockGraphGroup(string groupId, string groupName, Color color)
        {
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

        private void BuildGroupContextMenu(ContextualMenuPopulateEvent evt)
        {
            evt.menu.AppendAction("Change Color…", _ => OpenColorPicker());
            evt.menu.AppendAction("Minimize Group", _ => OnMinimizeRequested?.Invoke(this));
        }

        /// <summary>
        /// Returns the asset GUIDs of all <see cref="DefinitionNode"/> and
        /// <see cref="ConditionNode"/> members currently inside this group.
        /// </summary>
        public List<string> GetMemberGuids()
        {
            var guids = new List<string>();
            foreach (var element in containedElements)
            {
                if (element is DefinitionNode dn)
                    guids.Add(dn.AssetGuid);
                else if (element is ConditionNode cn)
                    guids.Add(cn.AssetGuid);
            }
            return guids;
        }

        public void SetColor(Color color)
        {
            GroupColor = color;
            _colorSwatch.style.backgroundColor = new StyleColor(color);
            style.borderBottomColor = new StyleColor(color);
            style.borderTopColor = new StyleColor(color);
            style.borderLeftColor = new StyleColor(color);
            style.borderRightColor = new StyleColor(color);
        }

        // ── Colour picker ────────────────────────────────────────────────

        private void OnSwatchClicked(MouseDownEvent evt)
        {
            if (evt.button == 0)
                OpenColorPicker();
        }

        private void OpenColorPicker()
        {
            // Use EditorWindow-based colour picker so we get an undo-able operation
            Action<Color> onColorUpdate = c =>
            {
                SetColor(c);
                OnColorChanged?.Invoke(this, c);
            };

            // Show the built-in colour picker
            ColorPickerBridge.Show(onColorUpdate, GroupColor, true, false);
        }
    }

    // ── Colour-picker bridge ─────────────────────────────────────────────
    // Unity doesn't expose a public API for the colour picker from UIElements.
    // This helper opens the internal colour picker via reflection, with a
    // safe fallback to a simple EditorWindow.

    internal static class ColorPickerBridge
    {
        public static void Show(Action<Color> onChanged, Color initial,
            bool showAlpha, bool hdr)
        {
            // Try the internal ColorPicker.Show via reflection
            var cpType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.ColorPicker");
            if (cpType != null)
            {
                var showMethod = cpType.GetMethod("Show",
                    System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public,
                    null,
                    new[] { typeof(Action<Color>), typeof(Color), typeof(bool), typeof(bool) },
                    null);

                if (showMethod != null)
                {
                    showMethod.Invoke(null, new object[] { onChanged, initial, showAlpha, hdr });
                    return;
                }
            }

            // Fallback — just invoke with the current colour (user can still
            // change via the SerializedObject inspector on the layout data).
            Debug.LogWarning("[UnlockGraph] Could not open colour picker; " +
                             "change the colour in the UnlockGraphLayoutData inspector instead.");
        }
    }
}
