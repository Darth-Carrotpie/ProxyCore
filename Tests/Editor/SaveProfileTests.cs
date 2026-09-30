using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProxyCore.Tests {
    /// <summary>
    /// Covers profile-scoped persistence: injective id composition, ProxyCore-owned state
    /// following the active profile (unlocks and flags alike), the data lifecycle API,
    /// autosave opt-out, catalog scoping, and crash/corruption behaviour.
    ///
    /// Every test runs under throwaway profile ids and deletes them again, so a developer's
    /// real save data is never touched.
    /// </summary>
    [TestFixture]
    public class SaveProfileTests {
        private const string P1 = "pctest-one";
        private const string P2 = "pctest-two";
        private const string P3 = "pctest-three";

        private static readonly string[] AllTestProfiles = { P1, P2, P3 };

        private bool _autoSaveBefore;

        [SetUp]
        public void SetUp() {
            Assert.IsNotNull(UnlockManager.Instance,
                "No UnlockManager asset is discoverable. Place one in a Resources folder.");
            _autoSaveBefore = SaveProfile.AutoSave;
            SaveProfile.AutoSave = true;
            CleanTestProfiles();
        }

        [TearDown]
        public void TearDown() {
            SaveProfile.AutoSave = _autoSaveBefore;
            // Deselect before deleting: leaving a test profile active would make the
            // switch-away flush recreate the directory we are trying to remove.
            SaveProfile.SetActive("");
            CleanTestProfiles();
        }

        private static void CleanTestProfiles() {
            foreach (string id in AllTestProfiles) {
                if (SaveProfile.ProfileExists(id))
                    SaveProfile.DeleteProfile(id);
            }

            // Flag collections created before a profile was selected flush to the default root;
            // clear those so the fixture leaves the developer's save directory as it found it.
            foreach (string stray in Directory.GetFiles(Application.persistentDataPath, "flags_pctest-*.json"))
                File.Delete(stray);
            foreach (string stray in Directory.GetFiles(Application.persistentDataPath, "unlocks_pctest-*.json"))
                File.Delete(stray);
        }

        private static StandaloneUnlockable Item(string key) =>
            new StandaloneUnlockable(key, savesAcrossSessions: true, UnlockBehavior.HideWhenLocked);

        // ── 1. Injective id composition ──────────────────────────────────

        [Test]
        public void Id_AmbiguousSegmentSplits_ProduceDifferentIds() {
            string a = SaveProfile.Id("a_b", "c");
            string b = SaveProfile.Id("a", "b_c");

            Assert.AreNotEqual(a, b,
                "Segment lists that differ only in where the underscore falls must not collide");
        }

        [Test]
        public void Id_AwkwardSegments_AreValidDistinctAndStable() {
            var ids = new List<string> {
                SaveProfile.Id("a/b"),
                SaveProfile.Id("a:b"),
                SaveProfile.Id("a\\b"),
                SaveProfile.Id("..", ".."),
                SaveProfile.Id("trailing "),
                SaveProfile.Id("trailing"),
                SaveProfile.Id("Case"),
                SaveProfile.Id("case"),
                SaveProfile.Id("emoji-\U0001F513"),
            };

            CollectionAssert.AllItemsAreUnique(ids, "Distinct segments must produce distinct ids");

            char[] illegal = Path.GetInvalidFileNameChars();
            foreach (string id in ids) {
                Assert.IsNotEmpty(id);
                Assert.AreEqual(-1, id.IndexOfAny(illegal), $"'{id}' contains a filename-illegal character");
                Assert.IsFalse(id.Contains(".."), $"'{id}' still contains a traversal sequence");
                Assert.AreEqual(id, id.Trim(), $"'{id}' has surrounding whitespace");
            }

            // Stable across calls.
            Assert.AreEqual(SaveProfile.Id("a/b"), SaveProfile.Id("a/b"));
            Assert.AreEqual(SaveProfile.Id("x", "y"), SaveProfile.Id("x", "y"));
        }

        [Test]
        public void Id_CaseOnlyDifference_SurvivesCaseInsensitiveFilesystems() {
            // "Case" and "case" must not resolve to the same directory on Windows/macOS.
            string upper = SaveProfile.Id("Case");
            string lower = SaveProfile.Id("case");

            Assert.AreNotEqual(upper.ToLowerInvariant(), lower.ToLowerInvariant(),
                "Ids differing only by case would collide on a case-insensitive filesystem");
        }

        // ── 2 & 4. All ProxyCore state follows the profile ───────────────

        [Test]
        public void Profiles_DoNotObserveEachOthersUnlocks() {
            var item = Item("Test:ProfileScopedUnlock");

            SaveProfile.SetActive(P1);
            UnlockManager.Unlock(item);
            Assert.IsTrue(UnlockManager.IsUnlocked(item));

            SaveProfile.SetActive(P2);
            Assert.IsFalse(UnlockManager.IsUnlocked(item), "P2 must not see P1's unlocks");

            SaveProfile.SetActive(P1);
            Assert.IsTrue(UnlockManager.IsUnlocked(item), "P1's unlocks must survive the round trip");
        }

        [Test]
        public void Profiles_DoNotObserveEachOthersFlags() {
            var flags = MakeFlagCollection("pctest-flags", savesAcrossSessions: true, "boss_defeated");

            SaveProfile.SetActive(P1);
            flags.SetFlag("boss_defeated", true);
            Assert.IsTrue(flags.GetFlag("boss_defeated"));

            SaveProfile.SetActive(P2);
            Assert.IsFalse(flags.GetFlag("boss_defeated"), "P2 must not see P1's flags");

            SaveProfile.SetActive(P1);
            Assert.IsTrue(flags.GetFlag("boss_defeated"), "P1's flags must survive the round trip");

            Object.DestroyImmediate(flags);
        }

        [Test]
        public void SwitchingProfile_ReloadsFlagsNotJustUnlocks() {
            var flags = MakeFlagCollection("pctest-reload", savesAcrossSessions: true, "seen_intro");

            SaveProfile.SetActive(P1);
            flags.SetFlag("seen_intro", true);

            // A mid-session switch must push flags through OnProfileChanged, not leave stale
            // in-memory state behind from whenever the asset last ran OnEnable.
            SaveProfile.SetActive(P2);
            Assert.IsFalse(flags.GetFlag("seen_intro"),
                "flag state leaked across a mid-session profile switch");

            Object.DestroyImmediate(flags);
        }

        [Test]
        public void SessionOnlyFlags_AlsoClearOnProfileSwitch() {
            var flags = MakeFlagCollection("pctest-session", savesAcrossSessions: false, "temp_flag");

            SaveProfile.SetActive(P1);
            flags.SetFlag("temp_flag", true);

            SaveProfile.SetActive(P2);
            Assert.IsFalse(flags.GetFlag("temp_flag"),
                "session-only flags must not leak between save games either");

            Object.DestroyImmediate(flags);
        }

        // ── 3. Profile-changed notification ──────────────────────────────

        [Test]
        public void ProfileChanged_FiresWithNewIdAfterStoresReload() {
            var item = Item("Test:NotifyOrdering");
            string observed = null;
            bool unlockedWhenNotified = true;

            SaveProfile.SetActive(P1);
            UnlockManager.Unlock(item);

            void Handler(string id) {
                observed = id;
                // Stores must already have reloaded by the time subscribers run.
                unlockedWhenNotified = UnlockManager.IsUnlocked(item);
            }

            SaveProfile.ProfileChanged += Handler;
            try {
                SaveProfile.SetActive(P2);
            }
            finally {
                SaveProfile.ProfileChanged -= Handler;
            }

            Assert.AreEqual(P2, observed, "event must carry the new profile id");
            Assert.IsFalse(unlockedWhenNotified,
                "ProxyCore stores must be reloaded before ProfileChanged fires");
        }

        // ── 4. Lifecycle API ─────────────────────────────────────────────

        [Test]
        public void ListProfiles_ReportsProfilesWithData() {
            SaveProfile.SetActive(P1);
            UnlockManager.Unlock(Item("Test:Listed"));
            SaveProfile.SetActive("");

            CollectionAssert.Contains(SaveProfile.ListProfiles(), P1);
            CollectionAssert.DoesNotContain(SaveProfile.ListProfiles(), P3);
        }

        [Test]
        public void DeleteProfile_RemovesOnlyThatProfilesFiles() {
            var item = Item("Test:DeleteScope");

            SaveProfile.SetActive(P1);
            UnlockManager.Unlock(item);
            SaveProfile.SetActive(P2);
            UnlockManager.Unlock(item);

            SaveProfile.SetActive("");
            SaveProfile.DeleteProfile(P1);

            Assert.IsFalse(SaveProfile.ProfileExists(P1), "P1's directory should be gone");
            Assert.IsTrue(SaveProfile.ProfileExists(P2), "P2 must be untouched");

            SaveProfile.SetActive(P2);
            Assert.IsTrue(UnlockManager.IsUnlocked(item), "P2's data must still load");
        }

        [Test]
        public void DeletingActiveProfile_DeselectsItAndDoesNotResurrectTheDirectory() {
            SaveProfile.SetActive(P1);
            UnlockManager.Unlock(Item("Test:DeleteActive"));

            SaveProfile.DeleteProfile(P1);
            Assert.AreEqual("", SaveProfile.Active,
                "deleting the active profile must deselect it");

            // Switching away flushes registered stores; that flush must not recreate P1.
            SaveProfile.SetActive(P2);
            Assert.IsFalse(SaveProfile.ProfileExists(P1),
                "a deleted save must not reappear after the next profile switch");
            CollectionAssert.DoesNotContain(SaveProfile.ListProfiles(), P1);
        }

        [Test]
        public void CopyProfile_ProducesIndependentCopy() {
            var shared = Item("Test:CopyShared");
            var onlyInCopy = Item("Test:CopyOnlyInDestination");

            SaveProfile.SetActive(P1);
            UnlockManager.Unlock(shared);

            SaveProfile.SetActive("");
            SaveProfile.CopyProfile(P1, P2);

            SaveProfile.SetActive(P2);
            Assert.IsTrue(UnlockManager.IsUnlocked(shared), "copy must carry the source's unlocks");
            UnlockManager.Unlock(onlyInCopy);

            SaveProfile.SetActive(P1);
            Assert.IsTrue(UnlockManager.IsUnlocked(shared));
            Assert.IsFalse(UnlockManager.IsUnlocked(onlyInCopy),
                "writing to the copy must not affect the source");
        }

        [Test]
        public void CopyProfile_FlushesTheActiveSourceFirst() {
            var item = Item("Test:CopyFlush");

            SaveProfile.SetActive(P1);
            SaveProfile.AutoSave = false;
            UnlockManager.Unlock(item);          // in memory only

            SaveProfile.CopyProfile(P1, P2);     // must flush P1 before copying

            SaveProfile.AutoSave = true;
            SaveProfile.SetActive(P2);
            Assert.IsTrue(UnlockManager.IsUnlocked(item),
                "copying from the active profile must include unflushed changes");
        }

        // ── 5. Autosave opt-out ──────────────────────────────────────────

        [Test]
        public void AutoSaveDisabled_NothingHitsDiskUntilSave() {
            var item = Item("Test:DeferredWrite");

            SaveProfile.SetActive(P1);
            SaveProfile.AutoSave = false;

            UnlockManager.Unlock(item);
            string file = Path.Combine(SaveProfile.RootFor(P1), SaveProfile.UNLOCKS_FILE);
            Assert.IsFalse(File.Exists(file), "no file should be written while autosave is off");

            SaveProfile.Save();
            Assert.IsTrue(File.Exists(file), "explicit Save() must write regardless of autosave");

            SaveProfile.AutoSave = true;
            SaveProfile.Reload();
            Assert.IsTrue(UnlockManager.IsUnlocked(item), "the explicit save must be readable");
        }

        [Test]
        public void Reload_DiscardsUnsavedChanges() {
            var item = Item("Test:DiscardUnsaved");

            SaveProfile.SetActive(P1);
            SaveProfile.Save();                  // establish an empty on-disk state

            SaveProfile.AutoSave = false;
            UnlockManager.Unlock(item);
            SaveProfile.Reload();

            Assert.IsFalse(UnlockManager.IsUnlocked(item),
                "Reload() must drop in-memory changes that were never saved");
        }

        // ── 6. Backward compatibility ────────────────────────────────────

        [Test]
        public void DefaultProfile_UsesLegacyFlatPaths() {
            Assert.AreEqual("", SaveProfile.Active, "no profile should be active by default");
            Assert.AreEqual(Application.persistentDataPath, SaveProfile.ActiveRoot);
            Assert.AreEqual(
                Path.Combine(Application.persistentDataPath, "unlocks.json"),
                SaveProfile.PathFor(SaveProfile.UNLOCKS_FILE),
                "a project that never touches the profile API must keep using unlocks.json");
            Assert.AreEqual(
                Path.Combine(Application.persistentDataPath, "flags_Global.json"),
                SaveProfile.PathFor("flags_Global.json"),
                "flag collections must keep their legacy path when unprofiled");
        }

        // ── 7 & 8. Atomic writes and corruption ──────────────────────────

        [Test]
        public void InterruptedWrite_LeavesPreviousSaveReadable() {
            var item = Item("Test:AtomicWrite");

            SaveProfile.SetActive(P1);
            UnlockManager.Unlock(item);

            string file = Path.Combine(SaveProfile.RootFor(P1), SaveProfile.UNLOCKS_FILE);
            string good = File.ReadAllText(file);

            // A crash between "write temp" and "replace" leaves a stray .tmp behind.
            File.WriteAllText(file + ".tmp", "{\"savedUnlockedKeys\":[\"Test:Atomic");

            Assert.AreEqual(good, File.ReadAllText(file), "the committed save must be untouched");

            SaveProfile.Reload();
            Assert.IsTrue(UnlockManager.IsUnlocked(item),
                "the previous save must still load after an interrupted write");
        }

        [Test]
        public void CorruptSave_IsQuarantinedAndReportedNotSilentlyLost() {
            SaveProfile.SetActive(P1);
            UnlockManager.Unlock(Item("Test:Corrupt"));
            SaveProfile.Save();

            string file = Path.Combine(SaveProfile.RootFor(P1), SaveProfile.UNLOCKS_FILE);
            File.WriteAllText(file, "{ this is not json");

            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex(".*could not be read.*"));
            SaveProfile.Reload();

            Assert.IsTrue(File.Exists(file + ".corrupt"),
                "the unreadable file must be preserved for recovery, not deleted");
            Assert.IsFalse(File.Exists(file), "the unreadable file must be moved aside");

            File.Delete(file + ".corrupt");
        }

        // ── 9. Catalog scoping ───────────────────────────────────────────

        [Test]
        public void RegistryOverridingCatalog_DoesNotAutoUnlockDefinitionsOutsideTheSubset() {
            var inScope = MakeAutoUnlockDefinition("InScope");
            var outOfScope = MakeAutoUnlockDefinition("OutOfScope");

            var registry = ScriptableObject.CreateInstance<ScopedTestRegistry>();
            registry.definitions = new List<TestUnlockableDefinition> { inScope, outOfScope };
            registry.VisibleDefinitions = new List<BaseDefinition> { inScope };

            SaveProfile.SetActive(P1);
            using (new TemporaryRegistry(registry)) {
                UnlockManager.EvaluateAutoTriggers();

                Assert.IsTrue(UnlockManager.IsUnlocked(inScope),
                    "a definition inside the scoped catalog should still auto-unlock");
                Assert.IsFalse(UnlockManager.IsUnlocked(outOfScope),
                    "a definition outside the scoped catalog must not auto-unlock");
            }

            Object.DestroyImmediate(registry);
            Object.DestroyImmediate(inScope);
            Object.DestroyImmediate(outOfScope);
        }

        // ────────────────────────────────────────────────────────────────
        // Fixtures
        // ────────────────────────────────────────────────────────────────

        private static GameFlagCollection MakeFlagCollection(string assetName,
            bool savesAcrossSessions, params string[] declaredFlags) {
            var flags = ScriptableObject.CreateInstance<GameFlagCollection>();
            flags.name = assetName;

            SetPrivate(flags, "_definedFlags", new List<string>(declaredFlags));
            SetPrivate(flags, "_savesAcrossSessions", savesAcrossSessions);
            // Keep flag changes from cascading into the project's real registries.
            SetPrivate(flags, "_autoEvaluateOnSet", false);
            return flags;
        }

        private static TestUnlockableDefinition MakeAutoUnlockDefinition(string assetName) {
            var def = ScriptableObject.CreateInstance<TestUnlockableDefinition>();
            def.name = assetName;
            def.SetIndex(assetName.GetHashCode());
            return def;
        }

        private static void SetPrivate(object target, string fieldName, object value) {
            var field = target.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"field '{fieldName}' not found on {target.GetType().Name}");
            field.SetValue(target, value);
        }

        /// <summary>
        /// Swaps UnlockManager's serialized registry list for one containing only the supplied
        /// registry, and restores it on dispose. Reflection is the only way in — the list is a
        /// private serialized field and RegistryEntry is a private nested type.
        /// </summary>
        private sealed class TemporaryRegistry : System.IDisposable {
            private readonly FieldInfo _field;
            private readonly object _previous;

            public TemporaryRegistry(ScriptableObject registry) {
                _field = typeof(UnlockManager).GetField("_registries",
                    BindingFlags.NonPublic | BindingFlags.Instance);
                Assert.IsNotNull(_field, "UnlockManager._registries not found");

                _previous = _field.GetValue(UnlockManager.Instance);

                var entryType = typeof(UnlockManager).GetNestedType("RegistryEntry",
                    BindingFlags.NonPublic);
                Assert.IsNotNull(entryType, "UnlockManager.RegistryEntry not found");

                var entry = System.Activator.CreateInstance(entryType);
                entryType.GetField("Registry").SetValue(entry, registry);
                entryType.GetField("Enabled").SetValue(entry, true);

                var listType = typeof(List<>).MakeGenericType(entryType);
                var list = (IList)System.Activator.CreateInstance(listType);
                list.Add(entry);

                _field.SetValue(UnlockManager.Instance, list);
            }

            public void Dispose() => _field.SetValue(UnlockManager.Instance, _previous);
        }

        /// <summary>
        /// Auto-unlocks as soon as the unlock system can see it: no prerequisites means
        /// ArePrerequisitesMet passes trivially, so whether it unlocks depends only on
        /// whether the registry's catalog includes it.
        /// </summary>
        private class TestUnlockableDefinition : BaseDefinition, IUnlockable, IHasPrerequisites {
            private static readonly List<UnlockCondition> NoPrerequisites = new List<UnlockCondition>();

            string IUnlockable.UnlockKey => $"TestUnlockableDefinition:{name}";
            bool IUnlockable.SavesAcrossSessions => true;
            UnlockBehavior IUnlockable.LockedBehavior => UnlockBehavior.HideWhenLocked;
            bool IUnlockable.IsUnlockedByDefault => false;

            IReadOnlyList<UnlockCondition> IHasPrerequisites.Prerequisites => NoPrerequisites;
            ConditionMode IHasPrerequisites.PrerequisiteMode => ConditionMode.All;
            bool IHasPrerequisites.AutoUnlock => true;
        }

        /// <summary>Registry that shows the unlock system only a subset of its definitions.</summary>
        private class ScopedTestRegistry : BaseRegistry<TestUnlockableDefinition> {
            public List<BaseDefinition> VisibleDefinitions = new List<BaseDefinition>();

            public override IReadOnlyList<BaseDefinition> GetCatalogDefinitions() =>
                VisibleDefinitions.AsReadOnly();
        }
    }
}
