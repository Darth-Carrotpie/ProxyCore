using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;

namespace ProxyCore.Editor.Tests
{
    [TestFixture]
    public sealed class AgentSkillInstallerTests
    {
        private string _testRoot;
        private string _projectRoot;
        private string _sourceRoot;

        [SetUp]
        public void SetUp()
        {
            _testRoot = Path.Combine(
                Path.GetTempPath(),
                "ProxyCore.AgentSkillInstaller.Tests",
                Guid.NewGuid().ToString("N"));
            _projectRoot = Path.Combine(_testRoot, "project");
            _sourceRoot = Path.Combine(_testRoot, "source", "proxycore");

            Directory.CreateDirectory(_projectRoot);
            WriteValidSource("version-one", includeObsoleteFile: false);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_testRoot))
                Directory.Delete(_testRoot, recursive: true);
        }

        [Test]
        public void ValidateSource_AcceptsPortableSkillAndRejectsMissingLinkedResource()
        {
            var installer = new AgentSkillInstaller();

            AgentSkillSource source = installer.ValidateSource(_sourceRoot);

            Assert.That(source.Name, Is.EqualTo("proxycore"));
            Assert.That(source.ManagedFiles, Does.Contain("SKILL.md"));
            Assert.That(source.ManagedFiles, Does.Contain("references/events.md"));
            Assert.That(source.ManagedFiles.Any(path => path.EndsWith(".meta")), Is.False);

            File.Delete(Path.Combine(_sourceRoot, "references", "events.md"));

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => installer.ValidateSource(_sourceRoot));
            Assert.That(exception.Message, Does.Contain("missing local resource"));
        }

        [Test]
        public void ValidateSource_RejectsNameThatDoesNotMatchDirectory()
        {
            string skillFile = Path.Combine(_sourceRoot, "SKILL.md");
            string contents = File.ReadAllText(skillFile)
                .Replace("name: proxycore", "name: another-skill");
            File.WriteAllText(skillFile, contents);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => new AgentSkillInstaller().ValidateSource(_sourceRoot));

            Assert.That(exception.Message, Does.Contain("must match its parent directory"));
        }

        [Test]
        public void ValidateSource_AcceptsFoldedDescription()
        {
            string skillFile = Path.Combine(_sourceRoot, "SKILL.md");
            string contents = File.ReadAllText(skillFile).Replace(
                "description: Use ProxyCore for events, registries, and unlockable content.",
                "description: >-\n" +
                "  Use ProxyCore for events, registries, and unlockable content across\n" +
                "  all supported coding agents.");
            File.WriteAllText(skillFile, contents);

            AgentSkillSource source = new AgentSkillInstaller().ValidateSource(_sourceRoot);

            Assert.That(
                source.Description,
                Is.EqualTo(
                    "Use ProxyCore for events, registries, and unlockable content across " +
                    "all supported coding agents."));
        }

        [Test]
        public void ValidateSource_RejectsEmptyFoldedDescription()
        {
            string skillFile = Path.Combine(_sourceRoot, "SKILL.md");
            string contents = File.ReadAllText(skillFile).Replace(
                "description: Use ProxyCore for events, registries, and unlockable content.",
                "description: >-");
            File.WriteAllText(skillFile, contents);

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => new AgentSkillInstaller().ValidateSource(_sourceRoot));

            Assert.That(exception.Message, Does.Contain("non-empty description"));
        }

        [Test]
        public void ValidateSource_RejectsMissingExactSkillFile()
        {
            File.Delete(Path.Combine(_sourceRoot, "SKILL.md"));

            InvalidDataException exception = Assert.Throws<InvalidDataException>(
                () => new AgentSkillInstaller().ValidateSource(_sourceRoot));

            Assert.That(exception.Message, Does.Contain("exact SKILL.md"));
        }

        [Test]
        public void NativeTargets_ResolveToProviderSpecificProjectLocations()
        {
            Assert.That(
                Relative(AgentSkillTargets.ClaudeCode.GetDestination(_projectRoot)),
                Is.EqualTo(".claude/skills/proxycore"));
            Assert.That(
                Relative(AgentSkillTargets.GitHubCopilot.GetDestination(_projectRoot)),
                Is.EqualTo(".github/skills/proxycore"));
            Assert.That(
                Relative(AgentSkillTargets.OpenAICodex.GetDestination(_projectRoot)),
                Is.EqualTo(".agents/skills/proxycore"));
        }

        [Test]
        public void Install_AllNativeTargets_CopiesCompleteSkillAndSkipsUnityMeta()
        {
            var installer = new AgentSkillInstaller();

            AgentSkillInstallReport report = installer.Install(Request(
                AgentSkillTargets.All,
                packageVersion: "1.0.0"));

            Assert.That(report.Results.Count, Is.EqualTo(3));
            Assert.That(report.Results.All(result => result.Action == "installed"), Is.True);

            foreach (AgentSkillTarget target in AgentSkillTargets.All)
            {
                string destination = target.GetDestination(_projectRoot);
                Assert.That(File.Exists(Path.Combine(destination, "SKILL.md")), Is.True);
                Assert.That(
                    File.Exists(Path.Combine(destination, "references", "events.md")),
                    Is.True);
                Assert.That(
                    File.Exists(Path.Combine(destination, "references", "events.md.meta")),
                    Is.False);
                Assert.That(
                    File.Exists(Path.Combine(destination, AgentSkillInstaller.ManifestFileName)),
                    Is.True);
            }
        }

        [Test]
        public void Reinstall_WithSamePackageAndContent_IsIdempotent()
        {
            var installer = new AgentSkillInstaller();
            AgentSkillInstallRequest request = Request(
                new[] { AgentSkillTargets.OpenAICodex },
                packageVersion: "1.0.0");
            installer.Install(request);

            string destination = AgentSkillTargets.OpenAICodex.GetDestination(_projectRoot);
            string manifest = Path.Combine(destination, AgentSkillInstaller.ManifestFileName);
            DateTime sentinel = new DateTime(2001, 2, 3, 4, 5, 6, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(manifest, sentinel);

            AgentSkillInstallReport second = installer.Install(request);

            Assert.That(second.Results.Single().Action, Is.EqualTo("already up to date"));
            Assert.That(File.GetLastWriteTimeUtc(manifest), Is.EqualTo(sentinel));
        }

        [Test]
        public void ManagedUpdate_RemovesStaleManagedFilesAndPreservesUnmanagedExtras()
        {
            WriteValidSource("version-one", includeObsoleteFile: true);
            var installer = new AgentSkillInstaller();
            AgentSkillInstallRequest first = Request(
                new[] { AgentSkillTargets.ClaudeCode },
                packageVersion: "1.0.0");
            installer.Install(first);

            string destination = AgentSkillTargets.ClaudeCode.GetDestination(_projectRoot);
            File.WriteAllText(Path.Combine(destination, "local-notes.txt"), "keep me");
            File.WriteAllText(Path.Combine(destination, "SKILL.md.meta"), "tracked unity metadata");

            WriteValidSource("version-two", includeObsoleteFile: false);
            AgentSkillInstallReport report = installer.Install(Request(
                new[] { AgentSkillTargets.ClaudeCode },
                packageVersion: "2.0.0"));

            Assert.That(report.Results.Single().Action, Is.EqualTo("updated"));
            Assert.That(File.Exists(Path.Combine(destination, "obsolete.md")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(destination, "local-notes.txt")), Is.EqualTo("keep me"));
            Assert.That(File.Exists(Path.Combine(destination, "SKILL.md.meta")), Is.True);
            Assert.That(
                File.ReadAllText(Path.Combine(destination, "references", "events.md")),
                Does.Contain("version-two"));
        }

        [Test]
        public void ModifiedManagedFile_BlocksUpdateUntilReplacementIsExplicit()
        {
            var installer = new AgentSkillInstaller();
            AgentSkillInstallRequest first = Request(
                new[] { AgentSkillTargets.GitHubCopilot },
                packageVersion: "1.0.0");
            installer.Install(first);

            string destination = AgentSkillTargets.GitHubCopilot.GetDestination(_projectRoot);
            File.AppendAllText(Path.Combine(destination, "SKILL.md"), "\nlocal customization\n");
            WriteValidSource("version-two", includeObsoleteFile: false);

            AgentSkillTargetPlan plan = installer.Plan(Request(
                new[] { AgentSkillTargets.GitHubCopilot },
                packageVersion: "2.0.0")).Single();
            Assert.That(plan.State, Is.EqualTo(AgentSkillDestinationState.Conflict));

            Assert.Throws<InvalidOperationException>(() => installer.Install(Request(
                new[] { AgentSkillTargets.GitHubCopilot },
                packageVersion: "2.0.0")));
            Assert.That(
                File.ReadAllText(Path.Combine(destination, "SKILL.md")),
                Does.Contain("local customization"));

            installer.Install(Request(
                new[] { AgentSkillTargets.GitHubCopilot },
                packageVersion: "2.0.0",
                allowUnmanagedOverwrite: true));
            Assert.That(
                File.ReadAllText(Path.Combine(destination, "SKILL.md")),
                Does.Not.Contain("local customization"));
        }

        [Test]
        public void ManagedUpdate_NewManagedPathCollidingWithLocalExtraRequiresApproval()
        {
            var installer = new AgentSkillInstaller();
            AgentSkillTarget target = AgentSkillTargets.ClaudeCode;
            installer.Install(Request(new[] { target }, packageVersion: "1.0.0"));

            string destination = target.GetDestination(_projectRoot);
            string localFile = Path.Combine(destination, "references", "new-guidance.md");
            File.WriteAllText(localFile, "local content");

            string sourceFile = Path.Combine(_sourceRoot, "references", "new-guidance.md");
            File.WriteAllText(sourceFile, "package content");

            AgentSkillTargetPlan plan = installer.Plan(Request(
                new[] { target },
                packageVersion: "2.0.0")).Single();

            Assert.That(plan.State, Is.EqualTo(AgentSkillDestinationState.Conflict));
            Assert.That(plan.Detail, Does.Contain("new-guidance.md"));
            Assert.Throws<InvalidOperationException>(() => installer.Install(Request(
                new[] { target },
                packageVersion: "2.0.0")));
            Assert.That(File.ReadAllText(localFile), Is.EqualTo("local content"));

            installer.Install(Request(
                new[] { target },
                packageVersion: "2.0.0",
                allowUnmanagedOverwrite: true));
            Assert.That(File.ReadAllText(localFile), Is.EqualTo("package content"));
        }

        [Test]
        public void LegacyExactCopy_IsAdoptedAndPreservesExtraMetaFiles()
        {
            string destination = AgentSkillTargets.ClaudeCode.GetDestination(_projectRoot);
            CopyDirectory(_sourceRoot, destination, includeMeta: false);
            File.WriteAllText(Path.Combine(destination, "SKILL.md.meta"), "keep");

            var installer = new AgentSkillInstaller();
            AgentSkillTargetPlan plan = installer.Plan(Request(
                new[] { AgentSkillTargets.ClaudeCode },
                packageVersion: "1.0.0")).Single();

            Assert.That(plan.State, Is.EqualTo(AgentSkillDestinationState.LegacyExactCopy));

            installer.Install(Request(
                new[] { AgentSkillTargets.ClaudeCode },
                packageVersion: "1.0.0"));

            Assert.That(File.Exists(Path.Combine(destination, "SKILL.md.meta")), Is.True);
            Assert.That(
                File.Exists(Path.Combine(destination, AgentSkillInstaller.ManifestFileName)),
                Is.True);
        }

        [Test]
        public void CommitFailure_RollsBackEveryPreviouslyCommittedTarget()
        {
            var normalInstaller = new AgentSkillInstaller();
            AgentSkillTarget[] targets =
            {
                AgentSkillTargets.ClaudeCode,
                AgentSkillTargets.OpenAICodex
            };

            normalInstaller.Install(Request(targets, packageVersion: "1.0.0"));
            WriteValidSource("version-two", includeObsoleteFile: false);

            var faultingFileSystem = new FaultOnSecondStageCommitFileSystem();
            var faultingInstaller = new AgentSkillInstaller(faultingFileSystem);

            IOException exception = Assert.Throws<IOException>(() => faultingInstaller.Install(
                Request(targets, packageVersion: "2.0.0")));
            Assert.That(exception.Message, Does.Contain("rolled back"));

            foreach (AgentSkillTarget target in targets)
            {
                string eventsFile = Path.Combine(
                    target.GetDestination(_projectRoot),
                    "references",
                    "events.md");
                Assert.That(File.ReadAllText(eventsFile), Does.Contain("version-one"));
            }

            string operationRoot = Path.Combine(
                _projectRoot,
                "Library",
                "ProxyCore",
                "AgentSkillInstaller");
            Assert.That(
                Directory.Exists(operationRoot)
                    ? Directory.GetFileSystemEntries(operationRoot)
                    : Array.Empty<string>(),
                Is.Empty);
        }

        [Test]
        public void SuccessfulNativeInstall_RemovesOnlyInstallerOwnedLegacyBridges()
        {
            string instructions = Path.Combine(_projectRoot, ".github", "instructions");
            Directory.CreateDirectory(instructions);
            File.WriteAllText(
                Path.Combine(instructions, "proxycore.instructions.md"),
                LegacyCopilotInstruction());

            File.WriteAllText(
                Path.Combine(_projectRoot, "AGENTS.md"),
                "# Team guidance\n\n" +
                "<!-- BEGIN ProxyCore Agent Skill -->\n" +
                "generated content that may evolve\n" +
                "<!-- END ProxyCore Agent Skill -->\n\n" +
                "Keep this user-authored tail.\n");

            AgentSkillInstallReport report = new AgentSkillInstaller().Install(Request(
                new[]
                {
                    AgentSkillTargets.GitHubCopilot,
                    AgentSkillTargets.OpenAICodex
                },
                packageVersion: "1.0.0"));

            Assert.That(report.Warnings, Is.Empty);
            Assert.That(
                File.Exists(Path.Combine(instructions, "proxycore.instructions.md")),
                Is.False);

            string agents = File.ReadAllText(Path.Combine(_projectRoot, "AGENTS.md"));
            Assert.That(agents, Does.Contain("# Team guidance"));
            Assert.That(agents, Does.Contain("Keep this user-authored tail."));
            Assert.That(agents, Does.Not.Contain("BEGIN ProxyCore Agent Skill"));
        }

        [Test]
        public void ModifiedLegacyPointer_IsPreservedWithWarning()
        {
            string instructions = Path.Combine(_projectRoot, ".github", "instructions");
            Directory.CreateDirectory(instructions);
            string pointer = Path.Combine(instructions, "proxycore.instructions.md");
            File.WriteAllText(pointer, LegacyCopilotInstruction() + "\nUser addition.\n");

            AgentSkillInstallReport report = new AgentSkillInstaller().Install(Request(
                new[] { AgentSkillTargets.GitHubCopilot },
                packageVersion: "1.0.0"));

            Assert.That(File.Exists(pointer), Is.True);
            Assert.That(report.Warnings.Single(), Does.Contain("no longer matches"));
        }

        [Test]
        public void MalformedLegacyCodexMarkers_ArePreservedWithWarning()
        {
            string agentsFile = Path.Combine(_projectRoot, "AGENTS.md");
            const string original =
                "# Team guidance\n\n" +
                "<!-- BEGIN ProxyCore Agent Skill -->\n" +
                "unterminated generated block\n";
            File.WriteAllText(agentsFile, original);

            AgentSkillInstallReport report = new AgentSkillInstaller().Install(Request(
                new[] { AgentSkillTargets.OpenAICodex },
                packageVersion: "1.0.0"));

            Assert.That(File.ReadAllText(agentsFile), Is.EqualTo(original));
            Assert.That(report.Warnings.Single(), Does.Contain("malformed or duplicated"));
        }

        [Test]
        public void Uninstall_RemovesOnlyManifestOwnedFilesAndPreservesLocalExtras()
        {
            var installer = new AgentSkillInstaller();
            AgentSkillTarget target = AgentSkillTargets.OpenAICodex;
            installer.Install(Request(new[] { target }, packageVersion: "1.0.0"));

            string destination = target.GetDestination(_projectRoot);
            File.WriteAllText(Path.Combine(destination, "local-notes.txt"), "keep me");
            File.WriteAllText(Path.Combine(destination, "SKILL.md.meta"), "keep metadata");

            AgentSkillUninstallReport report = installer.Uninstall(
                UninstallRequest(new[] { target }));

            Assert.That(report.Results.Single().Action, Does.Contain("kept local extras"));
            Assert.That(File.Exists(Path.Combine(destination, "SKILL.md")), Is.False);
            Assert.That(
                File.Exists(Path.Combine(destination, "references", "events.md")),
                Is.False);
            Assert.That(
                File.Exists(Path.Combine(destination, AgentSkillInstaller.ManifestFileName)),
                Is.False);
            Assert.That(
                File.ReadAllText(Path.Combine(destination, "local-notes.txt")),
                Is.EqualTo("keep me"));
            Assert.That(File.Exists(Path.Combine(destination, "SKILL.md.meta")), Is.True);
        }

        [Test]
        public void Uninstall_CleanManagedCopyRemovesDestination()
        {
            var installer = new AgentSkillInstaller();
            AgentSkillTarget target = AgentSkillTargets.ClaudeCode;
            installer.Install(Request(new[] { target }, packageVersion: "1.0.0"));

            AgentSkillUninstallTargetPlan plan = installer.PlanUninstall(
                UninstallRequest(new[] { target })).Single();
            Assert.That(plan.State, Is.EqualTo(AgentSkillUninstallState.ManagedClean));

            AgentSkillUninstallReport report = installer.Uninstall(
                UninstallRequest(new[] { target }));

            Assert.That(report.Results.Single().Action, Is.EqualTo("removed"));
            Assert.That(Directory.Exists(target.GetDestination(_projectRoot)), Is.False);
        }

        [Test]
        public void Uninstall_ModifiedManagedFileRequiresExplicitApproval()
        {
            var installer = new AgentSkillInstaller();
            AgentSkillTarget target = AgentSkillTargets.GitHubCopilot;
            installer.Install(Request(new[] { target }, packageVersion: "1.0.0"));

            string destination = target.GetDestination(_projectRoot);
            string skillFile = Path.Combine(destination, "SKILL.md");
            File.AppendAllText(skillFile, "\nlocal customization\n");

            AgentSkillUninstallTargetPlan plan = installer.PlanUninstall(
                UninstallRequest(new[] { target })).Single();
            Assert.That(plan.State, Is.EqualTo(AgentSkillUninstallState.ManagedModified));

            Assert.Throws<InvalidOperationException>(() => installer.Uninstall(
                UninstallRequest(new[] { target })));
            Assert.That(File.Exists(skillFile), Is.True);

            installer.Uninstall(UninstallRequest(
                new[] { target },
                allowModifiedManagedFiles: true));
            Assert.That(Directory.Exists(destination), Is.False);
        }

        [Test]
        public void Uninstall_UnmanagedFolderIsPreserved()
        {
            AgentSkillTarget target = AgentSkillTargets.ClaudeCode;
            string destination = target.GetDestination(_projectRoot);
            Directory.CreateDirectory(destination);
            File.WriteAllText(Path.Combine(destination, "local.txt"), "unmanaged");

            AgentSkillUninstallReport report = new AgentSkillInstaller().Uninstall(
                UninstallRequest(new[] { target }));

            Assert.That(report.Results.Single().Action, Is.EqualTo("kept unmanaged folder"));
            Assert.That(report.Warnings.Single(), Does.Contain("not removed"));
            Assert.That(
                File.ReadAllText(Path.Combine(destination, "local.txt")),
                Is.EqualTo("unmanaged"));
        }

        [Test]
        public void Uninstall_SecondTargetCommitFailureRollsBackEveryTarget()
        {
            AgentSkillTarget[] targets =
            {
                AgentSkillTargets.ClaudeCode,
                AgentSkillTargets.OpenAICodex
            };
            var installer = new AgentSkillInstaller();
            installer.Install(Request(targets, packageVersion: "1.0.0"));

            var faultingInstaller = new AgentSkillInstaller(
                new FaultOnSecondBackupMoveFileSystem());
            IOException exception = Assert.Throws<IOException>(() =>
                faultingInstaller.Uninstall(UninstallRequest(targets)));

            Assert.That(exception.Message, Does.Contain("rolled back"));
            foreach (AgentSkillTarget target in targets)
            {
                string destination = target.GetDestination(_projectRoot);
                Assert.That(File.Exists(Path.Combine(destination, "SKILL.md")), Is.True);
                Assert.That(
                    File.Exists(Path.Combine(
                        destination,
                        AgentSkillInstaller.ManifestFileName)),
                    Is.True);
            }

            string operationRoot = Path.Combine(
                _projectRoot,
                "Library",
                "ProxyCore",
                "AgentSkillInstaller");
            Assert.That(
                Directory.Exists(operationRoot)
                    ? Directory.GetFileSystemEntries(operationRoot)
                    : Array.Empty<string>(),
                Is.Empty);
        }

        private AgentSkillInstallRequest Request(
            IReadOnlyList<AgentSkillTarget> targets,
            string packageVersion,
            bool allowUnmanagedOverwrite = false)
        {
            return new AgentSkillInstallRequest(
                _projectRoot,
                _sourceRoot,
                packageVersion,
                targets,
                allowUnmanagedOverwrite,
                removeLegacyBridges: true);
        }

        private AgentSkillUninstallRequest UninstallRequest(
            IReadOnlyList<AgentSkillTarget> targets,
            bool allowModifiedManagedFiles = false)
        {
            return new AgentSkillUninstallRequest(
                _projectRoot,
                targets,
                allowModifiedManagedFiles);
        }

        private void WriteValidSource(string marker, bool includeObsoleteFile)
        {
            Directory.CreateDirectory(Path.Combine(_sourceRoot, "references"));
            File.WriteAllText(
                Path.Combine(_sourceRoot, "SKILL.md"),
                "---\n" +
                "name: proxycore\n" +
                "description: Use ProxyCore for events, registries, and unlockable content.\n" +
                "---\n\n" +
                "# ProxyCore\n\n" +
                "Read [event guidance](references/events.md).\n\n" +
                "Fixture: " + marker + "\n");
            File.WriteAllText(
                Path.Combine(_sourceRoot, "references", "events.md"),
                "# Events\n\n" + marker + "\n");
            File.WriteAllText(
                Path.Combine(_sourceRoot, "references", "events.md.meta"),
                "Unity-only source metadata");

            string obsolete = Path.Combine(_sourceRoot, "obsolete.md");
            if (includeObsoleteFile)
                File.WriteAllText(obsolete, "obsolete managed file");
            else if (File.Exists(obsolete))
                File.Delete(obsolete);
        }

        private string Relative(string path)
        {
            return Path.GetRelativePath(_projectRoot, path).Replace('\\', '/');
        }

        private static void CopyDirectory(string source, string destination, bool includeMeta)
        {
            foreach (string directory in Directory.GetDirectories(
                         source,
                         "*",
                         SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, directory);
                Directory.CreateDirectory(Path.Combine(destination, relative));
            }

            foreach (string sourceFile in Directory.GetFiles(
                         source,
                         "*",
                         SearchOption.AllDirectories))
            {
                if (!includeMeta && sourceFile.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                    continue;

                string relative = Path.GetRelativePath(source, sourceFile);
                string destinationFile = Path.Combine(destination, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
                File.Copy(sourceFile, destinationFile, overwrite: true);
            }
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

        private sealed class FaultOnSecondStageCommitFileSystem : IAgentSkillFileSystem
        {
            private readonly IAgentSkillFileSystem _inner = new SystemAgentSkillFileSystem();
            private int _stageCommits;

            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
            public bool FileExists(string path) => _inner.FileExists(path);
            public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);
            public void CreateDirectory(string path) => _inner.CreateDirectory(path);
            public string[] GetDirectories(string path, SearchOption searchOption) =>
                _inner.GetDirectories(path, searchOption);
            public string[] GetFiles(string path, SearchOption searchOption) =>
                _inner.GetFiles(path, searchOption);
            public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
            public string ReadAllText(string path) => _inner.ReadAllText(path);
            public void WriteAllBytes(string path, byte[] contents) =>
                _inner.WriteAllBytes(path, contents);
            public void WriteAllText(string path, string contents) =>
                _inner.WriteAllText(path, contents);

            public void MoveDirectory(string source, string destination)
            {
                if (string.Equals(
                        Path.GetFileName(source),
                        "stage",
                        StringComparison.Ordinal))
                {
                    _stageCommits++;
                    if (_stageCommits == 2)
                        throw new IOException("Injected second-target commit failure.");
                }

                _inner.MoveDirectory(source, destination);
            }

            public void MoveFile(string source, string destination) =>
                _inner.MoveFile(source, destination);
            public void DeleteDirectory(string path, bool recursive) =>
                _inner.DeleteDirectory(path, recursive);
            public void DeleteFile(string path) => _inner.DeleteFile(path);
        }

        private sealed class FaultOnSecondBackupMoveFileSystem : IAgentSkillFileSystem
        {
            private readonly IAgentSkillFileSystem _inner = new SystemAgentSkillFileSystem();
            private int _backupMoves;

            public bool DirectoryExists(string path) => _inner.DirectoryExists(path);
            public bool FileExists(string path) => _inner.FileExists(path);
            public FileAttributes GetAttributes(string path) => _inner.GetAttributes(path);
            public void CreateDirectory(string path) => _inner.CreateDirectory(path);
            public string[] GetDirectories(string path, SearchOption searchOption) =>
                _inner.GetDirectories(path, searchOption);
            public string[] GetFiles(string path, SearchOption searchOption) =>
                _inner.GetFiles(path, searchOption);
            public byte[] ReadAllBytes(string path) => _inner.ReadAllBytes(path);
            public string ReadAllText(string path) => _inner.ReadAllText(path);
            public void WriteAllBytes(string path, byte[] contents) =>
                _inner.WriteAllBytes(path, contents);
            public void WriteAllText(string path, string contents) =>
                _inner.WriteAllText(path, contents);

            public void MoveDirectory(string source, string destination)
            {
                if (string.Equals(
                        Path.GetFileName(destination),
                        "backup",
                        StringComparison.Ordinal))
                {
                    _backupMoves++;
                    if (_backupMoves == 2)
                        throw new IOException("Injected second-target backup failure.");
                }

                _inner.MoveDirectory(source, destination);
            }

            public void MoveFile(string source, string destination) =>
                _inner.MoveFile(source, destination);
            public void DeleteDirectory(string path, bool recursive) =>
                _inner.DeleteDirectory(path, recursive);
            public void DeleteFile(string path) => _inner.DeleteFile(path);
        }
    }
}
