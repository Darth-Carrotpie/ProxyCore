using System.Collections.Generic;
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
    /// Flag state is session-only and resets on every application launch.
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
    public class GameFlagCollection : ScriptableObject {
        [Header("Declared Flags")]
        [Tooltip("All flags that belong to this collection. Declare every flag name here before using it at runtime.")]
        [SerializeField] private List<string> _definedFlags = new List<string>();

        [Header("Events")]
        [Tooltip("Fired with StringPayload(flagName) whenever any flag in this collection changes value.")]
        [SerializeField] private EventMessage _onFlagChanged;

        [Tooltip("When true, calling SetFlag() automatically re-evaluates all UnlockAutoTriggers on the UnlockManager, making flag changes immediately propagate to unlock chains.")]
        [SerializeField] private bool _autoEvaluateOnSet = true;

        private Dictionary<string, bool> _state;

        // Lazy init — ensures fresh state after domain reloads without requiring OnEnable overhead.
        private Dictionary<string, bool> State => _state ?? (_state = new Dictionary<string, bool>());

        private void OnEnable() => _state = new Dictionary<string, bool>();

        // ------------------------------------------------------------------ //
        //  Public API                                                          //
        // ------------------------------------------------------------------ //

        /// <summary>All declared flag names. Use to populate dropdowns or validate keys.</summary>
        public IReadOnlyList<string> DefinedFlags => _definedFlags.AsReadOnly();

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
    }
}
