using UnityEditor;
using UnityEngine;

namespace ProxyCore {

    [CustomEditor(typeof(BaseRegistry<CategoryDefinition>), editorForChildClasses: true)] //bridge
    public class CategoryRegistryEditor : BaseRegistryEditor<CategoryRegistry, CategoryDefinition> {
    }
}
