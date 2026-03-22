using System.Collections.Generic;
using ProxyCore;
using UnityEngine;

/// <summary>
/// MonoBehaviour helper for working with CharacterDefinition unlock state.
/// Provides filtered views of characters based on their current lock state
/// and LockedBehavior, and forwards mutations to the global UnlockManager.
/// </summary>
public class CharacterUnlockController : MonoBehaviour
{
    // ------------------------------------------------------------------ //
    //  State Mutations                                                     //
    // ------------------------------------------------------------------ //

    public void Unlock(CharacterDefinition character) =>
        UnlockManager.Instance.Unlock(character);

    public void Lock(CharacterDefinition character) =>
        UnlockManager.Instance.Lock(character);

    // ------------------------------------------------------------------ //
    //  State Queries                                                       //
    // ------------------------------------------------------------------ //

    public bool IsUnlocked(CharacterDefinition character) =>
        UnlockManager.Instance.IsUnlocked(character);

    public bool IsLocked(CharacterDefinition character) =>
        UnlockManager.Instance.IsLocked(character);

    /// <summary>
    /// Returns all characters that should be visible in UI.
    /// Locked characters with HideWhenLocked are excluded.
    /// Locked characters with ShowDisabledWhenLocked are included.
    /// </summary>
    public IReadOnlyList<CharacterDefinition> GetVisibleCharacters()
    {
        var result = new List<CharacterDefinition>();
        foreach (var character in CharacterRegistry.Instance.GetAllDefinitions())
        {
            if (UnlockManager.Instance.IsUnlocked(character))
                result.Add(character);
            else if (((IUnlockable)character).LockedBehavior == UnlockBehavior.ShowDisabledWhenLocked)
                result.Add(character);
        }
        return result;
    }

    /// <summary>
    /// Returns all characters whose unlock state is currently unlocked.
    /// </summary>
    public IReadOnlyList<CharacterDefinition> GetUnlockedCharacters()
    {
        var result = new List<CharacterDefinition>();
        foreach (var character in CharacterRegistry.Instance.GetAllDefinitions())
        {
            if (UnlockManager.Instance.IsUnlocked(character))
                result.Add(character);
        }
        return result;
    }
}
