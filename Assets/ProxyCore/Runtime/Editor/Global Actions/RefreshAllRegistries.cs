using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ProxyCore.Editor {
    public static class RefreshAllRegistries {
        [MenuItem("ProxyCore/Refresh All Registries")]
        public static void RefreshAll() {
            try {
                string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
                int refreshed = 0;
                var refreshedLines = new List<string>();

                for (int i = 0; i < guids.Length; i++) {
                    string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                    var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (so == null) continue;

                    var type = so.GetType();
                    if (!IsSubclassOfRawGeneric(type, typeof(BaseRegistry<>))) continue;

                    // Call RefreshDefinitions (public virtual in BaseRegistry<T>)
                    var mi = type.GetMethod("RefreshDefinitions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (mi == null) continue;

                    EditorUtility.DisplayProgressBar("Refreshing Registries", $"{so.name}", (float)i / Math.Max(1, guids.Length));
                    try {
                        mi.Invoke(so, null);
                        refreshed++;

                        int defCount = TryGetDefinitionCount(so);
                        string countText = defCount >= 0 ? $"{defCount} defs" : "defs: n/a";
                        refreshedLines.Add($"- {so.name} ({type.Name}) — {countText} — {path}");
                    }
                    catch (Exception ex) {
                        Debug.LogError($"Failed to refresh registry '{so.name}' at '{path}': {ex}");
                    }
                }

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (refreshed > 0) {
                    Debug.Log($"ProxyCore: Refreshed {refreshed} registries:\n{string.Join("\n", refreshedLines)}");
                }
                else {
                    Debug.Log("ProxyCore: No registries found to refresh.");
                }
            }
            finally {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("Tools/ProxyCore/Refresh All Registries", true)]
        public static bool ValidateRefreshAll() {
            // Always enabled in Editor
            return true;
        }

        private static bool IsSubclassOfRawGeneric(Type toCheck, Type generic) {
            while (toCheck != null && toCheck != typeof(object)) {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (cur == generic) return true;
                toCheck = toCheck.BaseType;
            }
            return false;
        }

        private static int TryGetDefinitionCount(ScriptableObject so) {
            var fi = so.GetType().GetField("definitions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null) {
                var listObj = fi.GetValue(so) as ICollection;
                if (listObj != null) return listObj.Count;
            }
            return -1;
        }
    }
}
