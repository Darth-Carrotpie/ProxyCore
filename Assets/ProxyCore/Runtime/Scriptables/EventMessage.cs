using System;
using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProxyCore
{
    /// <summary>
    /// ScriptableObject that defines an event message type.
    /// Contains display information, category hierarchy, expected payloads, and debug settings.
    /// Uses ScriptableObjectWithID to automatically generate and track unique IDs.
    /// </summary>
    [CreateAssetMenu(fileName = "New Event Message", menuName = "Definitions/Event Message")]
    public class EventMessage : BaseDefinition
    {

        [Header("Event Identity")]
        [Tooltip("Display name shown in UI and debug logs")]
        public string displayName;

        [Tooltip("Short name used in code generation and compact displays")]
        public string shortName;

        [Tooltip("Color associated with this event for visual identification")]
        public Color accentColor = Color.white;

        [Header("Category Hierarchy")]
        [Tooltip("Categories this event belongs to. Determines generated accessor paths (e.g., TriggerEvent.UI.Combat.EventName)")]
        public List<CategoryDefinition> categories;

        [Header("Expected Payloads")]
        [Tooltip("Payload types expected when triggering this event. Used for validation and documentation.")]
        [SerializeReference]
        public List<IEventMessagePayload> expectedPayloads = new List<IEventMessagePayload>();

        [Header("Validation Settings")]
        [Tooltip("Skip payload validation warnings when triggering this event")]
        public bool skipPayloadValidation;

        [Header("Debug Settings")]
        [Tooltip("Mute debug logging for this event (prevents console spam for frequent events)")]
        public bool muteDebugLog;

        /// <summary>
        /// Gets the unique event ID for dictionary lookups.
        /// </summary>
        public int GetEventID()
        {
            return ID;
        }

        /// <summary>
        /// Gets the name to use for code generation.
        /// Uses shortName if available, otherwise sanitizes displayName.
        /// </summary>
        public string GetCodeGenName()
        {
            if (!string.IsNullOrEmpty(shortName))
                return SanitizeForCodeGen(shortName);

            if (!string.IsNullOrEmpty(displayName))
                return SanitizeForCodeGen(displayName);

            return SanitizeForCodeGen(name);
        }

        /// <summary>
        /// Gets the full category path for this event (e.g., "UI.Combat").
        /// </summary>
        public string GetCategoryPath()
        {
            if (categories == null || categories.Count == 0)
                return string.Empty;

            var parts = new List<string>();
            foreach (var cat in categories)
            {
                if (cat == null) continue;

                string catName = cat.GetCodeGenName();
                if (!string.IsNullOrEmpty(catName))
                    parts.Add(catName);
            }

            return string.Join(".", parts);
        }

        /// <summary>
        /// Gets the full path including category and event name (e.g., "UI.Combat.Shoot").
        /// </summary>
        public string GetFullPath()
        {
            string catPath = GetCategoryPath();
            string eventName = GetCodeGenName();

            if (string.IsNullOrEmpty(catPath))
                return eventName;

            return $"{catPath}.{eventName}";
        }

        /// <summary>
        /// Sanitizes a string for use in code generation (removes spaces and special characters).
        /// </summary>
        public static string SanitizeForCodeGen(string input)
        {
            if (string.IsNullOrEmpty(input))
                return string.Empty;

            var result = new System.Text.StringBuilder();
            foreach (char c in input)
            {
                if (char.IsLetterOrDigit(c) || c == '_')
                {
                    result.Append(c);
                }
            }

            // Ensure doesn't start with a digit
            if (result.Length > 0 && char.IsDigit(result[0]))
            {
                result.Insert(0, '_');
            }

            return result.ToString();
        }

#if UNITY_EDITOR
        [Header("Editor")]
        [HideInInspector]
        public MonoScript eventTypeFile; // Reserved for future use

        [ProxyCore.ReadOnly]
        [SerializeField]
        private string eventTypeName;

        private void OnValidate() {
            if (eventTypeFile == null) {
                eventTypeName = string.Empty;
                return;
            }
            var _class = eventTypeFile.GetClass();
            if (_class != null) {
                eventTypeName = _class.FullName + ", " + _class.Assembly.GetName().Name;
            }
        }
#endif
    }
}
