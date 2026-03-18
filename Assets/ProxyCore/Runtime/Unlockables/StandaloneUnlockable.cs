namespace ProxyCore
{
    /// <summary>
    /// A plain C# implementation of IUnlockable for cases where the unlockable item
    /// is not a ScriptableObject definition — for example, runtime-generated inventory
    /// items, gameplay abilities, or any concept that does not live in a registry.
    ///
    /// Usage:
    ///   var myItem = new StandaloneUnlockable("Ability:DoubleJump", false, UnlockBehavior.HideWhenLocked);
    ///   UnlockManager.Instance.Unlock(myItem);
    ///   bool available = UnlockManager.Instance.IsUnlocked(myItem);
    /// </summary>
    public class StandaloneUnlockable : IUnlockable
    {
        public string UnlockKey { get; }
        public bool SavesAcrossSessions { get; }
        public UnlockBehavior LockedBehavior { get; }

        public StandaloneUnlockable(string key, bool savesAcrossSessions, UnlockBehavior lockedBehavior)
        {
            UnlockKey = key;
            SavesAcrossSessions = savesAcrossSessions;
            LockedBehavior = lockedBehavior;
        }
    }
}
