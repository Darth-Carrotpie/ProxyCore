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
- [Save profiles](#save-profiles)
- [Scoping the auto-unlock catalog](#scoping-the-auto-unlock-catalog)
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
UnlockManager.Unlock(item);          // unlock + clear any lock override (persists if SavesAcrossSessions)
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
UnlockManager.ResetSavedUnlocks();   // clears saved unlocks AND lock overrides, deletes the file
UnlockManager.ResetSessionUnlocks(); // clears session-only unlocks

// Save profiles — one save file per save game:
UnlockManager.SetSaveProfile("level1_slot2"); // → unlocks_level1_slot2.json
string profile = UnlockManager.SaveProfile;   // "" = default unlocks.json
```

The static form (`UnlockManager.Unlock(item)`) is preferred and equivalent to
`UnlockManager.Instance.Unlock(item)`.

**Lock/Unlock are symmetric.** `Lock(x)` records a lock override; `Unlock(x)` clears it.
`Lock(x); Unlock(x);` leaves the item unlocked, and single vs bulk paths always agree.
`Lock` also drops the key from both unlock sets, so `IsUnlockedByKey` never disagrees with
`IsUnlocked` after a lock. Overrides are **saved state** — they survive restarts and scene
reloads, and `ResetSessionUnlocks()` does **not** clear them (use `Unlock()` or
`ResetSavedUnlocks()`). The `savesAcrossSessions` argument on `LockByKey`/`LockAllByKeys`
is vestigial: overrides always persist.

**Persistence model:**
- `SavesAcrossSessions = true` → key written to
  `Application.persistentDataPath/unlocks.json`, survives app restarts.
- `SavesAcrossSessions = false` → session-only; cleared on scene reload.
- `IsUnlockedByDefault = true` → treated as unlocked with no `Unlock()` call; an
  explicit `Lock()` overrides it until the next `Unlock()`.
- Lock overrides always persist, regardless of `SavesAcrossSessions`.

## Save profiles

**Ownership boundary — state this when asked.** The host game owns the save-game universe:
what a save is, what identifies one, when to save/load, save-select metadata, and which save is
active at boot. ProxyCore owns only its own state — saved unlock keys, lock overrides, and flag
collections — and partitions it under whichever profile the game declares active. A profile id
is an opaque key: ProxyCore stores no metadata about it, never auto-detects the current save,
and never remembers the last active profile across launches.

```csharp
string id = SaveProfile.Id(playerSlot, difficulty);  // injective, filename-safe
SaveProfile.SetActive(id);        // flush → switch → reload every store → evaluate
SaveProfile.SetActive("");        // back to the default (unprofiled) save

SaveProfile.Active                        // "" when unprofiled
SaveProfile.Save();                       // flush now (ignores AutoSave)
SaveProfile.Reload();                     // discard memory, re-read disk
SaveProfile.ListProfiles();               // ids with data — no metadata
SaveProfile.ProfileExists(id);
SaveProfile.DeleteProfile(id);            // that profile's ProxyCore files only
SaveProfile.CopyProfile(from, to);        // independent copy
SaveProfile.ProfileChanged += id => { };  // fires after stores reload
SaveProfile.AutoSave = false;             // batch writes until Save()
```

`SaveProfile.Id(params string[])` is **injective** — `Id("a_b","c")` and `Id("a","b_c")` never
collide. Non-`[a-z0-9-]` characters are percent-encoded from UTF-8, so ids are filename-safe,
case-collision-proof, and free of `/` or `..`. ProxyCore assigns the segments no meaning.
Always compose ids with `Id()` rather than concatenating by hand.

`UnlockManager.SetSaveProfile(id)` still works and forwards to `SaveProfile.SetActive`.

**Layout.** With a profile active: `{persistentDataPath}/proxycore/{profileId}/unlocks.json`
plus one `flags_{name}.json` per collection. Unprofiled (the default): the legacy flat
`unlocks.json` / `flags_{name}.json`, so a project that never calls the API is unchanged.

**Flags follow the profile too.** `GameFlagCollection` is profile-scoped, and a mid-session
switch reloads persisted collections and clears session-only ones, so flags never leak between
saves. (This was not true before 2.5.0.)

**Custom stores.** Implement `IProfileScopedStore` (`OnProfileChanging` = flush,
`OnProfileChanged(root)` = clear + reload) and `SaveProfile.Register(this)` in `OnEnable`.
For ProxyCore-shaped state only — not a general game save system.

**Durability.** Writes are temp-file + atomic replace. An unparseable file is moved to
`{path}.corrupt` with a logged error and the store starts empty — never silently discarded.

## Scoping the auto-unlock catalog

`BaseRegistry<T>.GetCatalogDefinitions()` is `virtual`. Override it to return a subset and the
unlock system stops auto-unlocking (and stops saving keys for) everything outside it:

```csharp
public override IReadOnlyList<BaseDefinition> GetCatalogDefinitions() =>
    _activeChapter.ConvertAll(d => (BaseDefinition)d).AsReadOnly();
```

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
the flag flips — no polling. Enable `_savesAcrossSessions` to persist flags to disk (one file
per collection, keyed by asset name, **inside the active save profile's directory** — see
[Save profiles](#save-profiles)). Session-only collections are cleared on a profile switch too,
so neither kind leaks between save games.

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

## Editor: multiple graphs & save slots

A project can hold several unlock graphs — one `UnlockGraphLayoutData` asset each, typically
one per game level. The window's **graph dropdown** switches, creates, duplicates, renames,
and deletes them; the **`💾` dropdown** manages that graph's save slots.

A graph owns its node layout, groups, colours, **registry filter**, and save slot list.
The registry filter is how a graph is scoped to a level — hide the registries that level does
not use. Selecting a save slot calls
`UnlockManager.SetSaveProfile(SaveProfile.Id(graphId, slot))`.

Each graph has a **Graph Id**, auto-generated and editable in the asset's inspector. Bind it
to an id the game itself uses (a level or scenario id) and the graph previews exactly the
profile the game selects with `SaveProfile.Id(thatId, slot)`, slot names included — because
both sides compose through `SaveProfile.Id`, no hand-encoding is needed. Clearing the field
mints a fresh id.

The graph window never repoints `UnlockManager` in **Play Mode** — the running game owns the
active profile there. The pickers go read-only and the `💾` label shows the live
`SaveProfile.Active`; the graph's own slot is restored on exiting Play Mode.

Definition nodes carry a 🔒/🔓 button that locks or unlocks that definition on the spot, in
**Edit Mode as well as Play Mode**, writing to the active save profile. Useful for testing a
progression state without playing to it — but it mutates real save data, so pick a scratch
save slot first.

The **Cleanup** dialog lists Used / Mismatched / **Ineffective** / Unused conditions.
Ineffective means trivially true: a `DefinitionUnlockedCondition` whose target has
`IsUnlockedByDefault` gates nothing. Editing such an asset also logs a warning.

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
- Expecting `ResetSessionUnlocks()` to clear a `Lock()`. It no longer does — lock overrides
  are saved state. Call `Unlock()` or `ResetSavedUnlocks()`.
- Forgetting to call `SaveProfile.SetActive()` when loading a save game, so every slot writes
  to the same default file.
- Concatenating profile-id segments by hand instead of using `SaveProfile.Id()` — two different
  saves can collide on one directory.
- Expecting ProxyCore to remember the active profile across launches, or to hold save names and
  timestamps. It does neither; that is game-owned data.
