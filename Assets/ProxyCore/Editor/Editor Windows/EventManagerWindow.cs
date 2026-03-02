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

        // UI State
        [SerializeField] private CategoryDefinition selectedCategory = null; // null = ALL
        [SerializeField] private Vector2 scrollPosition;
        [SerializeField] private string searchFilter = "";

        // Settings
        private const string PREF_KEY_NEW_EVENT_PATH = "ProxyCore_EventManager_NewEventPath";
        private const string DEFAULT_NEW_EVENT_PATH = "Assets/ProxyEvents";

        private string newEventPath
        {
            get => EditorPrefs.GetString(PREF_KEY_NEW_EVENT_PATH, DEFAULT_NEW_EVENT_PATH);
            set => EditorPrefs.SetString(PREF_KEY_NEW_EVENT_PATH, value);
        }

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
        }

        private void DrawTopToolbar()
        {
            // Asset path row
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label("New Event Path:", GUILayout.Width(100));
            EditorGUI.BeginChangeCheck();
            string editedPath = EditorGUILayout.TextField(newEventPath);
            if (EditorGUI.EndChangeCheck())
            {
                // Ensure it always starts with "Assets/"
                if (!editedPath.StartsWith("Assets/"))
                    editedPath = "Assets/" + editedPath.TrimStart('/');
                newEventPath = editedPath;
            }
            EditorGUILayout.EndHorizontal();

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
            buttons.Add((allLabel, allWidth, () => selectedCategory = null, selectedCategory == null));

            foreach (var category in allCategories)
            {
                int count = allEvents.Count(e => e.categories != null && e.categories.Contains(category));
                string dispName = string.IsNullOrEmpty(category.categoryDisplayName) ? category.name : category.categoryDisplayName;
                string label = $"{dispName} ({count})";
                float w = Mathf.Min(150f, Mathf.Max(60f, buttonStyle.CalcSize(new GUIContent(label)).x));
                var cat = category;
                buttons.Add((label, w, () => selectedCategory = cat, selectedCategory == category));
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
            var filteredEvents = GetFilteredEvents();

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
            if (GUILayout.Button("📝", GUILayout.Width(30)))
            {
                EditorGUIUtility.PingObject(evt);
                Selection.activeObject = evt;
            }

            // Delete button
            Color originalBg = GUI.backgroundColor;
            GUI.backgroundColor = new Color(1f, 0.5f, 0.5f);
            if (GUILayout.Button("🗑", GUILayout.Width(30)))
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

            // Filter by category
            if (selectedCategory != null)
            {
                filtered = filtered.Where(e => e.categories != null && e.categories.Contains(selectedCategory));
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
            string message = $"Are you sure you want to delete event '{evt.GetDisplayName()}'?\n\nThis action will be applied when you click Save.";

            if (EditorUtility.DisplayDialog("Confirm Deletion", message, "Delete", "Cancel"))
            {
                if (!eventsToDelete.Contains(evt))
                {
                    eventsToDelete.Add(evt);
                }
                // Remove from dirty if it was there
                dirtyEvents.Remove(evt);
                Repaint();
            }
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

            AssetDatabase.CreateAsset(newEvent, assetPath);
            AssetDatabase.SaveAssets();

            // Reload data to include new event
            LoadAllData();

            // Select and ping the new event
            Selection.activeObject = newEvent;
            EditorGUIUtility.PingObject(newEvent);

            Repaint();
        }

        private void SaveAllChanges()
        {
            try
            {
                EditorUtility.DisplayProgressBar("Saving Events", "Saving modified events...", 0.2f);

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

                // Delete queued events
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

                EditorUtility.DisplayProgressBar("Saving Events", "Committing changes to disk...", 0.4f);
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
