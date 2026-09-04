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

        // Paths registered here are ignored by the postprocessor until explicitly released.
        // EventManagerWindow calls SuppressRegenerationForPath when it creates a new asset so that
        // the forced import does not trigger codegen (and a domain reload) before the designer saves.
        private static readonly HashSet<string> _suppressedPaths =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Prevents the asset postprocessor from scheduling regeneration for the given asset path.
        /// Call this BEFORE creating or importing the asset, then call AllowRegenerationForPath in
        /// SaveAllChanges (or on discard) to re-enable normal postprocessor behaviour.
        /// </summary>
        public static void SuppressRegenerationForPath(string assetPath) =>
            _suppressedPaths.Add(assetPath.Replace('\\', '/'));

        /// <summary>Re-enables postprocessor-driven regeneration for the given asset path.</summary>
        public static void AllowRegenerationForPath(string assetPath) =>
            _suppressedPaths.Remove(assetPath.Replace('\\', '/'));

        private static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            bool hasEventChanges = false;

            // Check if any EventMessage assets were affected, skipping suppressed paths.
            foreach (string path in importedAssets.Concat(deletedAssets).Concat(movedAssets))
            {
                if (IsEventMessageAsset(path) &&
                    !_suppressedPaths.Contains(path.Replace('\\', '/')))
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

            // Discover ALL pre-existing Generated/ directories by scanning the file system.
            // This catches directories that are no longer referenced by any surviving event
            // asset (e.g. after all events in a folder were deleted).
            var allGeneratedDirs = FindAllGeneratedDirectories();

            if (eventMessages.Count == 0)
            {
                // No events remain: wipe every Generated/ dir and remove empty ones.
                foreach (string dir in allGeneratedDirs)
                {
                    CleanGeneratedDirectory(dir);
                    DeleteDirectoryIfEmpty(dir);
                }
                AssetDatabase.Refresh();
                Debug.Log("[EventMessageCodeGenerator] No EventMessage assets found. Cleaned stale Generated directories.");
                return;
            }

            // ── Two-level grouping ────────────────────────────────────────────────
            // Level 1: source folder (each folder that contains .asset files gets its
            //          own Generated/ subfolder — regardless of other folders).
            // Level 2: category key within that folder (one file-pair per category).
            //
            // Grouping globally by category was the previous bug: events in two
            // different folders that share the same category key (e.g. "Uncategorized")
            // were merged into a single group, and only the folder of the first event
            // in that group received a Generated/ directory.
            // ─────────────────────────────────────────────────────────────────────

            // folder path → (categoryKey → events)
            var byFolder = new Dictionary<string, Dictionary<string, List<(EventMessage evt, string assetPath)>>>(
                StringComparer.OrdinalIgnoreCase);

            foreach (var (evt, path) in eventMessages)
            {
                string folder = Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
                string categoryKey = GetPrimaryCategoryKey(evt);

                if (!byFolder.TryGetValue(folder, out var catMap))
                {
                    catMap = new Dictionary<string, List<(EventMessage, string)>>();
                    byFolder[folder] = catMap;
                }
                if (!catMap.TryGetValue(categoryKey, out var list))
                {
                    list = new List<(EventMessage, string)>();
                    catMap[categoryKey] = list;
                }
                list.Add((evt, path));
            }

            // Collect Generated/ dirs that will receive new content.
            var activeGeneratedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string folder in byFolder.Keys)
                activeGeneratedDirs.Add(Path.Combine(folder, "Generated").Replace('\\', '/'));

            // Clean ALL Generated/ directories — active ones and stale ones alike.
            var allDirsToClean = new HashSet<string>(allGeneratedDirs, StringComparer.OrdinalIgnoreCase);
            allDirsToClean.UnionWith(activeGeneratedDirs);
            foreach (string dir in allDirsToClean)
                CleanGeneratedDirectory(dir);

            // Generate files: one pair per (folder × category).
            int filesGenerated = 0;
            foreach (var (folder, catMap) in byFolder)
            {
                string generatedDir = Path.Combine(folder, "Generated");
                if (!Directory.Exists(generatedDir))
                    Directory.CreateDirectory(generatedDir);

                foreach (var (categoryKey, events) in catMap)
                {
                    if (events.Count == 0) continue;

                    string triggerFilePath = Path.Combine(generatedDir, $"{categoryKey}.TriggerEvent.Generated.cs");
                    WriteFileIfChanged(triggerFilePath, GenerateTriggerEventClass(events));
                    filesGenerated++;

                    string listenFilePath = Path.Combine(generatedDir, $"{categoryKey}.ListenEvent.Generated.cs");
                    WriteFileIfChanged(listenFilePath, GenerateListenEventClass(events));
                    filesGenerated++;
                }
            }

            // Remove any Generated/ directories that are now empty.
            foreach (string dir in allDirsToClean)
                DeleteDirectoryIfEmpty(dir);

            AssetDatabase.Refresh();
            Debug.Log($"[EventMessageCodeGenerator] Generated {filesGenerated} files across {byFolder.Count} folder(s) for {eventMessages.Count} events.");
        }

        /// <summary>
        /// Returns all Generated/ directories that currently contain *.Generated.cs files,
        /// by walking the Assets folder on disk. This catches directories that are no longer
        /// referenced by any surviving event asset.
        /// </summary>
        private static HashSet<string> FindAllGeneratedDirectories()
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists("Assets")) return result;

            string[] files = Directory.GetFiles("Assets", "*.Generated.cs", SearchOption.AllDirectories);
            foreach (string file in files)
            {
                string dir = Path.GetDirectoryName(file)?.Replace('\\', '/');
                if (!string.IsNullOrEmpty(dir))
                    result.Add(dir);
            }
            return result;
        }

        /// <summary>
        /// Deletes a Generated/ directory and its Unity .meta file if the directory is empty.
        /// Called after regeneration so that folders whose events were all removed are fully cleaned up.
        /// </summary>
        private static void DeleteDirectoryIfEmpty(string dirPath)
        {
            if (!Directory.Exists(dirPath)) return;
            if (Directory.GetFiles(dirPath).Length > 0) return;

            try
            {
                Directory.Delete(dirPath);
                string metaPath = dirPath.TrimEnd('/') + ".meta";
                if (File.Exists(metaPath))
                    File.Delete(metaPath);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[EventMessageCodeGenerator] Could not remove empty Generated directory '{dirPath}': {ex.Message}");
            }
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
