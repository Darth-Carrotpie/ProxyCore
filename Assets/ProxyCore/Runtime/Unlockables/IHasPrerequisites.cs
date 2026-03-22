using System.Collections.Generic;

namespace ProxyCore {
    /// <summary>
    /// Opt-in interface for IUnlockable items that expose prerequisite conditions.
    /// Implement alongside IUnlockable on any definition that should support auto-unlock chains.
    /// Prerequisites are evaluated by UnlockAutoTrigger assets registered on the UnlockManager.
    /// </summary>
    public interface IHasPrerequisites {
        /// <summary>
        /// The list of conditions that must be evaluated before this item can auto-unlock.
        /// An empty list means no prerequisites (item is always ready for auto-unlock).
        /// </summary>
        IReadOnlyList<UnlockCondition> Prerequisites { get; }

        /// <summary>
        /// Whether ALL conditions must pass (AND) or ANY single condition is enough (OR).
        /// </summary>
        ConditionMode PrerequisiteMode { get; }
    }
}
