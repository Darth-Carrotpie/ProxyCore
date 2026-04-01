# ProxyCore Event System

## Intro

This is a modern, data-driven messaging pattern event system for Unity. It features modular payloads, ScriptableObject-based event definitions, code-generated static accessors, and fluent builder APIs.

If you are not familiar with messaging design patterns, check out [this Unity tutorial](https://learn.unity.com/tutorial/create-a-simple-messaging-system-with-events#) for core concepts, or the classic book "Design Patterns: Elements of Reusable Object-Oriented Software" by E.Gamma, R.Helm, R.Johnson, J.Vlissides.

## Installing

### Install: Manual
- Open Unity Package Manager > Plus > Instal package from git URL
- Paste the URL: https://github.com/Darth-Carrotpie/ProxyCore.git#2.0.0
- Replace the tag with newest version, see releases

## Core Concepts

The event system consists of these key components:

### EventMessage (ScriptableObject)
Defines an event type with categories, expected payloads, and debug settings. Create via **Create > Definitions > Event Message**.

### EventMessageData
A pooled container that holds payload data for an event. Automatically managed - you don't need to manually release it.

### Payloads
Modular, extensible payload classes that carry event data:
- `StringPayload` - string values
- `IntPayload` - integer values  
- `FloatPayload` - float values
- `TransformPayload` - Transform references
- `MonoRefsPayload` - lists of MonoBehaviour references

### TriggerEvent / ListenEvent
Auto-generated static accessor classes organized by category. Generated into a `Generated/` folder next to your EventMessage assets.

## Quick Start

### 1. Create Event Definitions

1. Right-click in Project > **Create > Definitions > Event Message**
2. Name your event (e.g., "DealDamage", "Heal")
3. Assign one or more categories
4. Optionally configure expected payloads for validation

### 2. Create an EventCoordinator

1. Right-click in Project > **Create > Registries > Event Coordinator**
2. The coordinator will auto-discover all EventMessage assets

### 3. Use Generated Accessors

After creating EventMessage assets, static accessors are auto-generated. Use them like this:

**Triggering Events:**
```csharp
using ProxyCore;
using ProxyCore.Generated;

// Simple trigger with .Send()
TriggerEvent.Health.DealDamage
    .With(new FloatPayload(25f))
    .With(new StringPayload("Fire damage"))
    .Send();

// Using pattern (auto-sends on dispose, allows conditional payloads)
using (var evt = TriggerEvent.Player.LevelUp
    .With(new IntPayload(newLevel)))
{
    if (hasBonus)
    {
        evt.With(new FloatPayload(bonusXP));
    }
}
```

**Listening to Events:**
```csharp
using ProxyCore;
using ProxyCore.Generated;

private IDisposable _damageSubscription;

void OnEnable()
{
    // Subscribe and store the disposable for cleanup
    _damageSubscription = ListenEvent.Health.DealDamage.Do(OnDamageReceived);
}

void OnDisable()
{
    // Clean up subscription
    _damageSubscription?.Dispose();
}

void OnDamageReceived(EventMessageData data)
{
    // Get payload values
    if (data.TryGet<FloatPayload>(out var damage))
    {
        health -= damage.Value;
    }
    
    if (data.TryGet<StringPayload>(out var source))
    {
        Debug.Log($"Damage from: {source.Value}");
    }
}
```

## Creating Custom Payloads

Extend `EventMessagePayload<T>` to create custom payload types:

```csharp
using System;

namespace ProxyCore
{
    [Serializable]
    public class Vector3Payload : EventMessagePayload<UnityEngine.Vector3>
    {
        public Vector3Payload() { }
        public Vector3Payload(UnityEngine.Vector3 value) : base(value) { }
        
        public Vector3Payload With(UnityEngine.Vector3 value)
        {
            Value = value;
            return this;
        }
    }
}
```

Custom payloads automatically appear in the EventMessage inspector's payload dropdown.

## Categories

Categories organize events and create the accessor hierarchy. An event can belong to multiple categories:

- Event "Heal" with categories [Health, Player] generates:
  - `TriggerEvent.Health.Heal`
  - `TriggerEvent.Player.Heal`

Both point to the same event, giving flexibility in how you access it.

Create categories via **Create > Definitions > Event Category**.

## Debug Features

### Per-Event Muting
Each EventMessage has a "Mute Debug Log" checkbox to silence specific events in logs.

### Payload Validation
Configure "Expected Payloads" on an EventMessage. At trigger time, the system warns if expected payloads are missing. Disable with "Skip Payload Validation" for performance-critical events.

### EventCoordinator Inspector
- **Enable Debugging**: Log all triggered events
- **Show Attached Events**: Log event chain triggers
- **Muted/Unmuted Events**: Quick buttons to view which events are muted

## Regenerating Accessors

Static accessors regenerate automatically when EventMessage assets change. To manually regenerate:

**Menu > ProxyCore > Regenerate Event Accessors**

Generated files are placed in a `Generated/` subfolder next to your EventMessage assets.

## Migration from Legacy System

If upgrading from the old `GameMessage`/`EventName` system:

| Old System | New System |
|------------|------------|
| `EventName.UI.ShowScore()` | `TriggerEvent.UI.ShowScore` |
| `GameMessage.Write().WithFloat(x)` | `.With(new FloatPayload(x))` |
| `EventCoordinator.TriggerEvent(name, msg)` | `TriggerEvent.Category.Event.With(...).Send()` |
| `EventCoordinator.StartListening(name, callback)` | `ListenEvent.Category.Event.Do(callback)` |
| `msg.floatValue` | `data.Get<FloatPayload>().Value` |

## API Reference

### EventTriggerBuilder
```csharp
.With(IEventMessagePayload payload)  // Add a payload
.Send()                               // Trigger immediately
// or use 'using' pattern for auto-send on dispose
```

### EventListenBuilder
```csharp
.Do(Action<EventMessageData> callback)  // Subscribe, returns IDisposable
```

### EventMessageData
```csharp
.Get<T>()           // Get payload (throws if missing)
.TryGet<T>(out T)   // Try get payload (returns bool)
.Has<T>()           // Check if payload exists
```

## Tips

- Use `TryGet<T>()` when payloads are optional
- Store subscription `IDisposable` and dispose in `OnDisable()`
- Use categories to organize events logically
- Enable debugging on EventCoordinator during development
- Mute high-frequency events (ticks, updates) to reduce log spam

## Extras

### Singleton Pattern
The `SingletonSO<T>` base class provides singleton behavior for ScriptableObjects.

### Extension Methods
Various Unity object, math, and utility extension methods included.

---

## Unlockables System

Gate any gameplay content — abilities, levels, characters, cosmetics — behind a lock state managed by a single `UnlockManager` ScriptableObject. Implement `IUnlockable` on any class to make it participate. Lock state can optionally persist to disk across sessions or remain session-only. Items can also declare prerequisites via `IHasPrerequisites` so that `UnlockAutoTrigger` assets automatically unlock them once conditions are met, enabling no-code progression chains.

**Minimal implementation** — add `IUnlockable` to any class or `BaseDefinition` subclass:

```csharp
public class SwordDefinition : BaseDefinition, IUnlockable
{
    string IUnlockable.UnlockKey             => $"SwordDefinition:{ID}";
    bool   IUnlockable.SavesAcrossSessions   => true;
    bool   IUnlockable.IsUnlockedByDefault   => false;
    UnlockBehavior IUnlockable.LockedBehavior => UnlockBehavior.HideWhenLocked;
}
```

**Runtime usage:**

```csharp
// Unlock an item (persists if SavesAcrossSessions = true)
UnlockManager.Instance.Unlock(swordDefinition);

// Query lock state anywhere
if (UnlockManager.Instance.IsUnlocked(swordDefinition))
    ShowItem(swordDefinition);

// Lock it again (e.g. seasonal content expiry)
UnlockManager.Instance.Lock(swordDefinition);
```

For prerequisite chains, `GameFlagCollection` flags, and standalone (non-registry) usage see the [full documentation](Assets/ProxyCore/Documentation/UnlockablesSystem.md).

---

## Documentation

- [Unlockables System](Assets/ProxyCore/Documentation/UnlockablesSystem.md) — Covers the `UnlockManager`, `IUnlockable`, prerequisite chains, `UnlockCondition`, `GameFlagCollection`, and both standalone and definition-registry usage modes.
- [Button Strip Usage](Assets/ProxyCore/Documentation/ButtonStripUsage.md) — Explains the Scene View toolbar overlay and how downstream projects can register their own buttons via `ProxyCoreToolbarRegistry`.

---

*ProxyCore Event System - Modern, modular, and maintainable event messaging for Unity*
