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
- [Writing a payload](#writing-a-payload)
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

// One or more payloads, then Send():
TriggerEvent.GameWorld.CellPlaced
    .With(new CellPlacedPayload(x, y, symbol, nextTurnId))
    .Send();

// No payload:
TriggerEvent.UI.ShowHUD.Send();

// Set extra optional fields with an object initializer on the payload:
TriggerEvent.GameWorld.MineTriggered
    .With(new TileCoordPayload(x, y) { Range = range })
    .Send();
```

`.With(...)` returns the builder so calls chain. `.Send()` dispatches immediately and
releases the data back to the pool.

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
        var p = data.TryGet<TurnStartedPayload>();
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
    var p = data.TryGet<CellPlacedPayload>();
    if (p == null) return;
    Render(p.X, p.Y, p.Symbol);
}
```

## Reading payloads

`EventMessageData` holds at most **one payload per concrete type**. Retrieve by type:

```csharp
var dmg = data.TryGet<FloatPayload>();   // returns the payload or null
if (dmg != null) health -= dmg.value;    // single-value payloads expose `.value`

float amount = data.Get<FloatPayload>().value;   // throws if the payload is absent
bool has     = data.Has<StringPayload>();
```

Prefer `TryGet<T>()` + null check for anything optional; reserve `Get<T>()` for
payloads guaranteed present by the event's contract.

## Writing a payload

Payloads implement `IEventMessagePayload`. There are two idioms — pick by shape.

**Single value** → extend `EventMessagePayload<T>`. You get `.value`, `Reset()`,
`GetValue()`, `ValueType` for free; add a ctor and a fluent `With`:

```csharp
using System;
using ProxyCore;

[Serializable]
public class Vector3Payload : EventMessagePayload<UnityEngine.Vector3>
{
    public Vector3Payload() { }                         // parameterless ctor required
    public Vector3Payload(UnityEngine.Vector3 v) => SetValue(v);
    public Vector3Payload With(UnityEngine.Vector3 v) { SetValue(v); return this; }
}
// read back with: data.TryGet<Vector3Payload>()?.value
```

**Multiple fields** → extend `EventMessagePayloadBase` directly, expose public fields,
and override `Reset()`, `GetValue()`, `ValueType`. Set `_isSet = true` in the ctor:

```csharp
using System;
using ProxyCore;

[Serializable]
public class CellPlacedPayload : EventMessagePayloadBase
{
    public int    X, Y;
    public string Symbol;
    public string NextTurnID;

    public CellPlacedPayload(int x, int y, string symbol, string nextTurnID)
    {
        X = x; Y = y; Symbol = symbol; NextTurnID = nextTurnID;
        _isSet = true;
    }

    public override void   Reset()    { X = 0; Y = 0; Symbol = null; NextTurnID = null; _isSet = false; }
    public override object GetValue() => Symbol;                 // most representative field
    public override Type   ValueType  => typeof(string);
}
```

Notes:
- A **parameterless constructor** must exist (the inspector's expected-payload picker
  and serialization use it). `EventMessagePayload<T>` subclasses should declare one
  explicitly since adding another ctor removes the implicit default.
- `[Serializable]` lets the payload appear in the `EventMessage` inspector's
  "Expected Payloads" list. If a newly added payload type doesn't show up there, run
  **ProxyCore ▸ Refresh Payload Types**.
- Optional extra fields can be public and set via object-initializer at the call site
  (`new TileCoordPayload(x, y) { Range = r }`).

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

## Validation and debugging

- **Expected Payloads** on an `EventMessage` are validated at trigger time; a mismatch
  logs a warning listing missing/extra types. Tick **Skip Payload Validation** for
  hot-path events.
- **Mute Debug Log** on an `EventMessage` silences it in the coordinator's log.
- **`EventCoordinator` ▸ Enable Debugging** logs every triggered event (and, with
  **Show Attached Events**, chained ones).
- Windows: **ProxyCore ▸ Event Manager**, **ProxyCore ▸ Event Debug Monitor**.

## Common mistakes

- Using `TryGet<T>(out var x)` — the method **returns** the payload (or null), it has
  no out-parameter.
- Reading `.Value` (capital V) — single-value payloads expose `.value`.
- Forgetting to dispose a subscription in `OnDisable` — leaks listeners across scenes.
- Expecting an accessor before regenerating, or for an event with no category.
- Placing the `EventCoordinator` asset outside a `Resources/` folder — `Instance` is
  null in a build and nothing dispatches.
- Caching an `EventMessageData` or a payload past the callback — the data is pooled and
  reused after dispatch.
