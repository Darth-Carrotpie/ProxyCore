using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Base editor for BaseDefinition-derived ScriptableObjects.
    /// Provides a refresh button that updates only the registry containing this definition type.
    /// </summary>
    public abstract class BaseDefinitionEditor<TDef> : UnityEditor.Editor
        where TDef : BaseDefinition
    {
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();
            DrawRefreshButton();
        }

        /// <summary>
        /// Draws the refresh registry button. Call this from derived classes that override OnInspectorGUI.
        /// </summary>
        protected void DrawRefreshButton()
        {
            GUILayout.Space(10);

            if (GUILayout.Button("🔄 Refresh Registry"))
            {
                RefreshRegistryForDefinition();
            }
        }

        private void RefreshRegistryForDefinition()
        {
            // Find the registry that handles this definition type
            var registryType = FindRegistryType();

            if (registryType == null)
            {
                Debug.LogWarning($"No registry found for definition type {typeof(TDef).Name}");
                return;
            }

            // Find the registry instance in the project
            var registry = FindRegistryInstance(registryType);

            if (registry == null)
            {
                Debug.LogWarning($"No registry instance found for type {registryType.Name}");
                return;
            }

            // Call RefreshDefinitions on the registry
            var refreshMethod = registryType.GetMethod("RefreshDefinitions",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (refreshMethod == null)
            {
                Debug.LogWarning($"RefreshDefinitions method not found on {registryType.Name}");
                return;
            }

            try
            {
                refreshMethod.Invoke(registry, null);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"Refreshed registry: {registry.name} ({registryType.Name})");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to refresh registry '{registry.name}': {ex}");
            }
        }

        private Type FindRegistryType()
        {
            // Look for a BaseRegistry<TDef> type in all loaded assemblies
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in assembly.GetTypes())
                    {
                        if (type.IsAbstract || type.IsInterface) continue;
                        if (!typeof(ScriptableObject).IsAssignableFrom(type)) continue;

                        // Check if this type is BaseRegistry<TDef>
                        if (IsRegistryForDefinition(type, typeof(TDef)))
                        {
                            return type;
                        }
                    }
                }
                catch
                {
                    // Skip assemblies that can't be reflected
                }
            }

            return null;
        }

        private bool IsRegistryForDefinition(Type type, Type defType)
        {
            // Walk up the inheritance chain looking for BaseRegistry<TDef>
            Type currentType = type;
            while (currentType != null && currentType != typeof(object))
            {
                if (currentType.IsGenericType)
                {
                    var genericDef = currentType.GetGenericTypeDefinition();
                    if (genericDef.Name == "BaseRegistry`1")
                    {
                        var genericArgs = currentType.GetGenericArguments();
                        if (genericArgs.Length > 0 && genericArgs[0] == defType)
                        {
                            return true;
                        }
                    }
                }
                currentType = currentType.BaseType;
            }
            return false;
        }

        private ScriptableObject FindRegistryInstance(Type registryType)
        {
            // Search for all ScriptableObject assets of this type
            string[] guids = AssetDatabase.FindAssets($"t:{registryType.Name}");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);

                if (asset != null && asset.GetType() == registryType)
                {
                    return asset;
                }
            }

            return null;
        }
    }
}
