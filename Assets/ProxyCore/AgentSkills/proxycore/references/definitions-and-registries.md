# ProxyCore — Definitions & Registries

A data-driven ScriptableObject layer: **definitions** are typed data assets with a
stable auto-generated `ID`; **registries** are singleton SOs that collect all
definitions of a type and look them up by `ID` or component type. A **catalog** is a
thin static wrapper you add when you want to look definitions up by a domain key
(e.g. a server string).

## Contents
- [Definitions](#definitions)
- [Registries](#registries)
- [Accessing a registry at runtime](#accessing-a-registry-at-runtime)
- [Catalog wrappers (key → definition)](#catalog-wrappers-key--definition)
- [Refreshing the definition list](#refreshing-the-definition-list)
- [Common mistakes](#common-mistakes)

## Definitions

Subclass `BaseDefinition` (which is a `ScriptableObjectWithID`). Add a
`[CreateAssetMenu]` so designers can author assets, and plain serialized fields for
the data. Definitions may also implement project interfaces (tooltips, etc.).

```csharp
using UnityEngine;
using ProxyCore;

[CreateAssetMenu(fileName = "PowerUpDefinition", menuName = "Definitions/PowerUp Definition")]
public class PowerUpDefinition : BaseDefinition
{
    [Tooltip("Stable domain key shared with the server/config. Distinct from the int ID.")]
    public string Key;

    public string DisplayName;
    [TextArea] public string Description;
    public Sprite Icon;
    public GameObject MapModel;
}
```

`ID` (int) is generated and persisted automatically the first time the asset is
enabled in the editor (`BaseDefinition.OnEnable` → `IDGenerator`). Treat it as
opaque and stable; use it for registry lookups. Do **not** hand-edit it. For a
human/wire-facing key, add your own `string Key` field (as above) — the int `ID` is
Unity-internal.

**Component type lookup (optional).** Override `GetComponentType()` to associate a
definition with a MonoBehaviour type; the registry then indexes it, enabling
`registry.GetDefinition(typeof(MyComponent))`.

## Registries

Subclass `BaseRegistry<TDefinition>`. Usually the body is empty — the base provides
the `definitions` list, `ID`/type lookups, and editor auto-refresh.

```csharp
using UnityEngine;
using ProxyCore;

[CreateAssetMenu(fileName = "PowerUpRegistry", menuName = "Registries/PowerUp Registry")]
public class PowerUpRegistry : BaseRegistry<PowerUpDefinition> { }
```

`BaseRegistry<T>` is a `SingletonSO`, so the **registry asset must live in a
`Resources/` folder** to resolve at runtime in a build. Create exactly one asset per
registry type. In the editor, with `autoRefresh` on (default), the `definitions`
list repopulates as assets are created/deleted/moved; you can also force it from the
registry inspector or **ProxyCore ▸ Refresh All Registries**.

Base API worth knowing:

```csharp
IReadOnlyList<T> GetAllDefinitions();
T GetDefinition(int id);
T GetDefinition(System.Type componentType);   // uses GetComponentType()
```

## Accessing a registry at runtime

The singleton is keyed by the **generic base type**, not your subclass (see the
cross-cutting rule in SKILL.md):

```csharp
var reg = BaseRegistry<PowerUpDefinition>.Instance;   // ✅
var def = reg.GetDefinition(someId);
// PowerUpRegistry.Instance                            // ✗ does not resolve the asset
```

## Catalog wrappers (key → definition)

`BaseRegistry` looks up by `ID`/type. When you need lookup by a **domain string**
(e.g. a server-sent key), add a small static catalog that builds a
`Dictionary<string, TDefinition>` once from the registry. This is the idiom used in
the sample projects:

```csharp
using System.Collections.Generic;
using ProxyCore;
using UnityEngine;

public static class PowerUpCatalog
{
    private static Dictionary<string, PowerUpDefinition> _byKey;

    public static PowerUpDefinition Definition(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        EnsureMap();
        _byKey.TryGetValue(key, out var def);
        return def;
    }

    private static void EnsureMap()
    {
        if (_byKey != null) return;
        _byKey = new Dictionary<string, PowerUpDefinition>();

        var registry = BaseRegistry<PowerUpDefinition>.Instance;
        if (registry == null)
        {
            Debug.LogError("[PowerUpCatalog] PowerUpRegistry not found in Resources.");
            return;
        }

        foreach (var def in registry.GetAllDefinitions())
        {
            if (def == null || string.IsNullOrEmpty(def.Key)) continue;
            if (!_byKey.ContainsKey(def.Key)) _byKey[def.Key] = def;
            else Debug.LogWarning($"[PowerUpCatalog] Duplicate Key '{def.Key}'.");
        }
    }
}
```

Note: `IUnlockableCatalog` (implemented by every `BaseRegistry<T>`) is a **different**
concept — it exposes definitions to the `UnlockManager` for auto-unlock evaluation,
not a key→definition lookup. See `references/unlockables.md`.

## Refreshing the definition list

- **Auto** — with `autoRefresh` on, editor asset changes repopulate the list.
- **Manual** — the registry inspector's Refresh button, or **ProxyCore ▸ Refresh All
  Registries**.
- `RefreshDefinitions()` finds assets via `t:{AssetTypeName}` (defaults to the
  definition type name). Override the protected `AssetTypeName` only if you search by
  a different asset type name.

## Common mistakes

- Registry/definition assets outside a `Resources/` folder → `Instance` is null in a
  build.
- Accessing `ConcreteRegistry.Instance` instead of `BaseRegistry<TDef>.Instance`.
- Hand-editing or hard-coding a definition's int `ID` — it's generated; key your own
  wire/domain values off a separate `string Key` field.
- Expecting `GetDefinition(int)` to work before the registry's `definitions` list is
  populated (create the registry asset and refresh).
