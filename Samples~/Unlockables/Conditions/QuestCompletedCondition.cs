using ProxyCore;
using UnityEngine;

/// <summary>
/// Condition that passes when a specific quest has been explicitly completed
/// (i.e. unlocked by game code, not merely set as unlocked-by-default).
///
/// Use this as a prerequisite when another definition should unlock only after
/// the player finishes a quest. Unlike <see cref="DefinitionUnlockedCondition"/>,
/// this will never evaluate true just because <c>_isUnlockedByDefault</c> is set.
/// </summary>
[CreateAssetMenu(fileName = "QuestCompleted", menuName = "Unlockables/Quest Completed (Condition)")]
public class QuestCompletedCondition : UnlockCondition {
    [SerializeField] private QuestDefinition _quest;

    public QuestDefinition Quest => _quest;

    public override bool Evaluate() {
        if (_quest == null) return false;
        // IsUnlockedByKey checks only the explicit saved/session sets — never IsUnlockedByDefault.
        return UnlockManager.IsUnlockedByKey(((IUnlockable)_quest).UnlockKey);
    }

#if UNITY_EDITOR
    private void OnValidate() {
        if (_quest == null)
            Debug.LogWarning($"[QuestCompletedCondition] '{name}' has no quest assigned.", this);
    }
#endif
}
