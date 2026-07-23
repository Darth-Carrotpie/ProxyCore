using System;
using System.Collections.Generic;

namespace ProxyCore
{
    /// <summary>
    /// Serializable container for unlock keys that persist across game sessions.
    /// Written to and read from Application.persistentDataPath/unlocks.json by UnlockManager.
    /// </summary>
    [Serializable]
    public class UnlockSaveData
    {
        public List<string> savedUnlockedKeys = new List<string>();
    }
}
