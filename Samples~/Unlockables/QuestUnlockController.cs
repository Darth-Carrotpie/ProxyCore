using System.Collections.Generic;
using ProxyCore;
using UnityEngine;

/// <summary>
/// MonoBehaviour helper for working with QuestDefinition unlock state.
/// Provides filtered views of quests based on their current lock state
/// and LockedBehavior, and forwards mutations to the global UnlockManager.
/// </summary>
public class QuestUnlockController : MonoBehaviour {
    // ------------------------------------------------------------------ //
    //  State Mutations                                                     //
    // ------------------------------------------------------------------ //

    public void Unlock(QuestDefinition quest) =>
        UnlockManager.Unlock(quest);

    public void Lock(QuestDefinition quest) =>
        UnlockManager.Lock(quest);

    // ------------------------------------------------------------------ //
    //  State Queries                                                       //
    // ------------------------------------------------------------------ //

    public bool IsUnlocked(QuestDefinition quest) =>
        UnlockManager.IsUnlocked(quest);

    public bool IsLocked(QuestDefinition quest) =>
        UnlockManager.IsLocked(quest);

    /// <summary>
    /// Returns true when the quest can be interacted with.
    /// Use this to drive UI enable/disable state.
    /// </summary>
    public bool IsAvailable(QuestDefinition quest) =>
        UnlockManager.IsUnlocked(quest);

    /// <summary>
    /// Returns all quests that should be visible in UI.
    /// Locked quests with HideWhenLocked are excluded.
    /// Locked quests with ShowDisabledWhenLocked (the default) are included.
    /// </summary>
    public IReadOnlyList<QuestDefinition> GetVisibleQuests() {
        var result = new List<QuestDefinition>();
        foreach (var quest in QuestRegistry.Instance.GetAllDefinitions()) {
            if (UnlockManager.IsUnlocked(quest))
                result.Add(quest);
            else if (((IUnlockable)quest).LockedBehavior == UnlockBehavior.ShowDisabledWhenLocked)
                result.Add(quest);
        }
        return result;
    }

    /// <summary>
    /// Returns all quests whose unlock state is currently unlocked.
    /// </summary>
    public IReadOnlyList<QuestDefinition> GetUnlockedQuests() {
        var result = new List<QuestDefinition>();
        foreach (var quest in QuestRegistry.Instance.GetAllDefinitions()) {
            if (UnlockManager.IsUnlocked(quest))
                result.Add(quest);
        }
        return result;
    }
}
