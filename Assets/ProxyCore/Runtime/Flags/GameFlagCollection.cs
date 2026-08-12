using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ProxyCore {
    /// <summary>
    /// A named collection of boolean flags for tracking game state.
    /// Declare flag names in the inspector, then set/query them at runtime.
    ///
    /// Multiple collections can coexist — create one per logical domain
    /// (e.g. "Global Game State", "Achievement Flags", "Tutorial Flags")
    /// to keep concerns separated.
    ///
    /// Flag state defaults to session-only and resets on every application launch.
    /// Enable _savesAcrossSessions to write flag state to disk and restore it on next launch.
    /// When _autoEvaluateOnSet is enabled, changing any flag automatically
    /// re-evaluates all UnlockAutoTriggers on the UnlockManager.
    ///
    /// Usage:
    ///   myCollection.SetFlag("boss_defeated", true);
    ///   bool done = myCollection.GetFlag("boss_defeated");
    ///
    /// Create: ProxyCore/Flags/Game Flag Collection
    /// </summary>
    [CreateAssetMenu(fileName = "GameFlagCollection",
        menuName = "Flags/Game Flag Collection")]
    public class GameFlagCollection : ScriptableObject, IProfileScopedStore {
        [Header("Declared Flags")]
        [Tooltip("All flags that belong to this collection. Declare every flag name here before using it at runtime.")]
        [SerializeField] private List<string> _definedFlags = new List<string>();

        [Header("Events")]
        [Tooltip("Fired with StringPayload(flagName) whenever any flag in this collection changes value.")]
        [SerializeField] private EventMessage _onFlagChanged;

        [Tooltip("When true, calling SetFlag() automatically re-evaluates all UnlockAutoTriggers on the UnlockManager, making flag changes immediately propagate to unlock chains.")]
        [SerializeField] private bool _autoEvaluateOnSet = true;

        [Header("Persistence")]
        [Tooltip("When true, flag state is written to disk and restored across game sessions. Each collection saves to its own file named after this asset. Renaming the asset changes the save file path.")]
        [SerializeField] private bool _savesAcrossSessions = false;

        private Dictionary<string, bool> _state;

        // Lazy init — ensures fresh state after domain reloads without requiring OnEnable overhead.
        private Dictionary<string, bool> State => _state ?? (_state = new Dictionary<string, bool>());

        private void OnEnable() {
            SaveProfile.Register(this);
            _state = new Dictionary<string, bool>();
            if (_savesAcrossSessions) Load();
        }

        private void OnDisable() {
            SaveProfile.Unregister(this);
        }

        // ------------------------------------------------------------------ //
        //  IProfileScopedStore                                                 //
        // ------------------------------------------------------------------ //

        /// <summary>Flushes flag state to the current profile. Ignores the autosave setting.</summary>
        void IProfileScopedStore.OnProfileChanging() {
            if (_savesAcrossSessions) WriteToDisk();
        }

        /// <summary>
        /// Drops flag state and reloads from the new profile. Session-only collections clear
        /// too, so flags never leak from one save game into another.
        /// </summary>
        void IProfileScopedStore.OnProfileChanged(string profileRoot) {
            _state = new Dictionary<string, bool>();
            if (_savesAcrossSessions) Load();
        }

        // ------------------------------------------------------------------ //
        //  Public API                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>All declared flag names. Use to populate dropdowns or validate keys.</summary>
        public IReadOnlyList<string> DefinedFlags => _definedFlags.AsReadOnly();

        /// <summary>When true, flag state is written to disk and restored across game sessions.</summary>
        public bool SavesAcrossSessions => _savesAcrossSessions;

        /// <summary>
        /// Sets a flag to the given value.
        /// The flag must be declared in the inspector list; undeclared names are rejected with a warning.
        /// </summary>
        public void SetFlag(string flagName, bool value) {
            if (!_definedFlags.Contains(flagName)) {
                Debug.LogWarning($"[GameFlagCollection:{name}] Flag '{flagName}' is not declared. Add it to the inspector list first.", this);
                return;
            }

            State[flagName] = value;

            if (_savesAcrossSessions) Save();

            if (_onFlagChanged != null)
                new EventTriggerBuilder(_onFlagChanged).With(new StringPayload(flagName)).Send();

            if (_autoEvaluateOnSet)
                UnlockManager.EvaluateAutoTriggers();
        }

        /// <summary>Returns the current value of a flag, or false if never set.</summary>
        public bool GetFlag(string flagName) {
            State.TryGetValue(flagName, out bool value);
            return value;
        }

        /// <summary>Returns true if the flag name is declared in this collection.</summary>
        public bool IsDeclared(string flagName) => _definedFlags.Contains(flagName);

        /// <summary>
        /// Returns declared flag names as an array.
        /// Used by FlagConditionEditor to populate its dropdown.
        /// </summary>
        public string[] GetDefinedFlagsArray() => _definedFlags.ToArray();

        /// <summary>Clears in-memory flag state and deletes the save file for this collection.</summary>
        public void ResetSavedFlags() {
            _state = new Dictionary<string, bool>();
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }

        // ------------------------------------------------------------------ //
        //  Persistence                                                         //
        // ------------------------------------------------------------------ //

        // Resolved through SaveProfile so flag state follows the active save game.
        private string SavePath => SaveProfile.PathFor($"flags_{name}.json");

        /// <summary>
        /// Persists after a mutation. Honours <see cref="ProxyCore.SaveProfile.AutoSave"/>, so a
        /// game that batches writes gets nothing on disk until it calls SaveProfile.Save().
        /// </summary>
        private void Save() {
            if (SaveProfile.AutoSave) WriteToDisk();
        }

        /// <summary>Writes flag state to the active profile unconditionally.</summary>
        private void WriteToDisk() {
            var data = new FlagSaveData();
            foreach (var kv in State)
                if (kv.Value) data.setFlagNames.Add(kv.Key);

            // Don't litter a save with files for collections that never had a flag set.
            // An existing file is still rewritten, so clearing every flag does persist.
            if (data.setFlagNames.Count == 0 && !File.Exists(SavePath)) return;

            SaveProfile.WriteAtomic(SavePath, JsonUtility.ToJson(data, prettyPrint: true));
        }

        private void Load() {
            // Returns null when missing, and quarantines the file when it cannot be parsed
            // rather than losing it silently — see SaveProfile.ReadJson.
            var data = SaveProfile.ReadJson<FlagSaveData>(SavePath);
            if (data?.setFlagNames == null) return;
            foreach (var flagName in data.setFlagNames)
                if (_definedFlags.Contains(flagName))
                    State[flagName] = true;
        }
    }
}
