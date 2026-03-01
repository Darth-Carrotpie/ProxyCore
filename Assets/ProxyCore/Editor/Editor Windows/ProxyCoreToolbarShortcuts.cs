using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor
{
    // ── SceneView Toolbar Shortcuts ───────────────────────────────────────────────
    // Buttons appear as a grouped strip in the Scene View top toolbar.
    // To add a new window shortcut:
    //   1. Create a new EditorToolbarButton subclass with a unique ID.
    //   2. Append the ID to ProxyCoreToolsOverlay's base() constructor.

    [EditorToolbarElement(ID)]
    sealed class EventManagerToolbarButton : EditorToolbarButton
    {
        public const string ID = "ProxyCore/EventManagerButton";

        public EventManagerToolbarButton()
        {
            tooltip = "Event Manager";
            clicked += EventManagerWindow.ShowWindow;
            RegisterCallback<AttachToPanelEvent>(OnAttach);
        }

        void OnAttach(AttachToPanelEvent _)
        {
            var content = EditorGUIUtility.IconContent("d_Animation.EventMarker");
            if (content != null) icon = content.image as Texture2D;
        }
    }

    // ── Overlay ───────────────────────────────────────────────────────────────────
    // Groups the buttons into a single draggable strip at the top of the Scene View.

    [Overlay(typeof(SceneView), "proxycore-editor-tools", "ProxyCore Tools",
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top)]
    sealed class ProxyCoreToolsOverlay : ToolbarOverlay
    {
        ProxyCoreToolsOverlay() : base(
            EventManagerToolbarButton.ID
        )
        { }
    }

    // ── Auto-enable overlay for new users ─────────────────────────────────────────
    // On first domain reload (fresh clone / first pull), enables the overlay in all
    // open SceneViews so team members see it immediately without manual toggling.
    // Uses EditorPrefs to run only once per user machine.

    [InitializeOnLoad]
    static class ProxyCoreToolsOverlayBootstrap
    {
        const string PrefKey = "ProxyCoreToolsOverlay_AutoEnabled_v1";

        static ProxyCoreToolsOverlayBootstrap()
        {
            if (EditorPrefs.GetBool(PrefKey, false))
                return;

            // Defer until the editor is fully loaded and SceneViews exist.
            EditorApplication.delayCall += EnableOverlayOnce;
        }

        static void EnableOverlayOnce()
        {
            foreach (var sceneView in SceneView.sceneViews)
            {
                if (sceneView is SceneView sv && sv.TryGetOverlay("proxycore-editor-tools", out var overlay))
                {
                    overlay.displayed = true;
                }
            }

            EditorPrefs.SetBool(PrefKey, true);
        }
    }
}
