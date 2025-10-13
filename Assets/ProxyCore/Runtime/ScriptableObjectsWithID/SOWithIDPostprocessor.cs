using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace ScriptableWithID
{
    public class SOWithIDPostprocessor : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets,
            string[] movedAssets, string[] movedFromAssetPaths)
        {
            foreach (string assetPath in importedAssets)
            {
                ScriptableObject obj = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);
                if (obj is not ScriptableObjectWithID config) return;
                if (config.IsValidID()) return;

                config.SetIndex(IDGenerator.GenerateID());

                EditorUtility.SetDirty(config);
                AssetDatabase.SaveAssets();
            }
        }
    }
}
#endif

