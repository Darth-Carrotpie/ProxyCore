using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor {
    /// <summary>
    /// Editor window for inspecting live unlock state during Play Mode.
    /// Shows keys in the Saved (disk), Session (memory) and Locked (override) sets with their
    /// provenance — unlock ordinal, age, and whether the game has acknowledged them — and
    /// provides reset and acknowledgement actions wired to UnlockManager without the Console.
    /// </summary>
    public class UnlockDebugWindow : EditorWindow {
        // ── Layout ─────────────────────────────────────────────────────

        private const float TOOLBAR_HEIGHT = 22f;
        private const float COLUMN_GAP = 6f;
        private const float TOGGLE_WIDTH = 16f;
        private const float ORDINAL_WIDTH = 42f;
        private const float AGE_WIDTH = 62f;

        private Vector2 _savedScroll;
        private Vector2 _sessionScroll;
        private Vector2 _lockedScroll;

        private bool _unacknowledgedOnly;

        // ── Styles (lazy) ──────────────────────────────────────────────

        private static GUIStyle _headerStyle;
        private static GUIStyle _keyStyle;
        private static GUIStyle _emptyLabelStyle;
        private static GUIStyle _metaStyle;

        // ── Menu & public API ──────────────────────────────────────────

        [MenuItem("ProxyCore/Unlock Debug Window")]
        public static void ShowWindow() {
            var w = GetWindow<UnlockDebugWindow>();
            w.titleContent = new GUIContent("Unlock Debug");
            w.minSize = new Vector2(560f, 300f);
            w.Show();
        }

        // ── Lifecycle ──────────────────────────────────────────────────

        private void OnEnable() {
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable() {
            EditorApplication.update -= OnEditorUpdate;
        }

        private void OnEditorUpdate() {
            // Repaint at ~4 Hz while playing so live changes are visible.
            if (Application.isPlaying)
                Repaint();
        }

        // ── GUI ────────────────────────────────────────────────────────

        private void OnGUI() {
            EnsureStyles();

            DrawToolbar();

            if (!Application.isPlaying) {
                GUILayout.FlexibleSpace();
                using (new EditorGUILayout.HorizontalScope()) {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.LabelField("Enter Play Mode to inspect unlock state.", _emptyLabelStyle);
                    GUILayout.FlexibleSpace();
                }
                GUILayout.FlexibleSpace();
                return;
            }

            var manager = UnlockManager.Instance;
            if (manager == null) {
                EditorGUILayout.HelpBox("No UnlockManager instance found.", MessageType.Warning);
                return;
            }

            GUILayout.Space(4f);

            // Marker -1 is below every ordinal (including the 0 given to migrated keys), so this
            // is every record, already in the stable order the query API guarantees.
            var all = UnlockManager.GetUnlocksSince(-1);
            var saved = new List<UnlockRecord>();
            var session = new List<UnlockRecord>();
            for (int i = 0; i < all.Count; i++) {
                if (_unacknowledgedOnly && all[i].Acknowledged) continue;
                if (all[i].IsSessionOnly) session.Add(all[i]);
                else saved.Add(all[i]);
            }

            using (new EditorGUILayout.HorizontalScope()) {
                DrawRecordList("Saved (Disk)", saved, ref _savedScroll,
                    new Color(0.35f, 0.65f, 0.35f)); // green tint

                GUILayout.Space(COLUMN_GAP);

                DrawRecordList("Session (Memory)", session, ref _sessionScroll,
                    new Color(0.45f, 0.60f, 0.85f)); // blue tint

                GUILayout.Space(COLUMN_GAP);

                // Locked keys carry no provenance — a record exists only while a key is
                // unlocked — so this column stays a plain key list.
                DrawKeyList("Locked (Override)", UnlockManager.LockedOverrideKeys, ref _lockedScroll,
                    new Color(0.85f, 0.55f, 0.40f)); // amber tint
            }
        }

        private void DrawToolbar() {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar, GUILayout.Height(TOOLBAR_HEIGHT))) {
                GUILayout.Label("Unlock State", EditorStyles.boldLabel);

                if (Application.isPlaying) {
                    GUILayout.Label($"Marker: {UnlockManager.UnlockMarker}", EditorStyles.miniLabel);
                }

                GUILayout.FlexibleSpace();

                using (new EditorGUI.DisabledScope(!Application.isPlaying)) {
                    _unacknowledgedOnly = GUILayout.Toggle(_unacknowledgedOnly, "Unacknowledged only",
                        EditorStyles.toolbarButton);

                    if (GUILayout.Button("Acknowledge All", EditorStyles.toolbarButton)) {
                        UnlockManager.AcknowledgeAllUnlocked();
                    }

                    if (GUILayout.Button("Reset Saved", EditorStyles.toolbarButton)) {
                        UnlockManager.ResetSavedUnlocks();
                    }

                    if (GUILayout.Button("Reset Session", EditorStyles.toolbarButton)) {
                        UnlockManager.ResetSessionUnlocks();
                    }
                }
            }
        }

        private void DrawRecordList(string title, IReadOnlyList<UnlockRecord> records,
            ref Vector2 scroll, Color accentColor) {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true))) {
                DrawColumnHeader(title, records.Count, accentColor);

                using (var sv = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true))) {
                    scroll = sv.scrollPosition;

                    if (records.Count == 0) {
                        GUILayout.Label("— none —", _emptyLabelStyle);
                    }
                    else {
                        for (int i = 0; i < records.Count; i++)
                            DrawRecordRow(records[i]);
                    }
                }
            }
        }

        private void DrawRecordRow(UnlockRecord record) {
            using (new EditorGUILayout.HorizontalScope()) {
                bool seen = EditorGUILayout.Toggle(record.Acknowledged, GUILayout.Width(TOGGLE_WIDTH));
                if (seen != record.Acknowledged)
                    UnlockManager.SetAcknowledgedByKey(record.Key, seen);

                GUILayout.Label($"#{record.Ordinal}", _metaStyle, GUILayout.Width(ORDINAL_WIDTH));
                GUILayout.Label(record.Key, _keyStyle);
                GUILayout.FlexibleSpace();
                GUILayout.Label(FormatAge(record), _metaStyle, GUILayout.Width(AGE_WIDTH));
            }
        }

        private void DrawKeyList(string title, IReadOnlyCollection<string> keys,
            ref Vector2 scroll, Color accentColor) {
            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true))) {
                DrawColumnHeader(title, keys == null ? 0 : keys.Count, accentColor);

                using (var sv = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.ExpandHeight(true))) {
                    scroll = sv.scrollPosition;

                    if (keys == null || keys.Count == 0) {
                        GUILayout.Label("— none —", _emptyLabelStyle);
                    }
                    else {
                        foreach (var key in keys)
                            GUILayout.Label(key, _keyStyle);
                    }
                }
            }
        }

        private static void DrawColumnHeader(string title, int count, Color accentColor) {
            var prevColor = GUI.color;
            GUI.color = accentColor;
            GUILayout.Label($"{title}  ({count})", _headerStyle);
            GUI.color = prevColor;

            EditorGUILayout.Space(2f);
        }

        /// <summary>
        /// Relative age from the record's timestamp. A key migrated from a save written before
        /// provenance existed has no timestamp and reads as a dash, which is how the window
        /// makes a back-catalogue key visibly distinct from one unlocked in this session.
        /// </summary>
        private static string FormatAge(UnlockRecord record) {
            if (!record.HasTimestamp) return "—";

            var age = System.DateTimeOffset.UtcNow - record.UnlockedAtUtc;
            double seconds = age.TotalSeconds;
            if (seconds < 0d) seconds = 0d;   // a clock that moved backwards, not a future unlock

            if (seconds < 60d) return $"{(int)seconds}s ago";
            if (seconds < 3600d) return $"{(int)(seconds / 60d)}m ago";
            if (seconds < 86400d) return $"{(int)(seconds / 3600d)}h ago";
            return $"{(int)(seconds / 86400d)}d ago";
        }

        // ── Style helpers ──────────────────────────────────────────────

        private static void EnsureStyles() {
            if (_headerStyle != null) return;

            _headerStyle = new GUIStyle(EditorStyles.boldLabel) {
                fontSize = 12,
                padding = new RectOffset(4, 4, 2, 2),
            };

            _keyStyle = new GUIStyle(EditorStyles.label) {
                padding = new RectOffset(6, 4, 1, 1),
                wordWrap = false,
            };

            _emptyLabelStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel) {
                fontStyle = FontStyle.Italic,
            };

            _metaStyle = new GUIStyle(EditorStyles.miniLabel) {
                padding = new RectOffset(2, 2, 1, 1),
                wordWrap = false,
            };
        }
    }
}
