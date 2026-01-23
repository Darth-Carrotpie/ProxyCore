using UnityEngine;
using UnityEditor;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Custom editor for EventCoordinator.
    /// Provides buttons to print muted/unmuted events for debugging.
    /// </summary>
    [CustomEditor(typeof(EventCoordinator))]
    public class EventCoordinatorEditor : UnityEditor.Editor
    {

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var coordinator = (EventCoordinator)target;

            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("Debug Tools", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("Print Muted Events", GUILayout.Height(30)))
            {
                PrintMutedEvents(coordinator);
            }

            if (GUILayout.Button("Print Unmuted Events", GUILayout.Height(30)))
            {
                PrintUnmutedEvents(coordinator);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Print All Events", GUILayout.Height(25)))
            {
                PrintAllEvents(coordinator);
            }
        }

        private void PrintMutedEvents(EventCoordinator coordinator)
        {
            var mutedEvents = coordinator.GetMutedEvents();

            Debug.Log("=== MUTED EVENTS ===");

            if (mutedEvents.Count == 0)
            {
                Debug.Log("No muted events found.");
                return;
            }

            foreach (var evt in mutedEvents)
            {
                string display = EventCoordinator.FormatEventForDisplay(evt);
                Debug.Log($"[MUTED] {display}");
            }

            Debug.Log($"Total: {mutedEvents.Count} muted events");
        }

        private void PrintUnmutedEvents(EventCoordinator coordinator)
        {
            var unmutedEvents = coordinator.GetUnmutedEvents();

            Debug.Log("=== LOGGING EVENTS ===");

            if (unmutedEvents.Count == 0)
            {
                Debug.Log("No logging events found.");
                return;
            }

            foreach (var evt in unmutedEvents)
            {
                string display = EventCoordinator.FormatEventForDisplay(evt);
                Debug.Log($"[LOGGING] {display}");
            }

            Debug.Log($"Total: {unmutedEvents.Count} logging events");
        }

        private void PrintAllEvents(EventCoordinator coordinator)
        {
            var allEvents = coordinator.GetAllDefinitions();

            Debug.Log("=== ALL EVENTS ===");

            if (allEvents.Count == 0)
            {
                Debug.Log("No events found. Try refreshing the registry.");
                return;
            }

            foreach (var evt in allEvents)
            {
                if (evt == null) continue;

                string status = evt.muteDebugLog ? "[MUTED]" : "[LOGGING]";
                string display = EventCoordinator.FormatEventForDisplay(evt);
                Debug.Log($"{status} {display}");
            }

            Debug.Log($"Total: {allEvents.Count} events");
        }
    }
}
