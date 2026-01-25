#if UNITY_EDITOR
using System.Collections;
using UnityEditor;
using UnityEngine;

namespace ProxyCore
{
    public static class EditorExtension
    {
        public static int DrawBitMaskField(Rect aPosition, int aMask, System.Type aType, GUIContent aLabel)
        {
            var itemNames = System.Enum.GetNames(aType);
            var itemValues = System.Enum.GetValues(aType) as int[];

            int val = aMask;
            int maskVal = 0;
            for (int i = 0; i < itemValues.Length; i++)
            {
                if (itemValues[i] != 0)
                {
                    if ((val & itemValues[i]) == itemValues[i])
                        maskVal |= 1 << i;
                }
                else if (val == 0)
                    maskVal |= 1 << i;
            }
            int newMaskVal = EditorGUI.MaskField(aPosition, aLabel, maskVal, itemNames);
            int changes = maskVal ^ newMaskVal;

            for (int i = 0; i < itemValues.Length; i++)
            {
                if ((changes & (1 << i)) != 0) // has this list item changed?
                {
                    if ((newMaskVal & (1 << i)) != 0) // has it been set?
                    {
                        if (itemValues[i] == 0) // special case: if "0" is set, just set the val to 0
                        {
                            val = 0;
                            break;
                        }
                        else
                            val |= itemValues[i];
                    }
                    else // it has been reset
                    {
                        val &= ~itemValues[i];
                    }
                }
            }
            return val;
        }
        public static void SetProperty(this Object obj, string propertyName, object value)
        {
            SerializedObject so = new SerializedObject(obj);
            var property = so.FindProperty(propertyName);
            if (property == null)
            {
                Debug.LogError($"Cannot find property {propertyName} in object: {obj.name}");
                return;
            }
            so.Update();
            property.SetValue(value);
            EditorUtility.SetDirty(obj);
            so.ApplyModifiedProperties();
        }
        public static void AddArrayElement(this SerializedProperty prop, object elementValue)
        {
            prop.arraySize++;
            prop.GetArrayElementAtIndex(prop.arraySize - 1).SetValue(elementValue);
        }
        public static void RemoveArrayElement(this SerializedProperty prop, int elementValue)
        {
            int toDel = 0;
            for (int i = 0; i < prop.arraySize; i++)
            {
                if (prop.GetArrayElementAtIndex(prop.arraySize - 1).intValue == elementValue)
                {
                    toDel = i;
                }
            }
            prop.DeleteArrayElementAtIndex(toDel);
        }
        public static void SetValue(this SerializedProperty p, object value)
        {
            switch (p.propertyType)
            {
                case SerializedPropertyType.Generic:
                    Debug.LogWarning((object)
                        "Get/Set of Generic SerializedProperty not supported");
                    break;
                case SerializedPropertyType.Integer:
                    p.intValue = (int)value;
                    break;
                case SerializedPropertyType.Boolean:
                    p.boolValue = (bool)value;
                    break;
                case SerializedPropertyType.Float:
                    p.floatValue = (float)value;
                    break;
                case SerializedPropertyType.String:
                    p.stringValue = (string)value;
                    break;
                case SerializedPropertyType.Color:
                    p.colorValue = (Color)value;
                    break;
                case SerializedPropertyType.ObjectReference:
                    p.objectReferenceValue = value as UnityEngine.Object;
                    break;
                case SerializedPropertyType.LayerMask:
                    p.intValue = (int)value;
                    break;
                case SerializedPropertyType.Enum:
                    p.enumValueIndex = (int)value;
                    break;
                case SerializedPropertyType.Vector2:
                    p.vector2Value = (Vector2)value;
                    break;
                case SerializedPropertyType.Vector3:
                    p.vector3Value = (Vector3)value;
                    break;
                case SerializedPropertyType.Vector4:
                    p.vector4Value = (Vector4)value;
                    break;
                case SerializedPropertyType.Rect:
                    p.rectValue = (Rect)value;
                    break;
                case SerializedPropertyType.ArraySize:
                    p.intValue = (int)value;
                    break;
                case SerializedPropertyType.Character:
                    p.stringValue = (string)value;
                    break;
                case SerializedPropertyType.AnimationCurve:
                    p.animationCurveValue = value as AnimationCurve;
                    break;
                case SerializedPropertyType.Bounds:
                    p.boundsValue = (Bounds)value;
                    break;
                case SerializedPropertyType.Gradient:
                    Debug.LogWarning((object)
                        "Get/Set of Gradient SerializedProperty not supported");
                    break;
                case SerializedPropertyType.Quaternion:
                    p.quaternionValue = (Quaternion)value;
                    break;
            }
        }
        public static void PrintProperties(this SerializedProperty p, bool visitChildren)
        {
            var report = new System.Text.StringBuilder();
            report.AppendLine($"Object.name: {p.name})");
            report.AppendLine($"Traversal result (visitChildren: {visitChildren})");
            do
            {
                report.AppendLine($"\tFound {p.propertyPath} (depth {p.depth})");
            }
            while (p.Next(visitChildren));
            Debug.Log(report);
        }
    }

    [CustomPropertyDrawer(typeof(BitMaskAttribute))]
    public class EnumBitMaskPropertyDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty prop, GUIContent label)
        {
            var typeAttr = attribute as BitMaskAttribute;
            // Add the actual int value behind the field name
            label.text = label.text + "(" + prop.intValue + ")";
            prop.intValue = EditorExtension.DrawBitMaskField(position, prop.intValue, typeAttr.propType, label);
        }
    }
}
#endif