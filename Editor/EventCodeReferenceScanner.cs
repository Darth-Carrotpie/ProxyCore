#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// A single code reference found by the scanner.
    /// </summary>
    public class EventCodeReference
    {
        public string FilePath;      // Relative to project root, e.g. "Assets/Scripts/Foo.cs"
        public int LineNumber;       // 1-based
        public string LineContent;   // Trimmed content of the matched line
        public CodeReferenceType ReferenceType;
    }

    public enum CodeReferenceType
    {
        Trigger,        // TriggerEvent.Category.EventName
        Listener,       // ListenEvent.Category.EventName
        DirectTrigger,  // new EventTriggerBuilder(...)  (best-effort)
        DirectListener  // new EventListenBuilder(...)   (best-effort)
    }

    /// <summary>
    /// Aggregated code references for a single event.
    /// </summary>
    public class EventCodeReferences
    {
        public List<EventCodeReference> References = new List<EventCodeReference>();
    }

    /// <summary>
    /// Scans .cs source files for references to events via the generated TriggerEvent/ListenEvent
    /// static accessors. Results are cached and invalidated when .cs files change.
    /// </summary>
    public static class EventCodeReferenceScanner
    {
        private static Dictionary<int, EventCodeReferences> _cache;
        private static bool _cacheValid;

        /// <summary>
        /// Invalidate the cache. Called by the AssetPostprocessor when .cs files change.
        /// </summary>
        public static void InvalidateCache()
        {
            _cacheValid = false;
        }

        /// <summary>
        /// Returns cached results, or re-scans if the cache is invalid.
        /// </summary>
        public static Dictionary<int, EventCodeReferences> GetOrScan()
        {
            if (_cacheValid && _cache != null)
                return _cache;

            _cache = ScanAll();
            _cacheValid = true;
            return _cache;
        }

        /// <summary>
        /// Scans for a single event. Uses the full cache internally.
        /// </summary>
        public static EventCodeReferences GetReferences(EventMessage evt)
        {
            if (evt == null) return new EventCodeReferences();
            var all = GetOrScan();
            return all.TryGetValue(evt.ID, out var refs) ? refs : new EventCodeReferences();
        }

        /// <summary>
        /// Force a full rescan, ignoring cache.
        /// </summary>
        public static Dictionary<int, EventCodeReferences> Rescan()
        {
            _cache = ScanAll();
            _cacheValid = true;
            return _cache;
        }

        // ── Core scanning logic ────────────────────────────────────────

        private static Dictionary<int, EventCodeReferences> ScanAll()
        {
            var results = new Dictionary<int, EventCodeReferences>();

            // Gather all EventMessage assets
            string[] guids = AssetDatabase.FindAssets("t:EventMessage");
            var events = new List<EventMessage>();
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var evt = AssetDatabase.LoadAssetAtPath<EventMessage>(path);
                if (evt != null)
                    events.Add(evt);
            }

            if (events.Count == 0)
                return results;

            // Build regex patterns per event.
            // Each event can appear under multiple categories, so we build patterns for each category path.
            // Pattern format: TriggerEvent\s*\.\s*{Category}\s*\.\s*{EventCodeGenName}
            var eventPatterns = new List<(EventMessage evt, Regex triggerRegex, Regex listenRegex)>();

            foreach (var evt in events)
            {
                string codeGenName = evt.GetCodeGenName();
                if (string.IsNullOrEmpty(codeGenName)) continue;

                var categoryNames = new HashSet<string>();
                if (evt.categories != null)
                {
                    foreach (var cat in evt.categories)
                    {
                        if (cat == null) continue;
                        string catName = cat.GetCodeGenName();
                        if (!string.IsNullOrEmpty(catName))
                            categoryNames.Add(catName);
                    }
                }

                if (categoryNames.Count == 0)
                {
                    // Event with no categories won't appear in generated accessors,
                    // but we can still search for direct builder references
                    results[evt.ID] = new EventCodeReferences();
                    continue;
                }

                // Build alternation for all category paths: (Cat1|Cat2)
                string catAlternation = string.Join("|", categoryNames.Select(Regex.Escape));
                string triggerPattern = $@"TriggerEvent\s*\.\s*(?:{catAlternation})\s*\.\s*{Regex.Escape(codeGenName)}\b";
                string listenPattern = $@"ListenEvent\s*\.\s*(?:{catAlternation})\s*\.\s*{Regex.Escape(codeGenName)}\b";

                try
                {
                    var tRegex = new Regex(triggerPattern, RegexOptions.Compiled);
                    var lRegex = new Regex(listenPattern, RegexOptions.Compiled);
                    eventPatterns.Add((evt, tRegex, lRegex));
                }
                catch
                {
                    // Malformed regex from unusual names — skip this event
                }

                if (!results.ContainsKey(evt.ID))
                    results[evt.ID] = new EventCodeReferences();
            }

            // Also build generic patterns for direct builder usage
            Regex directTriggerRegex = new Regex(@"new\s+EventTriggerBuilder\s*\(", RegexOptions.Compiled);
            Regex directListenRegex = new Regex(@"new\s+EventListenBuilder\s*\(", RegexOptions.Compiled);

            // Gather all .cs files under Assets/, excluding Generated, Library, Packages
            string assetsPath = Application.dataPath; // absolute path to Assets/
            var csFiles = Directory.GetFiles(assetsPath, "*.cs", SearchOption.AllDirectories)
                .Where(f =>
                {
                    string normalized = f.Replace('\\', '/');
                    if (normalized.Contains(".Generated.cs")) return false;
                    if (normalized.Contains("/Library/")) return false;
                    return true;
                })
                .ToArray();

            foreach (string absolutePath in csFiles)
            {
                string relativePath = "Assets" + absolutePath.Replace('\\', '/').Substring(assetsPath.Length);

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(absolutePath);
                }
                catch
                {
                    continue;
                }

                for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
                {
                    string line = lines[lineIdx];
                    int lineNum = lineIdx + 1;

                    // Check each event's trigger/listen patterns
                    foreach (var (evt, triggerRegex, listenRegex) in eventPatterns)
                    {
                        if (triggerRegex.IsMatch(line))
                        {
                            results[evt.ID].References.Add(new EventCodeReference
                            {
                                FilePath = relativePath,
                                LineNumber = lineNum,
                                LineContent = line.Trim(),
                                ReferenceType = CodeReferenceType.Trigger
                            });
                        }
                        if (listenRegex.IsMatch(line))
                        {
                            results[evt.ID].References.Add(new EventCodeReference
                            {
                                FilePath = relativePath,
                                LineNumber = lineNum,
                                LineContent = line.Trim(),
                                ReferenceType = CodeReferenceType.Listener
                            });
                        }
                    }

                    // Check direct builder patterns (best-effort — not linked to a specific event)
                    if (directTriggerRegex.IsMatch(line))
                    {
                        // Try to associate with an event by checking if any event name appears on the same line
                        foreach (var evt in events)
                        {
                            string shortName = evt.shortName ?? evt.name;
                            if (!string.IsNullOrEmpty(shortName) && line.Contains(shortName))
                            {
                                if (!results.ContainsKey(evt.ID))
                                    results[evt.ID] = new EventCodeReferences();

                                results[evt.ID].References.Add(new EventCodeReference
                                {
                                    FilePath = relativePath,
                                    LineNumber = lineNum,
                                    LineContent = line.Trim(),
                                    ReferenceType = CodeReferenceType.DirectTrigger
                                });
                            }
                        }
                    }
                    if (directListenRegex.IsMatch(line))
                    {
                        foreach (var evt in events)
                        {
                            string shortName = evt.shortName ?? evt.name;
                            if (!string.IsNullOrEmpty(shortName) && line.Contains(shortName))
                            {
                                if (!results.ContainsKey(evt.ID))
                                    results[evt.ID] = new EventCodeReferences();

                                results[evt.ID].References.Add(new EventCodeReference
                                {
                                    FilePath = relativePath,
                                    LineNumber = lineNum,
                                    LineContent = line.Trim(),
                                    ReferenceType = CodeReferenceType.DirectListener
                                });
                            }
                        }
                    }
                }
            }

            return results;
        }
    }

    /// <summary>
    /// AssetPostprocessor that invalidates the code reference cache when .cs files change.
    /// </summary>
    public class EventCodeReferenceCacheInvalidator : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(
            string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            bool anyCs = importedAssets.Any(p => p.EndsWith(".cs"))
                      || deletedAssets.Any(p => p.EndsWith(".cs"))
                      || movedAssets.Any(p => p.EndsWith(".cs"));

            if (anyCs)
            {
                EventCodeReferenceScanner.InvalidateCache();
            }
        }
    }
}
#endif
