using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace ProxyCore.Tests {
    /// <summary>
    /// Covers unlock provenance and acknowledgement: the durable, queryable complement to the
    /// onUnlocked EventMessage.
    ///
    /// The hole these guard: UnlockManager only broadcasts on a real transition, and the
    /// auto-unlock pass in OnAwake (and on every profile switch) runs before any UI exists, so
    /// those transitions were announced to nobody and lost. A listener-free query API has to
    /// answer correctly regardless of who was alive when.
    ///
    /// Every test runs under throwaway save profiles so a developer's real unlocks.json is
    /// never touched.
    /// </summary>
    [TestFixture]
    public class UnlockProvenanceTests {
        private const string P1 = "pctest-prov-one";
        private const string P2 = "pctest-prov-two";

        private static readonly string[] AllTestProfiles = { P1, P2 };

        private bool _autoSaveBefore;

        [SetUp]
        public void SetUp() {
            Assert.IsNotNull(UnlockManager.Instance,
                "No UnlockManager asset is discoverable. Place one in a Resources folder.");
            _autoSaveBefore = SaveProfile.AutoSave;
            SaveProfile.AutoSave = true;
            CleanTestProfiles();

            // Switching to a freshly-deleted profile resets the ordinal counter to 1, so each
            // test starts from a known marker. Tests still compare against captured markers
            // rather than literal ordinals wherever the absolute value does not matter.
            SaveProfile.SetActive(P1);
            UnlockManager.ResetSavedUnlocks();
            UnlockManager.ResetSessionUnlocks();
        }

        [TearDown]
        public void TearDown() {
            SaveProfile.AutoSave = _autoSaveBefore;
            // Deselect before deleting: leaving a test profile active would make the
            // switch-away flush recreate the directory we are trying to remove.
            SaveProfile.SetActive("");
            CleanTestProfiles();
            UnlockManager.ResetSessionUnlocks();
        }

        private static void CleanTestProfiles() {
            foreach (string id in AllTestProfiles) {
                if (SaveProfile.ProfileExists(id))
                    SaveProfile.DeleteProfile(id);
            }
        }

        // ── 1. Ordering and the marker ───────────────────────────────────

        [Test]
        public void Unlock_MintsStrictlyIncreasingOrdinals() {
            UnlockManager.Unlock(Item("Prov:First"));
            UnlockManager.Unlock(Item("Prov:Second"));

            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:First", out var first));
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:Second", out var second));
            Assert.Less(first.Ordinal, second.Ordinal,
                "a later unlock must carry a higher ordinal");
        }

        [Test]
        public void GetUnlocksSince_ReturnsOnlyWhatFollowedTheMarker() {
            UnlockManager.Unlock(Item("Prov:Before"));

            int marker = UnlockManager.UnlockMarker;
            UnlockManager.Unlock(Item("Prov:DuringA"));
            UnlockManager.Unlock(Item("Prov:DuringB"));

            CollectionAssert.AreEqual(
                new[] { "Prov:DuringA", "Prov:DuringB" },
                Keys(UnlockManager.GetUnlocksSince(marker)),
                "the match-scoped query must return exactly the unlocks after the marker, in order");
        }

        [Test]
        public void GetUnlocksSince_IsRepeatableAndTieBreaksMigratedKeysByName() {
            // Every migrated key shares ordinal 0, so only the key tie-break makes the order total.
            WriteRawUnlockFile(P1, "{\"savedUnlockedKeys\":[\"Legacy:C\",\"Legacy:A\",\"Legacy:B\"]}");
            SaveProfile.Reload();

            var first = Keys(UnlockManager.GetUnlocksSince(-1));
            var second = Keys(UnlockManager.GetUnlocksSince(-1));

            CollectionAssert.AreEqual(new[] { "Legacy:A", "Legacy:B", "Legacy:C" }, first,
                "records sharing an ordinal must fall back to key order");
            CollectionAssert.AreEqual(first, second,
                "repeated calls must not depend on dictionary iteration order");
        }

        [Test]
        public void Ordinal_IsNotReusedAfterTheNewestUnlockIsLocked() {
            var cycled = Item("Prov:Cycled");
            UnlockManager.Unlock(cycled);
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:Cycled", out var before));

            UnlockManager.Lock(cycled);
            UnlockManager.Unlock(Item("Prov:Later"));

            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:Later", out var after));
            Assert.Greater(after.Ordinal, before.Ordinal,
                "a retired ordinal must never be reissued, or a held marker would start hiding unlocks");
        }

        // ── 2. Acknowledgement ───────────────────────────────────────────

        [Test]
        public void GetUnacknowledgedUnlocks_TracksAcknowledgement() {
            UnlockManager.Unlock(Item("Prov:Fresh"));
            CollectionAssert.Contains(Keys(UnlockManager.GetUnacknowledgedUnlocks()), "Prov:Fresh",
                "a new unlock starts unacknowledged");

            UnlockManager.AcknowledgeByKey("Prov:Fresh");
            CollectionAssert.DoesNotContain(Keys(UnlockManager.GetUnacknowledgedUnlocks()), "Prov:Fresh",
                "acknowledging must remove it from the unseen list");
        }

        [Test]
        public void Acknowledgement_SurvivesSaveAndReload() {
            UnlockManager.Unlock(Item("Prov:Seen"));
            UnlockManager.AcknowledgeByKey("Prov:Seen");

            SaveProfile.Save();
            SaveProfile.Reload();

            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:Seen", out var record));
            Assert.IsTrue(record.Acknowledged,
                "acknowledgement must be durable — that is the whole point of persisting it");
        }

        [Test]
        public void UnacknowledgedUnlock_SurvivesAQuitAndIsQueryableAtNextBoot() {
            UnlockManager.Unlock(Item("Prov:EarnedNotShown"));
            SaveProfile.Save();

            // Reload stands in for a restart: memory is dropped and rebuilt from disk.
            SaveProfile.Reload();

            CollectionAssert.Contains(Keys(UnlockManager.GetUnacknowledgedUnlocks()), "Prov:EarnedNotShown",
                "an unlock earned but never shown must still be pending after a restart");
        }

        [Test]
        public void SetAcknowledgedByKey_False_UnmarksAKey() {
            UnlockManager.Unlock(Item("Prov:Toggle"));
            UnlockManager.AcknowledgeByKey("Prov:Toggle");

            UnlockManager.SetAcknowledgedByKey("Prov:Toggle", false);

            CollectionAssert.Contains(Keys(UnlockManager.GetUnacknowledgedUnlocks()), "Prov:Toggle",
                "the debug window's per-row toggle needs the un-acknowledge direction to work");
        }

        [Test]
        public void AcknowledgeAllUnlocked_EmptiesTheUnacknowledgedList() {
            UnlockManager.Unlock(Item("Prov:BulkA"));
            UnlockManager.Unlock(Item("Prov:BulkB"));
            UnlockManager.Unlock(Item("Prov:BulkSession", saves: false));

            UnlockManager.AcknowledgeAllUnlocked();

            CollectionAssert.IsEmpty(UnlockManager.GetUnacknowledgedUnlocks(),
                "acknowledging everything must cover session unlocks too");
        }

        [Test]
        public void Relock_ThenUnlock_MintsAFreshUnacknowledgedRecord() {
            var item = Item("Prov:Regained");

            UnlockManager.Unlock(item);
            UnlockManager.AcknowledgeByKey("Prov:Regained");
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:Regained", out var first));

            UnlockManager.Lock(item);
            Assert.IsFalse(UnlockManager.TryGetUnlockRecord("Prov:Regained", out _),
                "locking drops the record — it describes being unlocked");

            UnlockManager.Unlock(item);
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:Regained", out var second));
            Assert.Greater(second.Ordinal, first.Ordinal, "re-unlocking mints a new ordinal");
            Assert.IsFalse(second.Acknowledged,
                "content taken away and given back reads as new again");
        }

        // ── 3. Migration ─────────────────────────────────────────────────

        [Test]
        public void LegacyFile_MigratesToAcknowledgedWithOrdinalZero() {
            WriteRawUnlockFile(P1, "{\"savedUnlockedKeys\":[\"Legacy:A\",\"Legacy:B\"]}");
            SaveProfile.Reload();

            Assert.IsTrue(UnlockManager.IsUnlockedByKey("Legacy:A"), "the unlock itself must still load");
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Legacy:A", out var record));
            Assert.AreEqual(0, record.Ordinal, "migrated keys take the reserved pre-provenance ordinal");
            Assert.AreEqual(0L, record.UnlockedAtUnixSeconds,
                "the unlock time is genuinely unknown and must not be faked as 'now'");
            Assert.IsTrue(record.Acknowledged,
                "a live save must not present its entire back catalogue as new after the update");

            CollectionAssert.IsEmpty(UnlockManager.GetUnacknowledgedUnlocks(),
                "nothing from a pre-provenance save should be pending");
        }

        [Test]
        public void MigratedKeys_AreExcludedFromEveryNonNegativeMarkerQuery() {
            WriteRawUnlockFile(P1, "{\"savedUnlockedKeys\":[\"Legacy:A\"]}");
            SaveProfile.Reload();

            CollectionAssert.DoesNotContain(Keys(UnlockManager.GetUnlocksSince(0)), "Legacy:A",
                "ordinal 0 sits below every minted ordinal, so a back catalogue can never satisfy 'since my marker'");
        }

        [Test]
        public void LegacyFileWithLockOverrides_StillLoadsBothOverridesAndMigratedRecords() {
            WriteRawUnlockFile(P1,
                "{\"savedUnlockedKeys\":[\"Legacy:Unlocked\"],\"lockedOverrideKeys\":[\"Legacy:Locked\"]}");
            SaveProfile.Reload();

            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Legacy:Unlocked", out _),
                "the saved key must get a migrated record");
            CollectionAssert.Contains(UnlockManager.LockedOverrideKeys, "Legacy:Locked",
                "the pre-provenance override list must survive the schema change");
            Assert.IsFalse(UnlockManager.TryGetUnlockRecord("Legacy:Locked", out _),
                "a locked key is not unlocked, so it carries no record");
        }

        [Test]
        public void HandEditedNextOrdinal_BelowTheHighestRecord_SelfHeals() {
            WriteRawUnlockFile(P1,
                "{\"version\":1,\"savedUnlockedKeys\":[\"Prov:High\"],\"nextOrdinal\":2," +
                "\"unlockRecords\":[{\"key\":\"Prov:High\",\"ordinal\":50,\"unlockedAtUtc\":0,\"acknowledged\":true}]}");
            SaveProfile.Reload();

            Assert.AreEqual(50, UnlockManager.UnlockMarker,
                "the marker must reflect the highest ordinal actually present");

            UnlockManager.Unlock(Item("Prov:Next"));
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:Next", out var next));
            Assert.AreEqual(51, next.Ordinal,
                "a truncated counter must not hand out an ordinal a record already holds");
        }

        [Test]
        public void RecordForAKeyThatIsNotUnlocked_IsPrunedOnLoad() {
            WriteRawUnlockFile(P1,
                "{\"version\":1,\"savedUnlockedKeys\":[],\"nextOrdinal\":8," +
                "\"unlockRecords\":[{\"key\":\"Prov:Orphan\",\"ordinal\":7,\"unlockedAtUtc\":0,\"acknowledged\":false}]}");
            SaveProfile.Reload();

            Assert.IsFalse(UnlockManager.TryGetUnlockRecord("Prov:Orphan", out _),
                "a record whose key is not unlocked must not survive load");
            AssertRecordsMatchUnlockedSets("after loading a file carrying an orphan record");
            Assert.AreEqual(7, UnlockManager.UnlockMarker,
                "the pruned record's ordinal still stays retired");
        }

        // ── 4. Profile scoping ───────────────────────────────────────────

        [Test]
        public void ProvenanceAndAcknowledgement_AreScopedToTheProfile() {
            UnlockManager.Unlock(Item("Prov:P1Only"));
            UnlockManager.AcknowledgeByKey("Prov:P1Only");

            SaveProfile.SetActive(P2);
            Assert.IsFalse(UnlockManager.TryGetUnlockRecord("Prov:P1Only", out _),
                "P2 must not see P1's provenance");
            // Key-specific rather than an emptiness check: SetActive re-runs auto-unlock against
            // the project's own registries, which may legitimately add records of their own.
            CollectionAssert.DoesNotContain(Keys(UnlockManager.GetUnacknowledgedUnlocks()), "Prov:P1Only",
                "P1's pending unlocks must not leak into P2");

            SaveProfile.SetActive(P1);
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:P1Only", out var restored),
                "switching back must restore P1's provenance");
            Assert.IsTrue(restored.Acknowledged, "and its acknowledgement");
        }

        [Test]
        public void Acknowledgement_WithAutoSaveOff_IsHeldUntilAnExplicitSave() {
            UnlockManager.Unlock(Item("Prov:Batched"));
            SaveProfile.Save();

            SaveProfile.AutoSave = false;
            UnlockManager.AcknowledgeByKey("Prov:Batched");

            Assert.IsFalse(SavedRecord(P1, "Prov:Batched").acknowledged,
                "with AutoSave off nothing should reach disk yet");

            SaveProfile.Save();
            Assert.IsTrue(SavedRecord(P1, "Prov:Batched").acknowledged,
                "an explicit save must flush the batched acknowledgement");
        }

        [Test]
        public void ProfileChangingFlush_WritesProvenanceEvenWithAutoSaveOff() {
            SaveProfile.AutoSave = false;
            UnlockManager.Unlock(Item("Prov:Flushed"));

            // What SaveProfile.SetActive does to the outgoing profile.
            ((IProfileScopedStore)UnlockManager.Instance).OnProfileChanging();

            var record = SavedRecord(P1, "Prov:Flushed");
            Assert.IsNotNull(record, "the flush must ignore AutoSave, as it does for the key list");
            Assert.Greater(record.ordinal, 0, "the flushed record must carry its minted ordinal");
        }

        // ── 5. Session-only unlocks ──────────────────────────────────────

        [Test]
        public void SessionUnlock_IsQueryableButNeverWrittenToDisk() {
            UnlockManager.Unlock(Item("Prov:SessionOnly", saves: false));
            SaveProfile.Save();

            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:SessionOnly", out var record));
            Assert.IsTrue(record.IsSessionOnly, "the record must flag itself as session-scoped");
            CollectionAssert.Contains(Keys(UnlockManager.GetUnacknowledgedUnlocks()), "Prov:SessionOnly",
                "a session unlock still counts as unseen progress this match");

            Assert.IsNull(SavedRecord(P1, "Prov:SessionOnly"),
                "session provenance must never reach disk");
        }

        [Test]
        public void SessionRecords_VanishOnSceneReload_WithoutRewindingTheMarker() {
            UnlockManager.Unlock(Item("Prov:Saved"));
            UnlockManager.Unlock(Item("Prov:Session", saves: false));
            int markerBefore = UnlockManager.UnlockMarker;

            InvokeSceneReload();

            Assert.IsFalse(UnlockManager.TryGetUnlockRecord("Prov:Session", out _),
                "a session record dies with the session unlock it describes");
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord("Prov:Saved", out _),
                "saved provenance is untouched by a scene reload");
            Assert.AreEqual(markerBefore, UnlockManager.UnlockMarker,
                "the marker must not rewind, or a discarded ordinal could be reissued");
            AssertRecordsMatchUnlockedSets("after a scene reload");
        }

        // ── 6. The bug this feature exists to close ──────────────────────

        [Test]
        public void AutoUnlockWithNoListenerAlive_IsStillQueryableAfterwards() {
            var definition = MakeAutoUnlockDefinition("SilentAutoUnlock");
            var registry = ScriptableObject.CreateInstance<PlainTestRegistry>();
            registry.definitions = new List<TestUnlockableDefinition> { definition };

            using (new TemporaryRegistry(registry)) {
                // Exactly what OnAwake and every profile switch do, with nothing subscribed to
                // the onUnlocked EventMessage. Before provenance existed this was unrecoverable.
                UnlockManager.EvaluateAutoTriggers();

                string key = ((IUnlockable)definition).UnlockKey;
                Assert.IsTrue(UnlockManager.IsUnlocked(definition), "precondition: it auto-unlocked");
                CollectionAssert.Contains(Keys(UnlockManager.GetUnacknowledgedUnlocks()), key,
                    "an unlock nobody heard must still be reportable to the player later");
            }

            Object.DestroyImmediate(registry);
            Object.DestroyImmediate(definition);
        }

        // ── 7. The invariant, across every mutation path ─────────────────

        [Test]
        public void EveryMutationPath_LeavesARecordForExactlyTheUnlockedKeys() {
            var a = Item("Prov:InvA");
            var b = Item("Prov:InvB", saves: false);
            var c = Item("Prov:InvC");

            UnlockManager.UnlockByKey("Prov:InvKey", savesAcrossSessions: true);
            AssertRecordsMatchUnlockedSets("after UnlockByKey");

            UnlockManager.UnlockAll(new[] { a, b });
            AssertRecordsMatchUnlockedSets("after UnlockAll");

            UnlockManager.UnlockAllByKeys(new[] { ("Prov:InvBulkKey", true), ("Prov:InvBulkSession", false) });
            AssertRecordsMatchUnlockedSets("after UnlockAllByKeys");

            UnlockManager.LockByKey("Prov:InvKey", savesAcrossSessions: true);
            AssertRecordsMatchUnlockedSets("after LockByKey");

            UnlockManager.LockAll(new[] { a });
            AssertRecordsMatchUnlockedSets("after LockAll");

            UnlockManager.LockAllByKeys(new[] { ("Prov:InvBulkKey", true) });
            AssertRecordsMatchUnlockedSets("after LockAllByKeys");

            UnlockManager.Unlock(c);
            UnlockManager.ResetSessionUnlocks();
            AssertRecordsMatchUnlockedSets("after ResetSessionUnlocks");

            UnlockManager.ResetSavedUnlocks();
            AssertRecordsMatchUnlockedSets("after ResetSavedUnlocks");
        }

        [Test]
        public void PurgeStaleSavedKeys_DropsTheRecordsOfThePurgedKeys() {
            // A definition that used to save across sessions but no longer does: its key is in
            // the saved set on disk, and the startup purge must remove key and record together.
            var definition = MakeSessionOnlyDefinition("NowSessionOnly");
            var registry = ScriptableObject.CreateInstance<PlainTestRegistry>();
            registry.definitions = new List<TestUnlockableDefinition> { definition };

            string key = ((IUnlockable)definition).UnlockKey;
            UnlockManager.UnlockByKey(key, savesAcrossSessions: true);
            Assert.IsTrue(UnlockManager.TryGetUnlockRecord(key, out _), "precondition: record exists");

            using (new TemporaryRegistry(registry)) {
                UnlockManager.PurgeStaleSavedKeys();

                Assert.IsFalse(UnlockManager.TryGetUnlockRecord(key, out _),
                    "a purged key must not leave its record behind");
                AssertRecordsMatchUnlockedSets("after PurgeStaleSavedKeys");
            }

            Object.DestroyImmediate(registry);
            Object.DestroyImmediate(definition);
        }

        [Test]
        public void Queries_ReturnEmptyCollectionsRatherThanNull() {
            Assert.IsNotNull(UnlockManager.GetUnacknowledgedUnlocks(),
                "callers must be able to foreach the result without a null check");
            Assert.IsNotNull(UnlockManager.GetUnlocksSince(UnlockManager.UnlockMarker));
            CollectionAssert.IsEmpty(UnlockManager.GetUnlocksSince(UnlockManager.UnlockMarker),
                "nothing has been unlocked since the marker we just took");
        }

        // ────────────────────────────────────────────────────────────────
        // Fixtures
        // ────────────────────────────────────────────────────────────────

        private static StandaloneUnlockable Item(string key, bool saves = true) =>
            new StandaloneUnlockable(key, saves, UnlockBehavior.HideWhenLocked);

        private static List<string> Keys(IReadOnlyList<UnlockRecord> records) {
            var keys = new List<string>(records.Count);
            for (int i = 0; i < records.Count; i++)
                keys.Add(records[i].Key);
            return keys;
        }

        private static string UnlockFilePath(string profileId) =>
            Path.Combine(SaveProfile.RootFor(profileId), SaveProfile.UNLOCKS_FILE);

        private static void WriteRawUnlockFile(string profileId, string json) {
            string path = UnlockFilePath(profileId);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, json);
        }

        /// <summary>Reads one record straight off disk, or null when the file has none for the key.</summary>
        private static UnlockRecordData SavedRecord(string profileId, string key) {
            string path = UnlockFilePath(profileId);
            Assert.IsTrue(File.Exists(path), $"no save file at {path}");

            var data = JsonUtility.FromJson<UnlockSaveData>(File.ReadAllText(path));
            if (data?.unlockRecords == null) return null;

            foreach (var record in data.unlockRecords) {
                if (record != null && record.key == key) return record;
            }
            return null;
        }

        /// <summary>
        /// The invariant every mutation path must preserve: a record exists for exactly the
        /// keys that are currently unlocked. This is what catches a mint or drop call site
        /// missed when a new unlock path is added.
        /// </summary>
        private static void AssertRecordsMatchUnlockedSets(string context) {
            // A set, not a list: a key can legitimately sit in both unlock buckets, and the
            // invariant is about the union, not a multiset.
            var unlocked = new HashSet<string>(PrivateSet("_savedUnlocked"));
            unlocked.UnionWith(PrivateSet("_sessionUnlocked"));

            var field = typeof(UnlockManager).GetField("_records",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, "UnlockManager._records not found");
            var records = (IDictionary)field.GetValue(UnlockManager.Instance);

            var recordKeys = new List<string>();
            foreach (var key in records.Keys)
                recordKeys.Add((string)key);

            CollectionAssert.AreEquivalent(unlocked, recordKeys,
                $"a record must exist for exactly the currently unlocked keys — {context}");
        }

        private static HashSet<string> PrivateSet(string fieldName) {
            var field = typeof(UnlockManager).GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(field, $"UnlockManager.{fieldName} not found");
            return (HashSet<string>)field.GetValue(UnlockManager.Instance);
        }

        /// <summary>Drives the protected scene-reload hook that clears session state.</summary>
        private static void InvokeSceneReload() {
            var method = typeof(UnlockManager).GetMethod("OnSceneReload",
                BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(method, "UnlockManager.OnSceneReload not found");
            method.Invoke(UnlockManager.Instance, null);
        }

        private static TestUnlockableDefinition MakeAutoUnlockDefinition(string assetName) {
            var def = ScriptableObject.CreateInstance<TestUnlockableDefinition>();
            def.name = assetName;
            def.SetIndex(assetName.GetHashCode());
            return def;
        }

        private static TestUnlockableDefinition MakeSessionOnlyDefinition(string assetName) {
            var def = MakeAutoUnlockDefinition(assetName);
            def.SavesAcrossSessionsOverride = false;
            def.AutoUnlockOverride = false;
            return def;
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
        /// No prerequisites, so ArePrerequisitesMet passes trivially and the definition
        /// auto-unlocks as soon as the unlock system can see it. The two overrides let one
        /// type serve both the auto-unlock and the stale-purge tests.
        /// </summary>
        private class TestUnlockableDefinition : BaseDefinition, IUnlockable, IHasPrerequisites {
            private static readonly List<UnlockCondition> NoPrerequisites = new List<UnlockCondition>();

            public bool SavesAcrossSessionsOverride = true;
            public bool AutoUnlockOverride = true;

            string IUnlockable.UnlockKey => $"TestUnlockableDefinition:{name}";
            bool IUnlockable.SavesAcrossSessions => SavesAcrossSessionsOverride;
            UnlockBehavior IUnlockable.LockedBehavior => UnlockBehavior.HideWhenLocked;
            bool IUnlockable.IsUnlockedByDefault => false;

            IReadOnlyList<UnlockCondition> IHasPrerequisites.Prerequisites => NoPrerequisites;
            ConditionMode IHasPrerequisites.PrerequisiteMode => ConditionMode.All;
            bool IHasPrerequisites.AutoUnlock => AutoUnlockOverride;
        }

        private class PlainTestRegistry : BaseRegistry<TestUnlockableDefinition> { }
    }
}
