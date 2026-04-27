# ProxyCore — Unlockables System

> **Audience**: LLM coding agents and developers working on projects that use ProxyCore.

## Overview

The Unlockables system tracks whether gameplay items are **locked** or **unlocked** at runtime.
It supports two usage modes, a prerequisite / auto-unlock chain feature, and a named flag system for game-state conditions.

| Concept | Class / Interface | Purpose |
|---|---|---|
| Unlockable item | `IUnlockable` | Any item that has a lock state |
| Global state manager | `UnlockManager` | Singleton SO; source of truth for all lock state |
| Standalone item | `StandaloneUnlockable` | Plain C# `IUnlockable` (no ScriptableObject required) |
| Prerequisite interface | `IHasPrerequisites` | Opt-in; exposes a condition list on a definition |
| Condition base | `UnlockCondition` | Abstract SO; extend to create new condition types |
| Built-in condition | `DefinitionUnlockedCondition` | Passes when another `IUnlockable` definition is unlocked (checks `IsUnlocked`, including `IsUnlockedByDefault`) |
| Built-in condition | `FlagCondition` | Passes when a named flag is set in a `GameFlagCollection` |
| Auto-unlock trigger | `UnlockAutoTrigger` | SO registered on `UnlockManager`; auto-unlocks its target when prerequisites pass |
| Flag collection | `GameFlagCollection` | Named boolean flags; pushing a flag re-evaluates all triggers |
| Locked UI behavior | `UnlockBehavior` | `HideWhenLocked` or `ShowDisabledWhenLocked` |
| Condition logic | `ConditionMode` | `All` (AND) or `Any` (OR) per definition |

---

## Usage Mode 1 — Definition-Registry integration

Add `IUnlockable` (and optionally `IHasPrerequisites`) directly to a `BaseDefinition` subclass.

```csharp
public class CharacterDefinition : BaseDefinition, IUnlockable, IHasPrerequisites
{
    [SerializeField] private UnlockBehavior _lockedBehavior = UnlockBehavior.HideWhenLocked;
    [SerializeField] private bool _savesAcrossSessions = true;
    [SerializeField] private bool _isUnlockedByDefault = false;
    [SerializeField] private List<UnlockCondition> _prerequisites = new();
    [SerializeField] private ConditionMode _prerequisiteMode = ConditionMode.All;
    [SerializeField] private bool _autoUnlock = true;

    string IUnlockable.UnlockKey         => $"{GetType().Name}:{ID}";
    bool IUnlockable.SavesAcrossSessions => _savesAcrossSessions;
    UnlockBehavior IUnlockable.LockedBehavior => _lockedBehavior;
    bool IUnlockable.IsUnlockedByDefault => _isUnlockedByDefault;

    IReadOnlyList<UnlockCondition> IHasPrerequisites.Prerequisites  => _prerequisites.AsReadOnly();
    ConditionMode IHasPrerequisites.PrerequisiteMode                => _prerequisiteMode;
    bool IHasPrerequisites.AutoUnlock                               => _autoUnlock;
}
```

Key: `"{TypeName}:{ID}"` — namespaced so different definition types never collide.

---

## Usage Mode 2 — Standalone (no registry)

```csharp
var item = new StandaloneUnlockable(
    key: "Ability:DoubleJump",
    savesAcrossSessions: false,
    lockedBehavior: UnlockBehavior.HideWhenLocked,
    isUnlockedByDefault: false);

UnlockManager.Instance.Unlock(item);
bool available = UnlockManager.Instance.IsUnlocked(item);
```

---

## UnlockManager API

```csharp
// Mutations
UnlockManager.Unlock(item);         // unlock; saves if SavesAcrossSessions = true
UnlockManager.Lock(item);           // explicit lock; overrides IsUnlockedByDefault
// Queries
UnlockManager.IsUnlocked(item);     // true if unlocked OR IsUnlockedByDefault (and not overridden)
UnlockManager.IsLocked(item);
UnlockManager.IsUnlockedByKey(key); // true only if explicitly unlocked (saved or session); never true from IsUnlockedByDefault alone
// Bulk
UnlockManager.UnlockAll(items);     // single Save() pass for all persistent keys
UnlockManager.LockAll(items);
// Reset
UnlockManager.ResetSavedUnlocks();  // clears disk state (unlocks.json)
UnlockManager.ResetSessionUnlocks();// clears in-memory (session + overrides)
// Auto-unlock
UnlockManager.EvaluateAutoTriggers();// manually re-evaluate all registered triggers
```

### Persistence

- `SavesAcrossSessions = true` → key written to `Application.persistentDataPath/unlocks.json`.
- `SavesAcrossSessions = false` → key is session-only; cleared on every scene reload.
- `IsUnlockedByDefault = true` → item is treated as unlocked without any explicit `Unlock()` call. An explicit `Lock()` overrides this.

### IsUnlocked vs IsUnlockedByKey

`IsUnlocked` returns `true` if the item was explicitly unlocked **or** if `IsUnlockedByDefault` is set (and not overridden).
`IsUnlockedByKey` returns `true` only if the item was explicitly unlocked — it never reflects `IsUnlockedByDefault`.

Use `IsUnlockedByKey` in custom `UnlockCondition.Evaluate()` implementations when the condition
should represent a deliberate game action (e.g. quest completed), not a default-available state.

### Events

Assign `EventMessage` assets to `_onUnlocked` / `_onLocked` in the `UnlockManager` inspector.
Both fire with a `StringPayload(unlockKey)` through the `EventCoordinator`.

---

## Prerequisites & Auto-unlock Chains

### Concept

An `UnlockAutoTrigger` asset wraps one definition and watches its prerequisites.
Every time any item is unlocked, `UnlockManager.EvaluateAutoTriggers()` runs automatically.
If the prerequisites pass and the target is still locked, it is unlocked — which triggers
another evaluation pass, propagating chains of arbitrary depth.

### Setup (in the Unity Editor)

1. **Create condition assets** — e.g. `Assets > Create > ProxyCore > Unlockables > Conditions > Definition Is Unlocked`, assign the dependency definition.
2. **Assign conditions** — open the definition asset (e.g. `CharacterDefinition B`) and add the condition to the `Prerequisites` list. Set `PrerequisiteMode` to `All` or `Any`.
3. **Create a trigger** — `Assets > Create > ProxyCore > Unlockables > Unlock Auto Trigger`, set `_target` to definition B.
4. **Register the trigger** — open the `UnlockManager` asset and add the trigger to `Auto-unlock Triggers`.

Chains: if B's prerequisite is A, and C's prerequisite is B, create one trigger per definition and register all three on `UnlockManager`. Unlocking A will transitively unlock B then C in one evaluation cycle.

### ConditionMode

| Mode | Behaviour |
|---|---|
| `All` | Every condition in the list must return `true` (AND) |
| `Any` | At least one condition must return `true` (OR) |

---

## Flag Conditions

`GameFlagCollection` is a named set of boolean flags. Create one per logical domain.

### Setup

1. `Assets > Create > ProxyCore > Flags > Game Flag Collection` — declare flag names in the inspector.
2. `Assets > Create > ProxyCore > Unlockables > Conditions > Flag Is Set` — assign the collection; pick a flag from the dropdown.
3. Add the `FlagCondition` to a definition's `Prerequisites` list as described above.

### Runtime

```csharp
// Setting a flag — fires _onFlagChanged event and (if _autoEvaluateOnSet = true)
// re-evaluates all UnlockAutoTriggers automatically.
myFlagCollection.SetFlag("boss_defeated", true);

bool done = myFlagCollection.GetFlag("boss_defeated");
```

`_autoEvaluateOnSet` (default `true`) makes flag changes push-driven — no manual polling needed.
Disable it on collections that batch many flag changes at once and call `EvaluateAutoTriggers()` manually.

---

## Creating Custom Condition Types

Inherit from `UnlockCondition` and add `[CreateAssetMenu]`:

```csharp
[CreateAssetMenu(menuName = "ProxyCore/Unlockables/Conditions/Player Level Reached")]
public class PlayerLevelCondition : UnlockCondition
{
    [SerializeField] private int _requiredLevel;

    public override bool Evaluate() =>
        PlayerProgressManager.Instance.Level >= _requiredLevel;
}
```

When a custom condition is used as a direct edge in the Unlock Graph, pair it with a custom
`IDefinitionEdgeStrategy` that declares ownership via `OwnsCondition`. This allows the graph to
replace stale wrong-type conditions automatically when the pass mode changes. See
`UnlockEdgeStrategyGuidelines.md` for the full strategy implementation guide.

---

## Editor Tooling

| Tool | Access | Purpose |
|---|---|---|
| `UnlockDebugWindow` | Scene View toolbar lock icon | Live view of saved / session unlock keys during Play Mode |
| `ProxyCore > Unlockables > Clear Save Data` | Menu bar | Deletes `unlocks.json`; works in Edit and Play Mode |
| `ProxyCore > Unlockables > Reset Session Unlocks` | Menu bar | Clears in-memory state; Play Mode only |
| Condition Cleanup dialog | `ProxyCore > Unlock Graph > Condition Cleanup` | Lists Used, Mismatched, and Unused condition assets with bulk delete |

---

## File Map

```
Runtime/Unlockables/
  IUnlockable.cs
  UnlockBehavior.cs
  UnlockSaveData.cs
  UnlockManager.cs
  StandaloneUnlockable.cs
  ConditionMode.cs
  IHasPrerequisites.cs
  UnlockAutoTrigger.cs
  Conditions/
    UnlockCondition.cs               ← abstract base for custom conditions
    DefinitionUnlockedCondition.cs   ← passes when target IsUnlocked (includes IsUnlockedByDefault)
    FlagCondition.cs

Runtime/Flags/
  GameFlagCollection.cs

Editor/Class Editors/
  FlagConditionEditor.cs             ← flag-name dropdown
Editor/Global Actions/
  UnlockablesActions.cs              ← menu items
Editor/Editor Windows/
  UnlockDebugWindow.cs               ← live debug window
  ProxyCoreToolbarShortcuts.cs       ← toolbar button registration
Editor/Graph/UnlockDependencyGraph/
  IDefinitionEdgeStrategy.cs         ← strategy interface (CanHandle, GetOrCreateCondition, GetDirectEdgeSource, OwnsCondition)
  DefaultDefinitionEdgeStrategy.cs   ← fallback; creates DefinitionUnlockedCondition
  DefinitionEdgeStrategyRegistry.cs  ← register/lookup; TryGetOwningStrategy for mismatch detection
  ConditionCleanupDialog.cs          ← Used / Mismatched / Unused condition audit dialog

Samples/Unlockables/
  Definitions/
    CharacterDefinition.cs           ← IUnlockable + IHasPrerequisites example
    QuestDefinition.cs               ← IUnlockable + IHasPrerequisites example
  Conditions/
    QuestCompletedCondition.cs       ← sample UnlockCondition; evaluates via IsUnlockedByKey (not IsUnlocked)
  Registries/
    CharacterRegistry.cs
    QuestRegistry.cs
  CharacterUnlockController.cs       ← MonoBehaviour helper (queries registry singleton)
  QuestUnlockController.cs
  UnlockablesSampleDriver.cs         ← [ContextMenu] interactive test driver
  Editor/
    CharacterDefinitionEdgeStrategy.cs ← sample strategy for CharacterDefinition
    QuestDefinitionEdgeStrategy.cs     ← sample strategy for QuestDefinition; creates QuestCompletedCondition
    CharacterRegistryEditor.cs
    QuestRegistryEditor.cs
```
