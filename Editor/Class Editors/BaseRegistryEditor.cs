using UnityEngine;
namespace ProxyCore {

    public abstract class BaseRegistryEditor<TRegistry, TDef> : UnityEditor.Editor
    where TRegistry : BaseRegistry<TDef>
    where TDef : BaseDefinition {
        public override void OnInspectorGUI() {
            base.OnInspectorGUI();

            TRegistry registry = (TRegistry)target;

            GUILayout.Space(10);

            if (GUILayout.Button("🔄 Refresh Definitions")) {
                registry.RefreshDefinitions();
            }
        }
    }
}
