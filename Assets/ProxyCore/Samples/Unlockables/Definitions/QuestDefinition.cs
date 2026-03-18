using ProxyCore;
using UnityEngine;

[CreateAssetMenu(fileName = "QuestDefinition", menuName = "Definitions/Quest Definition", order = 2)]
public class QuestDefinition : BaseDefinition, IUnlockable
{
    public string description;
    public string title;

    [Header("Unlock Settings")]
    [Tooltip("Quests are shown but disabled while locked by default.")]
    [SerializeField] private UnlockBehavior _lockedBehavior = UnlockBehavior.ShowDisabledWhenLocked;

    [Tooltip("Quest unlock state is session-only by default and is not saved across sessions.")]
    [SerializeField] private bool _savesAcrossSessions = false;

    [Tooltip("When true, this quest is available from the start without an explicit Unlock() call.")]
    [SerializeField] private bool _isUnlockedByDefault = false;

    string IUnlockable.UnlockKey => $"{GetType().Name}:{ID}";
    bool IUnlockable.SavesAcrossSessions => _savesAcrossSessions;
    UnlockBehavior IUnlockable.LockedBehavior => _lockedBehavior;
    bool IUnlockable.IsUnlockedByDefault => _isUnlockedByDefault;
}
