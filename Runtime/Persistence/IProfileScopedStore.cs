namespace ProxyCore {
    /// <summary>
    /// Implemented by ProxyCore-owned stores whose state must follow the active save profile.
    ///
    /// Register with <see cref="SaveProfile.Register"/> when the store becomes active and
    /// unregister when it goes away — <see cref="SaveProfile"/> then drives the two callbacks
    /// below whenever the profile changes or the game asks for an explicit save/reload.
    ///
    /// This exists so ProxyCore can partition its own state. It is not a general save-system
    /// hook for game data: the host game owns save identity, metadata, and lifecycle.
    /// </summary>
    public interface IProfileScopedStore {
        /// <summary>
        /// Flush pending state to disk under the profile root that is still current.
        /// Called before the active profile switches, and by <see cref="SaveProfile.Save"/>.
        /// Must write even when <see cref="SaveProfile.AutoSave"/> is disabled — this is the
        /// explicit save path. A store with nothing to persist may do nothing.
        /// </summary>
        void OnProfileChanging();

        /// <summary>
        /// Discard in-memory state and reload from <paramref name="profileRoot"/>.
        /// Called after the active profile switches, and by <see cref="SaveProfile.Reload"/>.
        /// Session-only state should be cleared here too, so it never leaks between saves.
        /// </summary>
        /// <param name="profileRoot">Directory holding the new profile's ProxyCore files.</param>
        void OnProfileChanged(string profileRoot);
    }
}
