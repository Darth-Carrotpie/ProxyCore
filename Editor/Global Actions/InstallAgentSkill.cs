using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ProxyCore.Editor
{
    /// <summary>
    /// One-click installer that copies ProxyCore's bundled LLM agent skill
    /// (AgentSkills/proxycore) into the current project's agent folders.
    ///
    /// Focus is Claude Code (full skill copy to .claude/skills/proxycore). GitHub
    /// Copilot and OpenAI Codex are also detected; when present they get a lightweight
    /// pointer instruction that references the installed Claude skill folder.
    ///
    /// Menu: ProxyCore ▸ Install Agent Skill
    /// </summary>
    public static class InstallAgentSkill
    {
        private const string SkillId = "proxycore";
        private const string AgentSkillsDir = "AgentSkills";

        // Marker block used in shared instruction files so re-installs are idempotent.
        private const string BlockBegin = "<!-- BEGIN ProxyCore Agent Skill -->";
        private const string BlockEnd = "<!-- END ProxyCore Agent Skill -->";

        [MenuItem("ProxyCore/Install Agent Skill")]
        public static void Install()
        {
            string source = GetSkillSourceDir();
            if (source == null)
            {
                EditorUtility.DisplayDialog(
                    "ProxyCore — Install Agent Skill",
                    $"Could not locate the bundled skill folder ({AgentSkillsDir}/{SkillId}). " +
                    "It should ship inside the ProxyCore package.",
                    "OK");
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

            // Detect providers. Claude Code is the primary target and is always offered;
            // Copilot/Codex only when a clear signal exists (to avoid writing into repos
            // that don't use them).
            bool hasClaude = Directory.Exists(Path.Combine(projectRoot, ".claude"));
            bool hasCopilot = File.Exists(Path.Combine(projectRoot, ".github", "copilot-instructions.md"))
                              || Directory.Exists(Path.Combine(projectRoot, ".github", "instructions"));
            bool hasCodex = File.Exists(Path.Combine(projectRoot, "AGENTS.md"))
                            || Directory.Exists(Path.Combine(projectRoot, ".codex"));

            var plan = new StringBuilder();
            plan.AppendLine("The following will be installed into your project:");
            plan.AppendLine();
            plan.AppendLine("• Claude Code — full skill → .claude/skills/proxycore/" +
                            (hasClaude ? "  (detected)" : "  (folder will be created)"));
            plan.AppendLine("• GitHub Copilot — pointer instruction → .github/instructions/proxycore.instructions.md" +
                            (hasCopilot ? "  (detected)" : "  (not detected — skipped)"));
            plan.AppendLine("• OpenAI Codex — pointer block → AGENTS.md" +
                            (hasCodex ? "  (detected)" : "  (not detected — skipped)"));
            plan.AppendLine();
            plan.AppendLine("Existing installed files for these targets will be overwritten.");

            if (!EditorUtility.DisplayDialog("ProxyCore — Install Agent Skill", plan.ToString(), "Install", "Cancel"))
                return;

            var results = new List<string>();
            var errors = new List<string>();

            // --- Claude Code (primary) ---
            try
            {
                string dest = Path.Combine(projectRoot, ".claude", "skills", SkillId);
                ReplaceDirectory(source, dest);
                results.Add($"Claude Code: {ToProjectRelative(dest, projectRoot)}");
            }
            catch (Exception ex)
            {
                errors.Add($"Claude Code: {ex.Message}");
            }

            // --- GitHub Copilot (pointer, when detected) ---
            if (hasCopilot)
            {
                try
                {
                    string dir = Path.Combine(projectRoot, ".github", "instructions");
                    Directory.CreateDirectory(dir);
                    string file = Path.Combine(dir, "proxycore.instructions.md");
                    File.WriteAllText(file, CopilotInstruction(), new UTF8Encoding(false));
                    results.Add($"GitHub Copilot: {ToProjectRelative(file, projectRoot)}");
                }
                catch (Exception ex)
                {
                    errors.Add($"GitHub Copilot: {ex.Message}");
                }
            }

            // --- OpenAI Codex (AGENTS.md block, when detected) ---
            if (hasCodex)
            {
                try
                {
                    string file = Path.Combine(projectRoot, "AGENTS.md");
                    UpsertBlock(file, CodexBlock());
                    results.Add($"OpenAI Codex: {ToProjectRelative(file, projectRoot)}");
                }
                catch (Exception ex)
                {
                    errors.Add($"OpenAI Codex: {ex.Message}");
                }
            }

            var summary = new StringBuilder();
            if (results.Count > 0)
            {
                summary.AppendLine("Installed:");
                foreach (var r in results) summary.AppendLine("  • " + r);
            }
            if (errors.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Failed:");
                foreach (var e in errors) summary.AppendLine("  • " + e);
            }

            Debug.Log("[ProxyCore] Install Agent Skill\n" + summary);
            EditorUtility.DisplayDialog("ProxyCore — Install Agent Skill", summary.ToString(), "OK");
        }

        /// <summary>
        /// Resolves the bundled skill folder both when ProxyCore is an installed UPM
        /// package and when it is embedded under Assets/ in this dev project.
        /// </summary>
        private static string GetSkillSourceDir()
        {
            // 1) Installed as a UPM package — resolvedPath points at the package root.
            var pkg = PackageInfo.FindForAssembly(typeof(InstallAgentSkill).Assembly);
            if (pkg != null && !string.IsNullOrEmpty(pkg.resolvedPath))
            {
                string p = Path.Combine(pkg.resolvedPath, AgentSkillsDir, SkillId);
                if (Directory.Exists(p)) return p;
            }

            // 2) Embedded under Assets/ProxyCore — locate this script and walk up to the
            //    package root (…/ProxyCore/Editor/Global Actions/InstallAgentSkill.cs).
            foreach (string guid in AssetDatabase.FindAssets("InstallAgentSkill t:MonoScript"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith("/InstallAgentSkill.cs", StringComparison.Ordinal)) continue;

                DirectoryInfo packageRoot = Directory.GetParent(Path.GetFullPath(assetPath)) // Global Actions
                    ?.Parent   // Editor
                    ?.Parent;  // ProxyCore (package root)
                if (packageRoot == null) continue;

                string p = Path.Combine(packageRoot.FullName, AgentSkillsDir, SkillId);
                if (Directory.Exists(p)) return p;
            }

            return null;
        }

        /// <summary>Deletes <paramref name="dest"/> if present, then deep-copies the skill (ignoring .meta files).</summary>
        private static void ReplaceDirectory(string source, string dest)
        {
            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;

                string relative = file.Substring(source.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string target = Path.Combine(dest, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, overwrite: true);
            }
        }

        /// <summary>Creates or replaces a marker-delimited block in a shared instruction file, leaving the rest intact.</summary>
        private static void UpsertBlock(string file, string block)
        {
            string existing = File.Exists(file) ? File.ReadAllText(file) : string.Empty;

            int begin = existing.IndexOf(BlockBegin, StringComparison.Ordinal);
            int end = existing.IndexOf(BlockEnd, StringComparison.Ordinal);
            if (begin >= 0 && end > begin)
            {
                string before = existing.Substring(0, begin);
                string after = existing.Substring(end + BlockEnd.Length);
                File.WriteAllText(file, before + block + after, new UTF8Encoding(false));
                return;
            }

            string sep = existing.Length == 0 || existing.EndsWith("\n") ? string.Empty : "\n";
            File.WriteAllText(file, existing + sep + "\n" + block + "\n", new UTF8Encoding(false));
        }

        private static string CopilotInstruction() =>
            "---\n" +
            "applyTo: \"**\"\n" +
            "---\n" +
            "# ProxyCore\n\n" +
            "This project uses the ProxyCore Unity package. A full agent skill explaining its\n" +
            "correct usage (event system, definition-registries, unlockables) is installed at\n" +
            "`.claude/skills/proxycore/`. Read `.claude/skills/proxycore/SKILL.md` and the files\n" +
            "under `.claude/skills/proxycore/references/` before writing ProxyCore code — the\n" +
            "idioms there differ from generic Unity code.\n";

        private static string CodexBlock() =>
            BlockBegin + "\n" +
            "## ProxyCore\n\n" +
            "This project uses the ProxyCore Unity package. Read `.claude/skills/proxycore/SKILL.md`\n" +
            "and its `references/` before writing ProxyCore code (events, definitions/registries,\n" +
            "unlockables) — the correct idioms differ from generic Unity code.\n" +
            BlockEnd;

        private static string ToProjectRelative(string fullPath, string projectRoot)
        {
            if (fullPath.StartsWith(projectRoot, StringComparison.OrdinalIgnoreCase))
                return fullPath.Substring(projectRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Replace('\\', '/');
            return fullPath.Replace('\\', '/');
        }
    }
}
