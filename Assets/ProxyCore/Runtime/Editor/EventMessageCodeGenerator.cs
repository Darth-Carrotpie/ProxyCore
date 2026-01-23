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

        [MenuItem("ProxyCore/Regenerate Event Accessors")]
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

            // Collect all unique Generated directories and clean them before regenerating
            var generatedDirectories = new HashSet<string>();
            foreach (var kvp in eventsByCategory)
            {
                if (kvp.Value.Count == 0) continue;
                string firstAssetPath = kvp.Value[0].assetPath;
                string assetDir = Path.GetDirectoryName(firstAssetPath);
                string generatedDir = Path.Combine(assetDir, "Generated").Replace('\\', '/');
                generatedDirectories.Add(generatedDir);
            }

            // Clean all Generated directories
            foreach (string generatedDir in generatedDirectories)
            {
                CleanGeneratedDirectory(generatedDir);
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

        /// <summary>
        /// Deletes all .cs and .meta files in the Generated directory to ensure clean regeneration.
        /// </summary>
        private static void CleanGeneratedDirectory(string generatedDir)
        {
            if (!Directory.Exists(generatedDir))
            {
                return;
            }

            try
            {
                // Delete all .cs files and their .meta files
                string[] csFiles = Directory.GetFiles(generatedDir, "*.cs");
                foreach (string file in csFiles)
                {
                    File.Delete(file);

                    // Also delete the .meta file if it exists
                    string metaFile = file + ".meta";
                    if (File.Exists(metaFile))
                    {
                        File.Delete(metaFile);
                    }
                }

                // If directory is now empty, we could optionally delete it
                // but we'll keep it since we're about to regenerate into it
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EventMessageCodeGenerator] Failed to clean Generated directory '{generatedDir}': {ex.Message}");
            }
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
            sb.AppendLine("using ProxyCore;");
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
            sb.AppendLine("using ProxyCore;");
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

            // Group events by EACH category they belong to (not chained)
            // An event with categories [Health, Player] should appear in BOTH Health and Player
            var eventsByCategory = new Dictionary<string, List<EventMessage>>();

            foreach (var (evt, _) in events)
            {
                var categoryNames = GetAllCategoryNames(evt);

                foreach (string categoryName in categoryNames)
                {
                    if (!eventsByCategory.ContainsKey(categoryName))
                    {
                        eventsByCategory[categoryName] = new List<EventMessage>();
                    }
                    eventsByCategory[categoryName].Add(evt);
                }
            }

            // Build category classes
            string builderType = rootClassName == "TriggerEvent" ? "EventTriggerBuilder" : "EventListenBuilder";

            foreach (var kvp in eventsByCategory.OrderBy(x => x.Key))
            {
                string categoryName = kvp.Key;
                var categoryEvents = kvp.Value;

                sb.AppendLine($"{indent}{indent}public static partial class {categoryName} {{");

                foreach (var evt in categoryEvents.OrderBy(e => e.GetCodeGenName()))
                {
                    string eventName = evt.GetCodeGenName();
                    int eventId = evt.ID;

                    sb.AppendLine($"{indent}{indent}{indent}public static {builderType} {eventName} => new {builderType}(EventCoordinator.Instance.GetDefinition({eventId}) as EventMessage);");
                }

                sb.AppendLine($"{indent}{indent}}}");
            }

            sb.AppendLine($"{indent}}}");

            return sb.ToString();
        }

        /// <summary>
        /// Gets all category names for an event. Each category becomes a separate accessor path.
        /// </summary>
        private static List<string> GetAllCategoryNames(EventMessage evt)
        {
            var result = new List<string>();

            if (evt.categories == null || evt.categories.Count == 0)
            {
                result.Add("Uncategorized");
                return result;
            }

            foreach (var cat in evt.categories)
            {
                if (cat == null) continue;

                string catName = cat.GetCodeGenName();
                if (!string.IsNullOrEmpty(catName))
                {
                    result.Add(catName);
                }
            }

            if (result.Count == 0)
            {
                result.Add("Uncategorized");
            }

            return result;
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
