using System.IO;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    public static class UnlockablesActions
    {
        private static string SavePath =>
            Path.Combine(Application.persistentDataPath, "unlocks.json");

        [MenuItem("ProxyCore/Unlockables/Clear Save Data")]
        public static void ClearSaveData()
        {
            if (Application.isPlaying)
            {
                UnlockManager.Instance.ResetSavedUnlocks();
                Debug.Log("ProxyCore: Saved unlock data cleared at runtime.");
            }
            else
            {
                if (File.Exists(SavePath))
                {
                    File.Delete(SavePath);
                    Debug.Log($"ProxyCore: Deleted unlock save file at {SavePath}");
                }
                else
                {
                    Debug.Log("ProxyCore: No unlock save file found to delete.");
                }
            }
        }

        [MenuItem("ProxyCore/Unlockables/Reset Session Unlocks")]
        public static void ResetSessionUnlocks()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning("ProxyCore: Reset Session Unlocks is only available in Play Mode.");
                return;
            }

            UnlockManager.Instance.ResetSessionUnlocks();
            Debug.Log("ProxyCore: Session unlock state cleared.");
        }

        [MenuItem("ProxyCore/Unlockables/Reset Session Unlocks", validate = true)]
        static bool ValidateResetSession() => Application.isPlaying;
    }
}
