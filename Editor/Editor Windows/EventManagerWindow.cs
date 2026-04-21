#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Editor window for bulk viewing and editing of EventMessage assets.
    /// Supports filtering by category, inline editing, and batch operations.
    /// </summary>
    public class EventManagerWindow : EditorWindow
    {
        // Data
        private List<EventMessage> allEvents = new List<EventMessage>();
        private List<CategoryDefinition> allCategories = new List<CategoryDefinition>();
        private HashSet<EventMessage> dirtyEvents = new HashSet<EventMessage>();
        private List<EventMessage> eventsToDelete = new List<EventMessage>();
        private HashSet<EventMessage> newlyCreatedEvents = new HashSet<EventMessage>();

        // New-event focus state
        private bool showingNewOnly = false;
        private EventMessage pendingFocusEvent = null;
        private int pendingFocusFrames = 0;

        // UI State
        [SerializeField] private CategoryDefinition selectedCategory = null; // null = ALL
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private string searchFilter = "";

        // Settings — path selector
        private const string PREF_KEY_SELECTED_PATH = "ProxyCore_EventManager_NewEventPath"; // key kept for back-compat
        private const string PREF_KEY_EXTRA_PATHS   = "ProxyCore_EventManager_ExtraPaths";
        private const string DEFAULT_NEW_EVENT_PATH = "Assets/ProxyEvents";

        private List<string> knownPaths = new List<string>();
        private int selectedPathIndex = 0;
        private bool addingNewPath = false;
        private string newPathInput = "";

        // Computed from the current dropdown selection
        private string newEventPath => selectedPathIndex >= 0 && selectedPathIndex < knownPaths.Count
            ? knownPaths[selectedPathIndex]
            : DEFAULT_NEW_EVENT_PATH;

        // Layout constants
        private const float COLUMN_SHORT_NAME = 200f;
        private const float COLUMN_DISPLAY_OVERRIDE = 150f;
        private const float COLUMN_DESCRIPTION = 440f;
        private const float COLUMN_COLOR = 80f;
        private const float COLUMN_CATEGORIES = 150f;
        private const float COLUMN_PAYLOADS = 120f;
        private const float COLUMN_DEBUG = 100f;
        private const float COLUMN_ACTIONS = 80f;
        private const float ROW_HEIGHT = 22f;

        [MenuItem("ProxyCore/Event Manager")]
        public static void ShowWindow()
        {
            var window = GetWindow<EventManagerWindow>();
            window.titleContent = new GUIContent("Event Manager");
            window.minSize = new Vector2(900f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            LoadAllData();
        }

        private void OnFocus()
        {
            // Refresh data when window gains focus
            LoadAllData();
        }

        private void LoadAllData()
        {
            // Load all EventMessage assets
            string[] eventGuids = AssetDatabase.FindAssets("t:EventMessage");
            allEvents.Clear();

            foreach (string guid in eventGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                EventMessage evt = AssetDatabase.LoadAssetAtPath<EventMessage>(path);
                if (evt != null)
                {
                    allEvents.Add(evt);
                }
            }

            // Sort by short name (primary identifier)
            allEvents = allEvents.OrderBy(e => e.shortName ?? e.name).ToList();

            // Load all CategoryDefinition assets
            string[] categoryGuids = AssetDatabase.FindAssets("t:CategoryDefinition");
            allCategories.Clear();

            foreach (string guid in categoryGuids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                CategoryDefinition cat = AssetDatabase.LoadAssetAtPath<CategoryDefinition>(path);
                if (cat != null)
                {
                    allCategories.Add(cat);
                }
            }

            // Sort by display name
            allCategories = allCategories.OrderBy(c => c.categoryDisplayName).ToList();

            // Remove references to assets that were deleted outside this window
            newlyCreatedEvents?.RemoveWhere(e => e == null);

            RefreshKnownPaths();
        }

        /// <summary>
        /// Removes manually-pinned extra paths that no longer contain any EventMessage
        /// assets so the dropdown stays clean. Calls LoadAllData to rebuild the full list.
        /// </summary>
        private void PruneStalePaths()
        {
            // Build the set of folders that genuinely contain events right now.
            // Exclude events pending deletion — they are logically gone from the designer's
            // point of view even though they still live in allEvents until Save is clicked.
            var realFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var evt in allEvents)
            {
                if (eventsToDelete.Contains(evt)) continue;
                string ap = AssetDatabase.GetAssetPath(evt);
                if (string.IsNullOrEmpty(ap)) continue;
                string dir = System.IO.Path.GetDirectoryName(ap)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    realFolders.Add(dir);
            }

            // Filter PREF_KEY_EXTRA_PATHS: keep only paths that currently have events.
            // The default path is no longer special-cased — if it has no events after
            // Refresh it is treated like any other manually-added path.
            string existing = EditorPrefs.GetString(PREF_KEY_EXTRA_PATHS, "");
            if (!string.IsNullOrEmpty(existing))
            {
                var kept = existing.Split(';')
                    .Select(p => p.Trim())
                    .Where(p => !string.IsNullOrEmpty(p))
                    .Where(p => realFolders.Contains(p))
                    .ToList();
                EditorPrefs.SetString(PREF_KEY_EXTRA_PATHS, string.Join(";", kept));
            }

            // Reload everything — RefreshKnownPaths is called inside LoadAllData
            LoadAllData();
        }

        /// <summary>
        /// Rebuilds the list of known event-asset folders by scanning existing assets
        /// and merging with any manually added paths stored in EditorPrefs.
        /// Restores the previous path selection from EditorPrefs.
        /// </summary>
        private void RefreshKnownPaths()
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 1. Discover folders that already contain EventMessage assets
            foreach (var evt in allEvents)
            {
                string assetPath = AssetDatabase.GetAssetPath(evt);
                if (string.IsNullOrEmpty(assetPath)) continue;
                string dir = System.IO.Path.GetDirectoryName(assetPath)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    paths.Add(dir);
            }

            // 2. Merge manually pinned paths from EditorPrefs
            string extras = EditorPrefs.GetString(PREF_KEY_EXTRA_PATHS, "");
            if (!string.IsNullOrEmpty(extras))
            {
                foreach (string p in extras.Split(';'))
                {
                    string trimmed = p.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        paths.Add(trimmed);
                }
            }

            // 3. Add the default path only when its folder actually exists.
            //    If no paths were discovered at all, add it unconditionally so the
            //    dropdown is never empty (it will be created on first use).
            if (paths.Count == 0 || AssetDatabase.IsValidFolder(DEFAULT_NEW_EVENT_PATH))
                paths.Add(DEFAULT_NEW_EVENT_PATH);

            knownPaths = paths.OrderBy(p => p).ToList();

            // 4. Restore the previously selected path
            string saved = EditorPrefs.GetString(PREF_KEY_SELECTED_PATH, DEFAULT_NEW_EVENT_PATH);
            selectedPathIndex = knownPaths.FindIndex(p =>
                string.Equals(p, saved, StringComparison.OrdinalIgnoreCase));
            if (selectedPathIndex < 0) selectedPathIndex = 0;
        }

        private void OnGUI()
        {
            DrawTopToolbar();
            DrawSearchBar();
            DrawTableHeader();
            DrawEventTable();
            DrawBottomBar();

            // Handle keyboard shortcuts
            HandleKeyboardShortcuts();

            // Focus the short name field of a newly created event for 2–3 frames to ensure the control is rendered
            if (pendingFocusEvent != null)
            {
                string controlName = $"shortName_{pendingFocusEvent.GetInstanceID()}";
                EditorGUI.FocusTextInControl(controlName);
                pendingFocusFrames++;
                if (pendingFocusFrames > 2)
                {
                    pendingFocusEvent = null;
                    pendingFocusFrames = 0;
                }
                Repaint();
            }
        }

        private void DrawTopToolbar()
        {
            DrawPathSelector();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            // Title
            GUILayout.Label("Event Manager", EditorStyles.boldLabel, GUILayout.Width(120));

            GUILayout.FlexibleSpace();

            // Add New Event button
            if (GUILayout.Button("+ Add Event", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                CreateNewEvent();
            }

            // Refresh button
            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(70)))
            {
                LoadAllData();
            }

            // Save button (only enabled if there are changes)
            bool hasChanges = dirtyEvents.Count > 0 || eventsToDelete.Count > 0;
            EditorGUI.BeginDisabledGroup(!hasChanges);

            Color originalColor = GUI.backgroundColor;
            if (hasChanges)
            {
                GUI.backgroundColor = new Color(0.5f, 1f, 0.5f); // Light green
            }

            if (GUILayout.Button($"💾 Save ({dirtyEvents.Count + eventsToDelete.Count})", EditorStyles.toolbarButton, GUILayout.Width(100)))
            {
                SaveAllChanges();
            }

            GUI.backgroundColor = originalColor;
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// Draws the event-path dropdown row (and optional new-path input row).
        /// </summary>
        private void DrawPathSelector()
        {
            // Sentinel indices beyond the real path list
            const int SENTINEL_NEW_PATH     = 0; // offset from knownPaths.Count
            const int SENTINEL_REFRESH      = 1;

            // Display paths with " › " (spaced single right angle quotation) as the
            // segment separator. This is visually clear and does NOT trigger Unity's
            // Popup submenu-splitting which fires on '/'.
            string[] popupLabels = knownPaths
                .Select(p => p.Replace("/", " \u203a "))
                .Append("+ New Path\u2026")
                .Append("\u21bb Refresh Paths")
                .ToArray();

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Event Path:", GUILayout.Width(75));

            int displayIndex = Mathf.Clamp(selectedPathIndex, 0, knownPaths.Count - 1);
            int chosen = EditorGUILayout.Popup(displayIndex, popupLabels, EditorStyles.toolbarPopup);

            if (chosen == knownPaths.Count + SENTINEL_NEW_PATH) // "+ New Path…"
            {
                addingNewPath = true;
                if (string.IsNullOrEmpty(newPathInput))
                    newPathInput = newEventPath;
            }
            else if (chosen == knownPaths.Count + SENTINEL_REFRESH) // "↻ Refresh Paths"
            {
                PruneStalePaths();
            }
            else if (chosen != selectedPathIndex)
            {
                selectedPathIndex = chosen;
                addingNewPath = false;
                EditorPrefs.SetString(PREF_KEY_SELECTED_PATH, newEventPath);
            }

            EditorGUILayout.EndHorizontal();

            if (!addingNewPath) return;

            // ── New-path input row ─────────────────────────────────────────────
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("New Path:", GUILayout.Width(75));
            newPathInput = EditorGUILayout.TextField(newPathInput);

            if (GUILayout.Button("Add", EditorStyles.toolbarButton, GUILayout.Width(45)))
            {
                string trimmed = newPathInput.Trim().TrimEnd('/');
                if (!trimmed.StartsWith("Assets/"))
                    trimmed = "Assets/" + trimmed.TrimStart('/');

                if (!string.IsNullOrEmpty(trimmed) && trimmed != "Assets")
                {
                    // Persist as extra if not already discovered automatically
                    if (!knownPaths.Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase)))
                    {
                        string existing = EditorPrefs.GetString(PREF_KEY_EXTRA_PATHS, "");
                        var extraList = string.IsNullOrEmpty(existing)
                            ? new List<string>()
                            : existing.Split(';').Where(x => !string.IsNullOrEmpty(x.Trim())).ToList();
                        if (!extraList.Any(p => string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase)))
                            extraList.Add(trimmed);
                        EditorPrefs.SetString(PREF_KEY_EXTRA_PATHS, string.Join(";", extraList));
                    }

                    RefreshKnownPaths();

                    int idx = knownPaths.FindIndex(p =>
                        string.Equals(p, trimmed, StringComparison.OrdinalIgnoreCase));
                    if (idx >= 0)
                    {
                        selectedPathIndex = idx;
                        EditorPrefs.SetString(PREF_KEY_SELECTED_PATH, trimmed);
                    }
                }

                addingNewPath = false;
                newPathInput = "";
            }

            if (GUILayout.Button("Cancel", EditorStyles.toolbarButton, GUILayout.Width(55)))
            {
                addingNewPath = false;
                newPathInput = "";
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSearchBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Search:", GUILayout.Width(50));
            searchFilter = EditorGUILayout.TextField(searchFilter, EditorStyles.toolbarSearchField);

            if (GUILayout.Button("Clear", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                searchFilter = "";
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawCategoryFilter()
        {
            float availableWidth = position.width - 4f;
            Color originalBg = GUI.backgroundColor;
            var buttonStyle = EditorStyles.toolbarButton;
            const float filterLabelWidth = 50f;

            // Build button descriptors: (label, width, onClick, isSelected)
            var buttons = new List<(string label, float width, System.Action onClick, bool isSelected)>();

            string allLabel = $"ALL ({allEvents.Count})";
            float allWidth = Mathf.Max(80f, buttonStyle.CalcSize(new GUIContent(allLabel)).x);
            buttons.Add((allLabel, allWidth, () => { selectedCategory = null; showingNewOnly = false; }, selectedCategory == null && !showingNewOnly));

            foreach (var category in allCategories)
            {
                int count = allEvents.Count(e => e.categories != null && e.categories.Contains(category));
                string dispName = string.IsNullOrEmpty(category.categoryDisplayName) ? category.name : category.categoryDisplayName;
                string label = $"{dispName} ({count})";
                float w = Mathf.Min(150f, Mathf.Max(60f, buttonStyle.CalcSize(new GUIContent(label)).x));
                var cat = category;
                buttons.Add((label, w, () => { selectedCategory = cat; showingNewOnly = false; }, selectedCategory == category && !showingNewOnly));
            }

            // Prepend a virtual "New" filter button while there are unsaved newly created events
            if (newlyCreatedEvents.Count > 0)
            {
                string newLabel = $"✦ New ({newlyCreatedEvents.Count})";
                float newWidth = Mathf.Max(80f, buttonStyle.CalcSize(new GUIContent(newLabel)).x);
                buttons.Insert(0, (newLabel, newWidth, () => { showingNewOnly = true; selectedCategory = null; }, showingNewOnly));
            }

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("Filter:", GUILayout.Width(filterLabelWidth));
            float currentX = filterLabelWidth + 4f;

            for (int i = 0; i < buttons.Count; i++)
            {
                var (label, width, onClick, isSelected) = buttons[i];

                if (currentX + width > availableWidth && i > 0)
                {
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
                    GUILayout.Space(filterLabelWidth + 4f);
                    currentX = filterLabelWidth + 4f;
                }

                if (isSelected) GUI.backgroundColor = new Color(0.7f, 0.7f, 1f);
                if (GUILayout.Button(label, buttonStyle, GUILayout.Width(width)))
                    onClick();
                GUI.backgroundColor = originalBg;
                currentX += width + 2f;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawTableHeader()
        {
            // Category filter row
            DrawCategoryFilter();

            // Column headers
            var centeredBold = new GUIStyle(EditorStyles.boldLabel) { alignment = TextAnchor.MiddleCenter };
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            GUILayout.Label("Short Name", centeredBold, GUILayout.Width(COLUMN_SHORT_NAME));
            GUILayout.Label(new GUIContent("Display Name", "Optional override. If empty, Short Name is shown."), centeredBold, GUILayout.Width(COLUMN_DISPLAY_OVERRIDE));
            GUILayout.Label("Color", centeredBold, GUILayout.Width(COLUMN_COLOR));
            GUILayout.Label("Categories", centeredBold, GUILayout.Width(COLUMN_CATEGORIES));
            GUILayout.Label("Payloads", centeredBold, GUILayout.Width(COLUMN_PAYLOADS));
            GUILayout.Label("Debug", centeredBold, GUILayout.Width(COLUMN_DEBUG));
            GUILayout.Label("Actions", centeredBold, GUILayout.Width(COLUMN_ACTIONS));
            GUILayout.Label(new GUIContent("Description", "Describes what this event is used for and when it should be triggered."), centeredBold, GUILayout.Width(COLUMN_DESCRIPTION));

            EditorGUILayout.EndHorizontal();

            // Separator line
            EditorGUILayout.LabelField("", GUI.skin.horizontalSlider);
        }

        private void DrawEventTable()
        {
            // Materialize to a concrete list so that any GUI actions that modify
            // allEvents or eventsToDelete mid-loop don't corrupt the iterator.
            var filteredEvents = GetFilteredEvents().ToList();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var evt in filteredEvents)
            {
                if (evt == null) continue;

                DrawEventRow(evt);
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawEventRow(EventMessage evt)
        {
            EditorGUILayout.BeginHorizontal();

            // Short Name (primary identifier) — uses DelayedTextField so changes commit on Enter/blur
            // Assign a named control for newly created events so we can focus it programmatically
            if (newlyCreatedEvents.Contains(evt))
                GUI.SetNextControlName($"shortName_{evt.GetInstanceID()}");
            EditorGUI.BeginChangeCheck();
            string newShortName = EditorGUILayout.DelayedTextField(evt.shortName ?? "", GUILayout.Width(COLUMN_SHORT_NAME));
            if (EditorGUI.EndChangeCheck())
            {
                evt.shortName = newShortName;
                MarkDirty(evt);
            }

            // Display Name (override) — greyed out when showing fallback from shortName
            bool isOverridden = !string.IsNullOrEmpty(evt.displayName);
            string displayValue = isOverridden ? evt.displayName : (evt.shortName ?? evt.name);

            Color originalContentColor = GUI.contentColor;
            if (!isOverridden)
            {
                GUI.contentColor = new Color(
                    originalContentColor.r,
                    originalContentColor.g,
                    originalContentColor.b,
                    0.4f
                );
            }

            EditorGUI.BeginChangeCheck();
            string newDisplayName = EditorGUILayout.TextField(displayValue, GUILayout.Width(COLUMN_DISPLAY_OVERRIDE));
            if (EditorGUI.EndChangeCheck())
            {
                // If cleared or matches shortName, remove override
                if (string.IsNullOrEmpty(newDisplayName) ||
                    newDisplayName == (evt.shortName ?? evt.name))
                {
                    evt.displayName = "";
                }
                else
                {
                    evt.displayName = newDisplayName;
                }
                MarkDirty(evt);
            }

            GUI.contentColor = originalContentColor;

            // Accent Color
            EditorGUI.BeginChangeCheck();
            Color newColor = EditorGUILayout.ColorField(GUIContent.none, evt.accentColor, false, false, false, GUILayout.Width(COLUMN_COLOR));
            if (EditorGUI.EndChangeCheck())
            {
                evt.accentColor = newColor;
                MarkDirty(evt);
            }

            // Categories button
            int categoryCount = evt.categories?.Count ?? 0;
            string categoryLabel = categoryCount == 0 ? "None" : $"{categoryCount} selected";
            if (GUILayout.Button(categoryLabel, GUILayout.Width(COLUMN_CATEGORIES)))
            {
                ShowCategorySelector(evt);
            }

            // Payloads button
            int payloadCount = evt.expectedPayloads?.Count ?? 0;
            string payloadLabel = payloadCount == 0 ? "None" : $"{payloadCount} types";
            if (GUILayout.Button(payloadLabel, GUILayout.Width(COLUMN_PAYLOADS)))
            {
                ShowPayloadEditor(evt);
            }

            // Debug toggles (compact)
            EditorGUILayout.BeginVertical(GUILayout.Width(COLUMN_DEBUG));

            EditorGUI.BeginChangeCheck();
            bool newMuteLog = EditorGUILayout.ToggleLeft("Mute", evt.muteDebugLog, GUILayout.Width(COLUMN_DEBUG));
            if (EditorGUI.EndChangeCheck())
            {
                evt.muteDebugLog = newMuteLog;
                MarkDirty(evt);
            }

            EditorGUI.BeginChangeCheck();
            bool newSkipValidation = EditorGUILayout.ToggleLeft("Skip Val.", evt.skipPayloadValidation, GUILayout.Width(COLUMN_DEBUG));
            if (EditorGUI.EndChangeCheck())
            {
                evt.skipPayloadValidation = newSkipValidation;
                MarkDirty(evt);
            }

            EditorGUILayout.EndVertical();

            // Actions
            EditorGUILayout.BeginHorizontal(GUILayout.Width(COLUMN_ACTIONS));

            // Edit button (ping asset)
            if (GUILayout.Button("📝", GUILayout.Width(24)))
            {
                EditorGUIUtility.PingObject(evt);
                Selection.activeObject = evt;
            }

            // Monitor button (open debug monitor with this event selected)
            var monitorIcon = EditorGUIUtility.IconContent("d_UnityEditor.AnimationWindow");
            if (GUILayout.Button(monitorIcon, GUILayout.Width(24), GUILayout.Height(18)))
            {
                EventDebugMonitorWindow.ShowWindow(evt);
            }

            // Delete button
            Color originalBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("🗑", GUILayout.Width(24)))
            {
                ConfirmDelete(evt);
            }
            GUI.backgroundColor = originalBg;

            EditorGUILayout.EndHorizontal();

            // Description
            EditorGUI.BeginChangeCheck();
            string newDescription = EditorGUILayout.TextArea(evt.eventDescription ?? "", GUILayout.Width(COLUMN_DESCRIPTION), GUILayout.Height(ROW_HEIGHT * 2));
            if (EditorGUI.EndChangeCheck())
            {
                evt.eventDescription = newDescription;
                MarkDirty(evt);
            }

            EditorGUILayout.EndHorizontal();

            // Add subtle separator
            GUILayout.Space(2);
        }

        private void DrawBottomBar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            var filteredEvents = GetFilteredEvents();
            int totalEvents = allEvents.Count;
            int displayedEvents = filteredEvents.Count();

            string statusText = selectedCategory == null
                ? $"Showing {displayedEvents} events"
                : $"Showing {displayedEvents} of {totalEvents} events";

            if (!string.IsNullOrEmpty(searchFilter))
            {
                statusText += $" (filtered by: \"{searchFilter}\")";
            }

            GUILayout.Label(statusText);

            GUILayout.FlexibleSpace();

            if (dirtyEvents.Count > 0)
            {
                GUILayout.Label($"⚠ {dirtyEvents.Count} unsaved changes", EditorStyles.boldLabel);
            }

            if (eventsToDelete.Count > 0)
            {
                GUILayout.Label($"⚠ {eventsToDelete.Count} pending deletions", EditorStyles.boldLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        private IEnumerable<EventMessage> GetFilteredEvents()
        {
            IEnumerable<EventMessage> filtered = allEvents;

            if (showingNewOnly)
            {
                // "New" virtual filter — only unsaved newly created events
                filtered = filtered.Where(e => newlyCreatedEvents.Contains(e));
            }
            else
            {
                // Regular category filter
                if (selectedCategory != null)
                {
                    filtered = filtered.Where(e => e.categories != null && e.categories.Contains(selectedCategory));
                }

                // Float newly created events to the top of any view
                if (newlyCreatedEvents.Count > 0)
                {
                    var newOnes = filtered.Where(e => newlyCreatedEvents.Contains(e));
                    var rest = filtered.Where(e => !newlyCreatedEvents.Contains(e));
                    filtered = newOnes.Concat(rest);
                }
            }

            // Filter by search
            if (!string.IsNullOrEmpty(searchFilter))
            {
                string searchLower = searchFilter.ToLower();
                filtered = filtered.Where(e =>
                    (e.shortName != null && e.shortName.ToLower().Contains(searchLower)) ||
                    (e.GetDisplayName() != null && e.GetDisplayName().ToLower().Contains(searchLower))
                );
            }

            // Exclude events pending deletion
            filtered = filtered.Where(e => !eventsToDelete.Contains(e));

            return filtered;
        }

        private void MarkDirty(EventMessage evt)
        {
            if (!dirtyEvents.Contains(evt))
            {
                dirtyEvents.Add(evt);
            }
        }

        private void ShowCategorySelector(EventMessage evt)
        {
            GenericMenu menu = new GenericMenu();

            if (evt.categories == null)
            {
                evt.categories = new List<CategoryDefinition>();
            }

            foreach (var category in allCategories)
            {
                bool isSelected = evt.categories.Contains(category);
                string displayName = string.IsNullOrEmpty(category.categoryDisplayName) ? category.name : category.categoryDisplayName;

                menu.AddItem(
                    new GUIContent(displayName),
                    isSelected,
                    () => ToggleCategory(evt, category)
                );
            }

            if (allCategories.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No categories found"));
            }

            menu.ShowAsContext();
        }

        private void ToggleCategory(EventMessage evt, CategoryDefinition category)
        {
            if (evt.categories == null)
            {
                evt.categories = new List<CategoryDefinition>();
            }

            if (evt.categories.Contains(category))
            {
                evt.categories.Remove(category);
            }
            else
            {
                evt.categories.Add(category);
            }

            MarkDirty(evt);
            Repaint();
        }

        private void ShowPayloadEditor(EventMessage evt)
        {
            PayloadEditorWindow.ShowWindow(evt, () => {
                MarkDirty(evt);
                Repaint();
            });
        }

        private void ConfirmDelete(EventMessage evt)
        {
            // Defer the dialog to after the current GUI pass to avoid GUILayout
            // state corruption and collection-modified exceptions that occur when
            // a modal dialog is shown mid-layout.
            EditorApplication.delayCall += () =>
            {
                if (evt == null) return;
                string message = $"Are you sure you want to delete event '{evt.GetDisplayName()}'?\n\nThis action will be applied when you click Save.";
                if (EditorUtility.DisplayDialog("Confirm Deletion", message, "Delete", "Cancel"))
                {
                    // Release IMGUI keyboard/hot control BEFORE the list redraws.
                    // Without this, Unity reassigns the deleted row's DelayedTextField
                    // control ID to the next row, causing its pending value to be
                    // committed into the next event's shortName (accidental rename).
                    GUIUtility.hotControl = 0;
                    GUIUtility.keyboardControl = 0;
                    EditorGUIUtility.editingTextField = false;

                    if (!eventsToDelete.Contains(evt))
                        eventsToDelete.Add(evt);
                    dirtyEvents.Remove(evt);
                    // If this was a newly created event that never got saved, remove it
                    // from the new-only tracking set and exit that filter if empty.
                    newlyCreatedEvents.Remove(evt);
                    if (newlyCreatedEvents.Count == 0 && showingNewOnly)
                        showingNewOnly = false;
                    Repaint();
                }
            };
        }

        /// <summary>
        /// Ensures all folders in the given path exist, creating them recursively if needed.
        /// </summary>
        private static void EnsureFolderExists(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            if (!string.IsNullOrEmpty(parent) && !AssetDatabase.IsValidFolder(parent))
            {
                EnsureFolderExists(parent);
            }

            string folderName = System.IO.Path.GetFileName(folderPath);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private void CreateNewEvent()
        {
            // Create directory if it doesn't exist
            string targetDir = newEventPath.TrimEnd('/');
            EnsureFolderExists(targetDir);

            // Generate unique name: "NewEvent Event Message", "NewEvent_1 Event Message", etc.
            string baseShortName = "NewEvent";
            string assetPath = $"{targetDir}/{baseShortName} Event Message.asset";
            int counter = 1;

            while (AssetDatabase.LoadAssetAtPath<EventMessage>(assetPath) != null)
            {
                assetPath = $"{targetDir}/{baseShortName}_{counter} Event Message.asset";
                counter++;
            }

            // Create the asset
            EventMessage newEvent = ScriptableObject.CreateInstance<EventMessage>();
            newEvent.displayName = ""; // Empty = use shortName as fallback
            // Use first word of the asset file name as shortName
            string fileName = System.IO.Path.GetFileNameWithoutExtension(assetPath);
            newEvent.shortName = fileName.Split(' ')[0];
            newEvent.accentColor = Color.white;
            newEvent.categories = new List<CategoryDefinition>();
            newEvent.expectedPayloads = new List<IEventMessagePayload>();
            newEvent.muteDebugLog = false;
            newEvent.skipPayloadValidation = false;

            // Suppress BEFORE CreateAsset: CreateAsset fires OnPostprocessAllAssets
            // synchronously. If suppression is set after, codegen schedules before we
            // can stop it → .cs files written → script compile → domain reload → all
            // non-serialised window state (dirtyEvents, newlyCreatedEvents, showingNewOnly)
            // is wiped → Save stays grey, New filter is empty.
            EventMessageCodeGenerator.SuppressRegenerationForPath(assetPath);

            AssetDatabase.CreateAsset(newEvent, assetPath);

            // Force the asset to be indexed before LoadAllData scans for it.
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            // Track as newly created. Do NOT save or regenerate yet — let the designer fill in details first.
            newlyCreatedEvents.Add(newEvent);
            MarkDirty(newEvent);

            // Switch to the "New" filter and scroll to top so the event is immediately visible
            showingNewOnly = true;
            selectedCategory = null;
            scrollPosition = Vector2.zero;

            // Schedule focus on the short name field
            pendingFocusEvent = newEvent;
            pendingFocusFrames = 0;

            // Reload data to include new event
            LoadAllData();

            // Safety net: if LoadAllData didn't pick up the asset (timing edge-case),
            // insert it manually so the New filter is never empty.
            if (!allEvents.Contains(newEvent))
                allEvents.Insert(0, newEvent);

            // Select and ping the new event
            Selection.activeObject = newEvent;
            EditorGUIUtility.PingObject(newEvent);

            Repaint();
        }

        private void SaveAllChanges()
        {
            try
            {
                // Lift regeneration suppression for all pending new events.
                // Any path registered in CreateNewEvent must be released here so that
                // the postprocessor can react normally going forward, and so that the
                // explicit RegenerateAllEvents() call below sees the full asset set.
                foreach (var evt in newlyCreatedEvents)
                {
                    if (evt != null)
                    {
                        string p = AssetDatabase.GetAssetPath(evt);
                        if (!string.IsNullOrEmpty(p))
                            EventMessageCodeGenerator.AllowRegenerationForPath(p);
                    }
                }

                // ── Step 1: Delete queued events FIRST ──────────────────────────
                // Deletions must precede renames. If a dirty event was accidentally
                // given the same shortName as a to-be-deleted event (e.g. via an
                // IMGUI focus-steal), the rename would collide with the still-existing
                // asset and fail silently. Deleting first frees the name so the rename
                // succeeds, and codegen finds only one asset per shortName.
                EditorUtility.DisplayProgressBar("Saving Events", "Deleting removed events...", 0.15f);
                foreach (var evt in eventsToDelete)
                {
                    if (evt != null)
                    {
                        string path = AssetDatabase.GetAssetPath(evt);
                        if (!string.IsNullOrEmpty(path))
                        {
                            AssetDatabase.DeleteAsset(path);
                        }
                    }
                }

                // ── Step 2: Mark dirty and rename modified events ────────────────
                EditorUtility.DisplayProgressBar("Saving Events", "Saving modified events...", 0.35f);

                // Save modified events
                foreach (var evt in dirtyEvents)
                {
                    if (evt != null)
                    {
                        EditorUtility.SetDirty(evt);
                    }
                }

                // Rename assets whose shortName differs from the first word of the asset name
                foreach (var evt in dirtyEvents)
                {
                    if (evt != null && !string.IsNullOrEmpty(evt.shortName))
                    {
                        string currentFirstWord = evt.name.Split(' ')[0];
                        if (evt.shortName != currentFirstWord)
                        {
                            string path = AssetDatabase.GetAssetPath(evt);
                            if (!string.IsNullOrEmpty(path))
                            {
                                // Replace only the first word, keep the rest of the asset name
                                string rest = evt.name.Contains(" ") ? evt.name.Substring(evt.name.IndexOf(' ')) : "";
                                string newAssetName = evt.shortName + rest;
                                string result = AssetDatabase.RenameAsset(path, newAssetName);
                                if (!string.IsNullOrEmpty(result))
                                {
                                    Debug.LogWarning($"[Event Manager] Failed to rename '{evt.name}' to '{newAssetName}': {result}");
                                }
                            }
                        }
                    }
                }

                EditorUtility.DisplayProgressBar("Saving Events", "Committing changes to disk...", 0.55f);
                AssetDatabase.SaveAssets();

                EditorUtility.DisplayProgressBar("Saving Events", "Refreshing registry...", 0.6f);

                // Refresh the EventCoordinator registry
                var coordinator = EventCoordinator.Instance;
                if (coordinator != null)
                {
                    coordinator.RefreshDefinitions();
                }

                EditorUtility.DisplayProgressBar("Saving Events", "Regenerating code...", 0.8f);

                // Trigger code regeneration
                EventMessageCodeGenerator.RegenerateAllEvents();

                // Clear dirty state
                dirtyEvents.Clear();
                eventsToDelete.Clear();
                newlyCreatedEvents.Clear();
                showingNewOnly = false;

                EditorUtility.DisplayProgressBar("Saving Events", "Reloading data...", 0.9f);

                // Reload all data
                LoadAllData();

                EditorUtility.ClearProgressBar();

                Debug.Log($"[Event Manager] Successfully saved events and regenerated code.");
            }
            catch (Exception ex)
            {
                EditorUtility.ClearProgressBar();
                EditorUtility.DisplayDialog("Save Error", $"An error occurred while saving:\n\n{ex.Message}", "OK");
                Debug.LogError($"[Event Manager] Save failed: {ex}");
            }

            Repaint();
        }

        private void HandleKeyboardShortcuts()
        {
            Event e = Event.current;
            if (e.type == EventType.KeyDown)
            {
                // Ctrl+S to save
                if (e.control && e.keyCode == KeyCode.S)
                {
                    if (dirtyEvents.Count > 0 || eventsToDelete.Count > 0)
                    {
                        SaveAllChanges();
                        e.Use();
                    }
                }

                // Ctrl+R to refresh
                if (e.control && e.keyCode == KeyCode.R)
                {
                    LoadAllData();
                    e.Use();
                }
            }
        }
    }

    /// <summary>
    /// Popup window for editing event payloads.
    /// </summary>
    public class PayloadEditorWindow : EditorWindow
    {
        private EventMessage targetEvent;
        private Action onChanged;
        private Vector2 scrollPos;
        private UnityEditor.Editor payloadEditor;

        public static void ShowWindow(EventMessage evt, Action onChangedCallback)
        {
            var window = GetWindow<PayloadEditorWindow>(true, "Edit Payloads", true);
            window.targetEvent = evt;
            window.onChanged = onChangedCallback;
            window.minSize = new Vector2(400, 300);
            window.maxSize = new Vector2(600, 600);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            if (targetEvent == null)
            {
                EditorGUILayout.HelpBox("Target event is null.", MessageType.Error);
                if (GUILayout.Button("Close"))
                {
                    Close();
                }
                return;
            }

            EditorGUILayout.LabelField($"Event: {targetEvent.GetDisplayName()}", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("Edit the expected payload types for this event. This list is used for validation when triggering events.", MessageType.Info);
            EditorGUILayout.Space();

            scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

            // Create a serialized object for the event
            SerializedObject serializedEvent = new SerializedObject(targetEvent);
            SerializedProperty payloadsProp = serializedEvent.FindProperty("expectedPayloads");

            if (payloadsProp != null)
            {
                EditorGUI.BeginChangeCheck();
                EditorGUILayout.PropertyField(payloadsProp, new GUIContent("Expected Payloads"), true);

                if (EditorGUI.EndChangeCheck())
                {
                    serializedEvent.ApplyModifiedProperties();
                    onChanged?.Invoke();
                }
            }
            else
            {
                EditorGUILayout.HelpBox("Could not find 'expectedPayloads' property.", MessageType.Warning);
            }

            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();

            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Done", GUILayout.Width(100)))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
#endif
