 using UnityEditor;
using UnityEngine;

namespace ScriptableWithID
{
#if UNITY_EDITOR
    [CustomPropertyDrawer(typeof(SerializedIDAttribute))]
    public class ScriptableIDCustomDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            GUI.enabled = false;
            EditorGUI.PropertyField(position, property, label, true);
            GUI.enabled = true;

            Rect propertyRect = new Rect(position);
            propertyRect.xMax -= 100;
            Rect buttonRect = new Rect(position)
            {
                xMin = position.xMax - 100
            };

            if (property.serializedObject.targetObject is ScriptableObjectWithID scriptableObject)
            {
                if (GUI.Button(buttonRect, "Regenerate ID"))
                {
                    int newId = IDGenerator.GenerateID();
                    scriptableObject.ID = newId;
                    property.intValue = scriptableObject.ID;
                }
            }
        }
    }
#endif
}
