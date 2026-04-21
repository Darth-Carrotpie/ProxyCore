# Unlock Edge Strategy Guidelines

This guide explains how to implement and register custom definition-edge pass behaviors for the Unlock Graph.

## What The Strategy Controls

A definition-edge strategy controls three editor behaviors for direct edges:
- Which UnlockCondition asset is created or reused when you draw a direct Definition -> Definition edge.
- How a condition asset maps back to its source definition during graph rebuild.
- Which pass label appears on the source definition node in the graph.

## How Node-Level Pass Switching Works

Pass behavior is selected per definition node in the Unlock Graph.
- If only one strategy can handle that definition type, the node shows a fixed pass label.
- If multiple strategies can handle that definition type, the node shows a pass dropdown.
- The selected strategy is persisted in UnlockGraphLayoutData by definition asset GUID.
- New direct edges created from that source node use the selected strategy.

Note: Existing condition assets are not automatically migrated when you switch pass behavior. The switch affects newly created direct edges.

## Implementing A New Strategy

1. Create a class implementing IDefinitionEdgeStrategy.
2. Implement CanHandle(Type sourceType).
3. Return the desired PassStateLabel.
4. Implement GetOrCreateCondition(BaseDefinition source, string conditionsFolder).
5. Implement GetDirectEdgeSource(UnlockCondition condition).
6. Register the strategy from an editor-only InitializeOnLoad class.

## Registration Template

```csharp
using UnityEditor;

namespace ProxyCore.Editor.Graph {
    [InitializeOnLoad]
    internal static class MyDefinitionEdgeStrategyRegistrar {
        static MyDefinitionEdgeStrategyRegistrar() {
            DefinitionEdgeStrategyRegistry.Register(new MyDefinitionEdgeStrategy());
        }
    }
}
```

## Strategy Template

```csharp
using System;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor.Graph {
    internal sealed class MyDefinitionEdgeStrategy : IDefinitionEdgeStrategy {
        public bool CanHandle(Type sourceType) {
            return sourceType == typeof(MyDefinition) ||
                   sourceType.IsSubclassOf(typeof(MyDefinition));
        }

        public string PassStateLabel => "My Pass State";

        public UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder) {
            // Create or reuse a condition asset and wire source into its serialized field.
            // Use SerializedObject when fields are private.
            UnlockGraphView.EnsureFolderExists(conditionsFolder);

            var condition = ScriptableObject.CreateInstance<MyCondition>();
            condition.name = $"{source.name}_MyCondition";

            var so = new SerializedObject(condition);
            so.FindProperty("_mySourceField").objectReferenceValue = source;
            so.ApplyModifiedPropertiesWithoutUndo();

            var path = AssetDatabase.GenerateUniqueAssetPath(
                $"{conditionsFolder}/{condition.name}.asset");
            AssetDatabase.CreateAsset(condition, path);
            return condition;
        }

        public BaseDefinition GetDirectEdgeSource(UnlockCondition condition) {
            if (condition is not MyCondition typed) return null;
            var so = new SerializedObject(typed);
            return so.FindProperty("_mySourceField").objectReferenceValue as BaseDefinition;
        }
    }
}
```

## Example Patterns

### QuestDefinition: Quest Is Completed
- Definition type: QuestDefinition.
- Pass label: Quest Completed.
- Condition type: QuestCompletedCondition.
- Source field example: _quest.
- Runtime semantics: condition evaluates completion state, not just unlocked state.

### CharacterDefinition: Is Unlocked
- Definition type: CharacterDefinition.
- Pass label: Unlocked.
- Condition type: DefinitionUnlockedCondition or CharacterUnlockedCondition.
- Source field example: _target.

### FoodIngredientDefinition: Ingredient Is Tasted (example)
- Definition type: FoodIngredientDefinition.
- Pass label: Ingredient Tasted.
- Condition type: IngredientTastedCondition.
- Source field example: _ingredient.

## Coexisting Strategies For The Same Definition Type

You can register multiple strategies for one definition type.
- The graph node shows a pass dropdown so users can choose behavior per node.
- The selected strategy decides which condition asset type is created for new edges.
- Keep pass labels distinct to reduce ambiguity.

## Safety Recommendations

- Always use SerializedObject for private serialized fields.
- Reuse existing condition assets where sensible to avoid asset sprawl.
- Ensure GetDirectEdgeSource never throws. Return null for unhandled conditions.
- Keep CanHandle narrow and explicit.
- Use editor-only registration classes in editor assemblies/folders.

## Verification Checklist

1. Open Unlock Graph and select a node of the handled definition type.
2. Confirm pass label or dropdown appears as expected.
3. Draw a direct edge and confirm the expected condition asset type is created.
4. Reopen/rebuild graph and confirm the edge reconstructs as direct.
5. Delete one of multiple edges sharing a condition source and ensure remaining edges stay valid.
6. Confirm the selected pass mode persists after graph rebuild/reopen.
