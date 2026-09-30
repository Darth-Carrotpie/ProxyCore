using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace ProxyCore.Editor
{
    /// <summary>
    /// Unity-facing entry point for installing ProxyCore's bundled Agent Skill.
    /// The window gathers provider choices; AgentSkillInstaller owns validation,
    /// transactional filesystem changes, provenance, and legacy migration.
    /// </summary>
    public static class InstallAgentSkill
    {
        internal const string SkillId = "proxycore";
        internal const string AgentSkillsDirectory = "AgentSkills";

        [MenuItem("ProxyCore/Install Agent Skill")]
        public static void Install()
        {
            string source = ResolveSkillSourceDirectory();
            if (source == null)
            {
                EditorUtility.DisplayDialog(
                    "ProxyCore - Install Agent Skill",
                    $"Could not locate the bundled skill folder ({AgentSkillsDirectory}/{SkillId}). " +
                    "It should ship inside the ProxyCore package.",
                    "OK");
                return;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            AgentSkillInstallWindow.Open(projectRoot, source, ResolvePackageVersion());
        }

        /// <summary>
        /// Resolves the canonical skill both for an installed UPM package and for
        /// this repository's Assets/ProxyCore development layout.
        /// </summary>
        internal static string ResolveSkillSourceDirectory()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(InstallAgentSkill).Assembly);
            if (package != null && !string.IsNullOrEmpty(package.resolvedPath))
            {
                string packaged = Path.Combine(
                    package.resolvedPath,
                    AgentSkillsDirectory,
                    SkillId);
                if (Directory.Exists(packaged))
                    return Path.GetFullPath(packaged);
            }

            foreach (string guid in AssetDatabase.FindAssets("InstallAgentSkill t:MonoScript"))
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (!assetPath.EndsWith("/InstallAgentSkill.cs", StringComparison.Ordinal))
                    continue;

                string scriptPath = Path.GetFullPath(assetPath);
                DirectoryInfo packageRoot = Directory.GetParent(scriptPath) // Global Actions
                    ?.Parent // Editor
                    ?.Parent; // ProxyCore package root
                if (packageRoot == null)
                    continue;

                string embedded = Path.Combine(
                    packageRoot.FullName,
                    AgentSkillsDirectory,
                    SkillId);
                if (Directory.Exists(embedded))
                    return Path.GetFullPath(embedded);
            }

            return null;
        }

        internal static string ResolvePackageVersion()
        {
            PackageInfo package = PackageInfo.FindForAssembly(typeof(InstallAgentSkill).Assembly);
            return package != null && !string.IsNullOrWhiteSpace(package.version)
                ? package.version
                : "development";
        }
    }

    internal sealed class AgentSkillInstallWindow : EditorWindow
    {
        private const float WindowWidth = 620f;
        private const float WindowHeight = 560f;

        [SerializeField] private string _projectRoot;
        [SerializeField] private string _sourceDirectory;
        [SerializeField] private string _packageVersion;
        [SerializeField] private bool _installClaude = true;
        [SerializeField] private bool _installCopilot = true;
        [SerializeField] private bool _installCodex = true;
        [SerializeField] private bool _allowUnmanagedOverwrite;
        [SerializeField] private bool _allowModifiedUninstall;
        [SerializeField] private Vector2 _scroll;

        private readonly AgentSkillInstaller _installer = new AgentSkillInstaller();
        private string _validationError;

        internal static void Open(string projectRoot, string sourceDirectory, string packageVersion)
        {
            AgentSkillInstallWindow window = GetWindow<AgentSkillInstallWindow>(
                utility: true,
                title: "ProxyCore Agent Skill",
                focus: true);
            window.minSize = new Vector2(WindowWidth, WindowHeight);
            window.maxSize = new Vector2(WindowWidth, WindowHeight);
            window._projectRoot = projectRoot;
            window._sourceDirectory = sourceDirectory;
            window._packageVersion = packageVersion;
            window.ValidateSource();
            window.Show();
        }

        private void OnEnable()
        {
            if (!string.IsNullOrEmpty(_sourceDirectory))
                ValidateSource();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Install ProxyCore Agent Skill", EditorStyles.boldLabel);
            EditorGUILayout.LabelField(
                "Choose the coding agents used by this project. Each selected agent receives a complete, native skill copy.",
                EditorStyles.wordWrappedLabel);
            EditorGUILayout.Space(8);

            if (!string.IsNullOrEmpty(_validationError))
            {
                EditorGUILayout.HelpBox(_validationError, MessageType.Error);
                DrawFooter(canInstall: false, canUninstall: false);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);

            EditorGUILayout.LabelField("Native targets", EditorStyles.boldLabel);
            _installClaude = DrawTargetToggle(
                _installClaude,
                AgentSkillTargets.ClaudeCode,
                IsClaudeDetected());
            _installCopilot = DrawTargetToggle(
                _installCopilot,
                AgentSkillTargets.GitHubCopilot,
                IsCopilotDetected());
            _installCodex = DrawTargetToggle(
                _installCodex,
                AgentSkillTargets.OpenAICodex,
                IsCodexDetected());

            IReadOnlyList<AgentSkillTarget> selected = SelectedTargets();
            IReadOnlyList<AgentSkillTargetPlan> plans = Array.Empty<AgentSkillTargetPlan>();
            IReadOnlyList<AgentSkillUninstallTargetPlan> uninstallPlans =
                Array.Empty<AgentSkillUninstallTargetPlan>();
            string planningError = null;

            if (selected.Count > 0)
            {
                try
                {
                    plans = _installer.Plan(CreateRequest(selected));
                    uninstallPlans = _installer.PlanUninstall(
                        CreateUninstallRequest(selected));
                }
                catch (Exception ex)
                {
                    planningError = ex.Message;
                }
            }

            if (_installCopilot && (_installClaude || _installCodex))
            {
                EditorGUILayout.HelpBox(
                    "Current Copilot versions can also scan .claude/skills and .agents/skills. " +
                    "The .github copy has priority for a duplicate skill name. The installer keeps " +
                    "the managed payload synchronized across selected targets and records provenance.",
                    MessageType.Info);
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Preflight", EditorStyles.boldLabel);
            if (planningError != null)
            {
                EditorGUILayout.HelpBox(planningError, MessageType.Error);
            }
            else if (selected.Count == 0)
            {
                EditorGUILayout.HelpBox("Select at least one agent.", MessageType.Warning);
            }
            else
            {
                foreach (AgentSkillTargetPlan plan in plans)
                    DrawPlan(plan);
            }

            bool hasConflict = plans.Any(plan => plan.State == AgentSkillDestinationState.Conflict);
            if (hasConflict)
            {
                EditorGUILayout.Space(4);
                _allowUnmanagedOverwrite = EditorGUILayout.ToggleLeft(
                    "Replace conflicting skill folders (preserve non-colliding extra files)",
                    _allowUnmanagedOverwrite);
                EditorGUILayout.HelpBox(
                    "Conflicts can contain local changes. Leave this disabled unless you reviewed " +
                    "the listed folders and want ProxyCore's managed files to replace them.",
                    MessageType.Warning);
            }
            else
            {
                _allowUnmanagedOverwrite = false;
            }

            bool hasManagedUninstall = uninstallPlans.Any(plan =>
                plan.State == AgentSkillUninstallState.ManagedClean ||
                plan.State == AgentSkillUninstallState.ManagedModified);
            bool hasModifiedUninstall = uninstallPlans.Any(plan =>
                plan.State == AgentSkillUninstallState.ManagedModified);
            if (hasModifiedUninstall)
            {
                EditorGUILayout.Space(4);
                _allowModifiedUninstall = EditorGUILayout.ToggleLeft(
                    "Allow uninstall to remove locally modified managed files",
                    _allowModifiedUninstall);
            }
            else
            {
                _allowModifiedUninstall = false;
            }

            EditorGUILayout.Space(8);
            EditorGUILayout.HelpBox(
                "After a successful native install, the old generated Copilot pointer and marked " +
                "ProxyCore block in AGENTS.md are removed only when ownership can be proven. " +
                "Unrelated instructions are preserved.",
                MessageType.None);

            EditorGUILayout.EndScrollView();

            bool canInstall = selected.Count > 0 &&
                              planningError == null &&
                              (!hasConflict || _allowUnmanagedOverwrite);
            bool canUninstall = selected.Count > 0 &&
                                planningError == null &&
                                hasManagedUninstall &&
                                (!hasModifiedUninstall || _allowModifiedUninstall);
            DrawFooter(canInstall, canUninstall);
        }

        private bool DrawTargetToggle(bool value, AgentSkillTarget target, bool detected)
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            bool next = EditorGUILayout.ToggleLeft(
                target.DisplayName + (detected ? " (project signal detected)" : string.Empty),
                value,
                EditorStyles.boldLabel);
            EditorGUILayout.LabelField(target.RelativeDirectory + "/", EditorStyles.miniLabel);
            EditorGUILayout.EndVertical();
            return next;
        }

        private void DrawPlan(AgentSkillTargetPlan plan)
        {
            MessageType type = plan.State switch
            {
                AgentSkillDestinationState.Conflict => MessageType.Warning,
                AgentSkillDestinationState.UpToDate => MessageType.Info,
                _ => MessageType.None
            };

            string state = plan.State switch
            {
                AgentSkillDestinationState.Missing => "New install",
                AgentSkillDestinationState.UpToDate => "Up to date",
                AgentSkillDestinationState.ManagedUpdate => "Managed update",
                AgentSkillDestinationState.LegacyExactCopy => "Adopt legacy copy",
                AgentSkillDestinationState.Conflict => "Conflict",
                _ => plan.State.ToString()
            };

            EditorGUILayout.HelpBox(
                $"{plan.Target.DisplayName} - {state}\n{plan.Detail}",
                type);
        }

        private void DrawFooter(bool canInstall, bool canUninstall)
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Close", GUILayout.Width(90)))
                Close();

            using (new EditorGUI.DisabledScope(!canUninstall))
            {
                if (GUILayout.Button("Uninstall Managed", GUILayout.Width(130)))
                    RunUninstall();
            }

            using (new EditorGUI.DisabledScope(!canInstall))
            {
                if (GUILayout.Button("Install Selected", GUILayout.Width(130)))
                    RunInstall();
            }

            EditorGUILayout.EndHorizontal();
            EditorGUILayout.Space(8);
        }

        private void RunInstall()
        {
            IReadOnlyList<AgentSkillTarget> selected = SelectedTargets();
            var confirmation = new StringBuilder();
            confirmation.AppendLine("Install the full ProxyCore skill into:");
            confirmation.AppendLine();
            foreach (AgentSkillTarget target in selected)
                confirmation.AppendLine("- " + target.RelativeDirectory + "/");
            confirmation.AppendLine();
            confirmation.AppendLine("Selected targets are staged and committed as one transaction.");

            if (!EditorUtility.DisplayDialog(
                    "ProxyCore - Install Agent Skill",
                    confirmation.ToString(),
                    "Install",
                    "Cancel"))
            {
                return;
            }

            try
            {
                AgentSkillInstallReport report = _installer.Install(CreateRequest(selected));
                string summary = report.ToDisplayString(_projectRoot);
                Debug.Log("[ProxyCore] Install Agent Skill\n" + summary);
                EditorUtility.DisplayDialog(
                    "ProxyCore - Install Agent Skill",
                    summary,
                    "OK");
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "ProxyCore - Install Agent Skill",
                    "Installation failed. Existing managed installations were kept or rolled back.\n\n" +
                    ex.Message,
                    "OK");
                Repaint();
            }
        }

        private void RunUninstall()
        {
            IReadOnlyList<AgentSkillTarget> selected = SelectedTargets();
            var confirmation = new StringBuilder();
            confirmation.AppendLine("Uninstall ProxyCore-managed skill files from:");
            confirmation.AppendLine();
            foreach (AgentSkillTarget target in selected)
                confirmation.AppendLine("- " + target.RelativeDirectory + "/");
            confirmation.AppendLine();
            confirmation.AppendLine(
                "Only files recorded in a valid ownership manifest are removed. " +
                "Unmanaged folders and local extra files are preserved.");

            if (!EditorUtility.DisplayDialog(
                    "ProxyCore - Uninstall Agent Skill",
                    confirmation.ToString(),
                    "Uninstall",
                    "Cancel"))
            {
                return;
            }

            try
            {
                AgentSkillUninstallReport report = _installer.Uninstall(
                    CreateUninstallRequest(selected));
                string summary = report.ToDisplayString(_projectRoot);
                Debug.Log("[ProxyCore] Uninstall Agent Skill\n" + summary);
                EditorUtility.DisplayDialog(
                    "ProxyCore - Uninstall Agent Skill",
                    summary,
                    "OK");
                Repaint();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                EditorUtility.DisplayDialog(
                    "ProxyCore - Uninstall Agent Skill",
                    "Uninstall failed. Existing managed installations were kept or rolled back.\n\n" +
                    ex.Message,
                    "OK");
                Repaint();
            }
        }

        private AgentSkillInstallRequest CreateRequest(IReadOnlyList<AgentSkillTarget> targets)
        {
            return new AgentSkillInstallRequest(
                _projectRoot,
                _sourceDirectory,
                _packageVersion,
                targets,
                _allowUnmanagedOverwrite,
                removeLegacyBridges: true);
        }

        private AgentSkillUninstallRequest CreateUninstallRequest(
            IReadOnlyList<AgentSkillTarget> targets)
        {
            return new AgentSkillUninstallRequest(
                _projectRoot,
                targets,
                _allowModifiedUninstall);
        }

        private IReadOnlyList<AgentSkillTarget> SelectedTargets()
        {
            var targets = new List<AgentSkillTarget>();
            if (_installClaude)
                targets.Add(AgentSkillTargets.ClaudeCode);
            if (_installCopilot)
                targets.Add(AgentSkillTargets.GitHubCopilot);
            if (_installCodex)
                targets.Add(AgentSkillTargets.OpenAICodex);
            return targets;
        }

        private void ValidateSource()
        {
            try
            {
                _installer.ValidateSource(_sourceDirectory);
                _validationError = null;
            }
            catch (Exception ex)
            {
                _validationError = ex.Message;
            }
        }

        private bool IsClaudeDetected()
        {
            return Directory.Exists(Path.Combine(_projectRoot, ".claude"));
        }

        private bool IsCopilotDetected()
        {
            return Directory.Exists(Path.Combine(_projectRoot, ".github", "skills")) ||
                   File.Exists(Path.Combine(_projectRoot, ".github", "copilot-instructions.md")) ||
                   Directory.Exists(Path.Combine(_projectRoot, ".github", "instructions"));
        }

        private bool IsCodexDetected()
        {
            return Directory.Exists(Path.Combine(_projectRoot, ".agents", "skills")) ||
                   Directory.Exists(Path.Combine(_projectRoot, ".codex")) ||
                   File.Exists(Path.Combine(_projectRoot, "AGENTS.md"));
        }
    }
}
