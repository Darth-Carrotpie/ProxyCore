using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Generates static accessor classes for TriggerEvent and ListenEvent based on EventMessage assets.
    /// Output is placed in a Generated/ subfolder next to the EventMessage asset.
    /// Uses debouncing to batch rapid changes.
    /// </summary>
    public class EventMessageCodeGenerator : AssetPostprocessor
    {
        private static bool _regenerationScheduled;
        private static double _lastChangeTime;
        private const double DebounceDelay = 0.1; // 100ms debounce

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {

            bool hasEventChanges = false;

            // Check if any EventMessage assets were affected
            foreach (string path in importedAssets.Concat(deletedAssets).Concat(movedAssets))
            {
                if (IsEventMessageAsset(path))
                {
                    hasEventChanges = true;
                    break;
                }
            }

            if (hasEventChanges)
            {
                ScheduleRegeneration();
            }
        }

        private static bool IsEventMessageAsset(string path)
        {
            if (!path.EndsWith(".asset")) return false;

            var asset = AssetDatabase.LoadAssetAtPath<EventMessage>(path);
            return asset != null;
        }

        private static void ScheduleRegeneration()
        {
            _lastChangeTime = EditorApplication.timeSinceStartup;

            if (!_regenerationScheduled)
            {
                _regenerationScheduled = true;
                EditorApplication.delayCall += CheckAndRegenerate;
            }
        }

        private static void CheckAndRegenerate()
        {
            double elapsed = EditorApplication.timeSinceStartup - _lastChangeTime;

            if (elapsed < DebounceDelay)
            {
                // Not enough time passed, reschedule
                EditorApplication.delayCall += CheckAndRegenerate;
                return;
            }

            _regenerationScheduled = false;
            RegenerateAllEvents();
        }

        [MenuItem("Tools/ProxyCore/Regenerate Event Accessors")]
        public static void RegenerateAllEvents()
        {
            var eventMessages = FindAllEventMessages();

            if (eventMessages.Count == 0)
            {
                Debug.Log("[EventMessageCodeGenerator] No EventMessage assets found.");
                return;
            }

            // Group events by their first category for file organization
            var eventsByCategory = new Dictionary<string, List<(EventMessage evt, string assetPath)>>();

            foreach (var (evt, path) in eventMessages)
            {
                string categoryKey = GetPrimaryCategoryKey(evt);
                if (!eventsByCategory.ContainsKey(categoryKey))
                {
                    eventsByCategory[categoryKey] = new List<(EventMessage, string)>();
                }
                eventsByCategory[categoryKey].Add((evt, path));
            }

            // Generate files per category
            int filesGenerated = 0;
            foreach (var kvp in eventsByCategory)
            {
                string categoryKey = kvp.Key;
                var events = kvp.Value;

                if (events.Count == 0) continue;

                // Use the directory of the first event in this category for output
                string firstAssetPath = events[0].assetPath;
                string assetDir = Path.GetDirectoryName(firstAssetPath);
                string generatedDir = Path.Combine(assetDir, "Generated");

                // Ensure Generated directory exists
                if (!Directory.Exists(generatedDir))
                {
                    Directory.CreateDirectory(generatedDir);
                }

                // Generate TriggerEvent file
                string triggerFileName = $"{categoryKey}.TriggerEvent.Generated.cs";
                string triggerFilePath = Path.Combine(generatedDir, triggerFileName);
                string triggerContent = GenerateTriggerEventClass(events);
                WriteFileIfChanged(triggerFilePath, triggerContent);
                filesGenerated++;

                // Generate ListenEvent file
                string listenFileName = $"{categoryKey}.ListenEvent.Generated.cs";
                string listenFilePath = Path.Combine(generatedDir, listenFileName);
                string listenContent = GenerateListenEventClass(events);
                WriteFileIfChanged(listenFilePath, listenContent);
                filesGenerated++;
            }

            AssetDatabase.Refresh();
            Debug.Log($"[EventMessageCodeGenerator] Generated {filesGenerated} files for {eventMessages.Count} events.");
        }

        private static List<(EventMessage evt, string path)> FindAllEventMessages()
        {
            var result = new List<(EventMessage, string)>();
            string[] guids = AssetDatabase.FindAssets("t:EventMessage");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var evt = AssetDatabase.LoadAssetAtPath<EventMessage>(path);
                if (evt != null)
                {
                    result.Add((evt, path));
                }
            }

            return result;
        }

        private static string GetPrimaryCategoryKey(EventMessage evt)
        {
            if (evt.categories == null || evt.categories.Count == 0)
            {
                return "Uncategorized";
            }

            var firstCat = evt.categories[0];
            if (firstCat == null)
            {
                return "Uncategorized";
            }

            string catName = firstCat.GetCodeGenName();
            return string.IsNullOrEmpty(catName) ? "Uncategorized" : catName;
        }

        private static string GenerateTriggerEventClass(List<(EventMessage evt, string assetPath)> events)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// Auto-generated by EventMessageCodeGenerator. Do not edit manually.");
            sb.AppendLine("// Regenerate via Tools > ProxyCore > Regenerate Event Accessors");
            sb.AppendLine();
            sb.AppendLine("namespace ProxyCore.Generated {");

            // Build nested class structure
            var tree = BuildCategoryTree(events, "TriggerEvent");
            sb.Append(tree);

            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string GenerateListenEventClass(List<(EventMessage evt, string assetPath)> events)
        {
            var sb = new StringBuilder();

            sb.AppendLine("// Auto-generated by EventMessageCodeGenerator. Do not edit manually.");
            sb.AppendLine("// Regenerate via Tools > ProxyCore > Regenerate Event Accessors");
            sb.AppendLine();
            sb.AppendLine("namespace ProxyCore.Generated {");

            // Build nested class structure
            var tree = BuildCategoryTree(events, "ListenEvent");
            sb.Append(tree);

            sb.AppendLine("}");

            return sb.ToString();
        }

        private static string BuildCategoryTree(List<(EventMessage evt, string assetPath)> events, string rootClassName)
        {
            var sb = new StringBuilder();
            string indent = "    ";

            // Start with the root class
            sb.AppendLine($"{indent}public static partial class {rootClassName} {{");

            // Group events by their full category path
            var eventsByPath = new Dictionary<string, List<EventMessage>>();

            foreach (var (evt, _) in events)
            {
                string path = GetCategoryPathForNesting(evt);
                if (!eventsByPath.ContainsKey(path))
                {
                    eventsByPath[path] = new List<EventMessage>();
                }
                eventsByPath[path].Add(evt);
            }

            // Build nested structure
            var processedPaths = new HashSet<string>();

            foreach (var kvp in eventsByPath.OrderBy(x => x.Key))
            {
                string fullPath = kvp.Key;
                var pathEvents = kvp.Value;

                // Ensure all parent paths are created
                string[] parts = fullPath.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);

                string currentPath = "";
                int depth = 2;

                foreach (string part in parts)
                {
                    currentPath = string.IsNullOrEmpty(currentPath) ? part : $"{currentPath}.{part}";

                    if (!processedPaths.Contains(currentPath))
                    {
                        processedPaths.Add(currentPath);

                        string classIndent = new string(' ', depth * 4);
                        sb.AppendLine($"{classIndent}public static partial class {part} {{");
                    }
                    depth++;
                }

                // Add events at this level
                string eventIndent = new string(' ', depth * 4);
                string builderType = rootClassName == "TriggerEvent" ? "EventTriggerBuilder" : "EventListenBuilder";

                foreach (var evt in pathEvents)
                {
                    string eventName = evt.GetCodeGenName();
                    int eventId = evt.ID;

                    sb.AppendLine($"{eventIndent}public static {builderType} {eventName} => new {builderType}(EventCoordinatorNew.Instance.GetDefinition({eventId}) as EventMessage);");
                }

                // Close nested classes (in reverse order)
                for (int i = parts.Length - 1; i >= 0; i--)
                {
                    string closeIndent = new string(' ', (i + 2) * 4);
                    sb.AppendLine($"{closeIndent}}}");
                }
            }

            sb.AppendLine($"{indent}}}");

            return sb.ToString();
        }

        private static string GetCategoryPathForNesting(EventMessage evt)
        {
            if (evt.categories == null || evt.categories.Count == 0)
            {
                return "Uncategorized";
            }

            var parts = new List<string>();
            foreach (var cat in evt.categories)
            {
                if (cat == null) continue;

                string catName = cat.GetCodeGenName();
                if (!string.IsNullOrEmpty(catName))
                {
                    parts.Add(catName);
                }
            }

            if (parts.Count == 0)
            {
                return "Uncategorized";
            }

            return string.Join(".", parts);
        }

        private static void WriteFileIfChanged(string filePath, string content)
        {
            // Normalize path separators
            filePath = filePath.Replace('\\', '/');

            // Check if file exists and has same content
            if (File.Exists(filePath))
            {
                string existingContent = File.ReadAllText(filePath);
                if (existingContent == content)
                {
                    return; // No change needed
                }
            }

            File.WriteAllText(filePath, content);
        }
    }
}
