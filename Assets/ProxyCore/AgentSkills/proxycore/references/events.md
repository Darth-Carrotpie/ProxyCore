# ProxyCore — Event System

Data-driven messaging: `EventMessage` ScriptableObjects define event types,
categories build a generated accessor hierarchy, and typed `*Payload` objects carry
data. You trigger and listen through generated `TriggerEvent.*` / `ListenEvent.*`
static accessors.

## Contents
- [Runtime moving parts](#runtime-moving-parts)
- [Triggering events](#triggering-events)
- [Listening to events](#listening-to-events)
- [Reading payloads](#reading-payloads)
- [Payload design: reuse and compose](#payload-design-reuse-and-compose)
- [Writing a payload](#writing-a-payload)
- [Chaining and ordering events](#chaining-and-ordering-events)
- [Danger zone: dispatch is synchronous, same-frame, and never replayed](#danger-zone-dispatch-is-synchronous-same-frame-and-never-replayed)
- [Events, categories, and generating accessors](#events-categories-and-generating-accessors)
- [Where the generated files land](#where-the-generated-files-land)
- [Validation and debugging](#validation-and-debugging)
- [Common mistakes](#common-mistakes)

## Runtime moving parts

- **`EventMessage`** — a ScriptableObject asset (`Create ▸ Definitions ▸ Event Message`).
  Its `shortName` drives the generated accessor name; its `categories` list drives
  the generated namespace path.
- **`EventCategory` / `CategoryDefinition`** — a ScriptableObject
  (`Create ▸ Definitions ▸ Category Definition`). Each category becomes a nested
  static class (e.g. `TriggerEvent.Health`).
- **`EventCoordinator`** — a `BaseRegistry<EventMessage>` singleton
  (`Create ▸ Registries ▸ Event Coordinator`) that dispatches. It resolves events out of
  its own **serialized `definitions` list**, which is repopulated from the project's
  assets in the editor — not scanned at runtime. An event missing from that list is
  `null` at runtime; see [step 3](#step-3-is-real-and-it-is-the-one-people-miss).
  It is an SO singleton, so **the EventCoordinator asset must be in a `Resources/`
  folder** to resolve at runtime in a build (see the singleton rule in SKILL.md).
  Exactly one is expected per project.
- **`EventMessageData`** — a pooled payload container passed to listeners. Pooled and
  auto-released after dispatch; never cache it or its payloads past the callback.

## Triggering events

```csharp
using ProxyCore;
using ProxyCore.Generated;

// Compose several payloads with chained .With(...), then Send():
TriggerEvent.GameWorld.CellPlaced
    .With(new TileCoordPayload(x, y))
    .With(new SymbolPayload(symbol))
    .With(new TurnPayload(nextTurnId))
    .Send();

// No payload:
TriggerEvent.UI.ShowHUD.Send();

// Set extra optional fields with an object initializer on the payload:
TriggerEvent.GameWorld.MineTriggered
    .With(new TileCoordPayload(x, y) { Range = range })
    .Send();
```

`.With(...)` returns the builder, so **chain as many payloads as the event needs** —
`.With(a).With(b).With(c)`. Each payload is stored by its concrete type (one instance
per type per event; a second `.With()` of the same type replaces the first). `.Send()`
dispatches immediately and releases the data back to the pool.

**`using` auto-send** — for conditional payloads, the builder is `IDisposable` and
sends on dispose:

```csharp
using (var evt = TriggerEvent.Player.LevelUp.With(new IntPayload(newLevel)))
{
    if (hasBonus) evt.With(new FloatPayload(bonusXp));
}   // sent here
```

## Listening to events

`.Do(cb)` returns an `IDisposable`. **Store it and dispose in `OnDisable`** — the
`EventCoordinator` holds the delegate, so an undisposed listener survives scene
reloads and fires against a dead object.

```csharp
private IDisposable _placedSub;
private IDisposable _turnSub;

void OnEnable()
{
    _placedSub = ListenEvent.GameWorld.CellPlaced.Do(OnCellPlaced);
    _turnSub   = ListenEvent.GameWorld.TurnStarted.Do(data =>
    {
        var p = data.TryGet<TurnPayload>();
        if (p != null) _isMyTurn = p.IsMyTurn;
    });
}

void OnDisable()
{
    _placedSub?.Dispose();
    _turnSub?.Dispose();
}

void OnCellPlaced(EventMessageData data)
{
    var coord = data.TryGet<TileCoordPayload>();
    if (coord == null) return;
    Render(coord.X, coord.Y, data.TryGet<SymbolPayload>()?.value);
}
```

**Listening from a UI Toolkit element.** `OnAttachToPanel` fires in the Editor too, so a
`contextType == Player` check is not enough to keep runtime subscriptions out of edit
mode — add a play-mode guard:

```csharp
private void OnAttachToPanel(AttachToPanelEvent evt)
{
    if (panel.contextType != ContextType.Player) return;
    if (!Application.isPlaying) return;   // AttachToPanel also fires on editor hierarchy rebuilds
    _sub = ListenEvent.UI.MyEvent.Do(OnMyEvent);
}
```

## Reading payloads

`EventMessageData` holds at most **one payload per concrete type**. Retrieve each by
type — a listener reads only the payloads it cares about and ignores the rest:

```csharp
var coord = data.TryGet<TileCoordPayload>();   // returns the payload or null
if (coord != null) MoveCursor(coord.X, coord.Y);

var amount = data.TryGet<FloatPayload>();       // single-value payloads expose `.value`
if (amount != null) health -= amount.value;

float f  = data.Get<FloatPayload>().value;      // throws if the payload is absent
bool has = data.Has<SymbolPayload>();
```

Prefer `TryGet<T>()` + null check for anything optional; reserve `Get<T>()` for
payloads guaranteed present by the event's contract.

## Payload design: reuse and compose

**Do not create one payload type per event.** Payloads are generic, reusable data
carriers — name them by the **shape of the data**, not by the event, and share them
across every event that needs that shape. Model each event's data as a *composition*
of a few reusable payloads rather than one bespoke struct.

- A `TileCoordPayload` (an `(x, y)`, plus optional `Range`/`CasterID`) serves
  `MineTriggered`, `ExplosionTriggered`, `HighlightMoved`, targeting, AOE, aim, …
- A `TileCoordsPayload` (a coord array) serves `CellsRevealed`, `CellsRemoved`,
  `CellsHidden`, `ReachCellsUpdated`, …
- An `AmountPayload` / `IntPayload` / `StringPayload` / a definition-reference payload
  are each reused everywhere their shape fits.

So a `TargetCoordinate`-style payload is used by `TargetingEvent`, `AimEvent`,
`AOEEvent`, etc., **in tandem with additional payloads** that carry the parts specific
to each event:

```csharp
// Same coordinate payload, different companions per event:
TriggerEvent.Combat.Aim
    .With(new TileCoordPayload(tx, ty))
    .With(new CasterPayload(casterId))
    .Send();

TriggerEvent.Combat.AreaOfEffect
    .With(new TileCoordPayload(tx, ty) { Range = radius })
    .With(new DamagePayload(damage))
    .Send();
```

Benefits: fewer types to maintain, listeners that already understand a shape work
across many events, and each event's payload set reads as a clear list of facts.
Reserve a bespoke multi-field payload only for a genuinely unique bundle that will
never be reused.

## Writing a payload

Payloads implement `IEventMessagePayload`. Two idioms — pick by shape, and favour the
smallest reusable shape (see above).

**Single value** → extend `EventMessagePayload<T>`. You get `.value`, `Reset()`,
`GetValue()`, `ValueType` for free; add a ctor and a fluent `With`:

```csharp
using System;
using ProxyCore;

[Serializable]
public class AmountPayload : EventMessagePayload<int>
{
    public AmountPayload() { }                          // parameterless ctor required
    public AmountPayload(int v) => SetValue(v);
    public AmountPayload With(int v) { SetValue(v); return this; }
}
// read back with: data.TryGet<AmountPayload>()?.value
```

**A small fixed bundle** (e.g. a coordinate) → extend `EventMessagePayloadBase`,
expose public fields, override `Reset()`, `GetValue()`, `ValueType`, and add optional
fields that callers set via object-initializer so the type stays reusable:

```csharp
using System;
using ProxyCore;

[Serializable]
public class TileCoordPayload : EventMessagePayloadBase
{
    public int X, Y;
    public int    Range;      // optional extras — set via { Range = r } when relevant
    public string CasterID;

    public TileCoordPayload(int x, int y) { X = x; Y = y; _isSet = true; }

    public override void   Reset()    { X = 0; Y = 0; Range = 0; CasterID = null; _isSet = false; }
    public override object GetValue() => (X, Y);
    public override Type   ValueType  => typeof((int, int));
}
```

Notes:
- A **parameterless constructor** must exist (the inspector's expected-payload picker
  and serialization use it). `EventMessagePayload<T>` subclasses should declare one
  explicitly since adding another ctor removes the implicit default.
- `[Serializable]` lets the payload appear in the `EventMessage` inspector's
  "Expected Payloads" list. If a newly added payload type doesn't show up there, run
  **ProxyCore ▸ Refresh Payload Types**.

## Chaining and ordering events

Two distinct needs: ordering listeners *within* one event, and sequencing one event
*after* another.

**Ordering within one event.** Main listeners fire in subscription order. To run a
handler **after all main listeners** of an event, register it as an *attachment* with
`EventCoordinator.Attach`. `Attach`/`Detach`/`TriggerEventInternal` take the
`EventMessage` asset (the generated accessors don't expose attach), so hold a
serialized reference:

```csharp
[SerializeField] private EventMessage _primaryEvent;

void OnEnable()  => EventCoordinator.Attach(_primaryEvent, OnAfterPrimary);
void OnDisable() => EventCoordinator.Detach(_primaryEvent, OnAfterPrimary);

void OnAfterPrimary(EventMessageData data) { /* runs after every normal listener */ }
```

**Sequencing event B after event A.** Trigger B from inside a listener (or an
attachment) of A. Because dispatch is synchronous (see below), B fully completes
before A's `.Send()` returns — the ordering is guaranteed:

```csharp
_hitSub = ListenEvent.Combat.Hit.Do(data =>
{
    ApplyDamage(data);
    // "…Happened" follow-up, guaranteed after Hit's own handling:
    TriggerEvent.Combat.Damaged
        .With(new TileCoordPayload(x, y))
        .With(new DamagePayload(dmg))
        .Send();
});
```

Give the follow-up event its **own** freshly-built payloads with `.With(new …)`. Do
**not** forward the incoming `data` into a nested trigger — it is pooled and released
when its dispatch ends, so reusing it across events invites use-after-release bugs.

Sequential fan-out from a single source (e.g. a network message handler that fires
`Matched`, then `HPUpdated`, then `TurnStarted` in order) is the same pattern: just
`.Send()` them in the order you need within one method.

## Danger zone: dispatch is synchronous, same-frame, and never replayed

Event dispatch is **fully synchronous**. `.Send()` invokes every listener inline and
returns only after the last one finishes — there is no queue and no next-frame
deferral. Triggering another event inside a listener nests on the same call stack, so
a chain `A → B → C` runs **all** listeners of A, B and C before the original `.Send()`
returns. **The entire chain is consumed in one frame.**

This concentrates cost into a single frame and can cause a visible frame-time spike /
hitch when:
- a hot event has **many listeners**, or
- a **deep or wide chain** cascades many events from one trigger, or
- an event is triggered **at high frequency** (per-frame ticks, tight loops).

Guidance:
- Keep listener bodies cheap and allocation-free; move heavy work (spawning,
  pathfinding, IO) **off the dispatch** — schedule it for the next frame (coroutine /
  `UniTask` / job) instead of doing it inline.
- Don't trigger high-fan-out events every frame; coalesce or throttle.
- Watch for **feedback loops** (A triggers B triggers A). ProxyCore's event dispatch
  has no built-in re-entrancy guard (unlike `UnlockManager.EvaluateAutoTriggers`), so a
  cycle will recurse until the stack blows. Break cycles with a guard flag or by
  deferring the re-trigger.
- On genuinely hot events, tick **Mute Debug Log** and **Skip Payload Validation** to
  trim per-dispatch overhead.

### No replay: a late subscriber never learns the event happened

Same-frame dispatch is not only a performance property — it is a **correctness** one.
`.Send()` reaches whoever is subscribed *at that instant* and is then gone. ProxyCore has
**no sticky, replayed, or last-value events**: nothing is buffered, and there is no
"give me the last value" API. A listener that subscribes one frame later, or one
`Start()` later, never learns the event happened.

This bites hardest at boot, because **Unity does not order `Start()` between
components**. An event sent from one component's `Start()` races every listener that
subscribes in its own `Start()` — and the race is silent:

```csharp
// Boot component
void Start() => TriggerEvent.World.HideIntroScreen.Send();   // ✗ races every listener

// Seeder component — may run Start() *after* the send above
void Start() => _sub = ListenEvent.World.HideIntroScreen.Do(SeedEverything);
```

**Nothing logs an error, because nothing fails.** The send succeeds with zero listeners,
the seeder subscribes to an event that will never fire again, and the game simply starts
with nothing seeded. Debugging starts from a symptom (empty pools, missing state) with no
stack trace pointing at the cause.

Two remedies:

```csharp
// 1. Give every listener a frame to subscribe before the boot send.
IEnumerator Start()
{
    yield return null;                                  // all other Start()s have run
    TriggerEvent.World.HideIntroScreen.Send();
}

// 2. Subscribe-then-reconcile — subscribe for future sends, and read current state now,
//    so it does not matter whether you were late.
void OnEnable()
{
    _sub = ListenEvent.World.HideIntroScreen.Do(OnHidden);
    if (IntroScreen.AlreadyHidden) OnHidden(null);      // catch up on a send you missed
}
```

Prefer (1) for one-shot boot sequencing and (2) for anything that can be enabled at an
arbitrary time (pooled objects, lazily-loaded UI, additively-loaded scenes). Neither is a
replacement for the other: (1) fixes ordering, (2) removes the dependency on ordering.

## Events, categories, and generating accessors

`TriggerEvent.*` / `ListenEvent.*` are **generated C#**, not hand-written. To make a
new event usable in code:

1. **Create the categories** you want it under — `Create ▸ Definitions ▸ Category
   Definition` (e.g. `Health`, `UI`). **Optional.** A category with no valid codegen
   name is skipped, and an event left with none falls back to `Uncategorized` — see
   below. Categories are for readability, not for making the event work.
2. **Create the `EventMessage`** — `Create ▸ Definitions ▸ Event Message`. Set its
   `shortName` (this becomes the accessor identifier — the asset auto-syncs `shortName`
   to the first word of the asset name) and add zero or more `categories`.
3. **Register it with the `EventCoordinator`** — see below. Automatic on asset import;
   force it with **ProxyCore ▸ Refresh All Registries**.
4. **Regenerate** — accessors regenerate automatically when `EventMessage` assets
   change; to force it, **ProxyCore ▸ Regenerate Event Accessors**.

An event in multiple categories is reachable under **each**:
`Heal` in `[Health, Player]` → both `TriggerEvent.Health.Heal` and
`TriggerEvent.Player.Heal` (same underlying event).

### Step 3 is real, and it is the one people miss

`EventCoordinator` is a `BaseRegistry<EventMessage>`, and a registry resolves ids out of
its **serialized `definitions` list** — that list, not the project's asset folder, is what
`GetDefinition(id)` searches. An `EventMessage` asset that is not in the list resolves to
`null` **even when the accessor's baked ID matches the asset's ID exactly**. So verifying
the IDs match (below) is *necessary but not sufficient*.

Every registry with `autoRefresh` ticked (the default) repopulates itself when a
definition asset is created, imported, moved, or deleted, and `Regenerate Event
Accessors` refreshes before it generates — so ordinary asset creation needs nothing
extra. Reach for **ProxyCore ▸ Refresh All Registries** when the asset arrived some other
way:

- hand-written or generated `.asset` YAML,
- a version-control merge or branch switch that added event assets,
- `autoRefresh` unticked on that registry,
- a project still on an older ProxyCore.

Confirm by opening the `EventCoordinator` asset and looking for the event in
`definitions`, or:

```bash
# the event asset's GUID must appear in the coordinator's definitions list
grep "^guid:" "Assets/**/MyEvent Event Message.asset.meta"   # -> guid: bc60fdad…
grep "bc60fdad" "Assets/**/Resources/EventCoordinator.asset" # no hit = not registered
```

### An event with no category is not an error — it lands in `Uncategorized`

Leaving `categories` empty is legal and generates working accessors under an
`Uncategorized` bucket:

```csharp
TriggerEvent.Uncategorized.Lock.Send();
_sub = ListenEvent.Uncategorized.Unlock.Do(OnUnlock);
```

`Uncategorized` is a nested static **class**, not a namespace — the namespace is always
`ProxyCore.Generated`. The same fallback applies when every entry in `categories` is null
or has an empty codegen name. Don't go hunting for a bug when you see it; it means only
that nobody assigned a category. Assign one if you want the accessor to read better —
that is a naming choice, not a fix.

### If an accessor won't resolve

In rough order of likelihood:

1. accessors were **not regenerated** since the asset changed;
2. the asset is **not in `EventCoordinator.definitions`** (previous section) — compiles
   fine, `null` at runtime;
3. the asset's **ID did not persist** (next section) — also compiles fine, also `null`;
4. the `EventMessage` asset is missing, or its `shortName` is not what you typed;
5. you are looking under the wrong category name — the accessor exists, the *file* you
   grepped for does not (see [Where the generated files land](#where-the-generated-files-land)).

Note what is **not** on this list: having no category (that is `Uncategorized`, above).

### Verify the event ID persisted (or it resolves to null at runtime)

Accessors are `GetDefinition(<int id>)` where `<id>` is the event's `ID` (from
`ScriptableObjectWithID`, valid = non-zero). A **new** event whose ID wasn't
persisted to disk compiles fine but resolves to `null` at runtime —
`Cannot start listening to null EventMessage` — because the code generator and the
runtime registry can end up keyed by two *different* auto-generated IDs.

Why it happens: `BaseDefinition.OnEnable` assigns a **random, in-memory-only** ID
(`IDGenerator.GenerateID()` is GUID-based, different every call) and does not save
it; the `SOWithIDPostprocessor` that *should* persist one can skip a new asset in a
mixed import batch. So codegen bakes one random ID, the runtime mints another.

Do this when adding an event:
1. Create it via **Create ▸ Definitions ▸ Event Message** (a single-asset import that
   lets the postprocessor persist an ID) — don't hand-write the `.asset` with `ID: 0`.
2. Regenerate accessors.
3. **Verify the ID matches**: the `<id>` in the generated `GetDefinition(<id>)` must
   equal `<ID>k__BackingField` in the `.asset`, and that value must be non-zero and
   unique across all `ScriptableObjectWithID` assets.

```bash
# the id in the accessor and the id on disk must be the same non-zero number
grep -R "GetDefinition(" Assets/**/Generated/ | grep MyEvent
grep "k__BackingField" "Assets/**/MyEvent Event Message.asset"
```

If you must author the YAML by hand, set a non-zero unique `<ID>k__BackingField`
yourself, then regenerate so the accessor picks up that same value.

## Where the generated files land

The filenames do **not** map one-to-one onto categories, and assuming they do sends you
looking for accessors that exist. Three rules:

1. **One `Generated/` folder per source folder.** Every folder that holds `EventMessage`
   assets gets its own `Generated/` subfolder — there is no single central output
   directory.
2. **The filename uses only the *primary* (first) category** of the events in it:
   `<PrimaryCategory>.TriggerEvent.Generated.cs` and `.ListenEvent.Generated.cs`.
3. **Each file declares a `partial class` block for *every* category its events carry.**
   Multi-category events are emitted into every one of their category blocks, inside
   whichever file their primary category named.

The consequence: **a category can have perfectly good accessors and no file bearing its
name.** A `Quest` category whose events all list `World` or `UI` first is emitted from
`World.*.Generated.cs` and `UI.*.Generated.cs`; there is no `Quest.TriggerEvent.Generated.cs`
and nothing is wrong. Everything lands in `namespace ProxyCore.Generated` regardless, and
the classes are `partial`, so blocks for one category can be spread across several files.

In-repo example — `Samples/Basic/Events/Generated/Health.TriggerEvent.Generated.cs` is
named for `Health` and contains both a `Health` and a `Player` block, because its events
list `[Health, Player]`.

**So search for the accessor, never for the filename:**

```bash
grep -rn "class Quest\b" Assets --include=*.Generated.cs      # ✅ finds the block
grep -rn "MyEventName" Assets --include=*.Generated.cs        # ✅ finds the accessor
ls Assets/**/Generated/Quest.TriggerEvent.Generated.cs        # ✗ proves nothing
```

## Validation and debugging

- **Expected Payloads** on an `EventMessage` are validated at trigger time; a mismatch
  logs a warning listing missing/extra types. Tick **Skip Payload Validation** for
  hot-path events.
- **Mute Debug Log** on an `EventMessage` silences it in the coordinator's log.
- **`EventCoordinator` ▸ Enable Debugging** logs every triggered event (and, with
  **Show Attached Events**, chained ones).
- Windows: **ProxyCore ▸ Event Manager**, **ProxyCore ▸ Event Debug Monitor**.

## Common mistakes

- Creating a new payload type per event instead of reusing shape-named payloads and
  composing several with `.With().With()`.
- Using `TryGet<T>(out var x)` — the method **returns** the payload (or null), it has
  no out-parameter.
- Reading `.Value` (capital V) — single-value payloads expose `.value`.
- Forwarding a listener's `data` into a nested trigger — it's pooled and released;
  build fresh payloads for the follow-up event.
- Forgetting to dispose a subscription in `OnDisable` — leaks listeners across scenes.
- Triggering a high-fan-out or chained event every frame — the whole cascade runs in
  one frame and spikes frame time.
- Expecting an accessor before regenerating. (An event with **no** category is fine —
  it is `TriggerEvent.Uncategorized.X`.)
- Assuming a new `EventMessage` is registered with the `EventCoordinator` just because
  the IDs match. `GetDefinition` reads the registry's serialized `definitions` list; an
  asset that never got into it is `null` at runtime. Run **ProxyCore ▸ Refresh All
  Registries** for assets that did not arrive through a normal import.
- Concluding a category's accessors were never generated because no file is named after
  it. Filenames use only the *primary* category — grep the accessor, not the filename.
- Sending an event from `Start()` and expecting listeners that subscribe in their own
  `Start()` to receive it. Dispatch is never replayed and Unity does not order `Start()`
  between components, so the send silently reaches nobody — wait a frame, or have the
  listener reconcile against current state.
- Placing the `EventCoordinator` asset outside a `Resources/` folder — `Instance` is
  null in a build and nothing dispatches.
- Caching an `EventMessageData` or a payload past the callback — the data is pooled and
  reused after dispatch.
- Shipping an `EventMessage` asset with `ID: 0` (or relying on auto-assignment during a
  batch import). The accessor compiles but `GetDefinition(id)` returns `null` at runtime.
  Create via the menu, then verify the accessor's `GetDefinition(<id>)` equals the
  asset's non-zero `<ID>k__BackingField`.
- Subscribing to events from a UI Toolkit `VisualElement` inside `OnAttachToPanel`
  guarded only by `panel.contextType == ContextType.Player`. `AttachToPanelEvent` also
  fires in the **Editor** (hierarchy rebuilds), and scene UIDocument panels are always
  `Player` context — so listeners register at edit time against a cold registry (new
  events log null) and leak across domain reloads. Add `if (!Application.isPlaying) return;`.
