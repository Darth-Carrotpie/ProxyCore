using NUnit.Framework;

namespace ProxyCore.Tests {
    /// <summary>
    /// Regression tests for the Lock()/Unlock() symmetry contract.
    ///
    /// The bug these guard against: LockByKey added the key to _lockedOverrides but
    /// UnlockByKey never removed it, so Unlock() could not reverse Lock() — while the
    /// bulk paths (UnlockAll / UnlockAllByKeys) did clear it. Same input, two states.
    ///
    /// Every test runs against a throwaway save profile so the developer's real
    /// unlocks.json is never touched.
    /// </summary>
    [TestFixture]
    public class UnlockManagerLockTests {
        private const string TEST_PROFILE = "proxycore_tests";

        private static StandaloneUnlockable Item(string key, bool saves = true, bool unlockedByDefault = false) =>
            new StandaloneUnlockable(key, saves, UnlockBehavior.HideWhenLocked, unlockedByDefault);

        [SetUp]
        public void SetUp() {
            Assert.IsNotNull(UnlockManager.Instance,
                "No UnlockManager asset is discoverable. Place one in a Resources folder.");
            UnlockManager.SetSaveProfile(TEST_PROFILE);
            UnlockManager.ResetSavedUnlocks();
            UnlockManager.ResetSessionUnlocks();
        }

        [TearDown]
        public void TearDown() {
            // Delete every profile file the fixture may have created, then hand the
            // editor back its real save state.
            foreach (var profile in new[] { TEST_PROFILE + "_other", TEST_PROFILE + "_slot2", TEST_PROFILE }) {
                UnlockManager.SetSaveProfile(profile);
                UnlockManager.ResetSavedUnlocks();
            }
            UnlockManager.ResetSessionUnlocks();
            UnlockManager.SetSaveProfile("");
        }

        // ── The reported bug ─────────────────────────────────────────────

        [Test]
        public void Unlock_AfterLock_RestoresUnlockedState() {
            var item = Item("Test:ReversibleLock");

            UnlockManager.Unlock(item);
            Assert.IsTrue(UnlockManager.IsUnlocked(item), "precondition: item starts unlocked");

            UnlockManager.Lock(item);
            Assert.IsFalse(UnlockManager.IsUnlocked(item), "Lock() must take effect");

            UnlockManager.Unlock(item);
            Assert.IsTrue(UnlockManager.IsUnlocked(item),
                "Unlock() must clear the lock override set by Lock()");
        }

        [Test]
        public void Unlock_AfterLock_AlsoWorksForUnlockedByDefaultItems() {
            var item = Item("Test:DefaultUnlocked", unlockedByDefault: true);

            Assert.IsTrue(UnlockManager.IsUnlocked(item), "precondition: unlocked by default");

            UnlockManager.Lock(item);
            Assert.IsFalse(UnlockManager.IsUnlocked(item), "explicit Lock() outranks IsUnlockedByDefault");

            UnlockManager.Unlock(item);
            Assert.IsTrue(UnlockManager.IsUnlocked(item), "Unlock() must restore default access");
        }

        // ── Single vs bulk must not disagree ─────────────────────────────

        [Test]
        public void Unlock_And_UnlockAll_LeaveIdenticalState() {
            var single = Item("Test:ViaSingle");
            var bulk = Item("Test:ViaBulk");

            UnlockManager.Lock(single);
            UnlockManager.Lock(bulk);

            UnlockManager.Unlock(single);
            UnlockManager.UnlockAll(new[] { bulk });

            Assert.AreEqual(UnlockManager.IsUnlocked(bulk), UnlockManager.IsUnlocked(single),
                "Unlock() and UnlockAll() must produce the same IsUnlocked state");
            Assert.AreEqual(UnlockManager.IsUnlockedByKey(bulk.UnlockKey),
                UnlockManager.IsUnlockedByKey(single.UnlockKey),
                "Unlock() and UnlockAll() must produce the same IsUnlockedByKey state");
            Assert.IsTrue(UnlockManager.IsUnlocked(single), "both paths should end unlocked");
        }

        [Test]
        public void Lock_And_LockAll_LeaveIdenticalState() {
            var single = Item("Test:LockViaSingle");
            var bulk = Item("Test:LockViaBulk");

            UnlockManager.UnlockAll(new[] { single, bulk });

            UnlockManager.Lock(single);
            UnlockManager.LockAll(new[] { bulk });

            Assert.AreEqual(UnlockManager.IsUnlocked(bulk), UnlockManager.IsUnlocked(single),
                "Lock() and LockAll() must produce the same IsUnlocked state");
            Assert.IsFalse(UnlockManager.IsUnlocked(single), "both paths should end locked");
        }

        // ── IsUnlocked and IsUnlockedByKey must agree after a lock ───────

        [Test]
        public void Lock_ClearsBothUnlockSets() {
            var item = Item("Test:CrossSetLock");

            // Unlock into the session set, then lock through the saved path.
            UnlockManager.UnlockByKey(item.UnlockKey, savesAcrossSessions: false);
            Assert.IsTrue(UnlockManager.IsUnlockedByKey(item.UnlockKey), "precondition: session-unlocked");

            UnlockManager.LockByKey(item.UnlockKey, savesAcrossSessions: true);

            Assert.IsFalse(UnlockManager.IsUnlockedByKey(item.UnlockKey),
                "Lock must clear both unlock sets so IsUnlockedByKey agrees with IsUnlocked");
            Assert.IsFalse(UnlockManager.IsUnlocked(item));
        }

        // ── Overrides are saved state ────────────────────────────────────

        [Test]
        public void LockOverride_SurvivesReload() {
            var item = Item("Test:PersistentLock", unlockedByDefault: true);

            UnlockManager.Lock(item);
            Assert.IsFalse(UnlockManager.IsUnlocked(item));

            // Round-trip through another profile and back, forcing a Save/Load cycle.
            UnlockManager.SetSaveProfile(TEST_PROFILE + "_other");
            UnlockManager.SetSaveProfile(TEST_PROFILE);

            Assert.IsFalse(UnlockManager.IsUnlocked(item),
                "lock overrides are persisted and must survive a reload");

            UnlockManager.Unlock(item);
            UnlockManager.SetSaveProfile(TEST_PROFILE + "_other");
            UnlockManager.SetSaveProfile(TEST_PROFILE);

            Assert.IsTrue(UnlockManager.IsUnlocked(item),
                "clearing the override must persist too");
        }

        // ── Save profiles isolate state ──────────────────────────────────

        [Test]
        public void SetSaveProfile_IsolatesUnlockState() {
            var item = Item("Test:ProfileScoped");

            UnlockManager.Unlock(item);
            Assert.IsTrue(UnlockManager.IsUnlocked(item));

            UnlockManager.SetSaveProfile(TEST_PROFILE + "_slot2");
            Assert.IsFalse(UnlockManager.IsUnlocked(item),
                "a different save profile must not see the first profile's unlocks");

            UnlockManager.ResetSavedUnlocks();
            UnlockManager.SetSaveProfile(TEST_PROFILE);
            Assert.IsTrue(UnlockManager.IsUnlocked(item),
                "switching back must restore the original profile's state");
        }
    }
}
