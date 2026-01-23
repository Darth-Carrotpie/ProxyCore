using UnityEngine;
using UnityEditor;

namespace ProxyCore {
    [CustomEditor(typeof(EventRegistry))]
    public class EventRegistryEditor : BaseRegistryEditor<EventRegistry, EventDefinition> {

        public override void OnInspectorGUI() {
            base.OnInspectorGUI();

            EventRegistry registry = (EventRegistry)target;
        }
    }
}
