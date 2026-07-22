---
name: proxycore
description: >-
  Use ProxyCore correctly in a Unity project. ProxyCore (UPM package
  com.shakotis.proxycore) is a data-driven event/messaging system plus
  ScriptableObject definition-registries and an unlockables/progression system.
  Invoke this skill whenever working in a Unity project that references ProxyCore
  or ProxyCore.Generated — adding or wiring an event, writing a payload, adding a
  listener (TriggerEvent./ListenEvent.), creating a BaseDefinition / BaseRegistry
  / catalog, or gating content with IUnlockable / UnlockManager / GameFlagCollection /
  unlock conditions. Trigger even when the user just says "add an event", "make a
  new definition", "unlock this when…", or names a *Payload / *Definition / *Registry
  type, because the correct idioms here differ from generic Unity code and from
  ProxyCore's own README.
---

# ProxyCore

ProxyCore is a Unity package with three loosely-coupled subsystems. Almost every
task touches exactly one of them — read the matching reference file, not all three.

| You are… | Read |
|---|---|
| triggering/listening to events, writing a `*Payload`, adding a category, regenerating accessors | `references/events.md` |
| creating a `*Definition` (data asset) or a `*Registry`/catalog that looks them up | `references/definitions-and-registries.md` |
| locking/unlocking content, prerequisites/auto-unlock chains, flags, unlock conditions | `references/unlockables.md` |

The rules below are cross-cutting and apply no matter which subsystem you touch.
Read them before writing any ProxyCore code.

## Install

ProxyCore is a UPM git package. In the consuming project's `Packages/manifest.json`,
map the package id to the repo's git URL with a version tag:

```json
"com.shakotis.proxycore": "https://github.com/Darth-Carrotpie/ProxyCore.git#2.2.10"
```

The id is `com.shakotis.proxycore`; the value is the git URL. Pin a real release tag
(`#2.2.10` shown) and bump it to the newest — check the repo's releases.

Runtime code lives in the `ProxyCore` namespace; generated event accessors live in
`ProxyCore.Generated`. A typical file starts with:

```csharp
using ProxyCore;
using ProxyCore.Generated;
```

## Cross-cutting rules

### Singletons come in two flavours — don't mix them up

- **`SingletonSO<T>`** — a ScriptableObject singleton (asset on disk). `T.Instance`
  is resolved by **`Resources.Load`** first, then an editor asset search. Any SO
  singleton you rely on at runtime (an `EventCoordinator`, a `UnlockManager`, a
  registry) **must live in a `Resources/` folder** or `Instance` returns null in a
  build. `_persistent` defaults to **true** (runtime state survives scene reloads).
- **`Singleton<T>`** — a MonoBehaviour singleton (a component in the scene),
  resolved via `FindFirstObjectByType`. `_persistent` defaults to **false**; tick it
  in the inspector to `DontDestroyOnLoad`.

### Accessing a concrete registry: use the generic base type

`BaseRegistry<T>` derives from `SingletonSO<BaseRegistry<T>>`, so the singleton is
keyed by the **generic base**, not your concrete subclass. Access it as:

```csharp
var reg = BaseRegistry<PowerUpDefinition>.Instance;   // ✅ resolves the asset
// PowerUpRegistry.Instance                            // ✗ not how the singleton is keyed
```

### `TryGet<T>()` returns the payload (or null) — it is not an out-parameter

The README shows `data.TryGet<T>(out var x)`; the actual API returns a nullable
value. Use:

```csharp
var p = data.TryGet<FloatPayload>();   // null when absent
if (p != null) health -= p.value;      // note: lowercase `.value`
// data.Get<FloatPayload>()            // throws if absent — only when guaranteed present
```

### Event subscriptions are IDisposable — store and dispose them

`ListenEvent.…Do(cb)` returns an `IDisposable`. Keep the handle and dispose it in
`OnDisable`, or the listener leaks across scene reloads. See `references/events.md`.

## Event quick reference (the 80% case)

Events are the most-used subsystem, so the core idiom is inlined here. For payload
authoring, categories, and accessor generation, read `references/events.md`.

```csharp
using ProxyCore;
using ProxyCore.Generated;

// Trigger, with payloads:
TriggerEvent.Health.HPUpdated
    .With(new HPUpdatePayload(playerId, newHp, rowsScored, maxHp))
    .Send();

// Trigger, no payload:
TriggerEvent.UI.ShowHUD.Send();

// Listen — store the IDisposable, dispose in OnDisable:
private IDisposable _sub;

void OnEnable()
{
    _sub = ListenEvent.Health.HPUpdated.Do(data =>
    {
        var p = data.TryGet<HPUpdatePayload>();
        if (p == null) return;
        UpdateBar(p.PlayerId, p.NewHp, p.MaxHp);
    });
}

void OnDisable() => _sub?.Dispose();
```

`TriggerEvent.<Category>.<EventShortName>` and `ListenEvent.<Category>.<EventShortName>`
are **generated code**. They exist only after an `EventMessage` asset with that
short name and category exists and accessors have been generated (menu
**ProxyCore ▸ Regenerate Event Accessors**). If an accessor doesn't resolve, the
asset or the regeneration step is missing — see `references/events.md`.

## Utility extensions

ProxyCore also ships small extension helpers in the `ProxyCore` namespace
(`Transform.FindNearest`, `Renderer.IsVisibleFrom`, `GameObject.DestroyAllChildren`,
`List<MonoBehaviour>.EnableAll`, math/reflection helpers). They're plain `using
ProxyCore;` extension methods with no special contract — discover them via
IntelliSense; they need no dedicated reference here.
