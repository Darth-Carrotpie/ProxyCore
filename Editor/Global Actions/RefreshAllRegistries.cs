using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;

namespace ProxyCore.Editor
{
    public static class RefreshAllRegistries
    {
        [MenuItem("ProxyCore/Refresh All Registries")]
        public static void RefreshAll()
        {
            try
            {
                var refreshedLines = RefreshRegistries(respectAutoRefresh: false, showProgress: true);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();

                if (refreshedLines.Count > 0)
                {
                    Debug.Log($"ProxyCore: Refreshed {refreshedLines.Count} registries:\n{string.Join("\n", refreshedLines)}");
                }
                else
                {
                    Debug.Log("ProxyCore: No registries found to refresh.");
                }
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        [MenuItem("ProxyCore/Refresh All Registries", true)]
        public static bool ValidateRefreshAll()
        {
            // Always enabled in Editor
            return true;
        }

        /// <summary>
        /// Repopulates every <see cref="BaseRegistry{T}"/> asset's definitions list.
        /// </summary>
        /// <param name="respectAutoRefresh">
        /// When true, only registries with <c>autoRefresh</c> ticked are refreshed — this is
        /// the automatic import-driven path. The menu item passes false: an explicit refresh
        /// refreshes everything, opted out or not.
        /// </param>
        /// <returns>One summary line per registry actually refreshed.</returns>
        internal static List<string> RefreshRegistries(bool respectAutoRefresh, bool showProgress)
        {
            var refreshedLines = new List<string>();
            var registries = FindRegistryAssets();

            for (int i = 0; i < registries.Count; i++)
            {
                var (so, path) = registries[i];
                var type = so.GetType();

                if (respectAutoRefresh && !GetAutoRefresh(so)) continue;

                // RefreshDefinitions is public virtual on BaseRegistry<T>; the closed generic
                // base is not nameable from here, so go through reflection.
                var mi = type.GetMethod("RefreshDefinitions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (mi == null) continue;

                if (showProgress)
                    EditorUtility.DisplayProgressBar("Refreshing Registries", $"{so.name}", (float)i / Math.Max(1, registries.Count));

                try
                {
                    mi.Invoke(so, null);

                    int defCount = TryGetDefinitionCount(so);
                    string countText = defCount >= 0 ? $"{defCount} defs" : "defs: n/a";
                    refreshedLines.Add($"- {so.name} ({type.Name}) — {countText} — {path}");
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to refresh registry '{so.name}' at '{path}': {ex}");
                }
            }

            // No SaveAssets/Refresh here: RefreshDefinitions saves itself when it actually
            // changed something, and this runs on every definition import — an unconditional
            // AssetDatabase.Refresh() on every import is a full reimport scan for nothing.
            return refreshedLines;
        }

        /// <summary>
        /// Every registry asset in the project. Resolves the concrete registry *types* through
        /// TypeCache first, then searches assets per type — a project has a handful of registry
        /// types, so this is a few cheap searches instead of loading every ScriptableObject in
        /// the project to test its base class.
        /// </summary>
        internal static List<(ScriptableObject asset, string path)> FindRegistryAssets()
        {
            var result = new List<(ScriptableObject, string)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in TypeCache.GetTypesDerivedFrom<ScriptableObject>())
            {
                if (type.IsAbstract || type.IsGenericTypeDefinition) continue;
                if (!IsSubclassOfRawGeneric(type, typeof(BaseRegistry<>))) continue;

                foreach (string guid in AssetDatabase.FindAssets($"t:{type.Name}"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (!seen.Add(path)) continue;   // a subclass search can return the same asset twice

                    var so = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                    if (so == null) continue;
                    if (!IsSubclassOfRawGeneric(so.GetType(), typeof(BaseRegistry<>))) continue;

                    result.Add((so, path));
                }
            }

            return result;
        }

        /// <summary>
        /// The definition type a registry holds — the <c>T</c> of <c>BaseRegistry&lt;T&gt;</c>.
        /// Null when <paramref name="toCheck"/> is not a registry.
        /// </summary>
        internal static Type GetDefinitionType(Type toCheck)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                if (toCheck.IsGenericType && toCheck.GetGenericTypeDefinition() == typeof(BaseRegistry<>))
                    return toCheck.GetGenericArguments()[0];
                toCheck = toCheck.BaseType;
            }
            return null;
        }

        private static bool IsSubclassOfRawGeneric(Type toCheck, Type generic)
        {
            while (toCheck != null && toCheck != typeof(object))
            {
                var cur = toCheck.IsGenericType ? toCheck.GetGenericTypeDefinition() : toCheck;
                if (cur == generic) return true;
                toCheck = toCheck.BaseType;
            }
            return false;
        }

        private static bool GetAutoRefresh(ScriptableObject so)
        {
            var fi = so.GetType().GetField("autoRefresh", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi == null) return true;   // field is editor-only; assume opted in if it isn't there
            return fi.GetValue(so) is bool b && b;
        }

        private static int TryGetDefinitionCount(ScriptableObject so)
        {
            var fi = so.GetType().GetField("definitions", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null)
            {
                var listObj = fi.GetValue(so) as ICollection;
                if (listObj != null) return listObj.Count;
            }
            return -1;
        }
    }

    /// <summary>
    /// Makes <c>BaseRegistry&lt;T&gt;.autoRefresh</c> mean something: a definition asset that is
    /// created, imported, moved or deleted repopulates the registries that hold its kind, so a new
    /// asset is registered without anyone remembering to run ProxyCore ▸ Refresh All Registries.
    /// Without this, a brand new EventMessage resolves to null through its generated accessor even
    /// when its baked ID matches the asset exactly, because GetDefinition reads the serialized
    /// definitions list and the asset was never added to it.
    /// </summary>
    public class RegistryAutoRefreshPostprocessor : AssetPostprocessor
    {
        private static bool _refreshScheduled;
        private static double _lastChangeTime;
        private const double DebounceDelay = 0.1; // 100ms debounce, matching EventMessageCodeGenerator

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool hasDefinitionChanges = false;

            foreach (string path in importedAssets)
            {
                if (IsDefinitionAsset(path)) { hasDefinitionChanges = true; break; }
            }

            if (!hasDefinitionChanges)
            {
                foreach (string path in movedAssets)
                {
                    if (IsDefinitionAsset(path)) { hasDefinitionChanges = true; break; }
                }
            }

            if (!hasDefinitionChanges)
            {
                // A deleted asset can no longer be loaded, so its type is unknowable from the path.
                // Any deleted .asset gets a pass — it may have left a null hole in a definitions list.
                foreach (string path in deletedAssets)
                {
                    if (path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) { hasDefinitionChanges = true; break; }
                }
            }

            if (hasDefinitionChanges)
                ScheduleRefresh();
        }

        private static bool IsDefinitionAsset(string path)
        {
            if (!path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)) return false;
            return AssetDatabase.LoadAssetAtPath<BaseDefinition>(path) != null;
        }

        private static void ScheduleRefresh()
        {
            _lastChangeTime = EditorApplication.timeSinceStartup;

            if (!_refreshScheduled)
            {
                _refreshScheduled = true;
                // delayCall also keeps SaveAssets() out of the import callback, where it is illegal.
                EditorApplication.delayCall += CheckAndRefresh;
            }
        }

        private static void CheckAndRefresh()
        {
            if (EditorApplication.timeSinceStartup - _lastChangeTime < DebounceDelay)
            {
                EditorApplication.delayCall += CheckAndRefresh;
                return;
            }

            _refreshScheduled = false;

            // ponytail: refreshes every autoRefresh registry, not just the ones holding the changed
            // definition type — filter on RefreshAllRegistries.GetDefinitionType if a project with
            // many registries feels it. After BaseRegistry's unchanged-list early-out, an
            // unaffected registry costs one asset search and no write.
            RefreshAllRegistries.RefreshRegistries(respectAutoRefresh: true, showProgress: false);
        }
    }
}
