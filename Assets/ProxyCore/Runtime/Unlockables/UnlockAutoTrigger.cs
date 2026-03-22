using UnityEngine;

namespace ProxyCore {
    /// <summary>
    /// A data asset that registers one IUnlockable definition for automatic unlocking.
    /// When all prerequisites on the target definition pass, UnlockManager unlocks it automatically.
    ///
    /// Assign trigger assets to the UnlockManager's Auto-unlock Triggers list.
    /// The target must implement both IUnlockable and IHasPrerequisites.
    ///
    /// Chains are supported: unlocking A fires trigger evaluation, which may unlock B,
    /// which fires evaluation again, and so on — all within a single re-entrant-safe pass.
    ///
    /// Usage:
    ///   1. Create asset: ProxyCore/Unlockables/Unlock Auto Trigger
    ///   2. Assign a definition that implements IUnlockable + IHasPrerequisites
    ///   3. Add the asset to UnlockManager._autoTriggers
    /// </summary>
    [CreateAssetMenu(fileName = "UnlockAutoTrigger",
        menuName = "Unlockables/Unlock Auto Trigger")]
    public class UnlockAutoTrigger : ScriptableObject {
        [Tooltip("The definition to unlock when all prerequisites are met. Must implement IUnlockable and IHasPrerequisites.")]
        [SerializeField] private BaseDefinition _target;

        /// <summary>The target cast as IUnlockable. Null if the target doesn't implement IUnlockable.</summary>
        public IUnlockable Target => _target as IUnlockable;

        /// <summary>
        /// Returns true when the target's prerequisites are all satisfied (or Any, per the definition's ConditionMode).
        /// An empty prerequisites list always returns true — the trigger fires as soon as it's evaluated.
        /// Returns false if the target is null or doesn't implement IHasPrerequisites.
        /// </summary>
        public bool ArePrerequisitesMet() {
            if (_target == null || !(_target is IUnlockable)) return false;

            var hasPrereqs = _target as IHasPrerequisites;

            // No prerequisites → always ready.
            if (hasPrereqs == null || hasPrereqs.Prerequisites == null || hasPrereqs.Prerequisites.Count == 0)
                return true;

            var prereqs = hasPrereqs.Prerequisites;

            if (hasPrereqs.PrerequisiteMode == ConditionMode.All) {
                foreach (var condition in prereqs)
                    if (condition == null || !condition.Evaluate()) return false;
                return true;
            }
            else // Any
            {
                foreach (var condition in prereqs)
                    if (condition != null && condition.Evaluate()) return true;
                return false;
            }
        }
    }
}
