using System.Collections.Generic;
using System.IO;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ProxyCore {
    /// <summary>
    /// Global manager for all unlock state in the game.
    ///
    /// Tracks three sets of unlock keys:
    ///   _savedUnlocked   — written to disk; survives across game sessions.
    ///   _sessionUnlocked — in-memory only; cleared on every scene reload.
    ///   _lockedOverrides — keys explicitly locked via Lock(); outranks IsUnlockedByDefault.
    ///                      Written to disk with _savedUnlocked. Unlock() clears the override,
    ///                      so Lock() and Unlock() are symmetric.
    ///
    /// Call SetSaveProfile() (or SaveProfile.SetActive) to point disk state at a per-save-game
    /// directory — "proxycore/{profileId}/unlocks.json" instead of the shared "unlocks.json".
    ///
    /// Assign EventMessage assets to onUnlocked / onLocked to broadcast state changes
    /// through the ProxyCore EventCoordinator. Both fields are optional and null-guarded.
    ///
    /// Create one asset (Assets > Create > Managers > Unlock Manager) and place it
    /// anywhere discoverable by Resources.Load or AssetDatabase (see SingletonSO).
    /// </summary>
    [CreateAssetMenu(fileName = "UnlockManager", menuName = "Managers/Unlock Manager")]
    public class UnlockManager : SingletonSO<UnlockManager>, IProfileScopedStore {
        #region Fields

        [Header("Events")]
        [Tooltip("Fired with a StringPayload(unlockKey) whenever any item transitions to unlocked.")]
        [SerializeField] private EventMessage _onUnlocked;

        [Tooltip("Fired with a StringPayload(unlockKey) whenever any item transitions to locked.")]
        [SerializeField] private EventMessage _onLocked;

        [Tooltip("Fired with a StringPayload(unlockKey) for each saved key removed because its definition now has SavesAcrossSessions = false.")]
        [SerializeField] private EventMessage _onStalePurge;

        private HashSet<string> _savedUnlocked = new HashSet<string>();
        private HashSet<string> _sessionUnlocked = new HashSet<string>();
        // Keys that were explicitly locked via Lock() — overrides IsUnlockedByDefault.
        // Persisted alongside _savedUnlocked; cleared by Unlock() or ResetSavedUnlocks().
        // ponytail: one shared override set. Split into saved/session sets only if
        // session-only locks must stop persisting.
        private HashSet<string> _lockedOverrides = new HashSet<string>();

        // Provenance for every key currently unlocked, keyed by unlock key.
        // Invariant: _records.Keys == _savedUnlocked ∪ _sessionUnlocked — MintRecord adds,
        // ReconcileRecords prunes, and UnlockProvenanceTests asserts it after every mutation.
        // Session-ness is derived from set membership rather than stored, so WriteToDisk
        // simply filters on _savedUnlocked and session records never reach disk.
        private Dictionary<string, UnlockRecordData> _records = new Dictionary<string, UnlockRecordData>();

        // Ordinal the next unlock takes. Only ever increases within a profile. Deliberately not
        // rewound by ResetSavedUnlocks or a scene reload: a marker a caller is still holding must
        // never start hiding unlocks that happened after it. Only a profile switch resets it,
        // and Load() immediately restores the incoming profile's value.
        private int _nextOrdinal = 1;

        [Header("Auto-unlock Registries")]
        [Tooltip("Registries scanned each time any item is unlocked. Any definition implementing IHasPrerequisites with AutoUnlock=true will unlock automatically when its prerequisites pass. Use the Refresh button or ProxyCore > Unlock Actions > Refresh Unlock Registries to populate.")]
        [SerializeField] private List<RegistryEntry> _registries = new List<RegistryEntry>();

        private bool _evaluatingTriggers;
        [System.NonSerialized] private bool _didWarnEmptyRegistries;
        [System.NonSerialized] private bool _didWarnUnusableRegistries;
        [System.NonSerialized] private bool _didWarnStaleRegistries;

        [System.Serializable]
        private class RegistryEntry {
            [Tooltip("A registry asset (any BaseRegistry<T>). Must implement IUnlockableCatalog.")]
            public ScriptableObject Registry;
            [Tooltip("When false, this registry is skipped during auto-unlock evaluation.")]
            public bool Enabled = true;
        }

        // Resolved through SaveProfile so unlock state follows the active save game.
        private static string SavePath => SaveProfile.PathFor(SaveProfile.UNLOCKS_FILE);

        #endregion

        #region Lifecycle

        protected override void OnAwake() {
            base.OnAwake();
            // Session unlocks must clear on scene reload; persistent disk data is always re-loaded.
            _persistent = false;
            _didWarnEmptyRegistries = false;
            _didWarnUnusableRegistries = false;
            _didWarnStaleRegistries = false;
            SaveProfile.Register(this);
            Load();
#if UNITY_EDITOR
            ValidateRegistryConfigurationInEditor();
#endif
            PurgeStaleSavedKeys();
            EvaluateAutoTriggers();
        }

        protected override void OnDisable() {
            SaveProfile.Unregister(this);
            base.OnDisable();
        }

        protected override void OnSceneReload() {
            base.OnSceneReload();
            // Only session state resets here. _lockedOverrides is saved state — it is
            // reloaded from disk with _savedUnlocked and cleared by Unlock()/ResetSavedUnlocks().
            _sessionUnlocked.Clear();
            // Session records die with the session unlocks they describe. _nextOrdinal is left
            // alone so their ordinals are never handed out a second time.
            ReconcileRecords();
        }

        #endregion

        #region IProfileScopedStore

        /// <summary>Flushes unlock state to the current profile. Ignores the autosave setting.</summary>
        void IProfileScopedStore.OnProfileChanging() => WriteToDisk();

        /// <summary>Drops all unlock state — saved, session, and overrides — and reloads.</summary>
        void IProfileScopedStore.OnProfileChanged(string profileRoot) {
            _savedUnlocked.Clear();
            _sessionUnlocked.Clear();
            _lockedOverrides.Clear();
            // Provenance and the ordinal counter are per profile: a marker captured under the
            // outgoing profile means nothing under the incoming one. Load() restores the
            // incoming profile's counter, or leaves it at 1 when that profile has no file yet.
            _records.Clear();
            _nextOrdinal = 1;
            Load();
        }

        #endregion

        #region Debug — Read-only state (used by UnlockDebugWindow)

        /// <summary>Keys currently unlocked and persisted to disk.</summary>
        public static IReadOnlyCollection<string> SavedUnlockedKeys => Instance?._savedUnlocked;

        /// <summary>Keys unlocked this session only (not written to disk).</summary>
        public static IReadOnlyCollection<string> SessionUnlockedKeys => Instance?._sessionUnlocked;

        /// <summary>Keys explicitly locked via Lock(), outranking IsUnlockedByDefault.</summary>
        public static IReadOnlyCollection<string> LockedOverrideKeys => Instance?._lockedOverrides;

        #endregion

        #region Public API — IUnlockable overloads

        /// <summary>
        /// Unlocks the item and clears any explicit lock override on it.
        /// Unlock() always reverses a previous Lock() — the two are symmetric.
        /// </summary>
        public static void Unlock(IUnlockable item) =>
            UnlockByKey(item.UnlockKey, item.SavesAcrossSessions);

        /// <summary>
        /// Locks the item explicitly. The override outranks IsUnlockedByDefault and persists
        /// across sessions; clear it with <see cref="Unlock(IUnlockable)"/> or
        /// <see cref="ResetSavedUnlocks"/>.
        /// </summary>
        public static void Lock(IUnlockable item) =>
            LockByKey(item.UnlockKey, item.SavesAcrossSessions);

        /// <summary>
        /// Returns true when the item is explicitly unlocked OR unlocked by default.
        /// An explicit Lock() call takes precedence and will return false even if IsUnlockedByDefault is true,
        /// until a subsequent Unlock() clears the override.
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

        /// <summary>
        /// Unlocks a key and clears any explicit lock override on it, so Unlock() always
        /// reverses a previous Lock(). Broadcasts onUnlocked when either changes.
        /// </summary>
        public static void UnlockByKey(string key, bool savesAcrossSessions) {
            var inst = Instance;
            if (inst == null) return;

            // Symmetric with LockByKey: unlocking always clears an explicit lock override.
            bool clearedOverride = inst._lockedOverrides.Remove(key);
            bool nowUnlocked = savesAcrossSessions
                ? inst._savedUnlocked.Add(key)
                : inst._sessionUnlocked.Add(key);
            if (nowUnlocked) inst.MintRecord(key);
            if (!clearedOverride && !nowUnlocked) return;

            if (clearedOverride || savesAcrossSessions) inst.Save();
            inst.BroadcastUnlocked(key);
            EvaluateAutoTriggers();
        }

        /// <summary>
        /// Locks a key: records an explicit override (which outranks IsUnlockedByDefault)
        /// and drops the key from both unlock sets. Broadcasts onLocked when either changes.
        /// </summary>
        /// <param name="savesAcrossSessions">
        /// Retained for API symmetry with <see cref="UnlockByKey"/>. Lock overrides are always
        /// persisted, so this no longer selects a storage tier.
        /// </param>
        public static void LockByKey(string key, bool savesAcrossSessions) {
            var inst = Instance;
            if (inst == null) return;

            bool addedOverride = inst._lockedOverrides.Add(key);
            // "Locked" means "not unlocked" — clear both sets so IsUnlockedByKey agrees with IsUnlocked.
            bool removedSaved = inst._savedUnlocked.Remove(key);
            bool removedSession = inst._sessionUnlocked.Remove(key);
            // The record describes being unlocked, so it dies with the unlock. A later Unlock()
            // mints a fresh ordinal and acknowledged = false, re-badging the key as new.
            inst._records.Remove(key);
            if (!addedOverride && !removedSaved && !removedSession) return;

            inst.Save();
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
            bool anyPersistedChange = false;
            foreach (var item in items) {
                bool clearedOverride = inst._lockedOverrides.Remove(item.UnlockKey);
                anyPersistedChange |= clearedOverride;
                if (item.SavesAcrossSessions) {
                    if (inst._savedUnlocked.Add(item.UnlockKey)) {
                        inst.MintRecord(item.UnlockKey);
                        anyPersistedChange = true;
                        inst.BroadcastUnlocked(item.UnlockKey);
                    }
                    else if (clearedOverride) {
                        inst.BroadcastUnlocked(item.UnlockKey);
                    }
                }
                else {
                    // Add still runs exactly once and first, as before — hoisted only so the
                    // mint can see whether the key was genuinely new.
                    bool nowUnlocked = inst._sessionUnlocked.Add(item.UnlockKey);
                    if (nowUnlocked) inst.MintRecord(item.UnlockKey);
                    if (nowUnlocked || clearedOverride)
                        inst.BroadcastUnlocked(item.UnlockKey);
                }
            }
            if (anyPersistedChange) inst.Save();
            EvaluateAutoTriggers();
        }

        /// <summary>
        /// Locks all items in the collection. Saved items are written to disk in a single pass.
        /// </summary>
        public static void LockAll(IEnumerable<IUnlockable> items) {
            var inst = Instance;
            if (inst == null) return;
            bool anyPersistedChange = false;
            foreach (var item in items) {
                bool addedOverride = inst._lockedOverrides.Add(item.UnlockKey);
                bool removedSaved = inst._savedUnlocked.Remove(item.UnlockKey);
                bool removedSession = inst._sessionUnlocked.Remove(item.UnlockKey);
                inst._records.Remove(item.UnlockKey);
                if (!addedOverride && !removedSaved && !removedSession) continue;

                anyPersistedChange = true;
                inst.BroadcastLocked(item.UnlockKey);
            }
            if (anyPersistedChange) inst.Save();
        }

        /// <summary>
        /// Key-based bulk unlock. Each tuple supplies the key and whether it should be saved across sessions.
        /// </summary>
        public static void UnlockAllByKeys(IEnumerable<(string key, bool savesAcrossSessions)> entries) {
            var inst = Instance;
            if (inst == null) return;
            bool anyPersistedChange = false;
            foreach (var (key, saves) in entries) {
                bool clearedOverride = inst._lockedOverrides.Remove(key);
                anyPersistedChange |= clearedOverride;
                if (saves) {
                    if (inst._savedUnlocked.Add(key)) {
                        inst.MintRecord(key);
                        anyPersistedChange = true;
                        inst.BroadcastUnlocked(key);
                    }
                    else if (clearedOverride) {
                        inst.BroadcastUnlocked(key);
                    }
                }
                else {
                    // Add still runs exactly once and first, as before — hoisted only so the
                    // mint can see whether the key was genuinely new.
                    bool nowUnlocked = inst._sessionUnlocked.Add(key);
                    if (nowUnlocked) inst.MintRecord(key);
                    if (nowUnlocked || clearedOverride)
                        inst.BroadcastUnlocked(key);
                }
            }
            if (anyPersistedChange) inst.Save();
            EvaluateAutoTriggers();
        }

        /// <summary>
        /// Key-based bulk lock. Each tuple supplies the key and whether it is stored across sessions.
        /// </summary>
        public static void LockAllByKeys(IEnumerable<(string key, bool savesAcrossSessions)> entries) {
            var inst = Instance;
            if (inst == null) return;
            bool anyPersistedChange = false;
            foreach (var (key, saves) in entries) {
                bool addedOverride = inst._lockedOverrides.Add(key);
                bool removedSaved = inst._savedUnlocked.Remove(key);
                bool removedSession = inst._sessionUnlocked.Remove(key);
                inst._records.Remove(key);
                if (!addedOverride && !removedSaved && !removedSession) continue;

                anyPersistedChange = true;
                inst.BroadcastLocked(key);
            }
            if (anyPersistedChange) inst.Save();
        }

        #endregion

        #region Public API — Reset

        /// <summary>
        /// Removes saved (cross-session) unlock keys for any definition in the configured registries
        /// that now declares SavesAcrossSessions = false. Called automatically on startup after Load().
        /// Safe to call manually at any time while the manager is active.
        /// </summary>
        public static void PurgeStaleSavedKeys() {
            var inst = Instance;
            if (inst == null) return;
            if (inst._registries == null || inst._registries.Count == 0) return;

            var sessionOnlyKeys = new HashSet<string>();
            foreach (var entry in inst._registries) {
                if (entry == null || !entry.Enabled || entry.Registry == null) continue;
                var catalog = entry.Registry as IUnlockableCatalog;
                if (catalog == null) continue;
                foreach (var def in catalog.GetCatalogDefinitions()) {
                    if (def is IUnlockable u && !u.SavesAcrossSessions)
                        sessionOnlyKeys.Add(u.UnlockKey);
                }
            }

            var purged = new List<string>();
            foreach (var key in sessionOnlyKeys) {
                if (inst._savedUnlocked.Remove(key)) {
                    inst._records.Remove(key);
                    purged.Add(key);
                }
            }

            if (purged.Count == 0) return;

            inst.Save();
            foreach (var key in purged)
                inst.BroadcastStalePurge(key);
        }

        /// <summary>
        /// Clears all saved state — cross-session unlocks and explicit lock overrides —
        /// and deletes the active profile's save file from disk. Session unlocks are unaffected.
        /// </summary>
        public static void ResetSavedUnlocks() {
            var inst = Instance;
            if (inst == null) return;
            inst._savedUnlocked.Clear();
            inst._lockedOverrides.Clear();
            // Drops the records of the cleared saved keys; session records survive with their
            // unlocks. _nextOrdinal is deliberately not rewound — see the field comment.
            inst.ReconcileRecords();
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }

        /// <summary>
        /// Clears all session-only unlock state. Saved state (cross-session unlocks and
        /// lock overrides) is unaffected — call Unlock() to clear an override, or
        /// <see cref="ResetSavedUnlocks"/> to clear them all.
        /// </summary>
        public static void ResetSessionUnlocks() {
            var inst = Instance;
            if (inst == null) return;
            inst._sessionUnlocked.Clear();
            inst.ReconcileRecords();
        }

        /// <summary>
        /// Switches the active save profile, so every ProxyCore store — unlock state and flag
        /// collections alike — reads and writes that save's own files under
        /// "proxycore/{profileId}/" instead of the shared default.
        /// Use one profile per save game. All in-memory state is discarded and the new profile
        /// is loaded from disk.
        ///
        /// Convenience wrapper for <see cref="ProxyCore.SaveProfile.SetActive"/>. Compose ids
        /// with <see cref="ProxyCore.SaveProfile.Id"/> when a save is keyed by several values —
        /// it guarantees two different saves cannot collide on one directory.
        /// </summary>
        /// <param name="profileId">
        /// Profile id. Pass null or empty to return to the default (unprofiled) save.
        /// </param>
        public static void SetSaveProfile(string profileId) => SaveProfile.SetActive(profileId);

        #endregion

        #region Persistence

        /// <summary>
        /// Persists after a mutation. Honours <see cref="ProxyCore.SaveProfile.AutoSave"/>, so a
        /// game that batches writes gets nothing on disk until it calls SaveProfile.Save().
        /// </summary>
        private void Save() {
            if (SaveProfile.AutoSave) WriteToDisk();
        }

        /// <summary>Writes unlock state to the active profile unconditionally.</summary>
        private void WriteToDisk() {
            var data = new UnlockSaveData();
            data.version = UnlockSaveData.CURRENT_VERSION;
            data.savedUnlockedKeys.AddRange(_savedUnlocked);
            data.lockedOverrideKeys.AddRange(_lockedOverrides);
            data.nextOrdinal = _nextOrdinal;

            // Only saved keys get provenance on disk. Session records are dropped here rather
            // than filtered out of _records, so a session unlock stays queryable in memory while
            // never surviving a restart.
            foreach (var key in _savedUnlocked) {
                if (_records.TryGetValue(key, out var record))
                    data.unlockRecords.Add(record);
            }

            SaveProfile.WriteAtomic(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
        }

        private void Load() {
            _savedUnlocked.Clear();
            _lockedOverrides.Clear();

            // Returns null when missing, and quarantines the file when it cannot be parsed
            // rather than losing it silently — see SaveProfile.ReadJson.
            var data = SaveProfile.ReadJson<UnlockSaveData>(SavePath);
            if (data == null) {
                // Records for the saved keys just cleared would otherwise linger as orphans.
                ReconcileRecords();
                return;
            }

            if (data.savedUnlockedKeys != null) {
                foreach (var key in data.savedUnlockedKeys)
                    _savedUnlocked.Add(key);
            }

            // Absent in files written before lock overrides were persisted — loads as empty.
            if (data.lockedOverrideKeys != null) {
                foreach (var key in data.lockedOverrideKeys)
                    _lockedOverrides.Add(key);
            }

            LoadRecords(data);
        }

        /// <summary>
        /// Rebuilds provenance from a loaded file, synthesising records for keys that predate it.
        ///
        /// A file written before provenance existed carries version 0 and no records, so every
        /// saved key is migrated to ordinal 0 with acknowledged = true. Zero sits below every
        /// minted ordinal, so <see cref="GetUnlocksSince"/> can never surface a back catalogue as
        /// "since my marker"; acknowledged = true stops a live save presenting everything the
        /// player already owns as new on the first boot after the update. The cost is that an
        /// unlock genuinely earned but unseen just before the update is treated as seen —
        /// silent and one-off, which is the better failure than loud and total.
        /// </summary>
        private void LoadRecords(UnlockSaveData data) {
            int highestOrdinal = 0;

            if (data.unlockRecords != null) {
                foreach (var record in data.unlockRecords) {
                    if (record == null || string.IsNullOrEmpty(record.key)) continue;
                    _records[record.key] = record;
                    if (record.ordinal > highestOrdinal) highestOrdinal = record.ordinal;
                }
            }

            // Any saved key the file carries no record for — a pre-provenance file, or one
            // hand-edited to add a key. Both want the same conservative defaults.
            foreach (var key in _savedUnlocked) {
                if (_records.ContainsKey(key)) continue;
                _records[key] = new UnlockRecordData {
                    key = key,
                    ordinal = 0,
                    // Honestly unknown. Stamping "now" would date the entire back catalogue to
                    // the moment of the update and lie to anything displaying the timestamp.
                    unlockedAtUtc = 0,
                    acknowledged = true,
                };
            }

            // Drops records for keys the file no longer lists.
            ReconcileRecords();

            // Trust the stored counter, but never let a hand-edited or truncated one hand out an
            // ordinal a record already holds — including one just pruned, whose ordinal must
            // stay retired so a caller's marker keeps meaning what it meant.
            _nextOrdinal = Mathf.Max(Mathf.Max(data.nextOrdinal, highestOrdinal + 1), 1);
        }

        #endregion

        #region Public API — Provenance & acknowledgement

        /// <summary>
        /// Highest unlock ordinal handed out so far in the active profile. Capture this at a
        /// point you care about — match start, boot, opening a rewards screen — and pass it back
        /// to <see cref="GetUnlocksSince"/> later to get exactly what was unlocked in between.
        ///
        /// Unlike the onUnlocked EventMessage, this needs no listener to have been alive: it
        /// answers correctly even for the auto-unlock pass that runs during OnAwake, before any
        /// UI exists.
        ///
        /// The marker is scoped to the active save profile. Discard and re-capture held markers
        /// when <see cref="ProxyCore.SaveProfile.ProfileChanged"/> fires — a marker from one
        /// profile means nothing in another.
        /// </summary>
        public static int UnlockMarker {
            get {
                var inst = Instance;
                return inst != null ? inst._nextOrdinal - 1 : 0;
            }
        }

        /// <summary>
        /// Every currently unlocked key the host has not yet acknowledged, oldest first.
        /// Survives quitting: an unlock earned but never shown is still here at next boot.
        /// Includes session-only unlocks, flagged with <see cref="UnlockRecord.IsSessionOnly"/>.
        /// Never null.
        /// </summary>
        public static IReadOnlyList<UnlockRecord> GetUnacknowledgedUnlocks() {
            var inst = Instance;
            if (inst == null) return System.Array.Empty<UnlockRecord>();

            var results = new List<UnlockRecord>();
            foreach (var pair in inst._records) {
                if (pair.Value.acknowledged) continue;
                results.Add(inst.ToRecord(pair.Value));
            }
            results.Sort(CompareByOrdinal);
            return results;
        }

        /// <summary>
        /// Every key unlocked after the given marker, oldest first. Pass a value previously read
        /// from <see cref="UnlockMarker"/>; pass 0 for "everything unlocked this profile since
        /// provenance began" (which excludes keys migrated from a pre-provenance save).
        /// Never null.
        /// </summary>
        public static IReadOnlyList<UnlockRecord> GetUnlocksSince(int marker) {
            var inst = Instance;
            if (inst == null) return System.Array.Empty<UnlockRecord>();

            var results = new List<UnlockRecord>();
            foreach (var pair in inst._records) {
                if (pair.Value.ordinal <= marker) continue;
                results.Add(inst.ToRecord(pair.Value));
            }
            results.Sort(CompareByOrdinal);
            return results;
        }

        /// <summary>
        /// Looks up provenance for one key. False when the key is not currently unlocked —
        /// records exist only for unlocked keys.
        /// </summary>
        public static bool TryGetUnlockRecord(string key, out UnlockRecord record) {
            var inst = Instance;
            if (inst != null && key != null && inst._records.TryGetValue(key, out var data)) {
                record = inst.ToRecord(data);
                return true;
            }
            record = default;
            return false;
        }

        /// <summary>
        /// Sets whether the player has been shown this unlock. The single acknowledgement
        /// primitive — the Acknowledge helpers all route through it. Passing false un-marks a
        /// key, which is what the debug window's per-row toggle uses.
        ///
        /// Acknowledgement is independent of unlock state, but a record only exists while the
        /// key is unlocked: locking drops it, so a later unlock starts unacknowledged again.
        /// No-op for a key that is not currently unlocked.
        /// </summary>
        public static void SetAcknowledgedByKey(string key, bool acknowledged) {
            var inst = Instance;
            if (inst == null) return;
            if (inst.SetAcknowledgedInternal(key, acknowledged)) inst.Save();
        }

        public static void Acknowledge(IUnlockable item) => AcknowledgeByKey(item.UnlockKey);

        public static void AcknowledgeByKey(string key) => SetAcknowledgedByKey(key, true);

        /// <summary>Acknowledges every item in the collection, writing to disk once.</summary>
        public static void AcknowledgeAll(IEnumerable<IUnlockable> items) {
            var inst = Instance;
            if (inst == null || items == null) return;

            bool anyPersistedChange = false;
            foreach (var item in items)
                anyPersistedChange |= inst.SetAcknowledgedInternal(item.UnlockKey, true);
            if (anyPersistedChange) inst.Save();
        }

        /// <summary>Acknowledges every key in the collection, writing to disk once.</summary>
        public static void AcknowledgeAllByKeys(IEnumerable<string> keys) {
            var inst = Instance;
            if (inst == null || keys == null) return;

            bool anyPersistedChange = false;
            foreach (var key in keys)
                anyPersistedChange |= inst.SetAcknowledgedInternal(key, true);
            if (anyPersistedChange) inst.Save();
        }

        /// <summary>
        /// Acknowledges everything currently unlocked, writing to disk once. The "player closed
        /// the rewards screen" call.
        /// </summary>
        public static void AcknowledgeAllUnlocked() {
            var inst = Instance;
            if (inst == null) return;

            bool anyPersistedChange = false;
            foreach (var pair in inst._records) {
                if (pair.Value.acknowledged) continue;
                pair.Value.acknowledged = true;
                if (inst._savedUnlocked.Contains(pair.Key)) anyPersistedChange = true;
            }
            if (anyPersistedChange) inst.Save();
        }

        #endregion

        #region Provenance internals

        /// <summary>
        /// Stamps a key that has just entered an unlock set. Always overwrites, so a key that
        /// was locked and unlocked again gets a fresh ordinal and reads as unacknowledged.
        /// The ordinal is minted in memory regardless of AutoSave, so unlock order stays
        /// coherent within a session even when nothing is flushed.
        /// </summary>
        private void MintRecord(string key) {
            _records[key] = new UnlockRecordData {
                key = key,
                ordinal = _nextOrdinal++,
                unlockedAtUtc = System.DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                acknowledged = false,
            };
        }

        /// <summary>
        /// Restores the invariant that a record exists only for a currently unlocked key, after
        /// any operation that clears a whole set. Retired ordinals are never reissued — see
        /// _nextOrdinal.
        /// </summary>
        private void ReconcileRecords() {
            if (_records.Count == 0) return;

            List<string> orphans = null;
            foreach (var key in _records.Keys) {
                if (_savedUnlocked.Contains(key) || _sessionUnlocked.Contains(key)) continue;
                (orphans ??= new List<string>()).Add(key);
            }

            if (orphans == null) return;
            foreach (var key in orphans)
                _records.Remove(key);
        }

        /// <summary>
        /// Applies an acknowledgement. Returns true only when a <b>persisted</b> record actually
        /// changed, so callers can batch and skip a pointless whole-file rewrite: session
        /// records never reach disk, so changing one is not worth a write.
        /// </summary>
        private bool SetAcknowledgedInternal(string key, bool acknowledged) {
            if (key == null || !_records.TryGetValue(key, out var record)) return false;
            if (record.acknowledged == acknowledged) return false;

            record.acknowledged = acknowledged;
            return _savedUnlocked.Contains(key);
        }

        private UnlockRecord ToRecord(UnlockRecordData data) =>
            new UnlockRecord(data.key, data.ordinal, data.unlockedAtUtc, data.acknowledged,
                _sessionUnlocked.Contains(data.key));

        /// <summary>
        /// Minted ordinals are unique, but every key migrated from a pre-provenance file shares
        /// ordinal 0 — so the key breaks the tie and the returned order stays total, repeatable
        /// across calls, and independent of dictionary iteration order.
        /// </summary>
        private static int CompareByOrdinal(UnlockRecord a, UnlockRecord b) {
            int byOrdinal = a.Ordinal.CompareTo(b.Ordinal);
            return byOrdinal != 0 ? byOrdinal : string.CompareOrdinal(a.Key, b.Key);
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
            if (inst == null || inst._evaluatingTriggers) return;
            if (inst._registries == null || inst._registries.Count == 0) {
                inst.WarnEmptyRegistriesOnce();
                return;
            }

            inst._evaluatingTriggers = true;
            try {
                bool anyNew;
                do {
                    anyNew = false;
                    bool hasUsableCatalog = false;
                    foreach (var entry in inst._registries) {
                        if (entry == null || !entry.Enabled || entry.Registry == null) continue;
                        var catalog = entry.Registry as IUnlockableCatalog;
                        if (catalog == null) continue;
                        hasUsableCatalog = true;

                        foreach (var def in catalog.GetCatalogDefinitions()) {
                            if (!(def is IUnlockable unlockable)) continue;
                            if (!(def is IHasPrerequisites prereqs)) continue;
                            if (!prereqs.AutoUnlock || IsUnlocked(unlockable)) continue;

                            if (ArePrerequisitesMet(prereqs)) {
                                Unlock(unlockable);
                                // Only count it if state actually moved. Without this, an item
                                // that stays locked after Unlock() re-qualifies every pass and
                                // spins the do/while loop forever.
                                if (IsUnlocked(unlockable)) anyNew = true;
                            }
                        }
                    }

                    if (!hasUsableCatalog) {
                        inst.WarnUnusableRegistriesOnce();
                        return;
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
                    if (c == null || !TryEvaluateCondition(prereqs, c, failOnException: true, out bool result) || !result)
                        return false;
                return true;
            }
            else {
                foreach (var c in conditions)
                    if (c != null && TryEvaluateCondition(prereqs, c, failOnException: false, out bool result) && result)
                        return true;
                return false;
            }
        }

        private static bool TryEvaluateCondition(IHasPrerequisites prereqs,
            UnlockCondition condition, bool failOnException, out bool result) {
            result = false;
            try {
                result = condition.Evaluate();
                return true;
            }
            catch (System.Exception ex) {
                LogConditionEvaluationError(prereqs, condition, ex);
                return !failOnException;
            }
        }

        private static void LogConditionEvaluationError(IHasPrerequisites prereqs,
            UnlockCondition condition, System.Exception ex) {
            string ownerName = GetPrereqOwnerName(prereqs);
            Debug.LogError(
                $"[UnlockManager] Condition '{condition.name}' threw while evaluating prerequisites for '{ownerName}': {ex}",
                condition);
        }

        private static string GetPrereqOwnerName(IHasPrerequisites prereqs) {
            if (prereqs is Object obj && obj != null) return obj.name;
            return prereqs?.GetType().Name ?? "<unknown>";
        }

        private void WarnEmptyRegistriesOnce() {
            if (_didWarnEmptyRegistries) return;
            _didWarnEmptyRegistries = true;
            Debug.LogWarning(
                "[UnlockManager] Auto-unlock registry list is empty. Run ProxyCore/Unlockable Actions/Refresh Unlock Registries or click Refresh Registries in the UnlockManager inspector.",
                this);
        }

        private void WarnUnusableRegistriesOnce() {
            if (_didWarnUnusableRegistries) return;
            _didWarnUnusableRegistries = true;
            Debug.LogWarning(
                "[UnlockManager] Auto-unlock registries are configured but none are usable (disabled, null, or not IUnlockableCatalog). Run Refresh Unlock Registries and verify Enabled flags.",
                this);
        }

#if UNITY_EDITOR
        private void ValidateRegistryConfigurationInEditor() {
            if (_didWarnStaleRegistries) return;

            int configuredCount = CountConfiguredCatalogs();
            int discoveredCount = DiscoverCatalogCountInProject();
            if (configuredCount == discoveredCount) return;

            _didWarnStaleRegistries = true;
            Debug.LogWarning(
                $"[UnlockManager] Configured unlock registries ({configuredCount}) do not match discovered catalogs ({discoveredCount}). Run ProxyCore/Unlockable Actions/Refresh Unlock Registries.",
                this);
        }

        private int CountConfiguredCatalogs() {
            if (_registries == null || _registries.Count == 0) return 0;

            var uniqueInstanceIds = new HashSet<int>();
            foreach (var entry in _registries) {
                if (entry?.Registry is not IUnlockableCatalog) continue;
                uniqueInstanceIds.Add(entry.Registry.GetInstanceID());
            }

            return uniqueInstanceIds.Count;
        }

        private static int DiscoverCatalogCountInProject() {
            var uniqueInstanceIds = new HashSet<int>();
            string[] guids = AssetDatabase.FindAssets("t:ScriptableObject");
            foreach (string guid in guids) {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(path);
                if (asset is IUnlockableCatalog)
                    uniqueInstanceIds.Add(asset.GetInstanceID());
            }

            return uniqueInstanceIds.Count;
        }
#endif

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

        private void BroadcastStalePurge(string key) {
            if (_onStalePurge == null) return;
            new EventTriggerBuilder(_onStalePurge)
                .With(new StringPayload(key))
                .Send();
        }

        #endregion
    }
}
