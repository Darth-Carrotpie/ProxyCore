namespace ProxyCore {
    /// <summary>
    /// Controls how multiple prerequisites are combined when evaluating an UnlockAutoTrigger.
    /// </summary>
    public enum ConditionMode {
        /// <summary>Every prerequisite must pass (logical AND).</summary>
        All,

        /// <summary>At least one prerequisite must pass (logical OR).</summary>
        Any,
    }
}
