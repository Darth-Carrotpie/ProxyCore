# ProxyCore — Scene View Toolbar Button Strip

> **Audience**: LLM coding agents working on projects that consume ProxyCore as a Unity package.

## Overview

ProxyCore provides a **Scene View toolbar overlay** (`"ProxyCore Tools"`) that appears as a
horizontal button strip docked to the top of every Scene View. Downstream (child) projects can
**add their own buttons** to this strip so all editor shortcuts live in one place.

The system has three parts:

| Component | File | Purpose |
|---|---|---|
| `EditorToolbarButton` subclasses | `ProxyCoreToolbarShortcuts.cs` | Individual buttons |
| `ProxyCoreToolsOverlay` | `ProxyCoreToolbarShortcuts.cs` | Groups buttons into the strip |
| `ProxyCoreToolbarRegistry` | `ProxyCoreToolbarRegistry.cs` | Static registry for button IDs |

All types live in the `ProxyCore.Editor` namespace / assembly.

---

## How to add a button from a downstream project

### Prerequisites

- The downstream editor assembly must reference **`ProxyCore.Editor`**.
  If it uses an `.asmdef`, add `"ProxyCore.Editor"` to its `references` array.
  If it compiles into the default `Assembly-CSharp-Editor` (no `.asmdef`), the reference
  is automatic because `ProxyCore.Editor` has `autoReferenced: true`.

### Step-by-step

#### 1. Define the button class

Create a new `EditorToolbarButton` subclass with a **globally unique** `[EditorToolbarElement]` ID.
Use the pattern `"YourProject/ButtonName"`.

```csharp
using UnityEditor;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

[EditorToolbarElement(ID)]
sealed class MyCustomToolbarButton : EditorToolbarButton
{
    public const string ID = "MyProject/MyCustomButton";

    public MyCustomToolbarButton()
    {
        tooltip = "My Custom Window";
        clicked += MyCustomWindow.ShowWindow;           // must be static void
        RegisterCallback<AttachToPanelEvent>(OnAttach);
    }

    void OnAttach(AttachToPanelEvent _)
    {
        // Use any built-in Unity icon name. Browse them via the Icon Selector
        // or https://github.com/halak/unity-editor-icons
        var content = EditorGUIUtility.IconContent("d_ScriptableObject Icon");
        if (content != null) icon = content.image as Texture2D;
    }
}
```

#### 2. Register the button at domain reload

Use `[InitializeOnLoad]` to register the button ID **before** Unity constructs the overlay.
This is critical — registration must happen in a static constructor, not in `delayCall`.

```csharp
using UnityEditor;
using ProxyCore.Editor;

[InitializeOnLoad]
static class MyProjectToolbarRegistration
{
    static MyProjectToolbarRegistration()
    {
        ProxyCoreToolbarRegistry.RegisterButton(MyCustomToolbarButton.ID);
    }
}
```

That's it. The button will appear in the **ProxyCore Tools** strip on the next domain reload.

#### 3. (Optional) Auto-enable the overlay for first-time users

If the overlay might not be visible yet (first import), add a bootstrap class.
Follow this pattern with **project-scoped** `EditorPrefs` and retry logic:

```csharp
using UnityEditor;
using UnityEditor.Overlays;

[InitializeOnLoad]
static class MyProjectOverlayBootstrap
{
    static readonly string PrefKey =
        $"MyProjectOverlay_AutoEnabled_v1_{Application.dataPath.GetHashCode():X8}";
    static int _retries;

    static MyProjectOverlayBootstrap()
    {
        if (EditorPrefs.GetBool(PrefKey, false)) return;
        EditorApplication.delayCall += EnableOverlay;
    }

    static void EnableOverlay()
    {
        bool any = false;
        foreach (var sv in SceneView.sceneViews)
        {
            if (sv is SceneView view &&
                view.TryGetOverlay("proxycore-editor-tools", out var overlay))
            {
                overlay.displayed = true;
                any = true;
            }
        }

        if (any)
            EditorPrefs.SetBool(PrefKey, true);
        else if (++_retries < 10)
            EditorApplication.delayCall += EnableOverlay;   // retry
    }
}
```

---

## Creating a separate overlay strip (instead of extending ProxyCore's)

If the downstream project prefers its **own independent** overlay strip rather than appending to
ProxyCore's, define a standalone `ToolbarOverlay` with a different overlay ID:

```csharp
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;

[Overlay(typeof(SceneView), "myproject-editor-tools", "My Project Tools",
    defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top)]
sealed class MyProjectToolsOverlay : ToolbarOverlay
{
    MyProjectToolsOverlay() : base(
        MyCustomToolbarButton.ID,
        AnotherToolbarButton.ID
    ) { }
}
```

Both overlays will appear side-by-side in the Scene View toolbar. Each can be toggled
independently via the Scene View's overlay menu (⋮ → Overlays).

> **Important**: Do NOT reuse `"proxycore-editor-tools"` as the overlay ID —
> Unity allows only one `[Overlay]` class per ID. Using the same ID will cause one to
> silently replace the other.

---

## Architecture notes for agents

- **Registration order**: `[InitializeOnLoad]` static constructors run in undefined order
  across assemblies, but all run before Unity constructs UI elements. This means
  `ProxyCoreToolbarRegistry.RegisterButton(...)` calls are guaranteed to complete before
  `ProxyCoreToolsOverlay`'s constructor reads the registry.
- **Namespace**: ProxyCore editor types are in `namespace ProxyCore.Editor`.
- **Assembly**: `ProxyCore.Editor` (defined by `ProxyCore.Editor.asmdef`), editor-only,
  `autoReferenced: true`.
- **Overlay ID**: `"proxycore-editor-tools"` — used in both the `[Overlay]` attribute and
  bootstrap `TryGetOverlay` calls.
- **EditorPrefs pitfall**: `EditorPrefs` is machine-wide, not project-scoped. Always include
  a project-specific discriminator (e.g., `Application.dataPath.GetHashCode()`) in pref keys
  to avoid cross-project contamination.
- **Icon reference**: The `EditorGUIUtility.IconContent(name)` call can return `null` if the
  icon doesn't exist in the current Unity version. Always null-check before assigning.

---

## File locations (ProxyCore repo)

| File | Path |
|---|---|
| Toolbar buttons + overlay | `Assets/ProxyCore/Editor/Editor Windows/ProxyCoreToolbarShortcuts.cs` |
| Button registry | `Assets/ProxyCore/Editor/Editor Windows/ProxyCoreToolbarRegistry.cs` |
| Editor asmdef | `Assets/ProxyCore/Editor/ProxyCore.Editor.asmdef` |
| This documentation | `Assets/ProxyCore/Documentation/ButtonStripUsage.md` |