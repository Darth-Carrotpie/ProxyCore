using System;
using System.Collections.Generic;

namespace ProxyCore
{
    /// <summary>
    /// Provenance for one currently-unlocked key: when it entered the unlocked set, and
    /// whether the game has shown it to the player yet.
    ///
    /// One record exists per key in UnlockManager's unlocked sets and dies with the unlock —
    /// locking a key drops its record, so a later unlock mints a fresh one and the key reads
    /// as new again. Records are never kept for locked keys, so the file cannot grow with
    /// repeated locking.
    /// </summary>
    [Serializable]
    public class UnlockRecordData
    {
        public string key;

        /// <summary>
        /// Position in this profile's unlock order. Minted from a counter that only ever
        /// increases and never reuses a value, so two unlocks in the same frame still compare
        /// unambiguously. Zero is reserved for keys migrated from a file written before
        /// provenance existed — see UnlockSaveData.version.
        /// </summary>
        public int ordinal;

        /// <summary>
        /// Unix seconds (UTC) at the moment of the unlock; 0 when unknown. Debug and display
        /// metadata only — ProxyCore never sorts, filters, or branches on this. Ordering is
        /// always by <see cref="ordinal"/>, which no clock change can perturb.
        /// </summary>
        public long unlockedAtUtc;

        /// <summary>
        /// True once the game has told the player about this unlock. Independent of unlock
        /// state and set by the host through UnlockManager.SetAcknowledgedByKey.
        /// </summary>
        public bool acknowledged;
    }

    /// <summary>
    /// Serializable container for unlock state that persists across game sessions.
    /// Written to and read from Application.persistentDataPath/unlocks.json by UnlockManager,
    /// or proxycore/{profileId}/unlocks.json when a save profile is active (see SaveProfile).
    /// </summary>
    [Serializable]
    public class UnlockSaveData
    {
        /// <summary>
        /// Schema version. Files written before provenance existed carry no version field and
        /// load as 0, which is the signal to synthesise records for every saved key
        /// (see UnlockManager.Load). Current writes set <see cref="CURRENT_VERSION"/>.
        /// </summary>
        public int version;

        /// <summary>Version stamped on every file this build writes.</summary>
        public const int CURRENT_VERSION = 1;

        public List<string> savedUnlockedKeys = new List<string>();

        /// <summary>
        /// Keys explicitly locked via UnlockManager.Lock(). Overrides IsUnlockedByDefault.
        /// Files written before this field existed simply load an empty list.
        /// </summary>
        public List<string> lockedOverrideKeys = new List<string>();

        /// <summary>
        /// Provenance for the keys in <see cref="savedUnlockedKeys"/>. Session-only unlocks are
        /// never written here.
        ///
        /// ponytail: savedUnlockedKeys stays the authority on "is unlocked" and these records
        /// annotate it, rather than the records replacing the key list. The ~20 bytes/key of
        /// duplication buys a migration that cannot lose state and keeps every existing reader
        /// of this file working.
        /// </summary>
        public List<UnlockRecordData> unlockRecords = new List<UnlockRecordData>();

        /// <summary>
        /// Ordinal the next unlock will take. Persisted rather than derived so that locking the
        /// most recent unlock cannot make the next one reuse its ordinal, which would hide it
        /// from a marker a caller was already holding. Reconciled upward on load against the
        /// records actually present, so a hand-edited counter self-heals.
        /// </summary>
        public int nextOrdinal = 1;
    }
}
