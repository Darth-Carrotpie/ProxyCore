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
- [Danger zone: dispatch is synchronous and same-frame](#danger-zone-dispatch-is-synchronous-and-same-frame)
- [Events, categories, and generating accessors](#events-categories-and-generating-accessors)
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
  (`Create ▸ Registries ▸ Event Coordinator`) that auto-discovers every `EventMessage`
  asset and dispatches. It is an SO singleton, so **the EventCoordinator asset must be
  in a `Resources/` folder** to resolve at runtime in a build (see the singleton rule
  in SKILL.md). Exactly one is expected per project.
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

## Danger zone: dispatch is synchronous and same-frame

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

## Events, categories, and generating accessors

`TriggerEvent.*` / `ListenEvent.*` are **generated C#**, not hand-written. To make a
new event usable in code:

1. **Create the categories** you want it under — `Create ▸ Definitions ▸ Category
   Definition` (e.g. `Health`, `UI`). A category with no valid codegen name won't
   produce an accessor path.
2. **Create the `EventMessage`** — `Create ▸ Definitions ▸ Event Message`. Set its
   `shortName` (this becomes the accessor identifier — the asset auto-syncs `shortName`
   to the first word of the asset name) and add one or more `categories`.
3. **Regenerate** — accessors regenerate automatically when `EventMessage` assets
   change; to force it, **ProxyCore ▸ Regenerate Event Accessors**. Generated files
   land in a `Generated/` folder next to the `EventMessage` assets.

An event in multiple categories is reachable under **each**:
`Heal` in `[Health, Player]` → both `TriggerEvent.Health.Heal` and
`TriggerEvent.Player.Heal` (same underlying event).

If an accessor won't resolve, the cause is almost always: the `EventMessage` asset is
missing, it has **no category**, or accessors were not regenerated.

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
- Expecting an accessor before regenerating, or for an event with no category.
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
