namespace ProxyCore
{
    /// <summary>
    /// Controls how a locked IUnlockable item is presented in UI.
    /// </summary>
    public enum UnlockBehavior
    {
        /// <summary>
        /// The item is hidden entirely while it is locked.
        /// Use for content that should not be known to exist until unlocked (e.g., secret characters).
        /// </summary>
        HideWhenLocked,

        /// <summary>
        /// The item is visible while locked but cannot be interacted with or activated.
        /// Use for content that acts as a goal or teaser (e.g., quests the player can see but not start).
        /// </summary>
        ShowDisabledWhenLocked,
    }
}
