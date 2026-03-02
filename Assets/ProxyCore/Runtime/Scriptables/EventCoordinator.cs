using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace ProxyCore
{
    /// <summary>
    /// Central coordinator for the event message system.
    /// Manages event registration, triggering, and validation.
    /// Inherits from BaseRegistry to auto-discover EventMessage assets.
    /// </summary>
    [CreateAssetMenu(fileName = "EventCoordinator", menuName = "Registries/Event Coordinator")]
    public class EventCoordinator : BaseRegistry<EventMessage>
    {

        [Header("Debug Settings")]
        [Tooltip("Enable debug logging for triggered events")]
        public bool enableDebugging;

        [Tooltip("Show attached event chains in debug logs")]
        public bool showAttachedEvents;

        // New system: int-keyed dictionaries for faster lookup
        private Dictionary<int, List<Action<EventMessageData>>> _eventListeners;
        private Dictionary<int, List<Action<EventMessageData>>> _attachmentListeners;

        protected override void OnInit()
        {
            base.OnInit();
            InitializeDictionaries();
        }

        protected override void OnAwake()
        {
            base.OnAwake();
            InitializeDictionaries();
        }

        private void InitializeDictionaries()
        {
            if (_eventListeners == null)
            {
                _eventListeners = new Dictionary<int, List<Action<EventMessageData>>>();
            }
            if (_attachmentListeners == null)
            {
                _attachmentListeners = new Dictionary<int, List<Action<EventMessageData>>>();
            }
        }

        #region New API - EventMessage based

        /// <summary>
        /// Registers a listener for the specified event.
        /// </summary>
        public static void StartListening(EventMessage eventMessage, Action<EventMessageData> listener)
        {
            if (eventMessage == null)
            {
                Debug.LogError("Cannot start listening to null EventMessage");
                return;
            }

            var inst = Instance as EventCoordinator;
            if (inst == null) return;

            inst.InitializeDictionaries();

            int id = eventMessage.ID;
            if (!inst._eventListeners.TryGetValue(id, out var listeners))
            {
                listeners = new List<Action<EventMessageData>>();
                inst._eventListeners[id] = listeners;
            }

            if (!listeners.Contains(listener))
            {
                listeners.Add(listener);
            }
        }

        /// <summary>
        /// Removes a listener from the specified event.
        /// </summary>
        public static void StopListening(EventMessage eventMessage, Action<EventMessageData> listener)
        {
            var inst = Instance as EventCoordinator;
            if (inst == null || eventMessage == null) return;

            inst.InitializeDictionaries();

            int id = eventMessage.ID;
            if (inst._eventListeners.TryGetValue(id, out var listeners))
            {
                listeners.Remove(listener);
            }
        }

        /// <summary>
        /// Attaches a listener that will be called after the main event listeners.
        /// Used for event chaining.
        /// </summary>
        public static void Attach(EventMessage eventMessage, Action<EventMessageData> listener)
        {
            if (eventMessage == null)
            {
                Debug.LogError("Cannot attach to null EventMessage");
                return;
            }

            var inst = Instance as EventCoordinator;
            if (inst == null) return;

            inst.InitializeDictionaries();

            int id = eventMessage.ID;
            if (!inst._attachmentListeners.TryGetValue(id, out var listeners))
            {
                listeners = new List<Action<EventMessageData>>();
                inst._attachmentListeners[id] = listeners;
            }

            if (!listeners.Contains(listener))
            {
                listeners.Add(listener);
            }
        }

        /// <summary>
        /// Removes an attached listener.
        /// </summary>
        public static void Detach(EventMessage eventMessage, Action<EventMessageData> listener)
        {
            var inst = Instance as EventCoordinator;
            if (inst == null || eventMessage == null) return;

            inst.InitializeDictionaries();

            int id = eventMessage.ID;
            if (inst._attachmentListeners.TryGetValue(id, out var listeners))
            {
                listeners.Remove(listener);
            }
        }

        /// <summary>
        /// Internal method to trigger an event. Called by EventTriggerBuilder.
        /// Validates payloads, invokes listeners, and auto-releases data.
        /// </summary>
        public static void TriggerEventInternal(EventMessage eventMessage, EventMessageData data)
        {
            var inst = Instance as EventCoordinator;
            if (inst == null || eventMessage == null)
            {
                data?.Release();
                return;
            }

            inst.InitializeDictionaries();

            int id = eventMessage.ID;

            // Payload validation
            if (!eventMessage.skipPayloadValidation)
            {
                ValidatePayloads(eventMessage, data);
            }

            // Debug logging
            if (inst.enableDebugging && !eventMessage.muteDebugLog)
            {
                Debug.Log($"[Event] {eventMessage.GetFullPath()}: {data}");
            }

            // Invoke main listeners
            if (inst._eventListeners.TryGetValue(id, out var listeners))
            {
                foreach (var listener in listeners.ToList())
                { // ToList() to allow modification during iteration
                    try
                    {
                        listener?.Invoke(data);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[EventCoordinator] Error invoking listener for {eventMessage.GetDisplayName()}: {ex}");
                    }
                }
            }

            // Invoke attachment listeners
            if (inst._attachmentListeners.TryGetValue(id, out var attachments))
            {
                if (inst.showAttachedEvents && inst.enableDebugging && !eventMessage.muteDebugLog)
                {
                    Debug.Log($"[Event Attachment] {eventMessage.GetFullPath()}: {data}");
                }

                foreach (var listener in attachments.ToList())
                {
                    try
                    {
                        listener?.Invoke(data);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[EventCoordinator] Error invoking attachment for {eventMessage.GetDisplayName()}: {ex}");
                    }
                }
            }

            // Auto-release data back to pool
            data?.Release();
        }

        /// <summary>
        /// Validates that the provided data contains all expected payloads.
        /// </summary>
        private static void ValidatePayloads(EventMessage eventMessage, EventMessageData data)
        {
            if (eventMessage.expectedPayloads == null || eventMessage.expectedPayloads.Count == 0)
                return;

            var expectedTypes = new HashSet<Type>();
            foreach (var payload in eventMessage.expectedPayloads)
            {
                if (payload != null)
                {
                    expectedTypes.Add(payload.GetType());
                }
            }

            var actualTypes = new HashSet<Type>(data.GetPayloadTypes());

            var missing = expectedTypes.Except(actualTypes).ToList();
            var extra = actualTypes.Except(expectedTypes).ToList();

            if (missing.Count > 0 || extra.Count > 0)
            {
                var sb = new StringBuilder();
                sb.AppendLine($"[EventMessage: {eventMessage.GetDisplayName()}] Payload mismatch!");
                sb.AppendLine($"  Expected: {string.Join(", ", expectedTypes.Select(t => t.Name))}");
                sb.AppendLine($"  Actual: {string.Join(", ", actualTypes.Select(t => t.Name))}");

                if (missing.Count > 0)
                {
                    sb.AppendLine($"  Missing: {string.Join(", ", missing.Select(t => t.Name))}");
                }
                if (extra.Count > 0)
                {
                    sb.AppendLine($"  Extra: {string.Join(", ", extra.Select(t => t.Name))}");
                }

                Debug.LogWarning(sb.ToString());
            }
        }

        /// <summary>
        /// Checks if any listeners are registered for the specified event.
        /// </summary>
        public static bool HasListeners(EventMessage eventMessage)
        {
            var inst = Instance as EventCoordinator;
            if (inst == null || eventMessage == null) return false;

            int id = eventMessage.ID;
            return inst._eventListeners.TryGetValue(id, out var listeners) && listeners.Count > 0;
        }

        /// <summary>
        /// Called when scene is reloaded and _persistent is false.
        /// Clears all event subscriptions and attachment listeners to force fresh registrations.
        /// </summary>
        protected override void OnSceneReload()
        {
            base.OnSceneReload();

            // Clear all event listeners
            if (_eventListeners != null)
            {
                _eventListeners.Clear();
            }

            // Clear all attachment listeners
            if (_attachmentListeners != null)
            {
                _attachmentListeners.Clear();
            }

#if UNITY_EDITOR
            Debug.Log($"EventCoordinator cleared subscriptions on scene reload (persistent={_persistent})");
#endif
        }

        #endregion

        #region Editor Helpers

#if UNITY_EDITOR
        /// <summary>
        /// Gets all muted events for editor display.
        /// </summary>
        public List<EventMessage> GetMutedEvents() {
            return definitions.Where(e => e != null && e.muteDebugLog).ToList();
        }

        /// <summary>
        /// Gets all unmuted events for editor display.
        /// </summary>
        public List<EventMessage> GetUnmutedEvents() {
            return definitions.Where(e => e != null && !e.muteDebugLog).ToList();
        }

        /// <summary>
        /// Formats an event for debug display: Category>Category>DisplayName (ShortName), FileName.asset
        /// </summary>
        public static string FormatEventForDisplay(EventMessage evt) {
            if (evt == null) return "(null)";

            string effectiveName = evt.GetDisplayName();
            string catPath = evt.GetCategoryPath();
            string displayPath = string.IsNullOrEmpty(catPath)
                ? effectiveName
                : $"{catPath.Replace(".", ">")}>{effectiveName}";

            // Show shortName in parens only when displayName is overridden (to reveal the codegen name)
            string shortNamePart = !string.IsNullOrEmpty(evt.displayName) && !string.IsNullOrEmpty(evt.shortName)
                ? $" ({evt.shortName})" : "";
            string fileName = evt.name + ".asset";

            return $"{displayPath}{shortNamePart}, {fileName}";
        }
#endif

        #endregion
    }
}
