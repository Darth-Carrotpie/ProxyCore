using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace ProxyCore.Editor
{
    public static class PropertyDrawersHelper
    {

        public static string[] AllSceneNames()
        {
            var temp = new List<string>();
            foreach (UnityEditor.EditorBuildSettingsScene S in UnityEditor.EditorBuildSettings.scenes)
            {
                if (S.enabled)
                {
                    string name = S.path.Substring(S.path.LastIndexOf('/') + 1);
                    name = name.Substring(0, name.Length - 6);
                    temp.Add(name);
                }
            }
            return temp.ToArray();
        }

        /// <summary>
        /// Gets all EventMessage display names from the EventCoordinatorNew registry.
        /// </summary>
        public static string[] AllEventNames()
        {
            var eventMessages = FindAllEventMessages();
            return eventMessages.Select(e => e.GetFullPath()).ToArray();
        }

        /// <summary>
        /// Finds all EventMessage assets in the project.
        /// </summary>
        private static List<EventMessage> FindAllEventMessages()
        {
            var result = new List<EventMessage>();
            string[] guids = AssetDatabase.FindAssets("t:EventMessage");

            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var evt = AssetDatabase.LoadAssetAtPath<EventMessage>(path);
                if (evt != null)
                {
                    result.Add(evt);
                }
            }

            return result;
        }
    }
}
