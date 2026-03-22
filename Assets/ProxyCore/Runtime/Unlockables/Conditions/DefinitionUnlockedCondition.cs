using UnityEngine;

namespace ProxyCore {
    /// <summary>
    /// Condition that passes when a specific definition is currently unlocked.
    ///
    /// The target must implement IUnlockable. Use this to build dependency chains:
    /// e.g. "Character B unlocks only after Character A is unlocked."
    ///
    /// Usage:
    ///   1. Create asset: ProxyCore/Unlockables/Conditions/Definition Is Unlocked
    ///   2. Assign the target definition in the inspector
    ///   3. Add to a definition's Prerequisites list
    /// </summary>
    [CreateAssetMenu(fileName = "DefinitionIsUnlocked",
        menuName = "Unlockables/Definition Is Unlocked (Condition)")]
    public class DefinitionUnlockedCondition : UnlockCondition {
        [Tooltip("The definition whose unlock state is checked. Must implement IUnlockable.")]
        [SerializeField] private BaseDefinition _target;

        public override bool Evaluate() {
            if (_target == null) return false;

            var unlockable = _target as IUnlockable;
            if (unlockable == null) {
                Debug.LogWarning($"[DefinitionUnlockedCondition] '{_target.name}' does not implement IUnlockable.", this);
                return false;
            }

            return UnlockManager.Instance.IsUnlocked(unlockable);
        }
    }
}
