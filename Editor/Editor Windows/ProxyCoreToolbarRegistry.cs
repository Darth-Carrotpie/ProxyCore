using System.Collections.Generic;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Static registry of toolbar button IDs for the ProxyCore Scene View overlay.
    /// Downstream projects call <see cref="RegisterButton"/> from an
    /// <c>[InitializeOnLoad]</c> static constructor to inject their own buttons
    /// into the <c>ProxyCore Tools</c> strip.
    /// </summary>
    public static class ProxyCoreToolbarRegistry
    {
        static readonly List<string> s_ButtonIds = new();

        /// <summary>
        /// Register an <see cref="UnityEditor.Toolbars.EditorToolbarButton"/> ID
        /// so it appears in the ProxyCore toolbar overlay.
        /// Must be called during domain reload (i.e. from an
        /// <c>[InitializeOnLoad]</c> static constructor) before Unity constructs
        /// the overlay UI.
        /// </summary>
        /// <param name="elementId">
        /// The <c>[EditorToolbarElement]</c> ID string, e.g.
        /// <c>"MyProject/MyButton"</c>.
        /// </param>
        public static void RegisterButton(string elementId)
        {
            if (!string.IsNullOrEmpty(elementId) && !s_ButtonIds.Contains(elementId))
                s_ButtonIds.Add(elementId);
        }

        /// <summary>
        /// Returns every registered button ID (ProxyCore built-ins + external).
        /// Called by <see cref="ProxyCoreToolsOverlay"/>'s constructor.
        /// </summary>
        public static string[] GetAllButtonIds() => s_ButtonIds.ToArray();
    }
}
