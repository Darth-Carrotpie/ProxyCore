using System.Collections.Generic;
using ProxyCore;
using UnityEngine;

/// <summary>
/// Sample driver for the ProxyCore Unlockables system.
///
/// Attach to a GameObject in the UnlockablesSample scene.
/// Wire up the inspector references, then hit Play and use the inspector
/// buttons to exercise every code path. Results appear in the Console and
/// in the read-only Last Result field below each group.
///
/// Covered scenarios:
///   1. Default-unlocked character   — IsUnlocked returns true with no Unlock() call
///   2. Persistent character unlock  — survives session restart (check unlocks.json)
///   3. Explicit Lock() override     — Lock() returns false even when IsUnlockedByDefault = true
///   4. Session-only quest unlock    — cleared on scene reload, not written to disk
///   5. GetVisibleCharacters()       — HideWhenLocked excluded; ShowDisabledWhenLocked included
///   6. GetVisibleQuests()           — same filter for quests
///   7. Bulk UnlockAll characters    — single Save() pass
///   8. StandaloneUnlockable         — no registry, plain C# IUnlockable
///   9. ResetSavedUnlocks()          — wipes disk state
///  10. ResetSessionUnlocks()        — wipes in-memory state
/// </summary>
public class UnlockablesSampleDriver : MonoBehaviour
{
    [Header("Controllers")]
    [SerializeField] private CharacterUnlockController _characterController;
    [SerializeField] private QuestUnlockController _questController;

    [Header("Sample Definitions")]
    [Tooltip("A character with IsUnlockedByDefault = true")]
    [SerializeField] private CharacterDefinition _defaultUnlockedCharacter;

    [Tooltip("A character with IsUnlockedByDefault = false and SavesAcrossSessions = true")]
    [SerializeField] private CharacterDefinition _persistentCharacter;

    [Tooltip("A session-only quest")]
    [SerializeField] private QuestDefinition _sessionQuest;

    // ── Standalone (no registry) ───────────────────────────────────────
    // Created at runtime to demonstrate IUnlockable without ScriptableObjects.
    private readonly StandaloneUnlockable _standaloneItem =
        new StandaloneUnlockable("Standalone:TestAbility", savesAcrossSessions: false,
            UnlockBehavior.HideWhenLocked, isUnlockedByDefault: false);

    // ── Characters ────────────────────────────────────────────────────

    [Header("Characters")]
    [SerializeField, ProxyCore.ReadOnly] private string _characterResult;

    [EditorCools.Button(name: "1. Is default-unlocked character available?")]
    private void Scenario1_DefaultUnlocked()
    {
        bool unlocked = _characterController.IsUnlocked(_defaultUnlockedCharacter);
        _characterResult = $"IsUnlocked = {unlocked}  (expected: True — no Unlock() needed)";
        Debug.Log($"[Unlockables] {_defaultUnlockedCharacter.name}: {_characterResult}");
    }

    [EditorCools.Button(name: "2. Unlock persistent character")]
    private void Scenario2_PersistentUnlock()
    {
        _characterController.Unlock(_persistentCharacter);
        bool unlocked = _characterController.IsUnlocked(_persistentCharacter);
        _characterResult = $"IsUnlocked = {unlocked}. Stop Play and re-enter — should remain True.";
        Debug.Log($"[Unlockables] {_persistentCharacter.name}: {_characterResult}\n" +
                  $"Save file: {System.IO.Path.Combine(Application.persistentDataPath, "unlocks.json")}");
    }

    [EditorCools.Button(name: "3. Lock default-unlocked character (override)")]
    private void Scenario3_LockOverride()
    {
        bool before = _characterController.IsUnlocked(_defaultUnlockedCharacter);
        _characterController.Lock(_defaultUnlockedCharacter);
        bool after = _characterController.IsUnlocked(_defaultUnlockedCharacter);
        _characterResult = $"Before = {before}  →  After = {after}  (expected: True → False)";
        Debug.Log($"[Unlockables] Lock() override: {_characterResult}");
    }

    [EditorCools.Button(name: "5. GetVisibleCharacters()")]
    private void Scenario5_VisibleCharacters()
    {
        IReadOnlyList<CharacterDefinition> visible = _characterController.GetVisibleCharacters();
        var sb = new System.Text.StringBuilder();
        sb.Append($"{visible.Count} visible: ");
        foreach (var c in visible)
        {
            string status = _characterController.IsUnlocked(c) ? "unlocked" : "locked-visible";
            sb.Append($"{c.name}[{status}] ");
        }
        _characterResult = sb.ToString().TrimEnd();
        Debug.Log($"[Unlockables] GetVisibleCharacters — {_characterResult}");
    }

    [EditorCools.Button(name: "7. Bulk UnlockAll characters", space: 4f)]
    private void Scenario7_BulkUnlock()
    {
        UnlockManager.Instance.UnlockAll(_characterController.GetVisibleCharacters());
        int count = _characterController.GetUnlockedCharacters().Count;
        _characterResult = $"Unlocked count: {count}. Single Save() made for all persistent keys.";
        Debug.Log($"[Unlockables] BulkUnlock — {_characterResult}");
    }

    // ── Quests ────────────────────────────────────────────────────────

    [Header("Quests")]
    [SerializeField, ProxyCore.ReadOnly] private string _questResult;

    [EditorCools.Button(name: "4. Unlock session quest (cleared on scene reload)")]
    private void Scenario4_SessionQuest()
    {
        _questController.Unlock(_sessionQuest);
        bool unlocked = _questController.IsUnlocked(_sessionQuest);
        _questResult = $"IsUnlocked = {unlocked}. Reload scene — will be False again.";
        Debug.Log($"[Unlockables] {_sessionQuest.name}: {_questResult}");
    }

    [EditorCools.Button(name: "6. GetVisibleQuests()")]
    private void Scenario6_VisibleQuests()
    {
        IReadOnlyList<QuestDefinition> visible = _questController.GetVisibleQuests();
        var sb = new System.Text.StringBuilder();
        sb.Append($"{visible.Count} visible: ");
        foreach (var q in visible)
        {
            string status = _questController.IsAvailable(q) ? "available" : "locked-visible";
            sb.Append($"{q.name}[{status}] ");
        }
        _questResult = sb.ToString().TrimEnd();
        Debug.Log($"[Unlockables] GetVisibleQuests — {_questResult}");
    }

    // ── Standalone ────────────────────────────────────────────────────

    [Header("Standalone (no registry)")]
    [SerializeField, ProxyCore.ReadOnly] private string _standaloneResult;

    [EditorCools.Button(name: "8. Toggle StandaloneUnlockable")]
    private void Scenario8_Standalone()
    {
        bool before = UnlockManager.Instance.IsUnlocked(_standaloneItem);
        if (before)
            UnlockManager.Instance.Lock(_standaloneItem);
        else
            UnlockManager.Instance.Unlock(_standaloneItem);
        bool after = UnlockManager.Instance.IsUnlocked(_standaloneItem);
        _standaloneResult = $"'{_standaloneItem.UnlockKey}'  {before} → {after}";
        Debug.Log($"[Unlockables] Standalone toggle — {_standaloneResult}");
    }

    // ── Reset ─────────────────────────────────────────────────────────

    [Header("Reset")]
    [SerializeField, ProxyCore.ReadOnly] private string _resetResult;

    [EditorCools.Button(name: "9. ResetSavedUnlocks() — wipes disk state", row: "reset-row")]
    private void Scenario9_ResetSaved()
    {
        UnlockManager.Instance.ResetSavedUnlocks();
        _resetResult = "Saved unlocks cleared. unlocks.json deleted.";
        Debug.Log($"[Unlockables] {_resetResult}");
    }

    [EditorCools.Button(name: "10. ResetSessionUnlocks() — wipes memory", row: "reset-row")]
    private void Scenario10_ResetSession()
    {
        UnlockManager.Instance.ResetSessionUnlocks();
        _resetResult = "Session unlocks and lock overrides cleared.";
        Debug.Log($"[Unlockables] {_resetResult}");
    }
}
