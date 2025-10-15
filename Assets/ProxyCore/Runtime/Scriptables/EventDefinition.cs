using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;

namespace ProxyCore {
    /// <summary>
    /// ScriptableObject that defines a resource type.
    /// Contains display information and basic configuration for a resource.
    /// Uses ScriptableObjectWithID to automatically generate and track unique IDs.
    /// </summary>
    [CreateAssetMenu(fileName = "New Event Definition", menuName = "Definitions/Event Definition")]
    public class EventDefinition : BaseDefinition {

        [Header("Base Definition")]
        [Tooltip("Display base definition properties")]
        public BaseDefinitionProperties baseDefinitionProperties;

        [Tooltip("Color associated with this resource")]
        public Color accentColor = Color.white;

        private string eventTypeName;

#if UNITY_EDITOR
        [HideInInspector]
        public MonoScript eventTypeFile;
#endif

#if UNITY_EDITOR
        private void OnValidate() {
            if (eventTypeFile == null) {
                return;
            }
            var _class = eventTypeFile.GetClass();
            eventTypeName = _class.FullName + ", " + _class.Assembly.GetName().Name;
        }
#endif
    }
}
