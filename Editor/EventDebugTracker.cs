#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Record of a single event trigger, stored in the history ring buffer.
    /// All data is captured as strings/value types so we don't hold references to pooled objects.
    /// </summary>
    public class EventTriggerRecord
    {
        public EventMessage EventMessage;
        public int EventId;
        public double Timestamp;          // EditorApplication.timeSinceStartup
        public int FrameCount;
        public string PayloadSnapshot;    // data.ToString() — full summary
        public List<PayloadDetail> PayloadDetails = new List<PayloadDetail>();
        public int ListenerCount;
        public string CallerStackTrace;   // null unless stack capture was enabled
    }

    /// <summary>
    /// Detail for a single payload within a trigger record.
    /// </summary>
    public class PayloadDetail
    {
        public string TypeName;
        public string DebugString;
    }

    /// <summary>
    /// Aggregated stats for a single event, updated on each trigger.
    /// </summary>
    public class EventStats
    {
        public int TriggerCount;
        public double LastTriggerTime;
        public string LastPayloadSnapshot;
        public List<PayloadDetail> LastPayloadDetails = new List<PayloadDetail>();
        public List<string> RecentCallers = new List<string>(); // deduplicated, max 10
    }

    /// <summary>
    /// Static editor-only tracker that hooks into EventCoordinator when capture is active.
    /// Maintains a ring buffer of trigger records and per-event aggregate stats.
    /// Zero overhead when capture is off (coordinator callbacks are null).
    /// </summary>
    [InitializeOnLoad]
    public static class EventDebugTracker
    {
        // ── Capture state ──────────────────────────────────────────────

        public static bool IsCapturing { get; private set; }
        public static bool CaptureStackTraces { get; private set; }

        // ── Data ───────────────────────────────────────────────────────

        private static List<EventTriggerRecord> _history = new List<EventTriggerRecord>();
        private static int _maxHistorySize = 512;
        private static Dictionary<int, EventStats> _stats = new Dictionary<int, EventStats>();

        /// <summary>Read-only view of the trigger history (oldest first).</summary>
        public static IReadOnlyList<EventTriggerRecord> History => _history;

        /// <summary>Read-only view of per-event stats.</summary>
        public static IReadOnlyDictionary<int, EventStats> Stats => _stats;

        // ── Events for the UI ──────────────────────────────────────────

        /// <summary>Fired each time a new trigger record is added.</summary>
        public static event Action<EventTriggerRecord> OnRecordAdded;

        /// <summary>Fired when listener set changes (for live subscriber refresh).</summary>
        public static event Action OnListenersChanged;

        // ── Lifecycle ──────────────────────────────────────────────────

        static EventDebugTracker()
        {
            // Ensure capture is off after domain reload (stale delegates would be invalid)
            IsCapturing = false;
            EventCoordinator.OnEventTriggered = null;
            EventCoordinator.OnListenerChanged = null;

            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private static void OnPlayModeChanged(PlayModeStateChange change)
        {
            // Auto-stop capture when exiting Play Mode so we don't leave stale hooks
            if (change == PlayModeStateChange.ExitingPlayMode ||
                change == PlayModeStateChange.EnteredEditMode)
            {
                if (IsCapturing)
                {
                    StopCapture();
                }
            }
        }

        // ── Public API ─────────────────────────────────────────────────

        /// <summary>
        /// Begins capturing event triggers and listener changes.
        /// </summary>
        public static void StartCapture(int maxHistory = 512, bool captureStackTraces = false)
        {
            if (IsCapturing) return;

            _maxHistorySize = Mathf.Clamp(maxHistory, 16, 8192);
            CaptureStackTraces = captureStackTraces;
            IsCapturing = true;

            EventCoordinator.OnEventTriggered = HandleEventTriggered;
            EventCoordinator.OnListenerChanged = HandleListenerChanged;
        }

        /// <summary>
        /// Stops capturing. Accumulated data is preserved until ClearData() is called.
        /// </summary>
        public static void StopCapture()
        {
            IsCapturing = false;
            EventCoordinator.OnEventTriggered = null;
            EventCoordinator.OnListenerChanged = null;
        }

        /// <summary>
        /// Clears all accumulated history and stats.
        /// </summary>
        public static void ClearData()
        {
            _history.Clear();
            _stats.Clear();
        }

        /// <summary>
        /// Updates the maximum history size. If the current buffer is larger, it trims from the front.
        /// </summary>
        public static void SetMaxHistorySize(int size)
        {
            _maxHistorySize = Mathf.Clamp(size, 16, 8192);
            TrimHistory();
        }

        /// <summary>
        /// Gets stats for a specific event. Returns null if no data recorded.
        /// </summary>
        public static EventStats GetStats(EventMessage evt)
        {
            if (evt == null) return null;
            _stats.TryGetValue(evt.ID, out var s);
            return s;
        }

        // ── Internal handlers ──────────────────────────────────────────

        private static void HandleEventTriggered(EventMessage eventMessage, EventMessageData data)
        {
            if (eventMessage == null) return;

            double now = EditorApplication.timeSinceStartup;
            int frame = Time.frameCount;

            // Build payload details snapshot (data is still valid, not yet released)
            var details = new List<PayloadDetail>();
            string snapshot = "(empty)";
            if (data != null)
            {
                snapshot = data.ToString();
                foreach (var payload in data.GetAllPayloads())
                {
                    if (payload == null) continue;
                    details.Add(new PayloadDetail
                    {
                        TypeName = payload.GetType().Name,
                        DebugString = payload.ToDebugString()
                    });
                }
            }

            int listenerCount = EventCoordinator.GetListenerCount(eventMessage);

            // Stack trace capture (expensive — only when user opted in)
            string stackTrace = null;
            if (CaptureStackTraces)
            {
                try
                {
                    // Skip 3 frames: HandleEventTriggered → OnEventTriggered?.Invoke → TriggerEventInternal
                    var st = new StackTrace(3, true);
                    stackTrace = FormatStackTrace(st);
                }
                catch
                {
                    stackTrace = "(stack capture failed)";
                }
            }

            // Create record
            var record = new EventTriggerRecord
            {
                EventMessage = eventMessage,
                EventId = eventMessage.ID,
                Timestamp = now,
                FrameCount = frame,
                PayloadSnapshot = snapshot,
                PayloadDetails = details,
                ListenerCount = listenerCount,
                CallerStackTrace = stackTrace
            };

            _history.Add(record);
            TrimHistory();

            // Update per-event stats
            if (!_stats.TryGetValue(eventMessage.ID, out var stats))
            {
                stats = new EventStats();
                _stats[eventMessage.ID] = stats;
            }

            stats.TriggerCount++;
            stats.LastTriggerTime = now;
            stats.LastPayloadSnapshot = snapshot;
            stats.LastPayloadDetails = details;

            // Store unique callers (from stack trace)
            if (stackTrace != null)
            {
                string caller = ExtractTopCaller(stackTrace);
                if (!string.IsNullOrEmpty(caller) && !stats.RecentCallers.Contains(caller))
                {
                    stats.RecentCallers.Add(caller);
                    if (stats.RecentCallers.Count > 10)
                        stats.RecentCallers.RemoveAt(0);
                }
            }

            OnRecordAdded?.Invoke(record);
        }

        private static void HandleListenerChanged(EventMessage eventMessage, Delegate listener, bool added)
        {
            OnListenersChanged?.Invoke();
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static void TrimHistory()
        {
            while (_history.Count > _maxHistorySize)
            {
                _history.RemoveAt(0);
            }
        }

        /// <summary>
        /// Formats a StackTrace into a readable multi-line string, filtering out internal ProxyCore frames.
        /// </summary>
        private static string FormatStackTrace(StackTrace st)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < st.FrameCount; i++)
            {
                var frame = st.GetFrame(i);
                if (frame == null) continue;

                var method = frame.GetMethod();
                if (method == null) continue;

                string declaringType = method.DeclaringType?.FullName ?? "(unknown)";

                // Skip internal ProxyCore plumbing frames
                if (declaringType.StartsWith("ProxyCore.EventCoordinator") ||
                    declaringType.StartsWith("ProxyCore.EventTriggerBuilder") ||
                    declaringType.StartsWith("ProxyCore.Editor.EventDebugTracker"))
                    continue;

                string fileName = frame.GetFileName();
                int lineNumber = frame.GetFileLineNumber();

                if (!string.IsNullOrEmpty(fileName))
                {
                    // Normalize path separators and make relative to Assets if possible
                    fileName = fileName.Replace('\\', '/');
                    int assetsIdx = fileName.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
                    if (assetsIdx >= 0)
                        fileName = fileName.Substring(assetsIdx);

                    sb.AppendLine($"{declaringType}.{method.Name}() — {fileName}:{lineNumber}");
                }
                else
                {
                    sb.AppendLine($"{declaringType}.{method.Name}()");
                }
            }
            return sb.Length > 0 ? sb.ToString().TrimEnd() : "(no user frames)";
        }

        /// <summary>
        /// Extracts the top-most user frame from a formatted stack trace string.
        /// </summary>
        private static string ExtractTopCaller(string formattedStack)
        {
            if (string.IsNullOrEmpty(formattedStack)) return null;
            int newline = formattedStack.IndexOf('\n');
            return newline > 0 ? formattedStack.Substring(0, newline).Trim() : formattedStack.Trim();
        }
    }
}
#endif
