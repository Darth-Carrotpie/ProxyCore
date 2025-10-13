using System.Collections.Generic;
using UnityEditor;

#if UNITY_EDITOR
namespace ScriptableWithID
{
    public class IDGenerator
    {
        public static int GenerateID()
        {
            HashSet<int> ids = GetActiveIDList();
            int newId;
            do newId = MD5HashGenerator.GenerateIntHash(GUID.Generate().ToString());
            while (IsIDOccupied(newId, ids));
            return newId;
        }

        private static bool IsIDOccupied(int newID, HashSet<int> ids)
        {
            foreach (int id in ids)
            {
                if (id == newID)
                {
                    return true;
                }
            }

            return false;
        }

        private static HashSet<int> GetActiveIDList()
        {
            HashSet<int> ids = new();
            string[] guids = AssetDatabase.FindAssets("t:" + typeof(ScriptableObjectWithID));
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                ScriptableObjectWithID so = AssetDatabase.LoadAssetAtPath<ScriptableObjectWithID>(path);
                ids.Add(so.ID);
            }

            return ids;
        }
    }
}
#endif
