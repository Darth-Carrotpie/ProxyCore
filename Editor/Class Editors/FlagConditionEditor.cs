using UnityEngine;
using UnityEditor;

namespace ProxyCore {
    /// <summary>
    /// Custom inspector for FlagCondition that replaces the plain string field for
    /// _flagName with a dropdown populated from the assigned GameFlagCollection.
    /// </summary>
    [CustomEditor(typeof(FlagCondition))]
    public class FlagConditionEditor : UnityEditor.Editor {
        private SerializedProperty _collection;
        private SerializedProperty _flagName;

        private void OnEnable() {
            _collection = serializedObject.FindProperty("_collection");
            _flagName = serializedObject.FindProperty("_flagName");
        }

        public override void OnInspectorGUI() {
            serializedObject.Update();

            EditorGUILayout.PropertyField(_collection);

            var collectionObj = _collection.objectReferenceValue as GameFlagCollection;
            if (collectionObj != null) {
                var flags = collectionObj.GetDefinedFlagsArray();
                if (flags.Length > 0) {
                    int current = Mathf.Max(0, System.Array.IndexOf(flags, _flagName.stringValue));
                    current = EditorGUILayout.Popup("Flag Name", current, flags);
                    _flagName.stringValue = flags[current];
                }
                else {
                    EditorGUILayout.HelpBox(
                        "No flags declared in this GameFlagCollection. Add flag names to the collection asset first.",
                        MessageType.Info);
                    EditorGUILayout.PropertyField(_flagName);
                }
            }
            else {
                EditorGUILayout.PropertyField(_flagName);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
