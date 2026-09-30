using UnityEditor;

namespace ProxyCore
{
    [CustomEditor(typeof(BaseRegistry<QuestDefinition>), editorForChildClasses: true)]
    public class QuestRegistryEditor : BaseRegistryEditor<QuestRegistry, QuestDefinition>
    {
    }
}
