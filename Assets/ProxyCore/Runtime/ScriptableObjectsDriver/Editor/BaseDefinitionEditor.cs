using UnityEngine;
namespace ProxyCore {
    public abstract class BaseDefinitionEditor<T> : UnityEditor.Editor where T : BaseDefinition {
        public override void OnInspectorGUI() {
            // Draw default inspector for base definitions
            DrawDefaultInspector();
            // ...extend in derived editors as needed...
        }
    }
}
