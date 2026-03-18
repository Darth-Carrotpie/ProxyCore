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
        }

        #endregion

        #region Public API — IUnlockable overloads

        public void Unlock(IUnlockable item) =>
            UnlockByKey(item.UnlockKey, item.SavesAcrossSessions);

        public void Lock(IUnlockable item) =>
            LockByKey(item.UnlockKey, item.SavesAcrossSessions);

        public bool IsUnlocked(IUnlockable item) =>
            IsUnlockedByKey(item.UnlockKey);

        public bool IsLocked(IUnlockable item) =>
            !IsUnlockedByKey(item.UnlockKey);

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
