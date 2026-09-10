using UnityEngine;
using UnityEditor;
using System.IO;
using System.Linq;

public static class ScriptableObjectUtility
{
    /// <summary>
    //  This makes it easy to create, name and place unique new ScriptableObject asset files.
    /// </summary>
    public static T CreateAsset<T>(string assetName) where T : ScriptableObject
    {
        T asset = ScriptableObject.CreateInstance<T>();

        string path = AssetDatabase.GetAssetPath(Selection.activeObject);
        if (path == "")
        {
            path = "Assets";
        }
        else if (Path.GetExtension(path) != "")
        {
            path = path.Replace(Path.GetFileName(AssetDatabase.GetAssetPath(Selection.activeObject)), "");
        }

        string assetPathAndName = AssetDatabase.GenerateUniqueAssetPath("Assets/Resources/ScriptableObjects/ " + assetName + "_" + typeof(T).ToString().Split('.').LastOrDefault() + ".asset");
        Debug.Log(assetPathAndName);

        AssetDatabase.CreateAsset(asset, assetPathAndName);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        EditorUtility.FocusProjectWindow();
        Selection.activeObject = asset;

        return asset;
    }

    public static void OnDestroy()
    {

        Debug.Log("SOU on Destory");
        AssetDatabase.SaveAssets();

    }

    public static void OnApplicationQuit()
    {
        Debug.Log("SOU on Quit");
        AssetDatabase.SaveAssets();
    }
}