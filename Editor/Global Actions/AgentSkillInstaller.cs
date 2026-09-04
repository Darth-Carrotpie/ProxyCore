using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ProxyCore.Editor
{
    internal enum AgentSkillProvider
    {
        ClaudeCode,
        GitHubCopilot,
        OpenAICodex
    }

    internal enum AgentSkillDestinationState
    {
        Missing,
        UpToDate,
        ManagedUpdate,
        LegacyExactCopy,
        Conflict
    }

    internal enum AgentSkillUninstallState
    {
        Missing,
        ManagedClean,
        ManagedModified,
        Unmanaged
    }

    internal sealed class AgentSkillTarget
    {
        public AgentSkillProvider Provider { get; }
        public string DisplayName { get; }
        public string RelativeDirectory { get; }

        public AgentSkillTarget(AgentSkillProvider provider, string displayName, string relativeDirectory)
        {
            Provider = provider;
            DisplayName = displayName;
            RelativeDirectory = relativeDirectory;
        }

        public string GetDestination(string projectRoot)
        {
            return Path.GetFullPath(Path.Combine(projectRoot, RelativeDirectory));
        }
    }

    internal static class AgentSkillTargets
    {
        internal static readonly AgentSkillTarget ClaudeCode = new AgentSkillTarget(
            AgentSkillProvider.ClaudeCode,
            "Claude Code",
            ".claude/skills/proxycore");

        internal static readonly AgentSkillTarget GitHubCopilot = new AgentSkillTarget(
            AgentSkillProvider.GitHubCopilot,
            "GitHub Copilot",
            ".github/skills/proxycore");

        internal static readonly AgentSkillTarget OpenAICodex = new AgentSkillTarget(
            AgentSkillProvider.OpenAICodex,
            "OpenAI Codex",
            ".agents/skills/proxycore");

        internal static readonly IReadOnlyList<AgentSkillTarget> All = new[]
        {
            ClaudeCode,
            GitHubCopilot,
            OpenAICodex
        };
    }

    internal sealed class AgentSkillTargetPlan
    {
        public AgentSkillTarget Target { get; }
        public string Destination { get; }
        public AgentSkillDestinationState State { get; }
        public string Detail { get; }

        public AgentSkillTargetPlan(
            AgentSkillTarget target,
            string destination,
            AgentSkillDestinationState state,
            string detail)
        {
            Target = target;
            Destination = destination;
            State = state;
            Detail = detail;
        }
    }

    internal sealed class AgentSkillInstallResult
    {
        public AgentSkillTarget Target { get; }
        public AgentSkillDestinationState PreviousState { get; }
        public string Destination { get; }
        public string Action { get; }

        public AgentSkillInstallResult(
            AgentSkillTarget target,
            AgentSkillDestinationState previousState,
            string destination,
            string action)
        {
            Target = target;
            PreviousState = previousState;
            Destination = destination;
            Action = action;
        }
    }

    internal sealed class AgentSkillInstallReport
    {
        public IReadOnlyList<AgentSkillInstallResult> Results { get; }
        public IReadOnlyList<string> Warnings { get; }

        public AgentSkillInstallReport(
            IReadOnlyList<AgentSkillInstallResult> results,
            IReadOnlyList<string> warnings)
        {
            Results = results;
            Warnings = warnings;
        }

        public string ToDisplayString(string projectRoot)
        {
            var summary = new StringBuilder();
            summary.AppendLine("Agent skill installation completed.");

            foreach (AgentSkillInstallResult result in Results)
            {
                summary.AppendLine(
                    $"- {result.Target.DisplayName}: {result.Action} " +
                    $"({AgentSkillPath.ToProjectRelative(result.Destination, projectRoot)})");
            }

            if (Warnings.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Warnings:");
                foreach (string warning in Warnings)
                    summary.AppendLine("- " + warning);
            }

            return summary.ToString().TrimEnd();
        }
    }

    internal sealed class AgentSkillUninstallTargetPlan
    {
        public AgentSkillTarget Target { get; }
        public string Destination { get; }
        public AgentSkillUninstallState State { get; }
        public string Detail { get; }
        public AgentSkillInstallManifest Manifest { get; }

        public AgentSkillUninstallTargetPlan(
            AgentSkillTarget target,
            string destination,
            AgentSkillUninstallState state,
            string detail,
            AgentSkillInstallManifest manifest = null)
        {
            Target = target;
            Destination = destination;
            State = state;
            Detail = detail;
            Manifest = manifest;
        }
    }

    internal sealed class AgentSkillUninstallResult
    {
        public AgentSkillTarget Target { get; }
        public AgentSkillUninstallState PreviousState { get; }
        public string Destination { get; }
        public string Action { get; }

        public AgentSkillUninstallResult(
            AgentSkillTarget target,
            AgentSkillUninstallState previousState,
            string destination,
            string action)
        {
            Target = target;
            PreviousState = previousState;
            Destination = destination;
            Action = action;
        }
    }

    internal sealed class AgentSkillUninstallReport
    {
        public IReadOnlyList<AgentSkillUninstallResult> Results { get; }
        public IReadOnlyList<string> Warnings { get; }

        public AgentSkillUninstallReport(
            IReadOnlyList<AgentSkillUninstallResult> results,
            IReadOnlyList<string> warnings)
        {
            Results = results;
            Warnings = warnings;
        }

        public string ToDisplayString(string projectRoot)
        {
            var summary = new StringBuilder();
            summary.AppendLine("Agent skill uninstall completed.");

            foreach (AgentSkillUninstallResult result in Results)
            {
                summary.AppendLine(
                    $"- {result.Target.DisplayName}: {result.Action} " +
                    $"({AgentSkillPath.ToProjectRelative(result.Destination, projectRoot)})");
            }

            if (Warnings.Count > 0)
            {
                summary.AppendLine();
                summary.AppendLine("Warnings:");
                foreach (string warning in Warnings)
                    summary.AppendLine("- " + warning);
            }

            return summary.ToString().TrimEnd();
        }
    }

    internal sealed class AgentSkillInstallRequest
    {
        public string ProjectRoot { get; }
        public string SourceDirectory { get; }
        public string PackageVersion { get; }
        public IReadOnlyList<AgentSkillTarget> Targets { get; }
        public bool AllowUnmanagedOverwrite { get; }
        public bool RemoveLegacyBridges { get; }

        public AgentSkillInstallRequest(
            string projectRoot,
            string sourceDirectory,
            string packageVersion,
            IReadOnlyList<AgentSkillTarget> targets,
            bool allowUnmanagedOverwrite,
            bool removeLegacyBridges = true)
        {
            ProjectRoot = projectRoot;
            SourceDirectory = sourceDirectory;
            PackageVersion = packageVersion;
            Targets = targets;
            AllowUnmanagedOverwrite = allowUnmanagedOverwrite;
            RemoveLegacyBridges = removeLegacyBridges;
        }
    }

    internal sealed class AgentSkillUninstallRequest
    {
        public string ProjectRoot { get; }
        public IReadOnlyList<AgentSkillTarget> Targets { get; }
        public bool AllowModifiedManagedFiles { get; }

        public AgentSkillUninstallRequest(
            string projectRoot,
            IReadOnlyList<AgentSkillTarget> targets,
            bool allowModifiedManagedFiles)
        {
            ProjectRoot = projectRoot;
            Targets = targets;
            AllowModifiedManagedFiles = allowModifiedManagedFiles;
        }
    }

    [Serializable]
    internal sealed class AgentSkillInstallManifest
    {
        public int schemaVersion;
        public string skillId;
        public string packageId;
        public string packageVersion;
        public string target;
        public string contentHash;
        public string[] managedFiles;
    }

    internal sealed class AgentSkillSource
    {
        public string Root { get; }
        public string Name { get; }
        public string Description { get; }
        public string ContentHash { get; }
        public IReadOnlyList<string> ManagedFiles { get; }

        public AgentSkillSource(
            string root,
            string name,
            string description,
            string contentHash,
            IReadOnlyList<string> managedFiles)
        {
            Root = root;
            Name = name;
            Description = description;
            ContentHash = contentHash;
            ManagedFiles = managedFiles;
        }
    }

    internal interface IAgentSkillFileSystem
    {
        bool DirectoryExists(string path);
        bool FileExists(string path);
        FileAttributes GetAttributes(string path);
        void CreateDirectory(string path);
        string[] GetDirectories(string path, SearchOption searchOption);
        string[] GetFiles(string path, SearchOption searchOption);
        byte[] ReadAllBytes(string path);
        string ReadAllText(string path);
        void WriteAllBytes(string path, byte[] contents);
        void WriteAllText(string path, string contents);
        void MoveDirectory(string source, string destination);
        void MoveFile(string source, string destination);
        void DeleteDirectory(string path, bool recursive);
        void DeleteFile(string path);
    }

    internal sealed class SystemAgentSkillFileSystem : IAgentSkillFileSystem
    {
        private static readonly UTF8Encoding Utf8WithoutBom = new UTF8Encoding(false);

        public bool DirectoryExists(string path) => Directory.Exists(path);
        public bool FileExists(string path) => File.Exists(path);
        public FileAttributes GetAttributes(string path) => File.GetAttributes(path);
        public void CreateDirectory(string path) => Directory.CreateDirectory(path);
        public string[] GetDirectories(string path, SearchOption searchOption) =>
            Directory.GetDirectories(path, "*", searchOption);
        public string[] GetFiles(string path, SearchOption searchOption) =>
            Directory.GetFiles(path, "*", searchOption);
        public byte[] ReadAllBytes(string path) => File.ReadAllBytes(path);
        public string ReadAllText(string path) => File.ReadAllText(path);
        public void WriteAllBytes(string path, byte[] contents) => File.WriteAllBytes(path, contents);
        public void WriteAllText(string path, string contents) =>
            File.WriteAllText(path, contents, Utf8WithoutBom);
        public void MoveDirectory(string source, string destination) => Directory.Move(source, destination);
        public void MoveFile(string source, string destination) => File.Move(source, destination);
        public void DeleteDirectory(string path, bool recursive) => Directory.Delete(path, recursive);
        public void DeleteFile(string path) => File.Delete(path);
    }

    internal sealed class AgentSkillInstaller
    {
        internal const string PackageId = "com.shakotis.proxycore";
        internal const string ManifestFileName = ".proxycore-skill-install.json";
        private const int ManifestSchemaVersion = 1;

        private readonly IAgentSkillFileSystem _fileSystem;

        public AgentSkillInstaller(IAgentSkillFileSystem fileSystem = null)
        {
            _fileSystem = fileSystem ?? new SystemAgentSkillFileSystem();
        }

        public AgentSkillSource ValidateSource(string sourceDirectory)
        {
            if (string.IsNullOrWhiteSpace(sourceDirectory))
                throw new ArgumentException("A skill source directory is required.", nameof(sourceDirectory));

            string sourceRoot = Path.GetFullPath(sourceDirectory);
            if (!_fileSystem.DirectoryExists(sourceRoot))
                throw new InvalidDataException($"Skill source directory does not exist: {sourceRoot}");
            EnsureDirectoryTreeHasNoReparsePoints(sourceRoot);

            string skillFile = Path.Combine(sourceRoot, "SKILL.md");
            bool hasExactSkillFile = _fileSystem
                .GetFiles(sourceRoot, SearchOption.TopDirectoryOnly)
                .Any(file => string.Equals(
                    Path.GetFileName(file),
                    "SKILL.md",
                    StringComparison.Ordinal));
            if (!hasExactSkillFile || !_fileSystem.FileExists(skillFile))
                throw new InvalidDataException("The skill source must contain an exact SKILL.md file.");
            if (_fileSystem.FileExists(Path.Combine(sourceRoot, ManifestFileName)))
            {
                throw new InvalidDataException(
                    $"{ManifestFileName} is reserved for installed-copy provenance and cannot be part of the source.");
            }

            string contents = _fileSystem.ReadAllText(skillFile);
            Dictionary<string, string> frontmatter = ParseFrontmatter(contents);

            if (!frontmatter.TryGetValue("name", out string name) || string.IsNullOrWhiteSpace(name))
                throw new InvalidDataException("SKILL.md frontmatter must contain a non-empty name.");

            name = Unquote(name.Trim());
            if (name.Length > 64 || !Regex.IsMatch(name, "^[a-z0-9]+(?:-[a-z0-9]+)*$"))
            {
                throw new InvalidDataException(
                    "The skill name must be at most 64 characters and contain only lowercase letters, numbers, and hyphens.");
            }

            string directoryName = new DirectoryInfo(sourceRoot).Name;
            if (!string.Equals(name, directoryName, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    $"The skill name '{name}' must match its parent directory '{directoryName}'.");
            }

            if (!frontmatter.TryGetValue("description", out string description) ||
                string.IsNullOrWhiteSpace(description))
            {
                throw new InvalidDataException("SKILL.md frontmatter must contain a non-empty description.");
            }

            description = NormalizeYamlScalar(description);
            if (string.IsNullOrWhiteSpace(description))
                throw new InvalidDataException("SKILL.md frontmatter must contain a non-empty description.");
            if (description.Length > 1024)
                throw new InvalidDataException("The skill description must not exceed 1024 characters.");

            ValidateMarkdownLinks(sourceRoot, skillFile, contents);

            string[] managedFiles = GetManagedFiles(sourceRoot);
            string contentHash = ComputeContentHash(sourceRoot, managedFiles);
            return new AgentSkillSource(sourceRoot, name, description, contentHash, managedFiles);
        }

        public IReadOnlyList<AgentSkillTargetPlan> Plan(AgentSkillInstallRequest request)
        {
            ValidateRequest(request);
            AgentSkillSource source = ValidateSource(request.SourceDirectory);
            string projectRoot = Path.GetFullPath(request.ProjectRoot);

            return request.Targets
                .Select(target => InspectTarget(projectRoot, target, source, request.PackageVersion))
                .ToArray();
        }

        public AgentSkillInstallReport Install(AgentSkillInstallRequest request)
        {
            ValidateRequest(request);

            AgentSkillSource source = ValidateSource(request.SourceDirectory);
            string projectRoot = Path.GetFullPath(request.ProjectRoot);

            AgentSkillTargetPlan[] plans = request.Targets
                .Select(target => InspectTarget(projectRoot, target, source, request.PackageVersion))
                .ToArray();

            AgentSkillTargetPlan[] blockingConflicts = plans
                .Where(plan => plan.State == AgentSkillDestinationState.Conflict)
                .ToArray();

            if (blockingConflicts.Length > 0 && !request.AllowUnmanagedOverwrite)
            {
                string names = string.Join(", ", blockingConflicts.Select(plan => plan.Target.DisplayName));
                throw new InvalidOperationException(
                    $"Unmanaged or modified skill folders already exist for: {names}. " +
                    "Review them or explicitly allow replacement.");
            }

            var results = new List<AgentSkillInstallResult>();
            var warnings = new List<string>();
            var prepared = new List<PreparedInstall>();

            try
            {
                foreach (AgentSkillTargetPlan plan in plans)
                {
                    if (plan.State == AgentSkillDestinationState.UpToDate)
                    {
                        results.Add(new AgentSkillInstallResult(
                            plan.Target,
                            plan.State,
                            plan.Destination,
                            "already up to date"));
                        continue;
                    }

                    prepared.Add(PrepareInstall(
                        projectRoot,
                        plan,
                        source,
                        request.PackageVersion,
                        request.AllowUnmanagedOverwrite));
                }

                CommitAll(prepared);
            }
            catch
            {
                CleanupPreparedDirectories(prepared);
                throw;
            }

            foreach (PreparedInstall install in prepared)
            {
                string action = install.Plan.State == AgentSkillDestinationState.Missing
                    ? "installed"
                    : "updated";

                results.Add(new AgentSkillInstallResult(
                    install.Plan.Target,
                    install.Plan.State,
                    install.Plan.Destination,
                    action));
            }

            CleanupBackups(prepared, warnings);

            if (request.RemoveLegacyBridges)
                warnings.AddRange(CleanupLegacyBridges(projectRoot, request.Targets));

            return new AgentSkillInstallReport(results, warnings);
        }

        public IReadOnlyList<AgentSkillUninstallTargetPlan> PlanUninstall(
            AgentSkillUninstallRequest request)
        {
            ValidateUninstallRequest(request);
            string projectRoot = Path.GetFullPath(request.ProjectRoot);

            return request.Targets
                .Select(target => InspectUninstallTarget(projectRoot, target))
                .ToArray();
        }

        public AgentSkillUninstallReport Uninstall(AgentSkillUninstallRequest request)
        {
            ValidateUninstallRequest(request);
            string projectRoot = Path.GetFullPath(request.ProjectRoot);
            AgentSkillUninstallTargetPlan[] plans = request.Targets
                .Select(target => InspectUninstallTarget(projectRoot, target))
                .ToArray();

            AgentSkillUninstallTargetPlan[] modified = plans
                .Where(plan => plan.State == AgentSkillUninstallState.ManagedModified)
                .ToArray();
            if (modified.Length > 0 && !request.AllowModifiedManagedFiles)
            {
                string names = string.Join(", ", modified.Select(plan => plan.Target.DisplayName));
                throw new InvalidOperationException(
                    $"Locally modified managed files exist for: {names}. " +
                    "Review them or explicitly allow their removal.");
            }

            var results = new List<AgentSkillUninstallResult>();
            var warnings = new List<string>();
            var prepared = new List<PreparedUninstall>();

            try
            {
                foreach (AgentSkillUninstallTargetPlan plan in plans)
                {
                    switch (plan.State)
                    {
                        case AgentSkillUninstallState.Missing:
                            results.Add(new AgentSkillUninstallResult(
                                plan.Target,
                                plan.State,
                                plan.Destination,
                                "already absent"));
                            break;
                        case AgentSkillUninstallState.Unmanaged:
                            results.Add(new AgentSkillUninstallResult(
                                plan.Target,
                                plan.State,
                                plan.Destination,
                                "kept unmanaged folder"));
                            warnings.Add(
                                $"{plan.Target.DisplayName} was not removed: {plan.Detail}");
                            break;
                        case AgentSkillUninstallState.ManagedClean:
                        case AgentSkillUninstallState.ManagedModified:
                            prepared.Add(PrepareUninstall(projectRoot, plan));
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }

                CommitAllUninstalls(prepared);
            }
            catch
            {
                CleanupPreparedUninstallDirectories(prepared);
                throw;
            }

            foreach (PreparedUninstall uninstall in prepared)
            {
                results.Add(new AgentSkillUninstallResult(
                    uninstall.Plan.Target,
                    uninstall.Plan.State,
                    uninstall.Plan.Destination,
                    uninstall.PreserveDestination
                        ? "removed managed files; kept local extras"
                        : "removed"));
            }

            CleanupUninstallBackups(prepared, warnings);
            return new AgentSkillUninstallReport(results, warnings);
        }

        private void ValidateRequest(AgentSkillInstallRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            ValidateProjectAndTargets(request.ProjectRoot, request.Targets, nameof(request));
        }

        private void ValidateUninstallRequest(AgentSkillUninstallRequest request)
        {
            if (request == null)
                throw new ArgumentNullException(nameof(request));
            ValidateProjectAndTargets(request.ProjectRoot, request.Targets, nameof(request));
        }

        private void ValidateProjectAndTargets(
            string projectRootValue,
            IReadOnlyList<AgentSkillTarget> targets,
            string parameterName)
        {
            if (string.IsNullOrWhiteSpace(projectRootValue))
                throw new ArgumentException("A project root is required.", parameterName);
            if (!_fileSystem.DirectoryExists(Path.GetFullPath(projectRootValue)))
                throw new DirectoryNotFoundException($"Project root does not exist: {projectRootValue}");
            if (targets == null || targets.Count == 0)
                throw new ArgumentException("Select at least one agent target.", parameterName);
            if (targets.Any(target => target == null))
                throw new ArgumentException("Agent targets cannot be null.", parameterName);
            if (targets.GroupBy(target => target.Provider).Any(group => group.Count() > 1))
                throw new ArgumentException("Each agent provider can only be selected once.", parameterName);

            string projectRoot = Path.GetFullPath(projectRootValue);
            if (targets
                .Select(target => target.GetDestination(projectRoot))
                .Distinct(AgentSkillPath.PathComparer)
                .Count() != targets.Count)
            {
                throw new ArgumentException(
                    "Selected agent targets must resolve to unique directories.",
                    parameterName);
            }
        }

        private AgentSkillTargetPlan InspectTarget(
            string projectRoot,
            AgentSkillTarget target,
            AgentSkillSource source,
            string packageVersion)
        {
            string destination = target.GetDestination(projectRoot);
            AgentSkillPath.EnsureDirectoryIsWithin(projectRoot, destination);
            EnsurePathHasNoReparsePoints(projectRoot, destination);

            if (!_fileSystem.DirectoryExists(destination))
            {
                return new AgentSkillTargetPlan(
                    target,
                    destination,
                    AgentSkillDestinationState.Missing,
                    "The native skill folder will be created.");
            }

            EnsureDirectoryTreeHasNoReparsePoints(destination);

            string manifestPath = Path.Combine(destination, ManifestFileName);
            if (_fileSystem.FileExists(manifestPath))
            {
                AgentSkillInstallManifest manifest;
                try
                {
                    manifest = JsonUtility.FromJson<AgentSkillInstallManifest>(
                        _fileSystem.ReadAllText(manifestPath));
                }
                catch (Exception ex)
                {
                    return new AgentSkillTargetPlan(
                        target,
                        destination,
                        AgentSkillDestinationState.Conflict,
                        "The existing ProxyCore ownership manifest is invalid: " + ex.Message);
                }

                if (!IsOwnedManifest(manifest, source.Name, target))
                {
                    return new AgentSkillTargetPlan(
                        target,
                        destination,
                        AgentSkillDestinationState.Conflict,
                        "The existing ownership manifest does not belong to this ProxyCore skill.");
                }

                if (!TryComputeInstalledHash(destination, manifest.managedFiles, out string installedHash) ||
                    !string.Equals(installedHash, manifest.contentHash, StringComparison.OrdinalIgnoreCase))
                {
                    return new AgentSkillTargetPlan(
                        target,
                        destination,
                        AgentSkillDestinationState.Conflict,
                        "Files managed by a previous install were modified or are missing.");
                }

                var previouslyManaged = new HashSet<string>(
                    manifest.managedFiles.Select(AgentSkillPath.NormalizeRelative),
                    AgentSkillPath.PathComparer);
                string[] ownershipCollisions = source.ManagedFiles
                    .Where(relative =>
                        !previouslyManaged.Contains(relative) &&
                        (
                            _fileSystem.FileExists(Path.Combine(
                                destination,
                                AgentSkillPath.ToPlatformPath(relative))) ||
                            _fileSystem.DirectoryExists(Path.Combine(
                                destination,
                                AgentSkillPath.ToPlatformPath(relative)))
                        ))
                    .ToArray();
                if (ownershipCollisions.Length > 0)
                {
                    return new AgentSkillTargetPlan(
                        target,
                        destination,
                        AgentSkillDestinationState.Conflict,
                        "The update would replace locally owned files now present in the " +
                        "managed payload: " + string.Join(", ", ownershipCollisions));
                }

                bool sameContent = string.Equals(
                    installedHash,
                    source.ContentHash,
                    StringComparison.OrdinalIgnoreCase);
                bool samePackage = string.Equals(
                    manifest.packageVersion ?? string.Empty,
                    packageVersion ?? string.Empty,
                    StringComparison.Ordinal);

                return new AgentSkillTargetPlan(
                    target,
                    destination,
                    sameContent && samePackage
                        ? AgentSkillDestinationState.UpToDate
                        : AgentSkillDestinationState.ManagedUpdate,
                    sameContent && samePackage
                        ? "The managed installation is current."
                        : "A managed installation will be updated atomically.");
            }

            if (TryComputeInstalledHash(destination, source.ManagedFiles, out string legacyHash) &&
                string.Equals(legacyHash, source.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                return new AgentSkillTargetPlan(
                    target,
                    destination,
                    AgentSkillDestinationState.LegacyExactCopy,
                    "An exact pre-manifest ProxyCore copy will be adopted; extra files will be preserved.");
            }

            return new AgentSkillTargetPlan(
                target,
                destination,
                AgentSkillDestinationState.Conflict,
                "An untracked or locally modified skill folder already exists.");
        }

        private AgentSkillUninstallTargetPlan InspectUninstallTarget(
            string projectRoot,
            AgentSkillTarget target)
        {
            string destination = target.GetDestination(projectRoot);
            AgentSkillPath.EnsureDirectoryIsWithin(projectRoot, destination);
            EnsurePathHasNoReparsePoints(projectRoot, destination);

            if (!_fileSystem.DirectoryExists(destination))
            {
                return new AgentSkillUninstallTargetPlan(
                    target,
                    destination,
                    AgentSkillUninstallState.Missing,
                    "The native skill folder is already absent.");
            }

            EnsureDirectoryTreeHasNoReparsePoints(destination);

            string manifestPath = Path.Combine(destination, ManifestFileName);
            if (!_fileSystem.FileExists(manifestPath))
            {
                return new AgentSkillUninstallTargetPlan(
                    target,
                    destination,
                    AgentSkillUninstallState.Unmanaged,
                    "no ProxyCore ownership manifest was found");
            }

            AgentSkillInstallManifest manifest;
            try
            {
                manifest = JsonUtility.FromJson<AgentSkillInstallManifest>(
                    _fileSystem.ReadAllText(manifestPath));
            }
            catch (Exception ex)
            {
                return new AgentSkillUninstallTargetPlan(
                    target,
                    destination,
                    AgentSkillUninstallState.Unmanaged,
                    "the ownership manifest is invalid: " + ex.Message);
            }

            if (!IsOwnedManifest(manifest, "proxycore", target))
            {
                return new AgentSkillUninstallTargetPlan(
                    target,
                    destination,
                    AgentSkillUninstallState.Unmanaged,
                    "the ownership manifest does not belong to this ProxyCore target");
            }

            bool isClean =
                TryComputeInstalledHash(destination, manifest.managedFiles, out string installedHash) &&
                string.Equals(
                    installedHash,
                    manifest.contentHash,
                    StringComparison.OrdinalIgnoreCase);

            return new AgentSkillUninstallTargetPlan(
                target,
                destination,
                isClean
                    ? AgentSkillUninstallState.ManagedClean
                    : AgentSkillUninstallState.ManagedModified,
                isClean
                    ? "Only files recorded in the ownership manifest will be removed."
                    : "One or more managed files were modified or are missing.",
                manifest);
        }

        private PreparedInstall PrepareInstall(
            string projectRoot,
            AgentSkillTargetPlan plan,
            AgentSkillSource source,
            string packageVersion,
            bool allowUnmanagedOverwrite)
        {
            string destinationParent = Path.GetDirectoryName(plan.Destination);
            if (string.IsNullOrEmpty(destinationParent))
                throw new InvalidOperationException($"Cannot resolve destination parent: {plan.Destination}");

            AgentSkillPath.EnsureDirectoryIsWithin(projectRoot, destinationParent, allowEqual: true);
            EnsurePathHasNoReparsePoints(projectRoot, plan.Destination);
            if (_fileSystem.DirectoryExists(plan.Destination))
                EnsureDirectoryTreeHasNoReparsePoints(plan.Destination);
            _fileSystem.CreateDirectory(destinationParent);

            string workspace = CreateOperationWorkspace(
                projectRoot,
                "install",
                plan.Target.Provider);
            string stage = Path.Combine(workspace, "stage");
            string backup = Path.Combine(workspace, "backup");

            try
            {
                CopySourceToStage(source, stage);
                PreserveUnmanagedFiles(plan, source, stage, allowUnmanagedOverwrite);

                var manifest = new AgentSkillInstallManifest
                {
                    schemaVersion = ManifestSchemaVersion,
                    skillId = source.Name,
                    packageId = PackageId,
                    packageVersion = packageVersion ?? string.Empty,
                    target = TargetId(plan.Target.Provider),
                    contentHash = source.ContentHash,
                    managedFiles = source.ManagedFiles.ToArray()
                };

                string stagedManifestPath = Path.Combine(stage, ManifestFileName);
                _fileSystem.WriteAllText(stagedManifestPath, JsonUtility.ToJson(manifest, true) + "\n");

                AgentSkillInstallManifest manifestReadBack =
                    JsonUtility.FromJson<AgentSkillInstallManifest>(
                        _fileSystem.ReadAllText(stagedManifestPath));
                if (!IsOwnedManifest(manifestReadBack, source.Name, plan.Target))
                    throw new IOException($"Staged ownership manifest validation failed for {plan.Target.DisplayName}.");

                if (!TryComputeInstalledHash(stage, source.ManagedFiles, out string stagedHash) ||
                    !string.Equals(stagedHash, source.ContentHash, StringComparison.OrdinalIgnoreCase))
                {
                    throw new IOException($"Staged copy validation failed for {plan.Target.DisplayName}.");
                }

                return new PreparedInstall(plan, workspace, stage, backup);
            }
            catch
            {
                if (_fileSystem.DirectoryExists(workspace))
                {
                    try
                    {
                        _fileSystem.DeleteDirectory(workspace, recursive: true);
                    }
                    catch
                    {
                        // Preserve the preparation exception.
                    }
                }
                throw;
            }
        }

        private void CopySourceToStage(AgentSkillSource source, string stage)
        {
            _fileSystem.CreateDirectory(stage);

            foreach (string directory in GetSafeDirectories(source.Root))
            {
                string relative = AgentSkillPath.ToRelative(source.Root, directory);
                if (AgentSkillPath.IsMetaPath(relative))
                    continue;
                _fileSystem.CreateDirectory(Path.Combine(stage, AgentSkillPath.ToPlatformPath(relative)));
            }

            foreach (string relative in source.ManagedFiles)
            {
                string sourceFile = Path.Combine(source.Root, AgentSkillPath.ToPlatformPath(relative));
                string destinationFile = Path.Combine(stage, AgentSkillPath.ToPlatformPath(relative));
                string destinationDirectory = Path.GetDirectoryName(destinationFile);
                if (!string.IsNullOrEmpty(destinationDirectory))
                    _fileSystem.CreateDirectory(destinationDirectory);
                _fileSystem.WriteAllBytes(destinationFile, _fileSystem.ReadAllBytes(sourceFile));
            }
        }

        private void PreserveUnmanagedFiles(
            AgentSkillTargetPlan plan,
            AgentSkillSource source,
            string stage,
            bool allowUnmanagedOverwrite)
        {
            if (!_fileSystem.DirectoryExists(plan.Destination))
                return;

            var managedBefore = new HashSet<string>(AgentSkillPath.PathComparer);
            string manifestPath = Path.Combine(plan.Destination, ManifestFileName);

            if (_fileSystem.FileExists(manifestPath))
            {
                AgentSkillInstallManifest manifest = null;
                try
                {
                    manifest = JsonUtility.FromJson<AgentSkillInstallManifest>(
                        _fileSystem.ReadAllText(manifestPath));
                }
                catch
                {
                    // Planning already classified this as an explicit conflict.
                    // Treat the manifest as unowned while preserving extra files.
                }

                if (IsOwnedManifest(manifest, source.Name, plan.Target))
                {
                    foreach (string relative in manifest.managedFiles)
                        managedBefore.Add(AgentSkillPath.NormalizeRelative(relative));
                }
            }
            else if (plan.State == AgentSkillDestinationState.LegacyExactCopy)
            {
                foreach (string relative in source.ManagedFiles)
                    managedBefore.Add(AgentSkillPath.NormalizeRelative(relative));
            }
            else if (plan.State == AgentSkillDestinationState.Conflict && !allowUnmanagedOverwrite)
            {
                return;
            }

            var newManaged = new HashSet<string>(
                source.ManagedFiles.Select(AgentSkillPath.NormalizeRelative),
                AgentSkillPath.PathComparer);

            foreach (string directory in GetSafeDirectories(plan.Destination))
            {
                string relative = AgentSkillPath.ToRelative(plan.Destination, directory);
                _fileSystem.CreateDirectory(Path.Combine(stage, AgentSkillPath.ToPlatformPath(relative)));
            }

            foreach (string existingFile in GetSafeFiles(plan.Destination))
            {
                string relative = AgentSkillPath.NormalizeRelative(
                    AgentSkillPath.ToRelative(plan.Destination, existingFile));

                if (AgentSkillPath.PathComparer.Equals(relative, ManifestFileName))
                    continue;
                if (managedBefore.Contains(relative))
                    continue;
                if (newManaged.Contains(relative))
                    continue;

                string preservedFile = Path.Combine(stage, AgentSkillPath.ToPlatformPath(relative));
                string preservedDirectory = Path.GetDirectoryName(preservedFile);
                if (!string.IsNullOrEmpty(preservedDirectory))
                    _fileSystem.CreateDirectory(preservedDirectory);
                _fileSystem.WriteAllBytes(preservedFile, _fileSystem.ReadAllBytes(existingFile));
            }
        }

        private PreparedUninstall PrepareUninstall(
            string projectRoot,
            AgentSkillUninstallTargetPlan plan)
        {
            string destinationParent = Path.GetDirectoryName(plan.Destination);
            if (string.IsNullOrEmpty(destinationParent))
                throw new InvalidOperationException($"Cannot resolve destination parent: {plan.Destination}");

            AgentSkillPath.EnsureDirectoryIsWithin(projectRoot, destinationParent, allowEqual: true);
            EnsurePathHasNoReparsePoints(projectRoot, plan.Destination);
            EnsureDirectoryTreeHasNoReparsePoints(plan.Destination);
            string workspace = CreateOperationWorkspace(
                projectRoot,
                "uninstall",
                plan.Target.Provider);
            string stage = Path.Combine(workspace, "stage");
            string backup = Path.Combine(workspace, "backup");

            var managed = new HashSet<string>(
                plan.Manifest.managedFiles.Select(AgentSkillPath.NormalizeRelative),
                AgentSkillPath.PathComparer);

            try
            {
                _fileSystem.CreateDirectory(stage);

                foreach (string existingFile in GetSafeFiles(plan.Destination))
                {
                    string relative = AgentSkillPath.NormalizeRelative(
                        AgentSkillPath.ToRelative(plan.Destination, existingFile));
                    if (AgentSkillPath.PathComparer.Equals(relative, ManifestFileName) ||
                        managed.Contains(relative))
                    {
                        continue;
                    }

                    string preservedFile = Path.Combine(
                        stage,
                        AgentSkillPath.ToPlatformPath(relative));
                    string preservedDirectory = Path.GetDirectoryName(preservedFile);
                    if (!string.IsNullOrEmpty(preservedDirectory))
                        _fileSystem.CreateDirectory(preservedDirectory);
                    _fileSystem.WriteAllBytes(
                        preservedFile,
                        _fileSystem.ReadAllBytes(existingFile));
                }

                bool preserveDestination =
                    _fileSystem.GetFiles(stage, SearchOption.AllDirectories).Length > 0;
                return new PreparedUninstall(
                    plan,
                    workspace,
                    stage,
                    backup,
                    preserveDestination);
            }
            catch
            {
                if (_fileSystem.DirectoryExists(workspace))
                {
                    try
                    {
                        _fileSystem.DeleteDirectory(workspace, recursive: true);
                    }
                    catch
                    {
                        // Preserve the preparation exception.
                    }
                }
                throw;
            }
        }

        private string CreateOperationWorkspace(
            string projectRoot,
            string operation,
            AgentSkillProvider provider)
        {
            string workspaceParent = Path.Combine(
                projectRoot,
                "Library",
                "ProxyCore",
                "AgentSkillInstaller");
            EnsurePathHasNoReparsePoints(projectRoot, workspaceParent);

            string workspace = Path.Combine(
                workspaceParent,
                operation + "-" + TargetId(provider) + "-" + Guid.NewGuid().ToString("N"));
            AgentSkillPath.EnsureDirectoryIsWithin(projectRoot, workspace);
            _fileSystem.CreateDirectory(workspace);
            return workspace;
        }

        private void CommitAll(IReadOnlyList<PreparedInstall> prepared)
        {
            var touched = new List<PreparedInstall>();

            try
            {
                foreach (PreparedInstall install in prepared)
                {
                    touched.Add(install);

                    if (_fileSystem.DirectoryExists(install.Plan.Destination))
                    {
                        _fileSystem.MoveDirectory(install.Plan.Destination, install.Backup);
                        install.BackupCreated = true;
                    }

                    _fileSystem.MoveDirectory(install.Stage, install.Plan.Destination);
                    install.Committed = true;
                }
            }
            catch (Exception commitException)
            {
                var rollbackErrors = new List<Exception>();

                for (int i = touched.Count - 1; i >= 0; i--)
                {
                    PreparedInstall install = touched[i];
                    try
                    {
                        bool backupExists = _fileSystem.DirectoryExists(install.Backup);
                        if ((install.Committed || install.BackupCreated || backupExists) &&
                            _fileSystem.DirectoryExists(install.Plan.Destination))
                        {
                            _fileSystem.DeleteDirectory(install.Plan.Destination, recursive: true);
                        }

                        if (backupExists)
                        {
                            _fileSystem.MoveDirectory(install.Backup, install.Plan.Destination);
                            install.BackupCreated = false;
                        }

                        install.Committed = false;
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackErrors.Add(new IOException(
                            $"Could not restore {install.TargetName}. Its backup may remain at " +
                            $"{install.Backup}.",
                            rollbackException));
                    }
                }

                if (rollbackErrors.Count > 0)
                {
                    throw new AggregateException(
                        "Agent skill installation failed and rollback was incomplete.",
                        new[] { commitException }.Concat(rollbackErrors));
                }

                throw new IOException(
                    "Agent skill installation failed; all committed targets were rolled back.",
                    commitException);
            }
        }

        private void CommitAllUninstalls(IReadOnlyList<PreparedUninstall> prepared)
        {
            var touched = new List<PreparedUninstall>();

            try
            {
                foreach (PreparedUninstall uninstall in prepared)
                {
                    touched.Add(uninstall);
                    _fileSystem.MoveDirectory(uninstall.Plan.Destination, uninstall.Backup);
                    uninstall.BackupCreated = true;

                    if (uninstall.PreserveDestination)
                        _fileSystem.MoveDirectory(uninstall.Stage, uninstall.Plan.Destination);

                    uninstall.Committed = true;
                }
            }
            catch (Exception commitException)
            {
                var rollbackErrors = new List<Exception>();

                for (int i = touched.Count - 1; i >= 0; i--)
                {
                    PreparedUninstall uninstall = touched[i];
                    try
                    {
                        bool backupExists = _fileSystem.DirectoryExists(uninstall.Backup);
                        if ((uninstall.Committed || uninstall.BackupCreated || backupExists) &&
                            _fileSystem.DirectoryExists(uninstall.Plan.Destination))
                        {
                            _fileSystem.DeleteDirectory(
                                uninstall.Plan.Destination,
                                recursive: true);
                        }

                        if (backupExists)
                        {
                            _fileSystem.MoveDirectory(
                                uninstall.Backup,
                                uninstall.Plan.Destination);
                            uninstall.BackupCreated = false;
                        }

                        uninstall.Committed = false;
                    }
                    catch (Exception rollbackException)
                    {
                        rollbackErrors.Add(new IOException(
                            $"Could not restore {uninstall.TargetName}. Its backup may remain at " +
                            $"{uninstall.Backup}.",
                            rollbackException));
                    }
                }

                if (rollbackErrors.Count > 0)
                {
                    throw new AggregateException(
                        "Agent skill uninstall failed and rollback was incomplete.",
                        new[] { commitException }.Concat(rollbackErrors));
                }

                throw new IOException(
                    "Agent skill uninstall failed; all committed targets were rolled back.",
                    commitException);
            }
        }

        private void CleanupBackups(IReadOnlyList<PreparedInstall> prepared, ICollection<string> warnings)
        {
            foreach (PreparedInstall install in prepared)
            {
                if (install.BackupCreated && _fileSystem.DirectoryExists(install.Backup))
                {
                    try
                    {
                        _fileSystem.DeleteDirectory(install.Backup, recursive: true);
                        install.BackupCreated = false;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(
                            $"{install.TargetName} installed successfully, but its temporary " +
                            $"backup was preserved at {install.Backup}: {ex.Message}");
                    }
                }

                CleanupSuccessfulWorkspace(
                    install.Workspace,
                    install.Backup,
                    install.TargetName,
                    warnings);
            }
        }

        private void CleanupUninstallBackups(
            IReadOnlyList<PreparedUninstall> prepared,
            ICollection<string> warnings)
        {
            foreach (PreparedUninstall uninstall in prepared)
            {
                if (uninstall.BackupCreated && _fileSystem.DirectoryExists(uninstall.Backup))
                {
                    try
                    {
                        _fileSystem.DeleteDirectory(uninstall.Backup, recursive: true);
                        uninstall.BackupCreated = false;
                    }
                    catch (Exception ex)
                    {
                        warnings.Add(
                            $"{uninstall.TargetName} was uninstalled, but its temporary backup " +
                            $"was preserved at {uninstall.Backup}: {ex.Message}");
                    }
                }

                CleanupSuccessfulWorkspace(
                    uninstall.Workspace,
                    uninstall.Backup,
                    uninstall.TargetName,
                    warnings);
            }
        }

        private void CleanupSuccessfulWorkspace(
            string workspace,
            string backup,
            string targetName,
            ICollection<string> warnings)
        {
            if (_fileSystem.DirectoryExists(backup) || !_fileSystem.DirectoryExists(workspace))
                return;

            try
            {
                _fileSystem.DeleteDirectory(workspace, recursive: true);
            }
            catch (Exception ex)
            {
                warnings.Add(
                    $"{targetName} completed, but its temporary workspace could not be " +
                    $"removed at {workspace}: {ex.Message}");
            }
        }

        private void CleanupPreparedDirectories(IReadOnlyList<PreparedInstall> prepared)
        {
            foreach (PreparedInstall install in prepared)
            {
                if (_fileSystem.DirectoryExists(install.Backup) ||
                    !_fileSystem.DirectoryExists(install.Workspace))
                {
                    continue;
                }

                try
                {
                    _fileSystem.DeleteDirectory(install.Workspace, recursive: true);
                }
                catch
                {
                    // Preserve the original installation exception.
                }
            }
        }

        private void CleanupPreparedUninstallDirectories(
            IReadOnlyList<PreparedUninstall> prepared)
        {
            foreach (PreparedUninstall uninstall in prepared)
            {
                if (_fileSystem.DirectoryExists(uninstall.Backup) ||
                    !_fileSystem.DirectoryExists(uninstall.Workspace))
                {
                    continue;
                }

                try
                {
                    _fileSystem.DeleteDirectory(uninstall.Workspace, recursive: true);
                }
                catch
                {
                    // Preserve the original uninstall exception.
                }
            }
        }

        private IReadOnlyList<string> CleanupLegacyBridges(
            string projectRoot,
            IReadOnlyList<AgentSkillTarget> targets)
        {
            var warnings = new List<string>();
            var selected = new HashSet<AgentSkillProvider>(targets.Select(target => target.Provider));

            if (selected.Contains(AgentSkillProvider.GitHubCopilot))
            {
                try
                {
                    CleanupLegacyCopilotInstruction(projectRoot, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add("Could not clean up the legacy GitHub Copilot pointer: " + ex.Message);
                }
            }

            if (selected.Contains(AgentSkillProvider.OpenAICodex))
            {
                try
                {
                    CleanupLegacyCodexBlock(projectRoot, warnings);
                }
                catch (Exception ex)
                {
                    warnings.Add("Could not clean up the legacy Codex AGENTS.md block: " + ex.Message);
                }
            }

            return warnings;
        }

        private void CleanupLegacyCopilotInstruction(string projectRoot, ICollection<string> warnings)
        {
            string file = Path.Combine(
                projectRoot,
                ".github",
                "instructions",
                "proxycore.instructions.md");

            if (!_fileSystem.FileExists(file))
                return;

            string existing = NormalizeNewlines(_fileSystem.ReadAllText(file));
            string generated = NormalizeNewlines(LegacyCopilotInstruction());

            if (!string.Equals(existing, generated, StringComparison.Ordinal))
            {
                warnings.Add(
                    "Kept .github/instructions/proxycore.instructions.md because it no longer " +
                    "matches the legacy installer-owned content.");
                return;
            }

            ReplaceOrDeleteLegacyFile(
                projectRoot,
                file,
                null,
                "GitHub Copilot",
                AgentSkillProvider.GitHubCopilot,
                warnings);
        }

        private void CleanupLegacyCodexBlock(string projectRoot, ICollection<string> warnings)
        {
            const string blockBegin = "<!-- BEGIN ProxyCore Agent Skill -->";
            const string blockEnd = "<!-- END ProxyCore Agent Skill -->";
            string file = Path.Combine(projectRoot, "AGENTS.md");

            if (!_fileSystem.FileExists(file))
                return;

            string existing = _fileSystem.ReadAllText(file);
            int beginCount = CountOccurrences(existing, blockBegin);
            int endCount = CountOccurrences(existing, blockEnd);

            if (beginCount == 0 && endCount == 0)
                return;
            if (beginCount != 1 || endCount != 1)
            {
                warnings.Add(
                    "Kept the legacy ProxyCore AGENTS.md block because its ownership markers are malformed or duplicated.");
                return;
            }

            int begin = existing.IndexOf(blockBegin, StringComparison.Ordinal);
            int end = existing.IndexOf(blockEnd, begin, StringComparison.Ordinal);
            if (end < begin)
            {
                warnings.Add(
                    "Kept the legacy ProxyCore AGENTS.md block because its ownership markers are out of order.");
                return;
            }

            int afterEnd = end + blockEnd.Length;
            string updated = existing.Remove(begin, afterEnd - begin);

            ReplaceOrDeleteLegacyFile(
                projectRoot,
                file,
                string.IsNullOrWhiteSpace(updated) ? null : updated,
                "OpenAI Codex",
                AgentSkillProvider.OpenAICodex,
                warnings);
        }

        private void ReplaceOrDeleteLegacyFile(
            string projectRoot,
            string file,
            string updatedContents,
            string displayName,
            AgentSkillProvider provider,
            ICollection<string> warnings)
        {
            string parent = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(parent))
                throw new InvalidOperationException($"Cannot resolve file parent: {file}");

            AgentSkillPath.EnsureDirectoryIsWithin(projectRoot, file);
            EnsurePathHasNoReparsePoints(projectRoot, parent);
            if ((_fileSystem.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Legacy cleanup refuses symbolic links and reparse points: {file}");
            }

            string workspace = CreateOperationWorkspace(projectRoot, "legacy", provider);
            string stage = Path.Combine(workspace, "stage.md");
            string backup = Path.Combine(workspace, "backup.md");

            try
            {
                if (updatedContents != null)
                    _fileSystem.WriteAllText(stage, updatedContents);

                _fileSystem.MoveFile(file, backup);

                if (updatedContents != null)
                    _fileSystem.MoveFile(stage, file);
            }
            catch (Exception commitException)
            {
                try
                {
                    if (_fileSystem.FileExists(backup))
                    {
                        if (_fileSystem.FileExists(file))
                            _fileSystem.DeleteFile(file);

                        _fileSystem.MoveFile(backup, file);
                    }
                }
                catch (Exception rollbackException)
                {
                    throw new AggregateException(
                        $"{displayName} legacy cleanup failed and rollback was incomplete. " +
                        $"The original may remain at {backup}.",
                        commitException,
                        rollbackException);
                }
                finally
                {
                    if (!_fileSystem.FileExists(backup) &&
                        _fileSystem.DirectoryExists(workspace))
                    {
                        try
                        {
                            _fileSystem.DeleteDirectory(workspace, recursive: true);
                        }
                        catch
                        {
                            // Preserve the original cleanup exception.
                        }
                    }
                }

                throw new IOException(
                    $"{displayName} legacy cleanup failed; the original file was restored.",
                    commitException);
            }

            try
            {
                if (_fileSystem.FileExists(backup))
                    _fileSystem.DeleteFile(backup);
            }
            catch (Exception ex)
            {
                warnings.Add(
                    $"{displayName} legacy cleanup completed, but its recoverable backup " +
                    $"was preserved at {backup}: {ex.Message}");
            }

            if (!_fileSystem.FileExists(backup) &&
                _fileSystem.DirectoryExists(workspace))
            {
                try
                {
                    _fileSystem.DeleteDirectory(workspace, recursive: true);
                }
                catch (Exception ex)
                {
                    warnings.Add(
                        $"{displayName} legacy cleanup completed, but its temporary workspace " +
                        $"could not be removed at {workspace}: {ex.Message}");
                }
            }
        }

        private void ValidateMarkdownLinks(string sourceRoot, string skillFile, string contents)
        {
            MatchCollection links = Regex.Matches(contents, @"\[[^\]]*\]\(([^)]+)\)");
            foreach (Match link in links)
            {
                string target = link.Groups[1].Value.Trim().Trim('<', '>');
                if (target.Length == 0 ||
                    target.StartsWith("#", StringComparison.Ordinal) ||
                    target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase) ||
                    target.Contains("://"))
                {
                    continue;
                }

                int fragment = target.IndexOfAny(new[] { '#', '?' });
                if (fragment >= 0)
                    target = target.Substring(0, fragment);

                string resolved = Path.GetFullPath(Path.Combine(
                    Path.GetDirectoryName(skillFile) ?? sourceRoot,
                    AgentSkillPath.ToPlatformPath(target)));
                AgentSkillPath.EnsureDirectoryIsWithin(sourceRoot, resolved, allowEqual: true);

                if (!_fileSystem.FileExists(resolved) && !_fileSystem.DirectoryExists(resolved))
                    throw new InvalidDataException($"SKILL.md references a missing local resource: {target}");
            }
        }

        private Dictionary<string, string> ParseFrontmatter(string contents)
        {
            string[] lines = NormalizeNewlines(contents).Split('\n');
            if (lines.Length < 3 || !string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
                throw new InvalidDataException("SKILL.md must begin with YAML frontmatter.");

            int closing = -1;
            for (int i = 1; i < lines.Length; i++)
            {
                if (string.Equals(lines[i].Trim(), "---", StringComparison.Ordinal))
                {
                    closing = i;
                    break;
                }
            }

            if (closing < 0)
                throw new InvalidDataException("SKILL.md YAML frontmatter is not closed.");

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            string currentKey = null;
            var currentValue = new StringBuilder();

            for (int i = 1; i < closing; i++)
            {
                string line = lines[i];
                Match keyMatch = Regex.Match(line, @"^([A-Za-z0-9_-]+):(?:\s*(.*))?$");
                if (keyMatch.Success)
                {
                    StoreFrontmatterValue(values, currentKey, currentValue);
                    currentKey = keyMatch.Groups[1].Value;
                    currentValue.Clear();
                    currentValue.Append(keyMatch.Groups[2].Value);
                }
                else if (currentKey != null && (line.StartsWith(" ") || line.StartsWith("\t")))
                {
                    if (currentValue.Length > 0)
                        currentValue.Append('\n');
                    currentValue.Append(line.Trim());
                }
            }

            StoreFrontmatterValue(values, currentKey, currentValue);
            return values;
        }

        private static void StoreFrontmatterValue(
            IDictionary<string, string> values,
            string key,
            StringBuilder value)
        {
            if (!string.IsNullOrEmpty(key))
                values[key] = value.ToString();
        }

        private static string NormalizeYamlScalar(string value)
        {
            string normalized = value.Trim();
            if (normalized == ">" || normalized == ">-" || normalized == "|" || normalized == "|-")
                return string.Empty;

            if (normalized.StartsWith(">-\n", StringComparison.Ordinal) ||
                normalized.StartsWith(">\n", StringComparison.Ordinal))
            {
                int newline = normalized.IndexOf('\n');
                return Regex.Replace(normalized.Substring(newline + 1), @"\s+", " ").Trim();
            }

            if (normalized.StartsWith("|-\n", StringComparison.Ordinal) ||
                normalized.StartsWith("|\n", StringComparison.Ordinal))
            {
                int newline = normalized.IndexOf('\n');
                return normalized.Substring(newline + 1).Trim();
            }

            return Unquote(normalized);
        }

        private void EnsurePathHasNoReparsePoints(string trustedRoot, string candidate)
        {
            string root = Path.GetFullPath(trustedRoot)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullCandidate = Path.GetFullPath(candidate);
            AgentSkillPath.EnsureDirectoryIsWithin(root, fullCandidate, allowEqual: true);

            string relative = Path.GetRelativePath(root, fullCandidate);
            if (string.Equals(relative, ".", StringComparison.Ordinal))
                return;

            string current = root;
            foreach (string segment in relative.Split(
                         new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                         StringSplitOptions.RemoveEmptyEntries))
            {
                current = Path.Combine(current, segment);
                bool directoryExists = _fileSystem.DirectoryExists(current);
                bool fileExists = _fileSystem.FileExists(current);
                if (!directoryExists && !fileExists)
                    break;

                if ((_fileSystem.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidOperationException(
                        $"Agent skill operations refuse symbolic links and reparse points: {current}");
                }

                if (fileExists)
                {
                    throw new InvalidOperationException(
                        $"An agent skill directory path is occupied by a regular file: {current}");
                }
            }
        }

        private void EnsureDirectoryTreeHasNoReparsePoints(string root)
        {
            if ((_fileSystem.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            {
                throw new InvalidOperationException(
                    $"Agent skill operations refuse symbolic links and reparse points: {root}");
            }

            // Enumeration is breadth-first and only recurses after checking each
            // directory, so a junction can never redirect traversal outside root.
            GetSafeDirectories(root);
            GetSafeFiles(root);
        }

        private IReadOnlyList<string> GetSafeDirectories(string root)
        {
            var directories = new List<string>();
            var pending = new Queue<string>();
            pending.Enqueue(root);

            while (pending.Count > 0)
            {
                string parent = pending.Dequeue();
                foreach (string directory in _fileSystem.GetDirectories(
                             parent,
                             SearchOption.TopDirectoryOnly))
                {
                    if ((_fileSystem.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            "Agent skill operations refuse symbolic links and reparse points: " +
                            directory);
                    }

                    directories.Add(directory);
                    pending.Enqueue(directory);
                }
            }

            return directories;
        }

        private IReadOnlyList<string> GetSafeFiles(string root)
        {
            var files = new List<string>();
            var directories = new List<string> { root };
            directories.AddRange(GetSafeDirectories(root));

            foreach (string directory in directories)
            {
                foreach (string file in _fileSystem.GetFiles(
                             directory,
                             SearchOption.TopDirectoryOnly))
                {
                    if ((_fileSystem.GetAttributes(file) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new InvalidOperationException(
                            "Agent skill operations refuse symbolic links and reparse points: " +
                            file);
                    }

                    files.Add(file);
                }
            }

            return files;
        }

        private string[] GetManagedFiles(string root)
        {
            return GetSafeFiles(root)
                .Select(file => AgentSkillPath.NormalizeRelative(AgentSkillPath.ToRelative(root, file)))
                .Where(relative =>
                    !AgentSkillPath.IsMetaPath(relative) &&
                    !AgentSkillPath.PathComparer.Equals(relative, ManifestFileName))
                .OrderBy(relative => relative, StringComparer.Ordinal)
                .ToArray();
        }

        private bool TryComputeInstalledHash(
            string root,
            IEnumerable<string> relativeFiles,
            out string contentHash)
        {
            try
            {
                string[] normalized = (relativeFiles ?? Array.Empty<string>())
                    .Select(AgentSkillPath.NormalizeRelative)
                    .Distinct(AgentSkillPath.PathComparer)
                    .OrderBy(relative => relative, StringComparer.Ordinal)
                    .ToArray();

                if (normalized.Length == 0)
                {
                    contentHash = null;
                    return false;
                }

                foreach (string relative in normalized)
                {
                    AgentSkillPath.ValidateRelativeFile(relative);
                    if (!_fileSystem.FileExists(Path.Combine(root, AgentSkillPath.ToPlatformPath(relative))))
                    {
                        contentHash = null;
                        return false;
                    }
                }

                contentHash = ComputeContentHash(root, normalized);
                return true;
            }
            catch
            {
                contentHash = null;
                return false;
            }
        }

        private string ComputeContentHash(string root, IEnumerable<string> relativeFiles)
        {
            using SHA256 sha = SHA256.Create();
            using var stream = new MemoryStream();
            using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

            foreach (string relative in relativeFiles.OrderBy(path => path, StringComparer.Ordinal))
            {
                string normalized = AgentSkillPath.NormalizeRelative(relative);
                AgentSkillPath.ValidateRelativeFile(normalized);
                byte[] pathBytes = Encoding.UTF8.GetBytes(normalized);
                byte[] contents = _fileSystem.ReadAllBytes(
                    Path.Combine(root, AgentSkillPath.ToPlatformPath(normalized)));

                writer.Write(pathBytes.Length);
                writer.Write(pathBytes);
                writer.Write((long)contents.Length);
                writer.Write(contents);
            }

            writer.Flush();
            stream.Position = 0;
            return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty);
        }

        private static bool IsOwnedManifest(
            AgentSkillInstallManifest manifest,
            string skillName,
            AgentSkillTarget target)
        {
            return manifest != null &&
                   manifest.schemaVersion == ManifestSchemaVersion &&
                   string.Equals(manifest.skillId, skillName, StringComparison.Ordinal) &&
                   string.Equals(manifest.packageId, PackageId, StringComparison.Ordinal) &&
                   string.Equals(manifest.target, TargetId(target.Provider), StringComparison.Ordinal) &&
                   HasValidManagedFiles(manifest.managedFiles) &&
                   !string.IsNullOrWhiteSpace(manifest.contentHash);
        }

        private static bool HasValidManagedFiles(IEnumerable<string> managedFiles)
        {
            if (managedFiles == null)
                return false;

            var seen = new HashSet<string>(AgentSkillPath.PathComparer);
            bool hasSkillFile = false;

            try
            {
                foreach (string relative in managedFiles)
                {
                    AgentSkillPath.ValidateRelativeFile(relative);
                    string normalized = AgentSkillPath.NormalizeRelative(relative);
                    if (AgentSkillPath.IsMetaPath(normalized) ||
                        AgentSkillPath.PathComparer.Equals(
                            normalized,
                            ManifestFileName) ||
                        !seen.Add(normalized))
                    {
                        return false;
                    }

                    hasSkillFile |= string.Equals(
                        normalized,
                        "SKILL.md",
                        StringComparison.Ordinal);
                }
            }
            catch
            {
                return false;
            }

            return hasSkillFile;
        }

        private static string TargetId(AgentSkillProvider provider)
        {
            return provider switch
            {
                AgentSkillProvider.ClaudeCode => "claude-code",
                AgentSkillProvider.GitHubCopilot => "github-copilot",
                AgentSkillProvider.OpenAICodex => "openai-codex",
                _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, null)
            };
        }

        private static string Unquote(string value)
        {
            if (value.Length >= 2 &&
                ((value[0] == '"' && value[value.Length - 1] == '"') ||
                 (value[0] == '\'' && value[value.Length - 1] == '\'')))
            {
                return value.Substring(1, value.Length - 2);
            }

            return value;
        }

        private static int CountOccurrences(string value, string token)
        {
            int count = 0;
            int index = 0;
            while ((index = value.IndexOf(token, index, StringComparison.Ordinal)) >= 0)
            {
                count++;
                index += token.Length;
            }

            return count;
        }

        private static string NormalizeNewlines(string value)
        {
            return value.Replace("\r\n", "\n").Replace('\r', '\n');
        }

        private static string LegacyCopilotInstruction()
        {
            return "---\n" +
                   "applyTo: \"**\"\n" +
                   "---\n" +
                   "# ProxyCore\n\n" +
                   "This project uses the ProxyCore Unity package. A full agent skill explaining its\n" +
                   "correct usage (event system, definition-registries, unlockables) is installed at\n" +
                   "`.claude/skills/proxycore/`. Read `.claude/skills/proxycore/SKILL.md` and the files\n" +
                   "under `.claude/skills/proxycore/references/` before writing ProxyCore code \u2014 the\n" +
                   "idioms there differ from generic Unity code.\n";
        }

        private sealed class PreparedInstall
        {
            public AgentSkillTargetPlan Plan { get; }
            public string Workspace { get; }
            public string Stage { get; }
            public string Backup { get; }
            public bool BackupCreated { get; set; }
            public bool Committed { get; set; }
            public string TargetName => Plan.Target.DisplayName;

            public PreparedInstall(
                AgentSkillTargetPlan plan,
                string workspace,
                string stage,
                string backup)
            {
                Plan = plan;
                Workspace = workspace;
                Stage = stage;
                Backup = backup;
            }
        }

        private sealed class PreparedUninstall
        {
            public AgentSkillUninstallTargetPlan Plan { get; }
            public string Workspace { get; }
            public string Stage { get; }
            public string Backup { get; }
            public bool PreserveDestination { get; }
            public bool BackupCreated { get; set; }
            public bool Committed { get; set; }
            public string TargetName => Plan.Target.DisplayName;

            public PreparedUninstall(
                AgentSkillUninstallTargetPlan plan,
                string workspace,
                string stage,
                string backup,
                bool preserveDestination)
            {
                Plan = plan;
                Workspace = workspace;
                Stage = stage;
                Backup = backup;
                PreserveDestination = preserveDestination;
            }
        }
    }

    internal static class AgentSkillPath
    {
        internal static StringComparer PathComparer =>
            Path.DirectorySeparatorChar == '\\'
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;

        public static string ToRelative(string root, string path)
        {
            return NormalizeRelative(Path.GetRelativePath(root, path));
        }

        public static string NormalizeRelative(string path)
        {
            return (path ?? string.Empty).Replace('\\', '/').TrimStart('/');
        }

        public static string ToPlatformPath(string path)
        {
            return NormalizeRelative(path).Replace('/', Path.DirectorySeparatorChar);
        }

        public static bool IsMetaPath(string relativePath)
        {
            return NormalizeRelative(relativePath).EndsWith(".meta", StringComparison.OrdinalIgnoreCase);
        }

        public static void ValidateRelativeFile(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath) ||
                Path.IsPathRooted(relativePath) ||
                relativePath.StartsWith("/", StringComparison.Ordinal) ||
                relativePath.StartsWith("\\", StringComparison.Ordinal))
            {
                throw new InvalidDataException($"Invalid managed file path: {relativePath}");
            }

            string normalized = NormalizeRelative(relativePath);
            string[] segments = normalized.Split('/');
            if (segments.Any(segment => segment == ".." || segment.Length == 0))
                throw new InvalidDataException($"Managed file path escapes the skill directory: {relativePath}");
        }

        public static void EnsureDirectoryIsWithin(
            string root,
            string candidate,
            bool allowEqual = false,
            bool mustBeInside = true)
        {
            string fullRoot = Path.GetFullPath(root)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string fullCandidate = Path.GetFullPath(candidate)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            bool equal = string.Equals(fullRoot, fullCandidate, comparison);
            string rootPrefix = fullRoot + Path.DirectorySeparatorChar;
            bool inside = fullCandidate.StartsWith(rootPrefix, comparison);

            if (mustBeInside && !(inside || (allowEqual && equal)))
                throw new InvalidOperationException($"Path escapes the project root: {candidate}");
            if (!mustBeInside && (inside || equal))
                throw new InvalidOperationException("The canonical skill source cannot be inside an install target.");
        }

        public static string ToProjectRelative(string fullPath, string projectRoot)
        {
            string relative = Path.GetRelativePath(projectRoot, fullPath);
            return NormalizeRelative(relative);
        }
    }
}
