#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Editor window for live runtime event monitoring and edit-mode code reference analysis.
    /// 3-panel layout: left event tree, right detail panel, bottom live log.
    ///
    /// In Play Mode: shows live subscriber counts, trigger history, payload snapshots,
    /// and optionally captured stack traces. Capture must be explicitly started/stopped.
    ///
    /// In Edit Mode: shows static code references (TriggerEvent.*/ListenEvent.* usages)
    /// found by regex scanning of .cs source files.
    /// </summary>
    public class EventDebugMonitorWindow : EditorWindow
    {
        // ── Layout constants ───────────────────────────────────────────

        private const float LEFT_PANEL_MIN_WIDTH = 220f;
        private const float LEFT_PANEL_DEFAULT_WIDTH = 280f;
        private const float BOTTOM_PANEL_MIN_HEIGHT = 80f;
        private const float BOTTOM_PANEL_DEFAULT_HEIGHT = 200f;
        private const float TOOLBAR_HEIGHT = 22f;
        private const float SPLITTER_WIDTH = 4f;

        // ── Persistent state ───────────────────────────────────────────

        [SerializeField] private float leftPanelWidth = LEFT_PANEL_DEFAULT_WIDTH;
        [SerializeField] private float bottomPanelHeight = BOTTOM_PANEL_DEFAULT_HEIGHT;
        [SerializeField] private bool showBottomPanel = true;
        [SerializeField] private string searchFilter = "";
        [SerializeField] private bool captureStackTraces;
        [SerializeField] private int maxHistorySize = 512;
        [SerializeField] private bool isPaused;
        [SerializeField] private bool autoScroll = true;

        // ── Runtime state ──────────────────────────────────────────────

        private List<EventMessage> allEvents = new List<EventMessage>();
        private List<CategoryDefinition> allCategories = new List<CategoryDefinition>();
        private EventMessage selectedEvent;
        private EventTriggerRecord selectedLogEntry;

        // Tree view
        private EventTreeView treeView;
        private TreeViewState treeViewState;

        // Scroll positions
        private Vector2 detailScrollPos;
        private Vector2 logScrollPos;
        private Vector2 codeRefScrollPos;

        // Splitter dragging
        private bool isDraggingLeftSplitter;
        private bool isDraggingBottomSplitter;

        // Repaint timer
        private double lastRepaintTime;
        private const double REPAINT_INTERVAL = 0.25; // 4 Hz when capturing

        // Cached display data (to avoid recalculating every frame)
        private int cachedTotalTriggers;
        private int displayedLogCount;

        // Edit mode code references
        private EventCodeReferences cachedCodeRefs;
        private EventMessage cachedCodeRefEvent;

        // Styles (lazy-initialized)
        private static GUIStyle _richLabelStyle;
        private static GUIStyle _logEntryStyle;
        private static GUIStyle _logEntrySelectedStyle;
        private static GUIStyle _headerStyle;
        private static GUIStyle _sectionHeaderStyle;
        private static GUIStyle _monoStyle;

        // ── Menu & public API ──────────────────────────────────────────

        [MenuItem("ProxyCore/Event Debug Monitor")]
        public static void ShowWindow()
        {
            var window = GetWindow<EventDebugMonitorWindow>();
            window.titleContent = new GUIContent("Event Monitor");
            window.minSize = new Vector2(800f, 500f);
            window.Show();
        }

        /// <summary>
        /// Opens the window with a specific event pre-selected.
        /// </summary>
        public static void ShowWindow(EventMessage preselect)
        {
            var window = GetWindow<EventDebugMonitorWindow>();
            window.titleContent = new GUIContent("Event Monitor");
            window.minSize = new Vector2(800f, 500f);
            window.selectedEvent = preselect;
            window.Show();
            if (window.treeView != null && preselect != null)
            {
                window.treeView.SelectEvent(preselect);
            }
        }

        // ── Lifecycle ──────────────────────────────────────────────────

        private void OnEnable()
        {
            LoadAllData();

            if (treeViewState == null)
                treeViewState = new TreeViewState();

            RebuildTreeView();

            EventDebugTracker.OnRecordAdded += OnNewRecord;
            EventDebugTracker.OnListenersChanged += OnListenersChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EventDebugTracker.OnRecordAdded -= OnNewRecord;
            EventDebugTracker.OnListenersChanged -= OnListenersChanged;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        }

        private void OnFocus()
        {
            LoadAllData();
            RebuildTreeView();
        }

        private void Update()
        {
            // Periodic repaint during capture for live counters
            if (EventDebugTracker.IsCapturing && !isPaused)
            {
                if (EditorApplication.timeSinceStartup - lastRepaintTime > REPAINT_INTERVAL)
                {
                    lastRepaintTime = EditorApplication.timeSinceStartup;
                    Repaint();
                }
            }
        }

        private void OnNewRecord(EventTriggerRecord record)
        {
            if (!isPaused)
                Repaint();
        }

        private void OnListenersChanged()
        {
            if (!isPaused)
                Repaint();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            Repaint();
        }

        // ── Data loading ───────────────────────────────────────────────

        private void LoadAllData()
        {
            string[] eventGuids = AssetDatabase.FindAssets("t:EventMessage");
            allEvents.Clear();
            foreach (string guid in eventGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var evt = AssetDatabase.LoadAssetAtPath<EventMessage>(path);
                if (evt != null) allEvents.Add(evt);
            }
            allEvents = allEvents.OrderBy(e => e.shortName ?? e.name).ToList();

            string[] catGuids = AssetDatabase.FindAssets("t:CategoryDefinition");
            allCategories.Clear();
            foreach (string guid in catGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var cat = AssetDatabase.LoadAssetAtPath<CategoryDefinition>(path);
                if (cat != null) allCategories.Add(cat);
            }
            allCategories = allCategories.OrderBy(c => c.categoryDisplayName).ToList();
        }

        private void RebuildTreeView()
        {
            treeView = new EventTreeView(treeViewState, allEvents, allCategories, searchFilter);
            treeView.OnEventSelected += evt =>
            {
                selectedEvent = evt;
                selectedLogEntry = null;
                cachedCodeRefs = null;
                cachedCodeRefEvent = null;
                Repaint();
            };
            treeView.Reload();

            if (selectedEvent != null)
                treeView.SelectEvent(selectedEvent);
        }

        // ── Styles (lazy init) ─────────────────────────────────────────

        private static void EnsureStyles()
        {
            if (_richLabelStyle != null) return;

            _richLabelStyle = new GUIStyle(EditorStyles.label) { richText = true, wordWrap = true };
            _logEntryStyle = new GUIStyle(EditorStyles.label)
            {
                richText = true,
                fontSize = 11,
                padding = new RectOffset(4, 4, 2, 2),
                fixedHeight = 20f
            };
            _logEntrySelectedStyle = new GUIStyle(_logEntryStyle);
            _logEntrySelectedStyle.normal.background = EditorGUIUtility.isProSkin
                ? MakeTex(1, 1, new Color(0.24f, 0.37f, 0.59f))
                : MakeTex(1, 1, new Color(0.24f, 0.48f, 0.90f));
            _headerStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 14 };
            _sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel) { fontSize = 12 };
            _monoStyle = new GUIStyle(EditorStyles.label)
            {
                font = Font.CreateDynamicFontFromOSFont("Consolas", 11),
                fontSize = 11,
                richText = true,
                wordWrap = true
            };
            if (_monoStyle.font == null)
                _monoStyle.font = Font.CreateDynamicFontFromOSFont("Courier New", 11);
        }

        private static Texture2D MakeTex(int w, int h, Color col)
        {
            var pix = new Color[w * h];
            for (int i = 0; i < pix.Length; i++) pix[i] = col;
            var tex = new Texture2D(w, h);
            tex.SetPixels(pix);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }

        // ── Main OnGUI ─────────────────────────────────────────────────

        private void OnGUI()
        {
            EnsureStyles();
            DrawToolbar();

            Rect contentRect = new Rect(0, TOOLBAR_HEIGHT * 2 + 2, position.width, position.height - TOOLBAR_HEIGHT * 2 - 2);

            // Calculate panel rects
            float btmHeight = showBottomPanel ? bottomPanelHeight : 0f;
            float topAreaHeight = contentRect.height - btmHeight - (showBottomPanel ? SPLITTER_WIDTH : 0f);

            Rect leftRect = new Rect(contentRect.x, contentRect.y, leftPanelWidth, topAreaHeight);
            Rect leftSplitterRect = new Rect(leftRect.xMax, contentRect.y, SPLITTER_WIDTH, topAreaHeight);
            Rect rightRect = new Rect(leftSplitterRect.xMax, contentRect.y,
                contentRect.width - leftPanelWidth - SPLITTER_WIDTH, topAreaHeight);

            Rect bottomSplitterRect = default;
            Rect bottomRect = default;
            if (showBottomPanel)
            {
                bottomSplitterRect = new Rect(contentRect.x, contentRect.y + topAreaHeight,
                    contentRect.width, SPLITTER_WIDTH);
                bottomRect = new Rect(contentRect.x, bottomSplitterRect.yMax,
                    contentRect.width, btmHeight);
            }

            // Handle splitter dragging
            HandleSplitters(leftSplitterRect, bottomSplitterRect, contentRect);

            // Draw panels
            DrawLeftPanel(leftRect);
            DrawRightPanel(rightRect);
            if (showBottomPanel)
            {
                DrawBottomSplitterVisual(bottomSplitterRect);
                DrawBottomPanel(bottomRect);
            }

            // Draw left splitter visual
            EditorGUI.DrawRect(leftSplitterRect, new Color(0.15f, 0.15f, 0.15f, 0.5f));

            HandleKeyboardShortcuts();
        }

        // ── Toolbar ────────────────────────────────────────────────────

        private void DrawToolbar()
        {
            bool isPlayMode = EditorApplication.isPlaying;

            // Row 1: Capture controls
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Start/Stop Capture
            EditorGUI.BeginDisabledGroup(!isPlayMode);
            if (EventDebugTracker.IsCapturing)
            {
                Color origBg = GUI.backgroundColor;
                GUI.backgroundColor = new Color(0.4f, 1f, 0.4f);
                if (GUILayout.Button("■ Stop Capture", EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    EventDebugTracker.StopCapture();
                }
                GUI.backgroundColor = origBg;
            }
            else
            {
                if (GUILayout.Button("▶ Start Capture", EditorStyles.toolbarButton, GUILayout.Width(110)))
                {
                    EventDebugTracker.StartCapture(maxHistorySize, captureStackTraces);
                }
            }
            EditorGUI.EndDisabledGroup();

            // Stack traces checkbox (only when not capturing)
            EditorGUI.BeginDisabledGroup(EventDebugTracker.IsCapturing);
            captureStackTraces = GUILayout.Toggle(captureStackTraces, "Stack Traces", EditorStyles.toolbarButton, GUILayout.Width(90));
            EditorGUI.EndDisabledGroup();

            // Pause/Resume
            EditorGUI.BeginDisabledGroup(!EventDebugTracker.IsCapturing);
            string pauseLabel = isPaused ? "▶ Resume" : "⏸ Pause";
            if (GUILayout.Button(pauseLabel, EditorStyles.toolbarButton, GUILayout.Width(75)))
            {
                isPaused = !isPaused;
            }
            EditorGUI.EndDisabledGroup();

            // Clear
            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                EventDebugTracker.ClearData();
                selectedLogEntry = null;
                Repaint();
            }

            GUILayout.FlexibleSpace();

            // Mode indicator
            string modeLabel;
            Color modeColor;
            if (isPlayMode && EventDebugTracker.IsCapturing)
            {
                modeLabel = "● CAPTURING";
                modeColor = new Color(0.3f, 1f, 0.3f);
            }
            else if (isPlayMode)
            {
                modeLabel = "○ PLAY MODE — Idle";
                modeColor = new Color(1f, 0.9f, 0.4f);
            }
            else
            {
                modeLabel = "✏ EDIT MODE — Static";
                modeColor = new Color(0.6f, 0.8f, 1f);
            }

            Color origContent = GUI.contentColor;
            GUI.contentColor = modeColor;
            GUILayout.Label(modeLabel, EditorStyles.boldLabel, GUILayout.Width(170));
            GUI.contentColor = origContent;

            EditorGUILayout.EndHorizontal();

            // Row 2: Search + settings
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Search:", GUILayout.Width(50));
            EditorGUI.BeginChangeCheck();
            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck())
            {
                RebuildTreeView();
            }
            if (GUILayout.Button("✕", EditorStyles.toolbarButton, GUILayout.Width(22)))
            {
                searchFilter = "";
                GUI.FocusControl(null);
                RebuildTreeView();
            }

            GUILayout.Space(10);

            // Show/hide log panel
            showBottomPanel = GUILayout.Toggle(showBottomPanel, "Log Panel", EditorStyles.toolbarButton, GUILayout.Width(75));

            // History size
            GUILayout.Label("History:", GUILayout.Width(50));
            EditorGUI.BeginChangeCheck();
            maxHistorySize = EditorGUILayout.IntField(maxHistorySize, GUILayout.Width(50));
            if (EditorGUI.EndChangeCheck())
            {
                maxHistorySize = Mathf.Clamp(maxHistorySize, 16, 8192);
                EventDebugTracker.SetMaxHistorySize(maxHistorySize);
            }

            GUILayout.FlexibleSpace();

            // Quick stats
            int totalRecords = EventDebugTracker.History.Count;
            int activeSubscribers = 0;
            if (isPlayMode)
            {
                foreach (var evt in allEvents)
                    activeSubscribers += EventCoordinator.GetListenerCount(evt);
            }
            GUILayout.Label($"Records: {totalRecords}  |  Subscribers: {activeSubscribers}", GUILayout.Width(220));

            EditorGUILayout.EndHorizontal();
        }

        // ── Left Panel: Event Tree ─────────────────────────────────────

        private void DrawLeftPanel(Rect rect)
        {
            if (treeView == null) return;
            treeView.searchString = searchFilter;
            treeView.OnGUI(rect);
        }

        // ── Right Panel: Detail View ───────────────────────────────────

        private void DrawRightPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);
            detailScrollPos = EditorGUILayout.BeginScrollView(detailScrollPos);

            if (selectedEvent == null)
            {
                EditorGUILayout.HelpBox("Select an event from the tree to view details.", MessageType.Info);
            }
            else if (EditorApplication.isPlaying)
            {
                DrawPlayModeDetail();
            }
            else
            {
                DrawEditModeDetail();
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawPlayModeDetail()
        {
            // Header
            DrawEventHeader(selectedEvent);
            EditorGUILayout.Space(4);

            // Stats
            var stats = EventDebugTracker.GetStats(selectedEvent);
            int listenerCount = EventCoordinator.GetListenerCount(selectedEvent);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Triggers:", stats != null ? stats.TriggerCount.ToString() : "0", GUILayout.Width(120));
            EditorGUILayout.LabelField("Subscribers:", listenerCount.ToString(), GUILayout.Width(120));
            if (stats != null && stats.TriggerCount > 0)
            {
                double elapsed = EditorApplication.timeSinceStartup - stats.LastTriggerTime;
                EditorGUILayout.LabelField($"Last: {elapsed:F1}s ago");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);

            // ─ Subscribers section ─
            EditorGUILayout.LabelField("Active Subscribers", _sectionHeaderStyle);

            if (listenerCount == 0)
            {
                EditorGUILayout.LabelField("  (none)", _richLabelStyle);
            }
            else
            {
                var listeners = EventCoordinator.GetListeners(selectedEvent);
                foreach (var del in listeners)
                {
                    if (del == null) continue;
                    string targetType = del.Target != null ? del.Target.GetType().Name : "(static)";
                    string methodName = del.Method.Name;
                    string declaringType = del.Method.DeclaringType?.Name ?? "?";

                    EditorGUILayout.BeginHorizontal();
                    string label = del.Target != null
                        ? $"  {declaringType}.{methodName}()"
                        : $"  (static) {declaringType}.{methodName}()";

                    EditorGUILayout.SelectableLabel(label, _monoStyle, GUILayout.Height(18));

                    if (GUILayout.Button("📋", GUILayout.Width(24), GUILayout.Height(18)))
                    {
                        string full = $"{del.Method.DeclaringType?.FullName}.{methodName}";
                        EditorGUIUtility.systemCopyBuffer = full;
                    }

                    // If target is a UnityEngine.Object, ping it
                    if (del.Target is UnityEngine.Object unityObj)
                    {
                        if (GUILayout.Button("◎", GUILayout.Width(24), GUILayout.Height(18)))
                        {
                            EditorGUIUtility.PingObject(unityObj);
                            Selection.activeObject = unityObj;
                        }
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }

            EditorGUILayout.Space(6);

            // ─ Last Payload section ─
            EditorGUILayout.LabelField("Last Payload", _sectionHeaderStyle);

            // Show selected log entry payload if any, otherwise last from stats
            List<PayloadDetail> payloadDetails = null;
            string payloadSnapshot = null;

            if (selectedLogEntry != null && selectedLogEntry.EventId == selectedEvent.ID)
            {
                payloadDetails = selectedLogEntry.PayloadDetails;
                payloadSnapshot = selectedLogEntry.PayloadSnapshot;
            }
            else if (stats != null && stats.LastPayloadDetails != null)
            {
                payloadDetails = stats.LastPayloadDetails;
                payloadSnapshot = stats.LastPayloadSnapshot;
            }

            if (payloadDetails != null && payloadDetails.Count > 0)
            {
                foreach (var detail in payloadDetails)
                {
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField($"  <b>{detail.TypeName}</b>", _richLabelStyle, GUILayout.Width(140));
                    EditorGUILayout.SelectableLabel(detail.DebugString, _monoStyle, GUILayout.Height(18));
                    EditorGUILayout.EndHorizontal();
                }
            }
            else
            {
                EditorGUILayout.LabelField("  (no payload data captured)", _richLabelStyle);
            }

            EditorGUILayout.Space(6);

            // ─ Trigger Sources section ─
            EditorGUILayout.LabelField("Trigger Sources", _sectionHeaderStyle);

            if (selectedLogEntry != null && !string.IsNullOrEmpty(selectedLogEntry.CallerStackTrace))
            {
                // Show the specific log entry's stack trace
                DrawClickableStackTrace(selectedLogEntry.CallerStackTrace);
            }
            else if (stats != null && stats.RecentCallers.Count > 0)
            {
                foreach (string caller in stats.RecentCallers)
                {
                    DrawClickableCallerLine(caller);
                }
            }
            else if (!EventDebugTracker.CaptureStackTraces && EventDebugTracker.IsCapturing)
            {
                EditorGUILayout.HelpBox(
                    "Stop capture, enable 'Stack Traces', and capture again to see trigger sources.",
                    MessageType.Info);
            }
            else if (!EventDebugTracker.IsCapturing && (stats == null || stats.RecentCallers.Count == 0))
            {
                EditorGUILayout.HelpBox(
                    "Start capture with 'Stack Traces' enabled to see trigger sources.",
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.LabelField("  (no caller data)", _richLabelStyle);
            }
        }

        private void DrawEditModeDetail()
        {
            // Header
            DrawEventHeader(selectedEvent);
            EditorGUILayout.Space(4);

            // Expected Payloads
            EditorGUILayout.LabelField("Expected Payloads", _sectionHeaderStyle);
            if (selectedEvent.expectedPayloads != null && selectedEvent.expectedPayloads.Count > 0)
            {
                foreach (var payload in selectedEvent.expectedPayloads)
                {
                    if (payload == null) continue;
                    EditorGUILayout.LabelField($"  • {payload.GetType().Name}", _richLabelStyle);
                }
            }
            else
            {
                EditorGUILayout.LabelField("  (none defined)", _richLabelStyle);
            }

            EditorGUILayout.Space(6);

            // Code References
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Code References", _sectionHeaderStyle);
            if (GUILayout.Button("🔄 Rescan", GUILayout.Width(75), GUILayout.Height(18)))
            {
                EventCodeReferenceScanner.Rescan();
                cachedCodeRefs = null;
                cachedCodeRefEvent = null;
            }
            EditorGUILayout.EndHorizontal();

            // Lazy-load references for selected event
            if (cachedCodeRefEvent != selectedEvent || cachedCodeRefs == null)
            {
                cachedCodeRefs = EventCodeReferenceScanner.GetReferences(selectedEvent);
                cachedCodeRefEvent = selectedEvent;
            }

            if (cachedCodeRefs.References.Count == 0)
            {
                EditorGUILayout.HelpBox("No code references found for this event.", MessageType.Info);
            }
            else
            {
                codeRefScrollPos = EditorGUILayout.BeginScrollView(codeRefScrollPos,
                    GUILayout.MaxHeight(400));

                // Group by type
                var triggers = cachedCodeRefs.References.Where(r =>
                    r.ReferenceType == CodeReferenceType.Trigger || r.ReferenceType == CodeReferenceType.DirectTrigger).ToList();
                var listeners = cachedCodeRefs.References.Where(r =>
                    r.ReferenceType == CodeReferenceType.Listener || r.ReferenceType == CodeReferenceType.DirectListener).ToList();

                if (triggers.Count > 0)
                {
                    EditorGUILayout.LabelField($"  Trigger Sites ({triggers.Count})", EditorStyles.boldLabel);
                    foreach (var r in triggers)
                    {
                        DrawCodeReference(r);
                    }
                }

                if (listeners.Count > 0)
                {
                    EditorGUILayout.LabelField($"  Listener Sites ({listeners.Count})", EditorStyles.boldLabel);
                    foreach (var r in listeners)
                    {
                        DrawCodeReference(r);
                    }
                }

                EditorGUILayout.EndScrollView();
            }
        }

        private void DrawEventHeader(EventMessage evt)
        {
            EditorGUILayout.BeginHorizontal();

            // Accent color swatch
            Rect swatchRect = GUILayoutUtility.GetRect(16, 16, GUILayout.Width(16));
            EditorGUI.DrawRect(swatchRect, evt.accentColor);

            // Name
            EditorGUILayout.LabelField(evt.GetDisplayName(), _headerStyle);

            // Ping button
            if (GUILayout.Button("◎ Ping", GUILayout.Width(55), GUILayout.Height(20)))
            {
                EditorGUIUtility.PingObject(evt);
                Selection.activeObject = evt;
            }

            EditorGUILayout.EndHorizontal();

            // Full path + description
            EditorGUILayout.LabelField($"Path: {evt.GetFullPath()}", _richLabelStyle);
            if (!string.IsNullOrEmpty(evt.eventDescription))
            {
                EditorGUILayout.LabelField(evt.eventDescription, EditorStyles.wordWrappedMiniLabel);
            }
        }

        private void DrawCodeReference(EventCodeReference r)
        {
            EditorGUILayout.BeginHorizontal();

            string icon = (r.ReferenceType == CodeReferenceType.Trigger || r.ReferenceType == CodeReferenceType.DirectTrigger)
                ? "→" : "←";
            string typeTag = r.ReferenceType == CodeReferenceType.DirectTrigger || r.ReferenceType == CodeReferenceType.DirectListener
                ? " (direct)" : "";

            string fileName = System.IO.Path.GetFileName(r.FilePath);
            string display = $"    {icon} {fileName}:{r.LineNumber}{typeTag}";

            if (GUILayout.Button(display, EditorStyles.linkLabel))
            {
                // Open file at line in IDE
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(r.FilePath);
                if (asset != null)
                {
                    AssetDatabase.OpenAsset(asset, r.LineNumber);
                }
            }

            EditorGUILayout.EndHorizontal();

            // Show trimmed line content
            if (!string.IsNullOrEmpty(r.LineContent))
            {
                EditorGUI.indentLevel += 3;
                EditorGUILayout.LabelField(r.LineContent, EditorStyles.wordWrappedMiniLabel);
                EditorGUI.indentLevel -= 3;
            }
        }

        private void DrawClickableStackTrace(string stackTrace)
        {
            string[] lines = stackTrace.Split('\n');
            foreach (string line in lines)
            {
                DrawClickableCallerLine(line.Trim());
            }
        }

        private void DrawClickableCallerLine(string callerLine)
        {
            if (string.IsNullOrEmpty(callerLine)) return;

            EditorGUILayout.BeginHorizontal();

            // Try to parse "TypeName.Method() — file:line"
            int dashIdx = callerLine.IndexOf(" — ", StringComparison.Ordinal);
            if (dashIdx > 0 && callerLine.Length > dashIdx + 3)
            {
                string methodPart = callerLine.Substring(0, dashIdx);
                string filePart = callerLine.Substring(dashIdx + 3);

                EditorGUILayout.LabelField($"  {methodPart}", _monoStyle, GUILayout.Width(300));

                if (GUILayout.Button(filePart, EditorStyles.linkLabel))
                {
                    TryOpenFileLink(filePart);
                }
            }
            else
            {
                EditorGUILayout.LabelField($"  {callerLine}", _monoStyle);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void TryOpenFileLink(string fileColonLine)
        {
            // Parse "Assets/path/file.cs:42"
            int colonIdx = fileColonLine.LastIndexOf(':');
            if (colonIdx > 0)
            {
                string filePath = fileColonLine.Substring(0, colonIdx);
                if (int.TryParse(fileColonLine.Substring(colonIdx + 1), out int lineNum))
                {
                    var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(filePath);
                    if (asset != null)
                    {
                        AssetDatabase.OpenAsset(asset, lineNum);
                        return;
                    }
                }
            }
            // Fallback — try whole string as path
            var fallback = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fileColonLine);
            if (fallback != null)
                AssetDatabase.OpenAsset(fallback);
        }

        // ── Bottom Panel: Live Log ─────────────────────────────────────

        private void DrawBottomSplitterVisual(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.15f, 0.5f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeVertical);
        }

        private void DrawBottomPanel(Rect rect)
        {
            GUILayout.BeginArea(rect);

            // Mini toolbar
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Live Event Log", EditorStyles.boldLabel, GUILayout.Width(100));

            autoScroll = GUILayout.Toggle(autoScroll, "Auto-scroll", EditorStyles.toolbarButton, GUILayout.Width(80));

            GUILayout.FlexibleSpace();

            int recordCount = EventDebugTracker.History.Count;
            GUILayout.Label($"{recordCount} entries", GUILayout.Width(80));

            EditorGUILayout.EndHorizontal();

            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode and start capture to see live events.", MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            if (recordCount == 0)
            {
                EditorGUILayout.HelpBox(
                    EventDebugTracker.IsCapturing
                        ? "Waiting for events..."
                        : "Click 'Start Capture' to begin recording event activity.",
                    MessageType.Info);
                GUILayout.EndArea();
                return;
            }

            // Scrollable log area
            logScrollPos = EditorGUILayout.BeginScrollView(logScrollPos);

            var history = EventDebugTracker.History;
            // Draw newest first
            for (int i = history.Count - 1; i >= 0; i--)
            {
                var record = history[i];
                if (record == null) continue;

                // Apply search filter
                if (!string.IsNullOrEmpty(searchFilter))
                {
                    string evtName = record.EventMessage != null ? record.EventMessage.GetDisplayName() : "";
                    if (!evtName.ToLower().Contains(searchFilter.ToLower()))
                        continue;
                }

                DrawLogEntry(record);
            }

            // Auto-scroll to top (newest first, so scroll to 0)
            if (autoScroll)
            {
                logScrollPos = Vector2.zero;
            }

            EditorGUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawLogEntry(EventTriggerRecord record)
        {
            bool isSelected = selectedLogEntry == record;
            var style = isSelected ? _logEntrySelectedStyle : _logEntryStyle;

            string evtName = record.EventMessage != null ? record.EventMessage.GetDisplayName() : $"(ID:{record.EventId})";
            Color accentColor = record.EventMessage != null ? record.EventMessage.accentColor : Color.grey;

            // Format time
            double secs = record.Timestamp;
            int mins = (int)(secs / 60) % 60;
            int hours = (int)(secs / 3600);
            double fracSec = secs % 60;
            string timeStr = $"{hours:D2}:{mins:D2}:{fracSec:00.000}";

            // Truncate payload to one line
            string payloadBrief = record.PayloadSnapshot ?? "";
            if (payloadBrief.Length > 60)
                payloadBrief = payloadBrief.Substring(0, 57) + "...";
            payloadBrief = payloadBrief.Replace("\n", " ");

            string colorHex = ColorUtility.ToHtmlStringRGB(accentColor);
            string label = $"<color=#{colorHex}>●</color> [{timeStr}] <b>{evtName}</b>  {payloadBrief}  → {record.ListenerCount} listeners";

            Rect entryRect = EditorGUILayout.GetControlRect(false, 20f);
            if (GUI.Button(entryRect, label, style))
            {
                selectedLogEntry = record;
                if (record.EventMessage != null)
                {
                    selectedEvent = record.EventMessage;
                    treeView?.SelectEvent(selectedEvent);
                }
                Repaint();
            }
        }

        // ── Splitter handling ──────────────────────────────────────────

        private void HandleSplitters(Rect leftSplitter, Rect bottomSplitter, Rect contentRect)
        {
            EditorGUIUtility.AddCursorRect(leftSplitter, MouseCursor.ResizeHorizontal);
            if (showBottomPanel)
                EditorGUIUtility.AddCursorRect(bottomSplitter, MouseCursor.ResizeVertical);

            Event e = Event.current;

            switch (e.type)
            {
                case EventType.MouseDown:
                    if (leftSplitter.Contains(e.mousePosition))
                    {
                        isDraggingLeftSplitter = true;
                        e.Use();
                    }
                    else if (showBottomPanel && bottomSplitter.Contains(e.mousePosition))
                    {
                        isDraggingBottomSplitter = true;
                        e.Use();
                    }
                    break;

                case EventType.MouseDrag:
                    if (isDraggingLeftSplitter)
                    {
                        leftPanelWidth = Mathf.Clamp(e.mousePosition.x, LEFT_PANEL_MIN_WIDTH, contentRect.width * 0.5f);
                        e.Use();
                        Repaint();
                    }
                    else if (isDraggingBottomSplitter)
                    {
                        float maxBtm = contentRect.height - 100f;
                        bottomPanelHeight = Mathf.Clamp(contentRect.yMax - e.mousePosition.y, BOTTOM_PANEL_MIN_HEIGHT, maxBtm);
                        e.Use();
                        Repaint();
                    }
                    break;

                case EventType.MouseUp:
                    isDraggingLeftSplitter = false;
                    isDraggingBottomSplitter = false;
                    break;
            }
        }

        // ── Keyboard shortcuts ─────────────────────────────────────────

        private void HandleKeyboardShortcuts()
        {
            Event e = Event.current;
            if (e.type != EventType.KeyDown) return;

            if (e.control && e.keyCode == KeyCode.R)
            {
                LoadAllData();
                RebuildTreeView();
                e.Use();
            }
            if (e.keyCode == KeyCode.Space && EditorApplication.isPlaying)
            {
                if (EventDebugTracker.IsCapturing)
                    EventDebugTracker.StopCapture();
                else
                    EventDebugTracker.StartCapture(maxHistorySize, captureStackTraces);
                e.Use();
            }
            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                EventDebugTracker.ClearData();
                selectedLogEntry = null;
                e.Use();
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Tree View — events grouped by category
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// IMGUI TreeView that displays events grouped by category with live stats badges.
    /// </summary>
    internal class EventTreeView : TreeView
    {
        private List<EventMessage> allEvents;
        private List<CategoryDefinition> allCategories;
        private string filter;

        // Mapping from tree item IDs to EventMessages
        private Dictionary<int, EventMessage> idToEvent = new Dictionary<int, EventMessage>();

        public event Action<EventMessage> OnEventSelected;

        public EventTreeView(TreeViewState state, List<EventMessage> events,
            List<CategoryDefinition> categories, string filter)
            : base(state)
        {
            this.allEvents = events;
            this.allCategories = categories;
            this.filter = filter ?? "";
            showAlternatingRowBackgrounds = true;
            showBorder = true;
        }

        protected override TreeViewItem BuildRoot()
        {
            var root = new TreeViewItem(0, -1, "Root");
            idToEvent.Clear();

            int nextId = 1;

            // "Uncategorized" group for events with no categories
            var uncategorizedEvents = allEvents
                .Where(e => e.categories == null || e.categories.Count == 0)
                .Where(e => MatchesFilter(e))
                .OrderBy(e => e.GetDisplayName())
                .ToList();

            // Category groups
            foreach (var cat in allCategories)
            {
                string catName = !string.IsNullOrEmpty(cat.categoryDisplayName) ? cat.categoryDisplayName : cat.name;

                var catEvents = allEvents
                    .Where(e => e.categories != null && e.categories.Contains(cat))
                    .Where(e => MatchesFilter(e))
                    .OrderBy(e => e.GetDisplayName())
                    .ToList();

                if (catEvents.Count == 0 && !string.IsNullOrEmpty(filter))
                    continue; // Hide empty categories when filtering

                var catItem = new TreeViewItem(nextId++, 0, $"{catName} ({catEvents.Count})");
                root.AddChild(catItem);

                foreach (var evt in catEvents)
                {
                    var evtItem = new EventTreeItem(nextId, 1, evt);
                    idToEvent[nextId] = evt;
                    catItem.AddChild(evtItem);
                    nextId++;
                }
            }

            if (uncategorizedEvents.Count > 0)
            {
                var uncatItem = new TreeViewItem(nextId++, 0, $"(Uncategorized) ({uncategorizedEvents.Count})");
                root.AddChild(uncatItem);

                foreach (var evt in uncategorizedEvents)
                {
                    var evtItem = new EventTreeItem(nextId, 1, evt);
                    idToEvent[nextId] = evt;
                    uncatItem.AddChild(evtItem);
                    nextId++;
                }
            }

            // Ensure root has children (TreeView requirement)
            if (!root.hasChildren)
            {
                root.AddChild(new TreeViewItem(nextId++, 0, "(no events)"));
            }

            SetupDepthsFromParentsAndChildren(root);
            return root;
        }

        private bool MatchesFilter(EventMessage evt)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            string f = filter.ToLower();
            string dn = evt.GetDisplayName()?.ToLower() ?? "";
            string sn = evt.shortName?.ToLower() ?? "";
            return dn.Contains(f) || sn.Contains(f);
        }

        protected override void RowGUI(RowGUIArgs args)
        {
            if (args.item is EventTreeItem evtItem)
            {
                var evt = evtItem.EventMessage;
                if (evt == null)
                {
                    base.RowGUI(args);
                    return;
                }

                Rect rowRect = args.rowRect;
                float indent = GetContentIndent(args.item);

                // Accent color dot
                Rect dotRect = new Rect(rowRect.x + indent, rowRect.y + 4, 10, 10);
                EditorGUI.DrawRect(dotRect, evt.accentColor);

                // Event name
                string displayName = evt.GetDisplayName() ?? evt.name;
                Rect nameRect = new Rect(dotRect.xMax + 4, rowRect.y, rowRect.width * 0.5f, rowRect.height);
                GUI.Label(nameRect, displayName, EditorStyles.label);

                // Live stats (only in play mode)
                if (EditorApplication.isPlaying)
                {
                    var stats = EventDebugTracker.GetStats(evt);
                    int trigCount = stats?.TriggerCount ?? 0;
                    int subCount = EventCoordinator.GetListenerCount(evt);

                    // Trigger count
                    Rect trigRect = new Rect(rowRect.xMax - 120, rowRect.y, 50, rowRect.height);
                    if (trigCount > 0)
                    {
                        GUI.Label(trigRect, $"×{trigCount}", EditorStyles.miniLabel);
                    }

                    // Subscriber badge
                    Rect subRect = new Rect(rowRect.xMax - 60, rowRect.y, 50, rowRect.height);
                    Color badgeColor = subCount > 0 ? new Color(0.3f, 0.8f, 0.3f) : new Color(0.5f, 0.5f, 0.5f);
                    Color origContent = GUI.contentColor;
                    GUI.contentColor = badgeColor;
                    GUI.Label(subRect, $"[{subCount}]", EditorStyles.miniLabel);
                    GUI.contentColor = origContent;

                    // Flash highlight for recent triggers
                    if (stats != null && stats.TriggerCount > 0)
                    {
                        double elapsed = EditorApplication.timeSinceStartup - stats.LastTriggerTime;
                        if (elapsed < 0.5)
                        {
                            float alpha = (float)(1.0 - elapsed / 0.5) * 0.15f;
                            EditorGUI.DrawRect(rowRect, new Color(1f, 1f, 0.3f, alpha));
                        }
                    }
                }
            }
            else
            {
                base.RowGUI(args);
            }
        }

        protected override void SelectionChanged(IList<int> selectedIds)
        {
            if (selectedIds.Count > 0 && idToEvent.TryGetValue(selectedIds[0], out var evt))
            {
                OnEventSelected?.Invoke(evt);
            }
        }

        protected override void ContextClickedItem(int id)
        {
            if (!idToEvent.TryGetValue(id, out var evt)) return;

            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Ping Asset"), false, () =>
            {
                EditorGUIUtility.PingObject(evt);
                Selection.activeObject = evt;
            });
            menu.AddItem(new GUIContent("Open in Event Manager"), false, () =>
            {
                EventManagerWindow.ShowWindow();
            });
            menu.AddItem(new GUIContent("Reset Stats"), false, () =>
            {
                // Reset just this event's stats
                var stats = EventDebugTracker.GetStats(evt);
                if (stats != null)
                {
                    stats.TriggerCount = 0;
                    stats.LastTriggerTime = 0;
                    stats.LastPayloadSnapshot = null;
                    stats.LastPayloadDetails?.Clear();
                    stats.RecentCallers?.Clear();
                }
            });
            menu.ShowAsContext();
        }

        /// <summary>
        /// Selects the tree item corresponding to the given event.
        /// </summary>
        public void SelectEvent(EventMessage evt)
        {
            if (evt == null) return;
            foreach (var kvp in idToEvent)
            {
                if (kvp.Value == evt)
                {
                    SetSelection(new[] { kvp.Key }, TreeViewSelectionOptions.RevealAndFrame);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Custom TreeViewItem that carries an EventMessage reference.
    /// </summary>
    internal class EventTreeItem : TreeViewItem
    {
        public EventMessage EventMessage { get; }

        public EventTreeItem(int id, int depth, EventMessage evt)
            : base(id, depth, evt != null ? evt.GetDisplayName() : "(null)")
        {
            EventMessage = evt;
        }
    }
}
#endif
