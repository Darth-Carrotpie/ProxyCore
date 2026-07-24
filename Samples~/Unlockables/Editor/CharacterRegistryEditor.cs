using UnityEditor;

namespace ProxyCore
{
    [CustomEditor(typeof(BaseRegistry<CharacterDefinition>), editorForChildClasses: true)]
    public class CharacterRegistryEditor : BaseRegistryEditor<CharacterRegistry, CharacterDefinition>
    {
    }
}
