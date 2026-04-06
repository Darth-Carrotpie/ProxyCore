using UnityEngine;
using ProxyCore;
using System.Collections.Generic;

[CreateAssetMenu(fileName = "CharacterDefinition", menuName = "Definitions/Character Definition", order = 2)]
public class CharacterDefinition : BaseDefinition, IUnlockable, IHasPrerequisites {
    public string fullName;
    public string shortName;

    [Tooltip("Custom greeting instruction appended to the prompt for this character's first message. Use this for non-merchant NPCs to avoid the default greeting that mentions items for sale. Leave empty to use the default merchant greeting.")]
    [TextArea(2, 5)]
    public string initialGreeting;

    [Header("Quests")]
    [Tooltip("Quests associated with this character. Assigned by the designer.")]
    public List<QuestDefinition> quests;

    [Header("Backstories")]
    [Tooltip("Backstories associated with this character. Assigned by the designer.")]
    [SerializeField]
    public List<CharacterBackStory> backStories;

    [Header("Unlock Settings")]
    [Tooltip("Characters are hidden while locked by default.")]
    [SerializeField] private UnlockBehavior _lockedBehavior = UnlockBehavior.HideWhenLocked;

    [Tooltip("When true, unlocked state survives across game sessions. Characters are persistent by default.")]
    [SerializeField] private bool _savesAcrossSessions = true;

    [Tooltip("When true, this character is available from the start without an explicit Unlock() call.")]
    [SerializeField] private bool _isUnlockedByDefault = false;

    [Header("Prerequisites")]
    [Tooltip("Conditions that must be satisfied for this character to auto-unlock. Evaluated with the mode below.")]
    [SerializeField] private List<UnlockCondition> _prerequisites = new List<UnlockCondition>();
    [Tooltip("All: every condition must pass. Any: at least one condition must pass.")]
    [SerializeField] private ConditionMode _prerequisiteMode = ConditionMode.All;
    [Tooltip("When true, UnlockManager will automatically unlock this character when all prerequisites are met.")]
    [SerializeField] private bool _autoUnlock = true;

    string IUnlockable.UnlockKey => $"{GetType().Name}:{ID}";
    bool IUnlockable.SavesAcrossSessions => _savesAcrossSessions;
    UnlockBehavior IUnlockable.LockedBehavior => _lockedBehavior;
    bool IUnlockable.IsUnlockedByDefault => _isUnlockedByDefault;
    IReadOnlyList<UnlockCondition> IHasPrerequisites.Prerequisites => _prerequisites.AsReadOnly();
    ConditionMode IHasPrerequisites.PrerequisiteMode => _prerequisiteMode;
    bool IHasPrerequisites.AutoUnlock => _autoUnlock;
}
