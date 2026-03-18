namespace ProxyCore
{
    /// <summary>
    /// Represents an item whose availability can be locked or unlocked at runtime.
    /// Implement this interface directly on any class — ScriptableObject definitions,
    /// MonoBehaviours, or plain C# objects — and pass them to UnlockManager.
    /// </summary>
    public interface IUnlockable
    {
        /// <summary>
        /// Unique string key used to track this item's lock state.
        /// Recommended format: "{TypeName}:{ID}" to avoid cross-type collisions in definition registries.
        /// </summary>
        string UnlockKey { get; }

        /// <summary>
        /// When true, this item's unlocked state is written to disk and survives across game sessions.
        /// When false, unlock state is session-only and resets between sessions.
        /// Note: this is distinct from SingletonSO._persistent, which controls runtime state across scene reloads.
        /// </summary>
        bool SavesAcrossSessions { get; }

        /// <summary>
        /// Defines how this item is presented in UI while it is locked.
        /// </summary>
        UnlockBehavior LockedBehavior { get; }
    }
}
