using System;
using UnityEditor;
using UnityEngine;
using ProxyCore;
using ProxyCore.Editor.Graph;

[InitializeOnLoad]
internal static class QuestDefinitionEdgeStrategyRegistrar {
    static QuestDefinitionEdgeStrategyRegistrar() {
        DefinitionEdgeStrategyRegistry.Register(new QuestDefinitionEdgeStrategy());
    }
}

internal sealed class QuestDefinitionEdgeStrategy : IDefinitionEdgeStrategy {
    public bool CanHandle(Type sourceType) =>
        sourceType != null &&
        (sourceType == typeof(QuestDefinition) || sourceType.IsSubclassOf(typeof(QuestDefinition)));

    public string PassStateLabel => "Quest Completed";

    public bool OwnsCondition(UnlockCondition condition) => condition is QuestCompletedCondition;

    public UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder) {
        if (source is not QuestDefinition questDefinition) return null;

        var existing = FindExistingConditionAsset(questDefinition);
        if (existing != null) return existing;

        var condition = ScriptableObject.CreateInstance<QuestCompletedCondition>();
        condition.name = $"{questDefinition.name}_Completed";

        var condSO = new SerializedObject(condition);
        var questProp = condSO.FindProperty("_quest");
        if (questProp != null) {
            questProp.objectReferenceValue = questDefinition;
            condSO.ApplyModifiedPropertiesWithoutUndo();
        }

        UnlockGraphView.EnsureFolderExists(conditionsFolder);
        string assetPath = AssetDatabase.GenerateUniqueAssetPath(
            $"{conditionsFolder}/{condition.name}.asset");
        AssetDatabase.CreateAsset(condition, assetPath);

        return condition;
    }

    public BaseDefinition GetDirectEdgeSource(UnlockCondition condition) {
        if (condition is not QuestCompletedCondition qcc) return null;
        return qcc.Quest;
    }

    private static QuestCompletedCondition FindExistingConditionAsset(QuestDefinition quest) {
        foreach (string guid in AssetDatabase.FindAssets("t:QuestCompletedCondition")) {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            var cond = AssetDatabase.LoadAssetAtPath<QuestCompletedCondition>(path);
            if (cond == null) continue;
            if (cond.Quest == quest) return cond;
        }

        return null;
    }
}
