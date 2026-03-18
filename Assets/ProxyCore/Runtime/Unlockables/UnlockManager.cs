using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProxyCore
{
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
    public class UnlockManager : SingletonSO<UnlockManager>
    {
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

        private static string SavePath =>
            Path.Combine(Application.persistentDataPath, "unlocks.json");

        #endregion

        #region Lifecycle

        protected override void OnAwake()
        {
            base.OnAwake();
            // Session unlocks must clear on scene reload; persistent disk data is always re-loaded.
            _persistent = false;
            Load();
        }

        protected override void OnSceneReload()
        {
            base.OnSceneReload();
            _sessionUnlocked.Clear();
            _lockedOverrides.Clear();
        }

        #endregion

        #region Debug — Read-only state (used by UnlockDebugWindow)

        /// <summary>Keys currently unlocked and persisted to disk.</summary>
        public IReadOnlyCollection<string> SavedUnlockedKeys => _savedUnlocked;

        /// <summary>Keys unlocked this session only (not written to disk).</summary>
        public IReadOnlyCollection<string> SessionUnlockedKeys => _sessionUnlocked;

        #endregion

        #region Public API — IUnlockable overloads

        public void Unlock(IUnlockable item) =>
            UnlockByKey(item.UnlockKey, item.SavesAcrossSessions);

        public void Lock(IUnlockable item) =>
            LockByKey(item.UnlockKey, item.SavesAcrossSessions);

        /// <summary>
        /// Returns true when the item is explicitly unlocked OR unlocked by default.
        /// An explicit Lock() call takes precedence and will return false even if IsUnlockedByDefault is true.
        /// </summary>
        public bool IsUnlocked(IUnlockable item) =>
            !_lockedOverrides.Contains(item.UnlockKey) &&
            (IsUnlockedByKey(item.UnlockKey) || item.IsUnlockedByDefault);

        public bool IsLocked(IUnlockable item) =>
            !IsUnlocked(item);

        #endregion

        #region Public API — Key overloads

        public void UnlockByKey(string key, bool savesAcrossSessions)
        {
            if (savesAcrossSessions)
            {
                if (_savedUnlocked.Add(key))
                {
                    Save();
                    BroadcastUnlocked(key);
                }
            }
            else
            {
                if (_sessionUnlocked.Add(key))
                    BroadcastUnlocked(key);
            }
        }

        public void LockByKey(string key, bool savesAcrossSessions)
        {
            _lockedOverrides.Add(key);

            bool changed;
            if (savesAcrossSessions)
            {
                changed = _savedUnlocked.Remove(key);
                if (changed) Save();
            }
            else
            {
                changed = _sessionUnlocked.Remove(key);
            }

            if (changed)
                BroadcastLocked(key);
        }

        public bool IsUnlockedByKey(string key) =>
            _savedUnlocked.Contains(key) || _sessionUnlocked.Contains(key);

        public bool IsLockedByKey(string key) =>
            !IsUnlockedByKey(key);

        #endregion

        #region Public API — Bulk helpers

        /// <summary>
        /// Unlocks all items in the collection. Saved items are written to disk in a single pass.
        /// </summary>
        public void UnlockAll(IEnumerable<IUnlockable> items)
        {
            bool anySaved = false;
            foreach (var item in items)
            {
                _lockedOverrides.Remove(item.UnlockKey);
                if (item.SavesAcrossSessions)
                {
                    if (_savedUnlocked.Add(item.UnlockKey))
                    {
                        anySaved = true;
                        BroadcastUnlocked(item.UnlockKey);
                    }
                }
                else
                {
                    if (_sessionUnlocked.Add(item.UnlockKey))
                        BroadcastUnlocked(item.UnlockKey);
                }
            }
            if (anySaved) Save();
        }

        /// <summary>
        /// Locks all items in the collection. Saved items are written to disk in a single pass.
        /// </summary>
        public void LockAll(IEnumerable<IUnlockable> items)
        {
            bool anySaved = false;
            foreach (var item in items)
            {
                _lockedOverrides.Add(item.UnlockKey);
                if (item.SavesAcrossSessions)
                {
                    if (_savedUnlocked.Remove(item.UnlockKey))
                    {
                        anySaved = true;
                        BroadcastLocked(item.UnlockKey);
                    }
                }
                else
                {
                    if (_sessionUnlocked.Remove(item.UnlockKey))
                        BroadcastLocked(item.UnlockKey);
                }
            }
            if (anySaved) Save();
        }

        /// <summary>
        /// Key-based bulk unlock. Each tuple supplies the key and whether it should be saved across sessions.
        /// </summary>
        public void UnlockAllByKeys(IEnumerable<(string key, bool savesAcrossSessions)> entries)
        {
            bool anySaved = false;
            foreach (var (key, saves) in entries)
            {
                _lockedOverrides.Remove(key);
                if (saves)
                {
                    if (_savedUnlocked.Add(key))
                    {
                        anySaved = true;
                        BroadcastUnlocked(key);
                    }
                }
                else
                {
                    if (_sessionUnlocked.Add(key))
                        BroadcastUnlocked(key);
                }
            }
            if (anySaved) Save();
        }

        /// <summary>
        /// Key-based bulk lock. Each tuple supplies the key and whether it is stored across sessions.
        /// </summary>
        public void LockAllByKeys(IEnumerable<(string key, bool savesAcrossSessions)> entries)
        {
            bool anySaved = false;
            foreach (var (key, saves) in entries)
            {
                _lockedOverrides.Add(key);
                if (saves)
                {
                    if (_savedUnlocked.Remove(key))
                    {
                        anySaved = true;
                        BroadcastLocked(key);
                    }
                }
                else
                {
                    if (_sessionUnlocked.Remove(key))
                        BroadcastLocked(key);
                }
            }
            if (anySaved) Save();
        }

        #endregion

        #region Public API — Reset

        /// <summary>
        /// Clears all saved (cross-session) unlock state and deletes the save file from disk.
        /// Session unlocks and locked overrides are unaffected.
        /// </summary>
        public void ResetSavedUnlocks()
        {
            _savedUnlocked.Clear();
            if (File.Exists(SavePath))
                File.Delete(SavePath);
        }

        /// <summary>
        /// Clears all session-only unlock state. Saved (cross-session) state is unaffected.
        /// </summary>
        public void ResetSessionUnlocks()
        {
            _sessionUnlocked.Clear();
            _lockedOverrides.Clear();
        }

        #endregion

        #region Persistence

        private void Save()
        {
            var data = new UnlockSaveData();
            data.savedUnlockedKeys.AddRange(_savedUnlocked);
            File.WriteAllText(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
        }

        private void Load()
        {
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

        #region Broadcasting

        private void BroadcastUnlocked(string key)
        {
            if (_onUnlocked == null) return;
            new EventTriggerBuilder(_onUnlocked)
                .With(new StringPayload(key))
                .Send();
        }

        private void BroadcastLocked(string key)
        {
            if (_onLocked == null) return;
            new EventTriggerBuilder(_onLocked)
                .With(new StringPayload(key))
                .Send();
        }

        #endregion
    }
}
