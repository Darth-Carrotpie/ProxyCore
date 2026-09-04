using System;

namespace ProxyCore
{
    /// <summary>
    /// An immutable snapshot of when one key became unlocked and whether the player has been
    /// shown it. Returned by UnlockManager's provenance queries.
    ///
    /// This is the durable, queryable complement to the onUnlocked EventMessage: it answers
    /// "what did the player unlock this match" and "what have they not seen yet" without a
    /// listener having been alive at the moment of the transition — which matters because
    /// auto-unlocks run during UnlockManager.OnAwake and on every save-profile switch, before
    /// any UI exists to hear them.
    /// </summary>
    public readonly struct UnlockRecord
    {
        /// <summary>The unlock key this record describes.</summary>
        public string Key { get; }

        /// <summary>
        /// Position in the active profile's unlock order. Compare against a marker captured
        /// from <see cref="UnlockManager.UnlockMarker"/>; higher means unlocked later.
        /// Zero means the key predates provenance and was migrated on load.
        /// </summary>
        public int Ordinal { get; }

        /// <summary>
        /// Unix seconds (UTC) at the moment of the unlock, or 0 when unknown — either migrated
        /// from an older save or hand-written. Display and debugging only; ProxyCore orders by
        /// <see cref="Ordinal"/> and never by this.
        /// </summary>
        public long UnlockedAtUnixSeconds { get; }

        /// <summary>True once the host game has marked this unlock as shown to the player.</summary>
        public bool Acknowledged { get; }

        /// <summary>
        /// True when the key is unlocked for this session only (its definition declares
        /// SavesAcrossSessions = false). Such records are never written to disk and vanish with
        /// the unlock on scene reload, so acknowledging one does not survive either.
        /// </summary>
        public bool IsSessionOnly { get; }

        public UnlockRecord(string key, int ordinal, long unlockedAtUnixSeconds, bool acknowledged, bool isSessionOnly)
        {
            Key = key;
            Ordinal = ordinal;
            UnlockedAtUnixSeconds = unlockedAtUnixSeconds;
            Acknowledged = acknowledged;
            IsSessionOnly = isSessionOnly;
        }

        /// <summary>True when <see cref="UnlockedAtUnixSeconds"/> holds a real time.</summary>
        public bool HasTimestamp => UnlockedAtUnixSeconds > 0;

        /// <summary>
        /// <see cref="UnlockedAtUnixSeconds"/> as a UTC instant. Meaningless when
        /// <see cref="HasTimestamp"/> is false — it reads as the Unix epoch.
        /// </summary>
        public DateTimeOffset UnlockedAtUtc => DateTimeOffset.FromUnixTimeSeconds(UnlockedAtUnixSeconds);

        public override string ToString() =>
            $"[#{Ordinal}] {Key}{(IsSessionOnly ? " (session)" : "")}{(Acknowledged ? " (seen)" : "")}";
    }
}
