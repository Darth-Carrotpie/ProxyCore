using UnityEngine;

namespace ProxyCore {
    /// <summary>
    /// Condition that passes when a specific named flag is set to true in a GameFlagCollection.
    ///
    /// Usage:
    ///   1. Create a GameFlagCollection asset and declare your flag names in it.
    ///   2. Create asset: Unlockables/Flag Is Set (Condition)
    ///   3. Assign the collection and pick the flag name from the dropdown (in the inspector).
    ///   4. Add to a definition's Prerequisites list.
    ///
    /// When GameFlagCollection._autoEvaluateOnSet is true, calling SetFlag() on the collection
    /// will automatically re-evaluate auto-unlock triggers — no manual polling needed.
    /// </summary>
    [CreateAssetMenu(fileName = "FlagCondition",
        menuName = "Unlockables/Flag Is Set (Condition)")]
    public class FlagCondition : UnlockCondition {
        [Tooltip("The flag collection to query.")]
        [SerializeField] private GameFlagCollection _collection;

        [Tooltip("The flag name to check. Must be declared in the collection above.")]
        [SerializeField] private string _flagName;

        public override bool Evaluate() {
            if (_collection == null || string.IsNullOrEmpty(_flagName)) return false;
            return _collection.GetFlag(_flagName);
        }
    }
}
