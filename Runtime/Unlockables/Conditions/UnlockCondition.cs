using UnityEngine;

namespace ProxyCore {
    /// <summary>
    /// Abstract base class for all unlock prerequisite conditions.
    ///
    /// Create a ScriptableObject asset for each condition and assign it to a definition's
    /// Prerequisites list. The UnlockManager evaluates all registered UnlockAutoTriggers
    /// by calling Evaluate() on each condition.
    ///
    /// To add a new condition type: inherit from this class, add [CreateAssetMenu], and
    /// override Evaluate() with your logic.
    /// </summary>
    public abstract class UnlockCondition : ScriptableObject {
        /// <summary>
        /// Returns true when this condition is currently satisfied.
        /// Called by UnlockAutoTrigger.ArePrerequisitesMet() during trigger evaluation.
        /// </summary>
        public abstract bool Evaluate();
    }
}
