using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor {
    public static class UnlockablesActions {
        [MenuItem("ProxyCore/Unlockable Actions/Clear Save Data")]
        public static void ClearSaveData() {
            // Route through the manager in both modes so the active save profile's file is
            // the one deleted. SingletonSO.Instance resolves via AssetDatabase in Edit Mode.
            if (UnlockManager.Instance == null) {
                Debug.LogWarning("ProxyCore: No UnlockManager asset found. Create one via Managers/Unlock Manager.");
                return;
            }

            UnlockManager.ResetSavedUnlocks();
            string profile = string.IsNullOrEmpty(SaveProfile.Active)
                ? "default" : SaveProfile.Active;
            Debug.Log($"ProxyCore: Saved unlock data cleared (profile: {profile}).");
        }

        [MenuItem("ProxyCore/Unlockable Actions/Reset Session Unlocks")]
        public static void ResetSessionUnlocks() {
            if (!Application.isPlaying) {
                Debug.LogWarning("ProxyCore: Reset Session Unlocks is only available in Play Mode.");
                return;
            }

            UnlockManager.ResetSessionUnlocks();
            Debug.Log("ProxyCore: Session unlock state cleared.");
        }

        [MenuItem("ProxyCore/Unlockable Actions/Reset Session Unlocks", validate = true)]
        static bool ValidateResetSession() => Application.isPlaying;

        [MenuItem("ProxyCore/Unlockable Actions/Refresh Unlock Registries")]
        public static void RefreshUnlockRegistries() {
            var manager = UnlockManager.Instance;
            if (manager == null) {
                Debug.LogWarning("ProxyCore: No UnlockManager asset found. Create one via Managers/Unlock Manager.");
                return;
            }
            UnlockManagerEditor.RefreshRegistries(manager);
        }
    }
}
