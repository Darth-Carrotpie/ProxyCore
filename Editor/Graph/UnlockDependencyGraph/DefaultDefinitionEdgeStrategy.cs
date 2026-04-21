using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Default strategy: direct edges use <see cref="DefinitionUnlockedCondition"/>.
    /// </summary>
    public sealed class DefaultDefinitionEdgeStrategy : IDefinitionEdgeStrategy {
        public bool CanHandle(System.Type sourceType) => true;

        public string PassStateLabel => "Unlocked";

        public UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder) {
            if (source == null) return null;

            var existing = FindExistingConditionAsset(source);
            if (existing != null) return existing;

            var condition = ScriptableObject.CreateInstance<DefinitionUnlockedCondition>();
            condition.name = $"{source.name}_Unlocked";

            var condSO = new SerializedObject(condition);
            var targetProp = condSO.FindProperty("_target");
            if (targetProp != null) {
                targetProp.objectReferenceValue = source;
                condSO.ApplyModifiedPropertiesWithoutUndo();
            }

            UnlockGraphView.EnsureFolderExists(conditionsFolder);
            string assetPath = AssetDatabase.GenerateUniqueAssetPath(
                $"{conditionsFolder}/{condition.name}.asset");
            AssetDatabase.CreateAsset(condition, assetPath);

            return condition;
        }

        public BaseDefinition GetDirectEdgeSource(UnlockCondition condition) {
            if (condition is not DefinitionUnlockedCondition duc) return null;
            var so = new SerializedObject(duc);
            var targetProp = so.FindProperty("_target");
            return targetProp?.objectReferenceValue as BaseDefinition;
        }

        private static DefinitionUnlockedCondition FindExistingConditionAsset(BaseDefinition source) {
            string[] guids = AssetDatabase.FindAssets("t:DefinitionUnlockedCondition");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var duc = AssetDatabase.LoadAssetAtPath<DefinitionUnlockedCondition>(path);
                if (duc == null) continue;

                var so = new SerializedObject(duc);
                var targetProp = so.FindProperty("_target");
                if (targetProp?.objectReferenceValue == source)
                    return duc;
            }

            return null;
        }
    }
}
