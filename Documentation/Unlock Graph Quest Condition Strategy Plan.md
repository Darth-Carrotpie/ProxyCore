# Unlock Graph — Quest Condition Strategy Plan

## Problem Statement

The Unlock Dependency Graph currently hardcodes all direct definition-to-definition edges to use `DefinitionUnlockedCondition`. This means a link from a `QuestDefinition` node to another definition fires when the quest **becomes available** (unlocked), not when it is **completed**.

The goal is to make direct edge semantics extensible so that different definition types can supply their own condition type and pass-state label — without modifying the core ProxyCore package per-project.

---

## Architecture Decision

### Strategy Pattern

Introduce a `IDefinitionEdgeStrategy` interface in ProxyCore that governs:
- Which `UnlockCondition` SO to create/reuse when a direct edge is drawn
- How to read the source definition back out of a given condition SO (for graph reconstruction)
- What label to show on the edge ("Quest Completed", "Unlocked", "Purchased", etc.)

A central `DefinitionEdgeStrategyRegistry` holds all registered strategies. Projects register their own strategies via `[InitializeOnLoad]` without touching ProxyCore source.

`UnlockGraphView` and `UnlockGraphBuilder` delegate to the registry instead of hardcoding `DefinitionUnlockedCondition`.

### Node-Level Pass Switching

Pass behavior is selected per definition node in the graph editor.
- If one strategy applies to that definition type, the node displays a fixed pass label.
- If multiple strategies apply, the node displays a pass dropdown.
- The selected strategy is stored in `UnlockGraphLayoutData` by definition asset GUID.
- New direct edges created from that node use the selected strategy.

### Migration Policy

**No automatic migration.** Existing `DefinitionUnlockedCondition` assets on existing edges remain unchanged and continue to function correctly (`DefinitionUnlockedCondition` becomes the fallback of the default strategy). Only new edges drawn after the change use the new strategy routing.

---

## Files To Create / Modify

### ProxyCore Source Repo (`Assets/ProxyCore/`)

#### 1. NEW — `Editor/Graph/UnlockDependencyGraph/IDefinitionEdgeStrategy.cs`

```
namespace ProxyCore.Editor.Graph

interface IDefinitionEdgeStrategy
    bool CanHandle(Type sourceType)
    string PassStateLabel { get; }
    UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder)
    BaseDefinition GetDirectEdgeSource(UnlockCondition condition)  // null if not handled
```

#### 2. NEW — `Editor/Graph/UnlockDependencyGraph/DefinitionEdgeStrategyRegistry.cs`

```
[InitializeOnLoad] static class DefinitionEdgeStrategyRegistry
    static List<IDefinitionEdgeStrategy> _strategies  (default: [DefaultDefinitionEdgeStrategy])
    static void Register(IDefinitionEdgeStrategy strategy)
    static IDefinitionEdgeStrategy GetStrategy(Type sourceType)
        → iterates strategies in reverse-registration order
        → first CanHandle(sourceType) wins
        → falls back to DefaultDefinitionEdgeStrategy
```

#### 3. NEW — `Editor/Graph/UnlockDependencyGraph/DefaultDefinitionEdgeStrategy.cs`

Extracts the existing `DefinitionUnlockedCondition` create/find logic from `UnlockGraphView`:
- `CanHandle`: always returns `true` (catch-all fallback)
- `PassStateLabel`: `"Unlocked"`
- `GetOrCreateCondition`: creates/reuses a `DefinitionUnlockedCondition` SO keyed by `_target` field
- `GetDirectEdgeSource`: returns `(condition as DefinitionUnlockedCondition)?._target`

#### 4. MODIFY — `Editor/Graph/UnlockDependencyGraph/UnlockGraphView.cs`

| Method | Current | Change |
|---|---|---|
| `CreateDependencyEdge` | Hardcodes `DefinitionUnlockedCondition` creation | Replace with `DefinitionEdgeStrategyRegistry.GetStrategy(source.GetType()).GetOrCreateCondition(source, ConditionsPath)` |
| `RemoveDependencyEdge` | Casts to `DefinitionUnlockedCondition` to identify direct edge | Replace with strategy probe loop: `strategy.GetDirectEdgeSource(condObj) == source` |
| `FindConditionSO` | Checks `is DefinitionUnlockedCondition` | Replace with strategy probe |
| `ResolveEdgeObject` | Checks `is DefinitionUnlockedCondition` | Replace with strategy probe |
| `EnsureFolderExists` | (already `internal static`) | No change required |

#### 5. MODIFY — `Editor/Graph/UnlockDependencyGraph/UnlockGraphBuilder.cs`

| Location | Current | Change |
|---|---|---|
| `Build()` condition loop (line ~89) | `if (condition is DefinitionUnlockedCondition duc)` → classifies as direct edge | Replace with strategy probe: `foreach strategy → GetDirectEdgeSource(condition) != null` |
| `GetDefinitionTarget(DefinitionUnlockedCondition)` (line ~250) | Private helper reading `_target` | **Delete** — logic moved into `DefaultDefinitionEdgeStrategy` |

#### 6. MODIFY — `Editor/Graph/UnlockDependencyGraph/Nodes/DefinitionNode.cs`

Add node-level pass behavior UI:
- fixed pass label when one strategy is available
- dropdown selector when multiple strategies are available
- emit change events so selected strategy can be persisted in `UnlockGraphLayoutData`

#### 7. NEW — `Documentation/UnlockEdgeStrategyGuidelines.md`

Create a practical guide for adding new strategy implementations with examples:
- `QuestDefinition` → "Quest Completed"
- `CharacterDefinition` → "Unlocked"
- `FoodIngredientDefinition` (example) → "Ingredient Tasted"

---

### Sample Extension in this repo (`Samples/Unlockables/Editor/`)

#### 8. NEW — `Assets/ProxyCore/Samples/Unlockables/Editor/QuestDefinitionEdgeStrategy.cs`

```
[InitializeOnLoad]
static class QuestDefinitionEdgeStrategyRegistrar
    static ctor → DefinitionEdgeStrategyRegistry.Register(new QuestDefinitionEdgeStrategy())

class QuestDefinitionEdgeStrategy : IDefinitionEdgeStrategy
    CanHandle(Type t) → t == typeof(QuestDefinition) || t.IsSubclassOf(typeof(QuestDefinition))
    PassStateLabel → "Quest Completed"
    GetOrCreateCondition(source, folder)
        → search for existing QuestCompletedCondition SO where _quest == source
        → if none found: create new SO at folder/"{source.name}_QuestCompleted.asset"
        → set _quest field via SerializedObject
        → return condition
    GetDirectEdgeSource(condition)
        → if condition is QuestCompletedCondition: return its _quest field (via SerializedObject)
        → else return null
```

**Key field names (from existing code):**
- `QuestCompletedCondition._quest` — `[SerializeField] private QuestDefinition _quest`
- `DefinitionUnlockedCondition._target` — `[SerializeField] private BaseDefinition _target`

---

## Data Flow After Implementation

```
User draws edge: QuestDefinition → CharacterDefinition
    ↓
UnlockGraphView.CreateDependencyEdge(source: QuestDefinition, target: CharacterDefinition)
    ↓
DefinitionEdgeStrategyRegistry.GetStrategy(typeof(QuestDefinition))
    → returns QuestDefinitionEdgeStrategy
    ↓
QuestDefinitionEdgeStrategy.GetOrCreateCondition(source, conditionsFolder)
    → creates QuestCompletedCondition SO with _quest = source
    ↓
Condition SO added to CharacterDefinition._prerequisites
    ↓
At runtime: UnlockManager.ArePrerequisitesMet(CharacterDefinition)
    → QuestCompletedCondition.Evaluate()
    → QuestManager.IsQuestCompleted(_quest)  ✓
```

```
Graph editor opens / rebuilds
    ↓
UnlockGraphBuilder.Build()
    → encounters QuestCompletedCondition in CharacterDefinition._prerequisites
    ↓
Strategy probe: QuestDefinitionEdgeStrategy.GetDirectEdgeSource(condition)
    → returns QuestDefinition SO  (not null)
    ↓
Classified as direct edge → draws arrow from QuestDefinition node to CharacterDefinition node
    (same visual as existing DefinitionUnlockedCondition edges)
```

---

## No-Touch Items

| Item | Reason |
|---|---|
| `QuestCompletedCondition.cs` | Not included in this baseline scope |
| `QuestManager.cs` | Not included in this baseline scope |
| `UnlockManager.asset` | QuestRegistry already registered |
| Existing condition `.asset` files | Not migrated — fallback strategy handles them transparently |
| Runtime unlock evaluation | Zero changes needed |

---

## Dependency Order for Implementation

```
Step 1: IDefinitionEdgeStrategy.cs          (no deps)
Step 2: DefaultDefinitionEdgeStrategy.cs    (depends on Step 1)
Step 3: DefinitionEdgeStrategyRegistry.cs   (depends on Steps 1-2)
Step 4: Modify UnlockGraphView.cs           (depends on Steps 1-3)
Step 5: Modify UnlockGraphBuilder.cs        (depends on Steps 1-3)
Step 6: DefinitionNode pass selector UI     (depends on Steps 1-3)
Step 7: Strategy guidelines documentation   (independent)
Step 8: QuestDefinitionEdgeStrategy sample  (depends on Steps 1-3)
```

Steps 1-3 can be written simultaneously. Steps 4-6 can be written after Steps 1-3. Step 8 requires Steps 1-3 compiled first.
