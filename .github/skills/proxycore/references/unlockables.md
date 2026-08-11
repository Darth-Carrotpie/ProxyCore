# ProxyCore — Unlockables & Progression

Gate any content (abilities, levels, characters, cosmetics) behind a lock state owned
by a single `UnlockManager`. Anything implementing `IUnlockable` participates. State is
optionally persisted to disk or session-only, and items can auto-unlock when
prerequisite conditions pass.

## Who owns what: AI vs Designer

The Unlock subsystem has two halves — keep them separate.

- **Implementation — AI-driven (this is your job).** Writing the code: implementing
  `IUnlockable` / `IHasPrerequisites` on definitions, authoring custom `UnlockCondition`
  types, calling `Unlock`/`Lock`/`IsUnlocked` at the right gameplay moments, wiring
  `EventMessage` reactions, `GameFlagCollection` flags, and (for custom conditions)
  the editor `IDefinitionEdgeStrategy`. Build all of this plumbing so the content is
  *capable* of being unlocked and the runtime behaves correctly.
- **Connecting the chains — Designer-driven (not you).** Deciding *what unlocks what*
  and wiring those prerequisite/dependency edges together lives in the visual **Unlock
  Dependency Graph** (`ProxyCore ▸ Unlock Dependency Graph`). That progression design
  is a human/designer task. Do **not** try to fabricate graph layouts, invent the
  progression tree, or hand-author the graph's edge/condition assets from code unless
  explicitly asked — expose the capability and let the designer connect it in the graph.

Rule of thumb: **you make things unlockable and make unlocking work; the designer
decides the unlock order in the graph.** When a task is "gate X behind Y", implement
both `X` (IUnlockable) and `Y` (a condition) and mention that the actual X→Y edge is
connected in the Unlock Graph.

## Contents
- [Making something unlockable](#making-something-unlockable)
- [UnlockManager API](#unlockmanager-api)
- [IsUnlocked vs IsUnlockedByKey](#isunlocked-vs-isunlockedbykey)
- [Prerequisites & auto-unlock chains](#prerequisites--auto-unlock-chains)
- [Conditions](#conditions)
- [Flags (GameFlagCollection)](#flags-gameflagcollection)
- [Reacting to unlock/lock](#reacting-to-unlocklock)
- [Editor: custom conditions in the Unlock Graph](#editor-custom-conditions-in-the-unlock-graph)
- [Common mistakes](#common-mistakes)

## Making something unlockable

Two modes.

**Mode 1 — on a definition (registry-backed).** Implement `IUnlockable` (and
optionally `IHasPrerequisites`) on a `BaseDefinition` subclass. Explicit interface
implementation keeps the API surface clean and lets serialized fields drive it:

```csharp
using System.Collections.Generic;
using UnityEngine;
using ProxyCore;

public class CharacterDefinition : BaseDefinition, IUnlockable, IHasPrerequisites
{
    [SerializeField] private UnlockBehavior _lockedBehavior = UnlockBehavior.HideWhenLocked;
    [SerializeField] private bool _savesAcrossSessions = true;
    [SerializeField] private bool _isUnlockedByDefault = false;
    [SerializeField] private List<UnlockCondition> _prerequisites = new();
    [SerializeField] private ConditionMode _prerequisiteMode = ConditionMode.All;
    [SerializeField] private bool _autoUnlock = true;

    string IUnlockable.UnlockKey              => $"{GetType().Name}:{ID}"; // namespaced — see below
    bool   IUnlockable.SavesAcrossSessions    => _savesAcrossSessions;
    UnlockBehavior IUnlockable.LockedBehavior => _lockedBehavior;
    bool   IUnlockable.IsUnlockedByDefault    => _isUnlockedByDefault;

    IReadOnlyList<UnlockCondition> IHasPrerequisites.Prerequisites => _prerequisites.AsReadOnly();
    ConditionMode IHasPrerequisites.PrerequisiteMode               => _prerequisiteMode;
    bool          IHasPrerequisites.AutoUnlock                     => _autoUnlock;
}
```

`UnlockKey` **must be globally unique**. Use `"{TypeName}:{ID}"` so different
definition types never collide in the shared save file.

**Mode 2 — standalone (no ScriptableObject).** For runtime concepts that don't live
in a registry:

```csharp
var doubleJump = new StandaloneUnlockable(
    key: "Ability:DoubleJump",
    savesAcrossSessions: false,
    lockedBehavior: UnlockBehavior.HideWhenLocked,
    isUnlockedByDefault: false);
```

`UnlockBehavior` is `HideWhenLocked` or `ShowDisabledWhenLocked` — a UI hint the
manager stores but does not enforce; your UI reads it.

## UnlockManager API

`UnlockManager` is a `SingletonSO` — **its asset must be in a `Resources/` folder**
(`Create ▸ Managers ▸ Unlock Manager`). The methods are **static**; each takes an
`IUnlockable` (or a raw key).

```csharp
UnlockManager.Unlock(item);          // unlock (persists if SavesAcrossSessions)
UnlockManager.Lock(item);            // explicit lock — overrides IsUnlockedByDefault
bool ok = UnlockManager.IsUnlocked(item);
bool no = UnlockManager.IsLocked(item);

// Key overloads (you supply whether it saves):
UnlockManager.UnlockByKey("Ability:DoubleJump", savesAcrossSessions: false);
bool set = UnlockManager.IsUnlockedByKey("Ability:DoubleJump");

// Bulk (single disk write for the persistent ones):
UnlockManager.UnlockAll(items);
UnlockManager.LockAll(items);

// Reset:
UnlockManager.ResetSavedUnlocks();   // clears disk state (unlocks.json)
UnlockManager.ResetSessionUnlocks(); // clears in-memory session + lock overrides
```

The static form (`UnlockManager.Unlock(item)`) is preferred and equivalent to
`UnlockManager.Instance.Unlock(item)`.

**Persistence model:**
- `SavesAcrossSessions = true` → key written to
  `Application.persistentDataPath/unlocks.json`, survives app restarts.
- `SavesAcrossSessions = false` → session-only; cleared on scene reload.
- `IsUnlockedByDefault = true` → treated as unlocked with no `Unlock()` call; an
  explicit `Lock()` still overrides it.

## IsUnlocked vs IsUnlockedByKey

- **`IsUnlocked(item)`** → true if explicitly unlocked **or** `IsUnlockedByDefault`
  (and not explicitly locked). Use this for "should the player have access?".
- **`IsUnlockedByKey(key)`** → true **only** if explicitly unlocked; never reflects
  `IsUnlockedByDefault`. Use this inside a custom `UnlockCondition.Evaluate()` when the
  condition means "a deliberate action happened" (e.g. quest completed), so
  default-available items don't falsely satisfy it.

## Prerequisites & auto-unlock chains

Any definition that implements `IHasPrerequisites` with `AutoUnlock = true` unlocks
automatically once its `Prerequisites` pass. Evaluation runs after **every** unlock and
on startup, so chains (A → B → C) resolve in one cascade.

Wiring:
1. On the definition, fill `Prerequisites` with `UnlockCondition` assets and set
   `PrerequisiteMode` (`All` = AND, `Any` = OR).
2. Register the **registries** that contain these definitions on the `UnlockManager`
   asset (its *Auto-unlock Registries* list). Every `BaseRegistry<T>` is an
   `IUnlockableCatalog`, so the manager scans them for `IUnlockable + IHasPrerequisites`
   items. Populate the list via **ProxyCore ▸ Unlockable Actions ▸ Refresh Unlock
   Registries** (or the manager inspector's Refresh button). If the list is empty or
   stale, the manager logs a one-time warning.
3. Force a pass manually anytime with `UnlockManager.EvaluateAutoTriggers()`.

## Conditions

A condition is a ScriptableObject deriving from `UnlockCondition` with one method:

```csharp
using UnityEngine;
using ProxyCore;

[CreateAssetMenu(menuName = "Unlockables/Player Level Reached")]
public class PlayerLevelCondition : UnlockCondition
{
    [SerializeField] private int _requiredLevel;
    public override bool Evaluate() => PlayerProgress.Instance.Level >= _requiredLevel;
}
```

Create one asset per condition and add it to a definition's `Prerequisites`.
`Evaluate()` runs during auto-unlock passes; exceptions are caught and logged (an
`All`-mode condition that throws fails safe).

Built-ins you can use without writing code:
- **`FlagCondition`** (`Create ▸ Unlockables ▸ Flag Is Set (Condition)`) — passes when
  a named flag in a `GameFlagCollection` is set.
- **`DefinitionUnlockedCondition`** (`Create ▸ Unlockables ▸ Definition Is Unlocked
  (Condition)`) — passes when another `IUnlockable` definition is unlocked (respects
  `IsUnlockedByDefault`).

## Flags (GameFlagCollection)

A named set of boolean flags for game-state conditions
(`Create ▸ Flags ▸ Game Flag Collection`). Create one per domain (global, achievements,
tutorial). **Declare every flag name in the inspector before using it** — undeclared
names are rejected with a warning.

```csharp
myFlags.SetFlag("boss_defeated", true);
bool done = myFlags.GetFlag("boss_defeated");
```

With `_autoEvaluateOnSet` on (default), `SetFlag` re-evaluates all auto-unlock
prerequisites immediately, so a `FlagCondition` gating an item unlocks it the moment
the flag flips — no polling. Enable `_savesAcrossSessions` to persist flags to disk
(one file per collection, keyed by asset name).

## Reacting to unlock/lock

Assign `EventMessage` assets to the `UnlockManager`'s `_onUnlocked` / `_onLocked`
fields (and `GameFlagCollection._onFlagChanged`). They fire with a
`StringPayload(key)` through the `EventCoordinator`, so UI can listen via
`ListenEvent.*` (see `references/events.md`) and refresh without polling.

## Editor: custom conditions in the Unlock Graph

The Unlock Dependency Graph (**ProxyCore ▸ Unlock Dependency Graph**) can draw a direct
edge from a source definition to a dependent one. For a **custom** condition type to
participate as a direct edge, pair it with an `IDefinitionEdgeStrategy` registered at
editor load. This lets the graph create/replace the right condition asset when you draw
or rewire an edge:

```csharp
using System;
using UnityEditor;
using ProxyCore;
using ProxyCore.Editor.Graph;

[InitializeOnLoad]
internal static class QuestEdgeStrategyRegistrar
{
    static QuestEdgeStrategyRegistrar() =>
        DefinitionEdgeStrategyRegistry.Register(new QuestDefinitionEdgeStrategy());
}

internal sealed class QuestDefinitionEdgeStrategy : IDefinitionEdgeStrategy
{
    public bool   CanHandle(Type sourceType) => typeof(QuestDefinition).IsAssignableFrom(sourceType);
    public string PassStateLabel             => "Quest Completed";
    public bool   OwnsCondition(UnlockCondition c) => c is QuestCompletedCondition;

    public UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder) { /* find-or-create the asset */ return null; }
    public BaseDefinition  GetDirectEdgeSource(UnlockCondition c) => (c as QuestCompletedCondition)?.Quest;
}
```

`OwnsCondition` is what lets the graph detect and replace a stale wrong-type condition
when the pass mode changes. Only needed for custom edge conditions; built-ins already
have strategies.

Menu actions: **ProxyCore ▸ Unlock Debug Window** (live saved/session keys in Play
Mode), **ProxyCore ▸ Unlockable Actions ▸ Clear Save Data / Reset Session Unlocks**.

## Common mistakes

- `UnlockManager` / registry assets outside a `Resources/` folder → `Instance` null in
  a build.
- Non-unique or non-namespaced `UnlockKey` → cross-type collisions in `unlocks.json`.
  Use `"{TypeName}:{ID}"`.
- Expecting auto-unlock to fire without registering the containing registries on the
  `UnlockManager` (run *Refresh Unlock Registries*).
- Using `IsUnlocked` inside a condition that should mean "deliberately unlocked" — use
  `IsUnlockedByKey` there.
- Calling `SetFlag` with a name not declared in the collection — it's rejected with a
  warning and does nothing.
