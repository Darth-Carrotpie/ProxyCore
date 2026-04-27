# Unlock Edge Strategy Guidelines

This guide explains how to implement and register custom definition-edge pass behaviors for the Unlock Graph.

## What The Strategy Controls

A definition-edge strategy controls four editor behaviors for direct edges:
- Which UnlockCondition asset is created or reused when you draw a direct Definition -> Definition edge.
- How a condition asset maps back to its source definition during graph rebuild.
- Which pass label appears on the source definition node in the graph.
- Whether a condition asset belongs to this strategy (ownership declaration via `OwnsCondition`).

## How Node-Level Pass Switching Works

Pass behavior is selected per definition node in the Unlock Graph.
- If only one strategy can handle that definition type, the node shows a fixed pass label.
- If multiple strategies can handle that definition type, the node shows a pass dropdown.
- The selected strategy is persisted in UnlockGraphLayoutData by definition asset GUID.
- New direct edges created from that source node use the selected strategy.

When you change the selected pass behavior and redraw an edge, the graph automatically removes
any condition for that source that the new strategy does not own, then creates the correct type.
This prevents stale wrong-type conditions from silently persisting in a definition's prerequisites.

## Implementing A New Strategy

1. Create a class implementing `IDefinitionEdgeStrategy`.
2. Implement `CanHandle(Type sourceType)`.
3. Return the desired `PassStateLabel`.
4. Implement `GetOrCreateCondition(BaseDefinition source, string conditionsFolder)`.
5. Implement `GetDirectEdgeSource(UnlockCondition condition)`.
6. Implement `OwnsCondition(UnlockCondition condition)` — return `true` only for the exact condition type your strategy produces.
7. Register the strategy from an editor-only `InitializeOnLoad` class.

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

        // Declare ownership: return true only for the condition type this strategy creates.
        // Used by the graph to replace stale conditions when the pass mode changes, and by
        // the Condition Cleanup dialog to surface type mismatches.
        public bool OwnsCondition(UnlockCondition condition) => condition is MyCondition;

        public UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder) {
            // Reuse an existing asset if one already exists for this source.
            var existing = FindExisting(source);
            if (existing != null) return existing;

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

        private static MyCondition FindExisting(BaseDefinition source) {
            foreach (string guid in AssetDatabase.FindAssets("t:MyCondition")) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var cond = AssetDatabase.LoadAssetAtPath<MyCondition>(path);
                if (cond == null) continue;
                // compare the serialized source field to source
                var so = new SerializedObject(cond);
                if (so.FindProperty("_mySourceField").objectReferenceValue == source) return cond;
            }
            return null;
        }
    }
}
```

## Example Patterns

### QuestDefinition: Quest Is Completed
- Definition type: `QuestDefinition`.
- Pass label: `Quest Completed`.
- Condition type: `QuestCompletedCondition` (sample-provided).
- Source field: `_quest`.
- `OwnsCondition`: `condition is QuestCompletedCondition`.
- Runtime semantics: `Evaluate()` calls `UnlockManager.IsUnlockedByKey(quest.UnlockKey)` — checks only explicit unlock state, never `IsUnlockedByDefault`. A quest is "completed" only when game code explicitly calls `Unlock()`.

### CharacterDefinition: Is Unlocked
- Definition type: `CharacterDefinition`.
- Pass label: `Unlocked` (or a custom label).
- Condition type: `DefinitionUnlockedCondition` (built-in) or a custom type.
- `OwnsCondition`: `condition is DefinitionUnlockedCondition`.
- Source field: `_target`.

### FoodIngredientDefinition: Ingredient Is Tasted (example)
- Definition type: `FoodIngredientDefinition`.
- Pass label: `Ingredient Tasted`.
- Condition type: `IngredientTastedCondition`.
- `OwnsCondition`: `condition is IngredientTastedCondition`.
- Source field: `_ingredient`.

## Coexisting Strategies For The Same Definition Type

You can register multiple strategies for one definition type.
- The graph node shows a pass dropdown so users can choose behavior per node.
- The selected strategy decides which condition asset type is created for new edges.
- When the pass mode changes and an edge is redrawn, the graph removes any condition that the
  new strategy does not own (via `OwnsCondition`) before creating the correct type.
- Keep pass labels distinct to reduce ambiguity.

## Condition Cleanup Dialog — Mismatch Detection

The Condition Cleanup dialog (`ProxyCore > Unlock Graph > Condition Cleanup`) reports three categories:

| Column | Meaning |
|---|---|
| Used | Condition is referenced by a definition's prerequisites and the owning strategy matches |
| Mismatched | Condition is referenced but its type is not owned by the strategy expected for its source |
| Unused | Condition is not referenced by any prerequisite list |

Mismatched conditions indicate a past strategy change whose stale condition was not cleaned up.
Select them and click **Delete Mismatched** to remove them, then redraw the edge to create the correct type.

## Assembly Structure For Sample Strategies

When implementing strategies outside the ProxyCore package (in a project's own scripts):

- Place the runtime `UnlockCondition` subclass in a **non-editor** assembly or folder.
- Place the strategy and registrar in an **editor-only** assembly or `Editor/` folder.
- Declare the editor assembly's references explicitly: `ProxyCore`, `ProxyCore.Editor`, and the runtime assembly that defines the condition type.

This separation is required because `UnlockCondition` assets are ScriptableObjects that must be
available at runtime for `UnlockAutoTrigger` evaluation, while strategy classes are editor-only.

## Safety Recommendations

- Always implement `OwnsCondition` to return `true` only for the exact condition type your strategy creates. Never return `true` for base types or types owned by another strategy.
- Always use `SerializedObject` for private serialized fields.
- Reuse existing condition assets (scan with `AssetDatabase.FindAssets`) to avoid asset sprawl.
- Ensure `GetDirectEdgeSource` never throws; return `null` for unhandled conditions.
- Ensure `OwnsCondition` never throws; return `false` on any exception.
- Keep `CanHandle` narrow and explicit.
- Use editor-only registration classes in editor assemblies/folders.

## Verification Checklist

1. Open Unlock Graph and select a node of the handled definition type.
2. Confirm pass label or dropdown appears as expected.
3. Draw a direct edge and confirm the expected condition asset type is created in the conditions folder.
4. Reopen/rebuild graph and confirm the edge reconstructs as direct (not manual).
5. Change the pass dropdown to a different strategy, redraw the edge, and confirm the old condition asset was deleted and the new type was created.
6. Open Condition Cleanup dialog and confirm conditions appear in the correct column (Used / Mismatched / Unused).
7. Delete one of multiple edges sharing a condition source and ensure remaining edges stay valid.
8. Confirm the selected pass mode persists after graph rebuild/reopen.
