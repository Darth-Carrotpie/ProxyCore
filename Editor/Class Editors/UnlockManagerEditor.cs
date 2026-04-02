using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    [CustomEditor(typeof(UnlockManager))]
    public class UnlockManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            GUILayout.Space(8);
            if (GUILayout.Button("Refresh Registries"))
            {
                RefreshRegistries((UnlockManager)target);
            }
        }

        /// <summary>
        /// Scans the AssetDatabase for all ScriptableObject assets that implement IUnlockableCatalog
        /// and merges them into the manager's _registries list.
        /// Existing entries keep their Enabled state; new entries default to Enabled = true.
        /// Null or missing entries are pruned.
        /// </summary>
        internal static void RefreshRegistries(UnlockManager manager)
        {
            var so = new SerializedObject(manager);
            var registriesProp = so.FindProperty("_registries");

            // Build a map of existing registry → enabled state.
            var existingEnabled = new Dictionary<Object, bool>();
            for (int i = 0; i < registriesProp.arraySize; i++)
            {
                var entry = registriesProp.GetArrayElementAtIndex(i);
                var registryRef = entry.FindPropertyRelative("Registry").objectReferenceValue;
                var enabled = entry.FindPropertyRelative("Enabled").boolValue;
                if (registryRef != null)
                    existingEnabled[registryRef] = enabled;
            }

            // Find all IUnlockableCatalog assets.
            var catalogs = new List<Object>();
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset is IUnlockableCatalog)
                    catalogs.Add(asset);
            }

            // Rebuild the list, retaining known Enabled states.
            registriesProp.ClearArray();
            for (int i = 0; i < catalogs.Count; i++)
            {
                registriesProp.InsertArrayElementAtIndex(i);
                var entry = registriesProp.GetArrayElementAtIndex(i);
                entry.FindPropertyRelative("Registry").objectReferenceValue = catalogs[i];
                entry.FindPropertyRelative("Enabled").boolValue =
                    existingEnabled.TryGetValue(catalogs[i], out bool prev) ? prev : true;
            }

            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(manager);
            AssetDatabase.SaveAssets();

            Debug.Log($"[UnlockManager] Refreshed unlock registries: {catalogs.Count} catalog(s) found.");
        }
    }
}
