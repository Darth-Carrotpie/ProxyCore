using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Editor window for inspecting live unlock state during Play Mode.
    /// Shows keys in the Saved (disk) and Session (memory) sets, and provides
    /// reset buttons wired to UnlockManager without requiring the Console.
    /// </summary>
    public class UnlockDebugWindow : EditorWindow
    {
        // ── Layout ─────────────────────────────────────────────────────

        private const float TOOLBAR_HEIGHT = 22f;
        private const float COLUMN_GAP = 6f;

        private Vector2 _savedScroll;
        private Vector2 _sessionScroll;

        // ── Styles (lazy) ──────────────────────────────────────────────

        private static GUIStyle _headerStyle;
        private static GUIStyle _keyStyle;
        private static GUIStyle _emptyLabelStyle;

        // ── Menu & public API ──────────────────────────────────────────

        [MenuItem("ProxyCore/Unlock Debug Window")]
        public static void ShowWindow()
        {
            var w = GetWindow<UnlockDebugWindow>();
            w.titleContent = new GUIContent("Unlock Debug");
            w.minSize = new Vector2(420f, 300f);
            w.Show();
        }

        // ── Lifecycle ──────────────────────────────────────────────────

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate()
        {
            // Repaint at ~4 Hz while playing so live changes are visible.
            if (Application.isPlaying)
                Repaint();
        }

        // ── GUI ────────────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();

            DrawToolbar();

            if (!Application.isPlaying)
            {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Enter Play Mode to inspect unlock state.", _emptyLabelStyle);
                    GUILayout.FlexibleSpace();
                }
                GUILayout.FlexibleSpace();
                return;
            }

            var manager = UnlockManager.Instance;
            if (manager == null)
            {
                EditorGUILayout.HelpBox("No UnlockManager instance found.", MessageType.Warning);
                return;
            }

            GUILayout.Space(4f);

            using (new EditorGUILayout.HorizontalScope())
            {
                DrawKeyList("Saved (Disk)",
                    manager.SavedUnlockedKeys,
                    ref _savedScroll,
                    new Color(0.35f, 0.65f, 0.35f)); // green tint

                GUILayout.Space(COLUMN_GAP);

                DrawKeyList("Session (Memory)",
                    manager.SessionUnlockedKeys,
                    ref _sessionScroll,
                    new Color(0.45f, 0.60f, 0.85f)); // blue tint
            }
        }

        private void DrawToolbar()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(TOOLBAR_HEIGHT)))
            {
                GUILayout.Label("Unlock State", EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!Application.isPlaying))
                {
                    if (GUILayout.Button("Reset Saved", EditorStyles.toolbarButton))
                    {
                        UnlockManager.Instance?.ResetSavedUnlocks();
                    }

                    if (GUILayout.Button("Reset Session", EditorStyles.toolbarButton))
                    {
                        UnlockManager.Instance?.ResetSessionUnlocks();
                    }
                }
            }
        }

        private void DrawKeyList(string title, IReadOnlyCollection<string> keys,
            ref Vector2 scroll, Color accentColor)
        {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true)))
            {
                // Column header
                var prevColor = GUI.color;
                GUI.color = accentColor;
                GUILayout.Label($"{title}  ({keys.Count})", _headerStyle);
                GUI.color = prevColor;

                EditorGUILayout.Space(2f);

                // Key list
                using (var sv = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true)))
                {
                    scroll = sv.scrollPosition;

                    if (keys.Count == 0)
                    {
                        GUILayout.Label("— none —", _emptyLabelStyle);
                    }
                    else
                    {
                        foreach (var key in keys)
                            GUILayout.Label(key, _keyStyle);
                    }
                }
            }
        }

        // ── Style helpers ──────────────────────────────────────────────

        private static void EnsureStyles()
        {
            if (_headerStyle != null) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                padding = new RectOffset(4, 4, 2, 2),
            };

            _keyStyle = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(6, 4, 1, 1),
                wordWrap = false,
            };

            _emptyLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontStyle = FontStyle.Italic,
            };
        }
    }
}
