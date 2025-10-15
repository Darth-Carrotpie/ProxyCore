using System;
using UnityEditor;
using UnityEngine;

namespace ProxyCore {
    [CreateAssetMenu(fileName = "NewEventCategory", menuName = "Definitions/Event Category Definition", order = 1)]
    public class EventCategoryDefinition : BaseDefinition {
        [Header("Category Properties")]
        public string categoryDisplayName;
        public Color categoryDisplayColor;
        public Sprite categoryDisplayIcon;

#if UNITY_EDITOR
        [HideInInspector]
        public MonoScript categoryTypeFile;
#endif

        public Type GetGeneratedCatComponentType() {
            // Use the generated struct name (spaces replaced with underscores)
            string typeName = this.name.Replace(" ", "_");
            // If you use a namespace for generated types, prepend it here, e.g. "ECS_Resources.Generated." + typeName
            // string typeName = $"ECS_Resources.Generated.{this.name.Replace(" ", "_")}";
            // Search all loaded assemblies for the type
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                var type = assembly.GetType(typeName, false);
                if (type != null)
                    return type;
            }
            return null;
        }

        /// <summary>
        /// Override GetComponentType to provide component type lookup for registry.
        /// </summary>
        /// <returns>The generated component type for this category definition.</returns>
        public override Type GetComponentType() {
            return GetGeneratedCatComponentType();
        }
    }
}
