using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProxyCore {
    /// <summary>
    /// Global manager for all unlock state in the game.
    ///
    /// Tracks two independent sets of unlock keys:
    ///   _savedUnlocked   — written to disk; survives across game sessions.
    ///   _sessionUnlocked — in-memory only; cleared on every scene reload.
    ///
    /// Assign EventMessage assets to onUnlocked / onLocked to broadcast state changes
    /// through the ProxyCore EventCoordinator. Both fields are optional and null-guarded.
    ///
    /// Create one asset (Assets > Create > Managers > Unlock Manager) and place it
    /// anywhere discoverable by Resources.Load or AssetDatabase (see SingletonSO).
    /// </summary>
    [CreateAssetMenu(fileName = "UnlockManager", menuName = "Managers/Unlock Manager")]
    public class UnlockManager : SingletonSO<UnlockManager> {
        #region Fields

        [Header("Events")]
        [Tooltip("Fired with a StringPayload(unlockKey) whenever any item transitions to unlocked.")]
        [SerializeField] private EventMessage _onUnlocked;

        [Tooltip("Fired with a StringPayload(unlockKey) whenever any item transitions to locked.")]
        [SerializeField] private EventMessage _onLocked;

        private HashSet<string> _savedUnlocked = new HashSet<string>();
        private HashSet<string> _sessionUnlocked = new HashSet<string>();
        // Keys that were explicitly locked via Lock() — overrides IsUnlockedByDefault.
        private HashSet<string> _lockedOverrides = new HashSet<string>();

        [Header("Auto-unlock Registries")]
        [Tooltip("Registries scanned each time any item is unlocked. Any definition implementing IHasPrerequisites with AutoUnlock=true will unlock automatically when its prerequisites pass. Use the Refresh button or ProxyCore > Unlock Actions > Refresh Unlock Registries to populate.")]
        [SerializeField] private List<RegistryEntry> _registries = new List<RegistryEntry>();

        private bool _evaluatingTriggers;

        [System.Serializable]
        private class RegistryEntry {
            [Tooltip("A registry asset (any BaseRegistry<T>). Must implement IUnlockableCatalog.")]
            public ScriptableObject Registry;
            [Tooltip("When false, this registry is skipped during auto-unlock evaluation.")]
            public bool Enabled = true;
        }

        private static string SavePath =>
            Path.Combine(Application.persistentDataPath, "unlocks.json");

        #endregion

        #region Lifecycle

        protected override void OnAwake() {
            base.OnAwake();
            // Session unlocks must clear on scene reload; persistent disk data is always re-loaded.
            _persistent = false;
            Load();
            EvaluateAutoTriggers();
        }

        protected override void OnSceneReload() {
            base.OnSceneReload();
            _sessionUnlocked.Clear();
            _lockedOverrides.Clear();
        }

        #endregion

        #region Debug — Read-only state (used by UnlockDebugWindow)

        /// <summary>Keys currently unlocked and persisted to disk.</summary>
        public static IReadOnlyCollection<string> SavedUnlockedKeys => Instance?._savedUnlocked;

        /// <summary>Keys unlocked this session only (not written to disk).</summary>
        public static IReadOnlyCollection<string> SessionUnlockedKeys => Instance?._sessionUnlocked;

        #endregion

        #region Public API — IUnlockable overloads

        public static void Unlock(IUnlockable item) =>
            UnlockByKey(item.UnlockKey, item.SavesAcrossSessions);

        public static void Lock(IUnlockable item) =>
            LockByKey(item.UnlockKey, item.SavesAcrossSessions);

        /// <summary>
        /// Returns true when the item is explicitly unlocked OR unlocked by default.
        /// An explicit Lock() call takes precedence and will return false even if IsUnlockedByDefault is true.
        /// </summary>
        public static bool IsUnlocked(IUnlockable item) {
            var inst = Instance;
            if (inst == null) return item.IsUnlockedByDefault;
            return !inst._lockedOverrides.Contains(item.UnlockKey) &&
                   (IsUnlockedByKey(item.UnlockKey) || item.IsUnlockedByDefault);
        }

        public static bool IsLocked(IUnlockable item) => !IsUnlocked(item);

        #endregion

        #region Public API — Key overloads

        public static void UnlockByKey(string key, bool savesAcrossSessions) {
            var inst = Instance;
            if (inst == null) return;
            if (savesAcrossSessions) {
                if (inst._savedUnlocked.Add(key)) {
                    inst.Save();
                    inst.BroadcastUnlocked(key);
                    EvaluateAutoTriggers();
                }
            }
            else {
                if (inst._sessionUnlocked.Add(key)) {
                    inst.BroadcastUnlocked(key);
                    EvaluateAutoTriggers();
                }
            }
        }

        public static void LockByKey(string key, bool savesAcrossSessions) {
            var inst = Instance;
            if (inst == null) return;
            inst._lockedOverrides.Add(key);

            bool changed;
            if (savesAcrossSessions) {
                changed = inst._savedUnlocked.Remove(key);
                if (changed) inst.Save();
            }
            else {
                changed = inst._sessionUnlocked.Remove(key);
            }

            if (changed)
                inst.BroadcastLocked(key);
        }

        public static bool IsUnlockedByKey(string key) {
            var inst = Instance;
            return inst != null && (inst._savedUnlocked.Contains(key) || inst._sessionUnlocked.Contains(key));
        }

        public static bool IsLockedByKey(string key) => !IsUnlockedByKey(key);

        #endregion

        #region Public API — Bulk helpers

        /// <summary>
        /// Unlocks all items in the collection. Saved items are written to disk in a single pass.
        /// </summary>
        public static void UnlockAll(IEnumerable<IUnlockable> items) {
            var inst = Instance;
            if (inst == null) return;
            bool anySaved = false;
            foreach (var item in items) {
                inst._lockedOverrides.Remove(item.UnlockKey);
                if (item.SavesAcrossSessions) {
                    if (inst._savedUnlocked.Add(item.UnlockKey)) {
                        anySaved = true;
                        inst.BroadcastUnlocked(item.UnlockKey);
                    }
                }
                else {
                    if (inst._sessionUnlocked.Add(item.UnlockKey))
                        inst.BroadcastUnlocked(item.UnlockKey);
                }
            }
            if (anySaved) inst.Save();
            EvaluateAutoTriggers();
        }

        /// <summary>
        /// Locks all items in the collection. Saved items are written to disk in a single pass.
        /// </summary>
        public static void LockAll(IEnumerable<IUnlockable> items) {
            var inst = Instance;
            if (inst == null) return;
            bool anySaved = false;
            foreach (var item in items) {
                inst._lockedOverrides.Add(item.UnlockKey);
                if (item.SavesAcrossSessions) {
                    if (inst._savedUnlocked.Remove(item.UnlockKey)) {
                        anySaved = true;
                        inst.BroadcastLocked(item.UnlockKey);
                    }
                }
                else {
                    if (inst._sessionUnlocked.Remove(item.UnlockKey))
                        inst.BroadcastLocked(item.UnlockKey);
                }
            }
            if (anySaved) inst.Save();
        }

        /// <summary>
        /// Key-based bulk unlock. Each tuple supplies the key and whether it should be saved across sessions.
        /// </summary>
        public static void UnlockAllByKeys(IEnumerable<(string key, bool savesAcrossSessions)> entries) {
            var inst = Instance;
            if (inst == null) return;
            bool anySaved = false;
            foreach (var (key, saves) in entries) {
                inst._lockedOverrides.Remove(key);
                if (saves) {
                    if (inst._savedUnlocked.Add(key)) {
                        anySaved = true;
                        inst.BroadcastUnlocked(key);
                    }
                }
                else {
                    if (inst._sessionUnlocked.Add(key))
                        inst.BroadcastUnlocked(key);
                }
            }
            if (anySaved) inst.Save();
            EvaluateAutoTriggers();
        }

        /// <summary>
        /// Key-based bulk lock. Each tuple supplies the key and whether it is stored across sessions.
        /// </summary>
        public static void LockAllByKeys(IEnumerable<(string key, bool savesAcrossSessions)> entries) {
            var inst = Instance;
            if (inst == null) return;
            bool anySaved = false;
            foreach (var (key, saves) in entries) {
                inst._lockedOverrides.Add(key);
                if (saves) {
                    if (inst._savedUnlocked.Remove(key)) {
                        anySaved = true;
                        inst.BroadcastLocked(key);
                    }
                }
                else {
                    if (inst._sessionUnlocked.Remove(key))
                        inst.BroadcastLocked(key);
                }
            }
            if (anySaved) inst.Save();
        }

        #endregion

        #region Public API — Reset

        /// <summary>
        /// Clears all saved (cross-session) unlock state and deletes the save file from disk.
        /// Session unlocks and locked overrides are unaffected.
        /// </summary>
        public static void ResetSavedUnlocks() {
            var inst = Instance;
            if (inst == null) return;
            inst._savedUnlocked.Clear();
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }

        /// <summary>
        /// Clears all session-only unlock state. Saved (cross-session) state is unaffected.
        /// </summary>
        public static void ResetSessionUnlocks() {
            var inst = Instance;
            if (inst == null) return;
            inst._sessionUnlocked.Clear();
            inst._lockedOverrides.Clear();
        }

        #endregion

        #region Persistence

        private void Save() {
            var data = new UnlockSaveData();
            data.savedUnlockedKeys.AddRange(_savedUnlocked);
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
        }

        private void Load() {
            _savedUnlocked.Clear();
            if (!File.Exists(SavePath))
                return;

            var data = JsonUtility.FromJson<UnlockSaveData>(File.ReadAllText(SavePath));
            if (data?.savedUnlockedKeys == null)
                return;

            foreach (var key in data.savedUnlockedKeys)
                _savedUnlocked.Add(key);
        }

        #endregion

        #region Auto-unlock

        /// <summary>
        /// Evaluates all registered auto-unlock triggers against current unlock state.
        /// Called automatically after every Unlock() call and on startup.
        ///
        /// Re-entrant-safe: nested calls (caused by chains) return immediately;
        /// the outer do-while loop keeps iterating until no new unlocks occur in a full pass,
        /// so arbitrarily deep chains resolve in O(depth) outer iterations.
        /// </summary>
        public static void EvaluateAutoTriggers() {
            var inst = Instance;
            if (inst == null || inst._evaluatingTriggers || inst._registries == null || inst._registries.Count == 0) return;
            inst._evaluatingTriggers = true;
            try {
                bool anyNew;
                do {
                    anyNew = false;
                    foreach (var entry in inst._registries) {
                        if (entry == null || !entry.Enabled || entry.Registry == null) continue;
                        var catalog = entry.Registry as IUnlockableCatalog;
                        if (catalog == null) continue;

                        foreach (var def in catalog.GetCatalogDefinitions()) {
                            if (!(def is IUnlockable unlockable)) continue;
                            if (!(def is IHasPrerequisites prereqs)) continue;
                            if (!prereqs.AutoUnlock || IsUnlocked(unlockable)) continue;

                            if (ArePrerequisitesMet(prereqs)) {
                                Unlock(unlockable);
                                anyNew = true;
                            }
                        }
                    }
                } while (anyNew);
            }
            finally {
                inst._evaluatingTriggers = false;
            }
        }

        private static bool ArePrerequisitesMet(IHasPrerequisites prereqs) {
            var conditions = prereqs.Prerequisites;
            if (conditions == null || conditions.Count == 0) return true;

            if (prereqs.PrerequisiteMode == ConditionMode.All) {
                foreach (var c in conditions)
                    if (c == null || !c.Evaluate()) return false;
                return true;
            }
            else {
                foreach (var c in conditions)
                    if (c != null && c.Evaluate()) return true;
                return false;
            }
        }

        #endregion

        #region Broadcasting

        private void BroadcastUnlocked(string key) {
            if (_onUnlocked == null) return;
            new EventTriggerBuilder(_onUnlocked)
                .With(new StringPayload(key))
                .Send();
        }

        private void BroadcastLocked(string key) {
            if (_onLocked == null) return;
            new EventTriggerBuilder(_onLocked)
                .With(new StringPayload(key))
                .Send();
        }

        #endregion
    }
}
