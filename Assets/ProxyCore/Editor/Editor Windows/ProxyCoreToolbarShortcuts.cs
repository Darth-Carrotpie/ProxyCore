using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.UIElements;

namespace ProxyCore.Editor
{
    // ── SceneView Toolbar Shortcuts ───────────────────────────────────────────────
    // Buttons appear as a grouped strip in the Scene View top toolbar.
    // To add a new button from a downstream project:
    //   1. Create a new EditorToolbarButton subclass with a unique ID.
    //   2. Call ProxyCoreToolbarRegistry.RegisterButton(id) from [InitializeOnLoad].
    // See Documentation/ButtonStripUsage.md for full instructions.

    [EditorToolbarElement(ID)]
    sealed class EventDebugMonitorToolbarButton : EditorToolbarButton
    {
        public const string ID = "ProxyCore/EventDebugMonitorButton";

        public EventDebugMonitorToolbarButton()
        {
            tooltip = "Event Debug Monitor";
            clicked += EventDebugMonitorWindow.ShowWindow;
            RegisterCallback<AttachToPanelEvent>(OnAttach);
        }

        void OnAttach(AttachToPanelEvent _)
        {
            var content = EditorGUIUtility.IconContent("d_UnityEditor.AnimationWindow");
            if (content != null) icon = content.image as Texture2D;
        }
    }

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

    [EditorToolbarElement(ID)]
    sealed class UnlockDebugWindowToolbarButton : EditorToolbarButton
    {
        public const string ID = "ProxyCore/UnlockDebugWindowButton";

        public UnlockDebugWindowToolbarButton()
        {
            tooltip = "Unlock Debug Window";
            clicked += UnlockDebugWindow.ShowWindow;
            RegisterCallback<AttachToPanelEvent>(OnAttach);
        }

        void OnAttach(AttachToPanelEvent _)
        {
            var content = EditorGUIUtility.IconContent("d_P4_LockedLocal");
            if (content != null) icon = content.image as Texture2D;
        }
    }

    [EditorToolbarElement(ID)]
    sealed class UnlockGraphToolbarButton : EditorToolbarButton
    {
        public const string ID = "ProxyCore/UnlockGraphButton";

        public UnlockGraphToolbarButton()
        {
            tooltip = "Unlock Dependency Graph";
            // Callback set in OnAttach to defer until the Graph assembly is loaded
            RegisterCallback<AttachToPanelEvent>(OnAttach);
        }

        void OnAttach(AttachToPanelEvent _)
        {
            var content = EditorGUIUtility.IconContent("d_SceneViewFx");
            if (content != null) icon = content.image as Texture2D;

            clicked -= OnClicked;
            clicked += OnClicked;
        }

        static void OnClicked()
        {
            EditorApplication.ExecuteMenuItem("ProxyCore/Unlock Dependency Graph");
        }
    }

    // ── Register built-in buttons ─────────────────────────────────────────────────
    // Runs at domain reload to seed the registry with ProxyCore's own buttons
    // before any downstream [InitializeOnLoad] classes register theirs.

    [InitializeOnLoad]
    static class ProxyCoreToolbarButtonRegistration
    {
        static ProxyCoreToolbarButtonRegistration()
        {
            ProxyCoreToolbarRegistry.RegisterButton(EventManagerToolbarButton.ID);
            ProxyCoreToolbarRegistry.RegisterButton(EventDebugMonitorToolbarButton.ID);
            ProxyCoreToolbarRegistry.RegisterButton(UnlockDebugWindowToolbarButton.ID);
            ProxyCoreToolbarRegistry.RegisterButton(UnlockGraphToolbarButton.ID);
        }
    }

    // ── Overlay ───────────────────────────────────────────────────────────────────
    // Groups the buttons into a single draggable strip at the top of the Scene View.
    // Reads button IDs from ProxyCoreToolbarRegistry to include external buttons.

    [Overlay(typeof(SceneView), "proxycore-editor-tools", "ProxyCore Tools",
        defaultDockZone = DockZone.TopToolbar, defaultDockPosition = DockPosition.Top)]
    sealed class ProxyCoreToolsOverlay : ToolbarOverlay
    {
        ProxyCoreToolsOverlay() : base(ProxyCoreToolbarRegistry.GetAllButtonIds()) { }
    }

    // ── Auto-enable overlay for new users ─────────────────────────────────────────
    // On first domain reload (fresh clone / first pull), enables the overlay in all
    // open SceneViews so team members see it immediately without manual toggling.
    // Uses a project-scoped EditorPrefs key to run only once per project per machine.

    [InitializeOnLoad]
    static class ProxyCoreToolsOverlayBootstrap
    {
        static readonly string PrefKey =
            $"ProxyCoreToolsOverlay_AutoEnabled_v1_{Application.dataPath.GetHashCode():X8}";

        static int s_Retries;

        static ProxyCoreToolsOverlayBootstrap()
        {
            if (EditorPrefs.GetBool(PrefKey, false))
                return;

            // Defer until the editor is fully loaded and SceneViews exist.
            EditorApplication.delayCall += EnableOverlay;
        }

        static void EnableOverlay()
        {
            bool anyEnabled = false;

            foreach (var sceneView in SceneView.sceneViews)
            {
                if (sceneView is SceneView sv &&
                    sv.TryGetOverlay("proxycore-editor-tools", out var overlay))
                {
                    overlay.displayed = true;
                    anyEnabled = true;
                }
            }

            if (anyEnabled)
            {
                EditorPrefs.SetBool(PrefKey, true);
            }
            else if (++s_Retries < 10)
            {
                // No SceneViews yet — retry on next editor tick.
                EditorApplication.delayCall += EnableOverlay;
            }
        }
    }
}
