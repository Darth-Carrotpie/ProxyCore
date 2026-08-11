using System;
using System.Collections.Generic;

namespace ProxyCore
{
    /// <summary>
    /// Serializable container for unlock state that persists across game sessions.
    /// Written to and read from Application.persistentDataPath/unlocks.json by UnlockManager
    /// (or unlocks_{profile}.json when a save profile is active).
    /// </summary>
    [Serializable]
    public class UnlockSaveData
    {
        public List<string> savedUnlockedKeys = new List<string>();

        /// <summary>
        /// Keys explicitly locked via UnlockManager.Lock(). Overrides IsUnlockedByDefault.
        /// Files written before this field existed simply load an empty list.
        /// </summary>
        public List<string> lockedOverrideKeys = new List<string>();
    }
}
