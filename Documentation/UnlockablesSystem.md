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
UnlockManager.ResetSavedUnlocks();  // clears saved state (unlocks + lock overrides) and deletes the file
UnlockManager.ResetSessionUnlocks();// clears session-only unlocks
// Save profiles
UnlockManager.SetSaveProfile("level1_slot2"); // point disk state at unlocks_level1_slot2.json
string profile = UnlockManager.SaveProfile;   // "" while using the default unlocks.json
// Auto-unlock
UnlockManager.EvaluateAutoTriggers();// manually re-evaluate all registered triggers
```

### Lock overrides — Lock() and Unlock() are symmetric

`Lock(item)` records the item's key in an internal *lock override* set. While a key is in
that set, `IsUnlocked` returns `false` even when `IsUnlockedByDefault` is `true`.

**`Unlock(item)` clears the override.** `Lock(x)` followed by `Unlock(x)` leaves the item
unlocked, and the single (`Unlock`) and bulk (`UnlockAll`, `UnlockAllByKeys`) paths always
produce identical state from identical input.

`Lock` also removes the key from *both* the saved and session unlock sets, so
`IsUnlockedByKey` never disagrees with `IsUnlocked` after a lock.

Lock overrides are **saved state**: they are written to the same file as saved unlocks and
survive app restarts and scene reloads. Two ways to clear one:

| Call | Effect on lock overrides |
|---|---|
| `Unlock(item)` / `UnlockAll(items)` | clears the override for those items |
| `ResetSavedUnlocks()` | clears **all** overrides and deletes the save file |
| `ResetSessionUnlocks()` | **no effect** — overrides are not session state |

> Changed in 2.4.0: `Unlock()` previously could not reverse `Lock()`, overrides were
> memory-only, and `ResetSessionUnlocks()` was the only way to clear them.

The `savesAcrossSessions` argument on `LockByKey` / `LockAllByKeys` is retained for API
symmetry only — overrides always persist, so it no longer selects a storage tier.

### Persistence

- `SavesAcrossSessions = true` → key written to `Application.persistentDataPath/unlocks.json`.
- `SavesAcrossSessions = false` → key is session-only; cleared on every scene reload.
- `IsUnlockedByDefault = true` → item is treated as unlocked without any explicit `Unlock()` call. An explicit `Lock()` overrides this until the next `Unlock()`.
- Lock overrides are always persisted, regardless of `SavesAcrossSessions`.
- Each saved key also carries a provenance record (ordinal, timestamp, acknowledged flag) — see
  [Unlock provenance](#unlock-provenance--acknowledgement). Older files carry none and migrate on load.

### Save profiles (one save game per profile)

#### Ownership boundary

**The host game owns the save-game universe.** It decides what a save is, what identifies one,
when to save and load, what a save-select screen shows, and which save is active at boot.

**ProxyCore owns only its own state** — saved unlock keys, lock overrides, and flag collections
— and keeps it partitioned under whichever profile the game declares active.

A profile id is an **opaque key**. ProxyCore assigns it no meaning, stores no metadata about it
(no names, timestamps, or counters), never auto-detects the current save, and never remembers
which profile was active across launches. The game re-selects on every boot.

#### Using it

```csharp
string id = SaveProfile.Id(playerSlot, difficulty);  // injective, filename-safe
SaveProfile.SetActive(id);                           // flush → switch → reload → evaluate
SaveProfile.SetActive("");                           // back to the default (unprofiled) save
```

`SaveProfile.Id(params string[])` composes an id from any number of opaque segments and is
**injective**: `Id("a_b","c")` and `Id("a","b_c")` never collide. Every character outside
`[a-z0-9-]` is percent-encoded from its UTF-8 bytes, which keeps ids filename-safe on every
platform, immune to case-insensitive filesystems collapsing two ids, and free of `/` or `..`
traversal. ProxyCore does not care how many segments there are or what they mean.

`UnlockManager.SetSaveProfile(id)` still works and forwards to `SaveProfile.SetActive`.

#### Layout

With a profile active, ProxyCore keeps its files in one directory per profile:

```
{persistentDataPath}/proxycore/{profileId}/
    unlocks.json        ← savedUnlockedKeys + lockedOverrideKeys
    flags_{name}.json   ← one per GameFlagCollection
```

With **no** profile active — the default — the legacy flat paths
`{persistentDataPath}/unlocks.json` and `flags_{name}.json` are used unchanged, so a project
that never touches this API behaves exactly as before.

#### Lifecycle API

```csharp
SaveProfile.Active                        // "" when unprofiled
SaveProfile.Save();                       // flush every ProxyCore store now
SaveProfile.Reload();                     // discard memory, re-read from disk
SaveProfile.ListProfiles();               // ids ProxyCore holds data for (no metadata)
SaveProfile.ProfileExists(id);
SaveProfile.DeleteProfile(id);            // removes that profile's ProxyCore files, nothing else
                                          //   deleting the ACTIVE profile also deselects it
SaveProfile.CopyProfile(from, to);        // independent copy; flushes `from` if it is active
SaveProfile.ProfileChanged += id => { };  // fires after ProxyCore stores have reloaded
SaveProfile.AutoSave = false;             // batch writes; Save() and switches still flush
```

`AutoSave` defaults to `true`, so unlock and flag mutations write immediately as before. Set it
`false` to control when a save game hits disk; explicit `Save()` and profile switches always
flush regardless.

`ProfileChanged` fires **after** every ProxyCore store has reloaded, so a handler can swap
game-owned state without ordering calls by hand.

#### Adding your own profile-scoped store

Implement `IProfileScopedStore` and register it. This is for ProxyCore-shaped state that must
follow the active save — it is not a general save system for game data.

```csharp
void OnProfileChanging();                  // flush to the outgoing profile
void OnProfileChanged(string profileRoot); // clear state and reload from the new root
```

`UnlockManager` and `GameFlagCollection` both implement it.

#### Durability

Writes go through a temp file and an atomic replace, so an interrupted write leaves the previous
save readable. A file that exists but cannot be parsed is **not** silently discarded: it is
moved to `{path}.corrupt`, an error naming both paths is logged, and that store starts from
empty state. The next save writes a fresh file and the quarantined one stays for recovery.

Projects that never call a profile API keep reading and writing the legacy flat paths, so
nothing needs migrating.

`SaveProfile.Active` is a static and resets on domain reload and on entering Play Mode — set it
as part of loading a save. The Unlock Dependency Graph window applies its selected graph's save
slot automatically.

### IsUnlocked vs IsUnlockedByKey

`IsUnlocked` returns `true` if the item was explicitly unlocked **or** if `IsUnlockedByDefault` is set (and not overridden).
`IsUnlockedByKey` returns `true` only if the item was explicitly unlocked — it never reflects `IsUnlockedByDefault`.

Use `IsUnlockedByKey` in custom `UnlockCondition.Evaluate()` implementations when the condition
should represent a deliberate game action (e.g. quest completed), not a default-available state.

### Events

Assign `EventMessage` assets to `_onUnlocked` / `_onLocked` in the `UnlockManager` inspector.
Both fire with a `StringPayload(unlockKey)` through the `EventCoordinator`.

They are **transition-only** and never replayed. Auto-unlocks run in `OnAwake` and on every
profile switch, before any UI exists — so don't rebuild "what's new" from broadcasts. Use
provenance below.

### Unlock provenance & acknowledgement

Every unlocked key carries a persisted record of when it was unlocked and whether the player has
been shown it. Queryable from any script at any time; no listener need have been alive.

```csharp
int marker = UnlockManager.UnlockMarker;                  // match start / boot — store the int
foreach (var r in UnlockManager.GetUnlocksSince(marker))  // what was earned since
    Show(r.Key);

foreach (var r in UnlockManager.GetUnacknowledgedUnlocks()) Badge(r.Key);   // survives a quit
UnlockManager.AcknowledgeAllUnlocked();                   // player closed the screen
```

Also: `TryGetUnlockRecord(key, out r)`, `SetAcknowledgedByKey(key, bool)` (the primitive — `false`
un-marks), and `Acknowledge` / `AcknowledgeByKey` / `AcknowledgeAll` / `AcknowledgeAllByKeys`.
`UnlockRecord` = `Key`, `Ordinal`, `UnlockedAtUnixSeconds` (+ `UnlockedAtUtc`, `HasTimestamp`),
`Acknowledged`, `IsSessionOnly`. Queries never return null and are ordered by ordinal.

Non-obvious behaviour:

- The marker is a monotonic `int`, not a time. The timestamp is for display only — ProxyCore
  never orders or filters by it, so no clock change can perturb progress.
- A record lives only while its key is unlocked. `Lock()` drops it, so re-unlocking mints a fresh
  ordinal and reads as unacknowledged again. `SetAcknowledgedByKey(key, true)` after to opt out.
- Session-only unlocks get records (`IsSessionOnly`), never written to disk, gone on scene reload
  — so acknowledging one is not durable either.
- Markers are **per profile**; re-capture on `SaveProfile.ProfileChanged`.
- Acknowledgement honours `SaveProfile.AutoSave` exactly as unlocks do.
- Saves written before this feature load with ordinal `0` and `acknowledged = true`, so an
  existing save never presents its whole back catalogue as "NEW". Ordinal `0` is below every
  minted ordinal, so those keys never satisfy `GetUnlocksSince`.

Host game's job: holding the marker, defining a "match", when to acknowledge, any time-based
rule, and mapping keys back to definitions for display.

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

### Scoping which definitions the unlock system sees

`EvaluateAutoTriggers()` and `PurgeStaleSavedKeys()` both read
`BaseRegistry<T>.GetCatalogDefinitions()`, which is `virtual`. Override it to show the unlock
system a subset:

```csharp
public class ChapterRegistry : BaseRegistry<ChapterDefinition> {
    public IReadOnlyList<ChapterDefinition> ActiveChapter { get; set; }

    public override IReadOnlyList<BaseDefinition> GetCatalogDefinitions() =>
        ActiveChapter.ConvertAll(d => (BaseDefinition)d).AsReadOnly();
}
```

Definitions outside the returned list are never auto-unlocked, so a save file does not
accumulate keys for content that save will never surface. Filtering here rather than through the
manager's registry list keeps the change in your code, where the scoping rule lives.

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

### Flags are profile-scoped

Flag collections are ProxyCore-owned state, so they follow the active save profile like unlocks
do. With a profile active a collection writes to `proxycore/{profileId}/flags_{name}.json`;
unprofiled it keeps the legacy `flags_{name}.json`. The file name still comes from the asset
name, so renaming the asset still changes its file.

Switching profiles mid-session reloads every collection — a `_savesAcrossSessions` collection
re-reads the new save's file, and a session-only collection is cleared. Flag state therefore
never leaks from one save game into another, which matters because `FlagCondition` feeds unlock
evaluation.

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
| `UnlockDebugWindow` | Scene View toolbar lock icon | Live saved / session / locked-override keys during Play Mode, each with its ordinal, age and acknowledged toggle; marker, `Acknowledge All` and an unacknowledged-only filter in the toolbar |
| `ProxyCore > Unlockable Actions > Clear Save Data` | Menu bar | Deletes the **active save profile's** file; works in Edit and Play Mode |
| `ProxyCore > Unlockable Actions > Reset Session Unlocks` | Menu bar | Clears session-only unlocks; Play Mode only |
| `ProxyCore > Unlockable Actions > Refresh Unlock Registries` | Menu bar | Repopulates the manager's auto-unlock registry list |
| Condition Cleanup dialog | Unlock Graph toolbar → `Cleanup` | Lists Used, Mismatched, Ineffective, and Unused condition assets with bulk delete |

**Ineffective conditions.** A `DefinitionUnlockedCondition` whose target has
`IsUnlockedByDefault` is trivially true, so it gates nothing wherever it is used as a
prerequisite. Editing such an asset logs a warning; the Cleanup dialog's **Ineffective**
column finds the ones already in the project.

### Unlock Dependency Graph — multiple graphs & save slots

Open with **ProxyCore ▸ Unlock Dependency Graph**.

- **Graph dropdown** (leftmost) — switch between graphs, or create, duplicate, rename, and
  delete them. Each graph is a separate `UnlockGraphLayoutData` asset, normally one per game
  level. A graph owns its node layout, groups, colours, **registry filter**, and save slots.
  The registry filter is what scopes a graph to its level: hide the registries that level
  does not use, via the `Registries ▾` dropdown.
- **Save dropdown** (`💾`) — the graph's save slots, plus New Save / Delete Save. Selecting a
  slot calls `UnlockManager.SetSaveProfile(SaveProfile.Id(graphId, slot))`, so each slot reads
  and writes its own profile directory. Deleting a slot erases that state.
- **Graph Id** — every graph carries an id, auto-generated on creation and visible in the
  asset's inspector. Set it to an id the game itself uses (a level or scenario id) and the
  graph previews exactly the profile the game selects with `SaveProfile.Id(thatId, slot)` —
  slot names then line up with whatever the game's second segment is. Clearing the field
  mints a fresh id.

> **Play Mode belongs to the game.** The graph window never repoints `UnlockManager` while
> playing: a game that calls `SaveProfile.SetActive(...)` in `Awake` keeps its own profile
> whether or not the window is open. The pickers become read-only and the `💾` label shows
> the live `SaveProfile.Active`; the graph's own slot is restored on exiting Play Mode.
- **Per-node lock toggle** — the 🔒/🔓 button in a definition node's title bar locks or
  unlocks that definition immediately. It works in **Edit Mode as well as Play Mode**, and
  writes to the active save profile's file. Auto-unlock cascades are applied, so other node
  badges update in the same click.

> The node toggle mutates real save data. Select a scratch save slot before using it if you
> do not want to disturb the default profile.

---

## File Map

```
Runtime/Unlockables/
  IUnlockable.cs
  UnlockBehavior.cs
  UnlockSaveData.cs
  UnlockRecord.cs                    ← public provenance snapshot returned by the query API
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
