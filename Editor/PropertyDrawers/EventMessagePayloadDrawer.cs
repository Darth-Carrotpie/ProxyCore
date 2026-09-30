using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Custom property drawer for SerializeReference fields that implement IEventMessagePayload.
    /// Provides a dropdown to select from available payload types.
    /// </summary>
    [CustomPropertyDrawer(typeof(IEventMessagePayload), true)]
    public class EventMessagePayloadDrawer : PropertyDrawer
    {
        private static Type[] _payloadTypes;
        private static string[] _payloadTypeNames;

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EnsurePayloadTypesLoaded();

            EditorGUI.BeginProperty(position, label, property);

            // Get current type
            object currentValue = property.managedReferenceValue;
            Type currentType = currentValue?.GetType();
            int currentIndex = currentType != null ? Array.IndexOf(_payloadTypes, currentType) : -1;

            // Layout
            Rect labelRect = new Rect(position.x, position.y, EditorGUIUtility.labelWidth, EditorGUIUtility.singleLineHeight);
            Rect dropdownRect = new Rect(position.x + EditorGUIUtility.labelWidth + 2, position.y, position.width - EditorGUIUtility.labelWidth - 2, EditorGUIUtility.singleLineHeight);

            EditorGUI.LabelField(labelRect, label);

            // Dropdown for type selection
            int newIndex = EditorGUI.Popup(dropdownRect, currentIndex + 1, GetDropdownOptions()) - 1;

            if (newIndex != currentIndex)
            {
                if (newIndex < 0)
                {
                    property.managedReferenceValue = null;
                }
                else if (newIndex < _payloadTypes.Length)
                {
                    property.managedReferenceValue = Activator.CreateInstance(_payloadTypes[newIndex]);
                }
                property.serializedObject.ApplyModifiedProperties();
            }

            EditorGUI.EndProperty();
        }

        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        private static string[] GetDropdownOptions()
        {
            var options = new string[_payloadTypeNames.Length + 1];
            options[0] = "(None)";
            for (int i = 0; i < _payloadTypeNames.Length; i++)
            {
                options[i + 1] = _payloadTypeNames[i];
            }
            return options;
        }

        private static void EnsurePayloadTypesLoaded()
        {
            if (_payloadTypes != null) return;

            var types = new List<Type>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (typeof(IEventMessagePayload).IsAssignableFrom(type) &&
                            !type.IsAbstract &&
                            !type.IsInterface &&
                            type.GetConstructor(Type.EmptyTypes) != null)
                        {
                            types.Add(type);
                        }
                    }
                }
                catch
                {
                    // Skip assemblies that can't be reflected
                }
            }

            _payloadTypes = types.OrderBy(t => t.Name).ToArray();
            _payloadTypeNames = _payloadTypes.Select(t => t.Name).ToArray();
        }

        /// <summary>
        /// Force refresh of payload types (call after creating new payload classes)
        /// </summary>
        [MenuItem("ProxyCore/Refresh Payload Types")]
        public static void RefreshPayloadTypes()
        {
            _payloadTypes = null;
            _payloadTypeNames = null;
            Debug.Log("[EventMessagePayloadDrawer] Payload types cache cleared.");
        }
    }
}
