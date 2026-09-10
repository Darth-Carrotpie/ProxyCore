using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Custom editor for EventMessage that provides an improved UI for managing expected payloads.
    /// </summary>
    [CustomEditor(typeof(EventMessage))]
    public class EventMessageEditor : BaseDefinitionEditor<EventMessage>
    {
        private ReorderableList _payloadList;
        private SerializedProperty _expectedPayloadsProperty;

        private static Type[] _payloadTypes;
        private static string[] _payloadTypeNames;

        private void OnEnable()
        {
            _expectedPayloadsProperty = serializedObject.FindProperty("expectedPayloads");
            EnsurePayloadTypesLoaded();

            _payloadList = new ReorderableList(serializedObject, _expectedPayloadsProperty, true, true, true, true);

            _payloadList.drawHeaderCallback = (Rect rect) =>
            {
                EditorGUI.LabelField(rect, "Expected Payloads (for validation & documentation)");
            };

            _payloadList.drawElementCallback = (Rect rect, int index, bool isActive, bool isFocused) =>
            {
                var element = _expectedPayloadsProperty.GetArrayElementAtIndex(index);
                rect.y += 2;
                rect.height = EditorGUIUtility.singleLineHeight;

                // Get current type
                object currentValue = element.managedReferenceValue;
                Type currentType = currentValue?.GetType();
                int currentTypeIndex = currentType != null ? Array.IndexOf(_payloadTypes, currentType) : -1;

                // Draw dropdown
                EditorGUI.BeginChangeCheck();
                int newTypeIndex = EditorGUI.Popup(rect, $"Payload {index + 1}", currentTypeIndex + 1, GetDropdownOptions()) - 1;

                if (EditorGUI.EndChangeCheck())
                {
                    if (newTypeIndex < 0)
                    {
                        element.managedReferenceValue = null;
                    }
                    else if (newTypeIndex < _payloadTypes.Length)
                    {
                        element.managedReferenceValue = Activator.CreateInstance(_payloadTypes[newTypeIndex]);
                    }
                }
            };

            _payloadList.onAddDropdownCallback = (Rect buttonRect, ReorderableList list) =>
            {
                var menu = new GenericMenu();

                for (int i = 0; i < _payloadTypes.Length; i++)
                {
                    int capturedIndex = i;
                    menu.AddItem(new GUIContent(_payloadTypeNames[i]), false, () =>
                    {
                        serializedObject.Update();
                        int newIndex = _expectedPayloadsProperty.arraySize;
                        _expectedPayloadsProperty.arraySize++;
                        var newElement = _expectedPayloadsProperty.GetArrayElementAtIndex(newIndex);
                        newElement.managedReferenceValue = Activator.CreateInstance(_payloadTypes[capturedIndex]);
                        serializedObject.ApplyModifiedProperties();
                    });
                }

                menu.ShowAsContext();
            };

            _payloadList.elementHeight = EditorGUIUtility.singleLineHeight + 4;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw all properties except expectedPayloads
            DrawPropertiesExcluding(serializedObject, "expectedPayloads", "m_Script");

            EditorGUILayout.Space(10);

            // Draw the reorderable list for payloads
            _payloadList.DoLayoutList();

            // Quick add buttons
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Quick Add:", GUILayout.Width(70));

            if (GUILayout.Button("String", GUILayout.Height(20)))
            {
                AddPayloadOfType<StringPayload>();
            }
            if (GUILayout.Button("Int", GUILayout.Height(20)))
            {
                AddPayloadOfType<IntPayload>();
            }
            if (GUILayout.Button("Float", GUILayout.Height(20)))
            {
                AddPayloadOfType<FloatPayload>();
            }
            if (GUILayout.Button("Transform", GUILayout.Height(20)))
            {
                AddPayloadOfType<TransformPayload>();
            }
            if (GUILayout.Button("MonoRefs", GUILayout.Height(20)))
            {
                AddPayloadOfType<MonoRefsPayload>();
            }

            EditorGUILayout.EndHorizontal();

            serializedObject.ApplyModifiedProperties();

            // Draw the refresh button from base class
            DrawRefreshButton();
        }

        private void AddPayloadOfType<T>() where T : IEventMessagePayload, new()
        {
            serializedObject.Update();
            int newIndex = _expectedPayloadsProperty.arraySize;
            _expectedPayloadsProperty.arraySize++;
            var newElement = _expectedPayloadsProperty.GetArrayElementAtIndex(newIndex);
            newElement.managedReferenceValue = new T();
            serializedObject.ApplyModifiedProperties();
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
    }
}
