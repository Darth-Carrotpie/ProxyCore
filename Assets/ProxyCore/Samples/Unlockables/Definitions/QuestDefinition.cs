using System.Collections.Generic;
using ProxyCore;
using UnityEngine;

[CreateAssetMenu(fileName = "QuestDefinition", menuName = "Definitions/Quest Definition", order = 2)]
public class QuestDefinition : BaseDefinition, IUnlockable, IHasPrerequisites {
    public string description;
    public string title;

    [Header("Unlock Settings")]
    [Tooltip("Quests are shown but disabled while locked by default.")]
    [SerializeField] private UnlockBehavior _lockedBehavior = UnlockBehavior.ShowDisabledWhenLocked;

    [Tooltip("Quest unlock state is session-only by default and is not saved across sessions.")]
    [SerializeField] private bool _savesAcrossSessions = false;

    [Tooltip("When true, this quest is available from the start without an explicit Unlock() call.")]
    [SerializeField] private bool _isUnlockedByDefault = false;

    [Header("Prerequisites")]
    [Tooltip("Conditions that must be satisfied for this quest to auto-unlock via an UnlockAutoTrigger. Evaluated with the mode below.")]
    [SerializeField] private List<UnlockCondition> _prerequisites = new List<UnlockCondition>();
    [Tooltip("All: every condition must pass. Any: at least one condition must pass.")]
    [SerializeField] private ConditionMode _prerequisiteMode = ConditionMode.All;

    string IUnlockable.UnlockKey => $"{GetType().Name}:{ID}";
    bool IUnlockable.SavesAcrossSessions => _savesAcrossSessions;
    UnlockBehavior IUnlockable.LockedBehavior => _lockedBehavior;
    bool IUnlockable.IsUnlockedByDefault => _isUnlockedByDefault;
    IReadOnlyList<UnlockCondition> IHasPrerequisites.Prerequisites => _prerequisites.AsReadOnly();
    ConditionMode IHasPrerequisites.PrerequisiteMode => _prerequisiteMode;
}
