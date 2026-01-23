using JetBrains.Annotations;
using UnityEditor;
using UnityEngine;
namespace ProxyCore {
    public abstract class SingletonSO<T> : SingletonSO where T : ScriptableObject {
        #region  Fields
        [CanBeNull]
        private static T _instance;

        [NotNull]
        private static readonly object Lock = new object();
        #endregion

        #region  Properties
        [NotNull]
        public static T Instance {
            get {
                if (Quitting) {
                    return null;
                }
                lock (Lock) {
                    if (_instance != null)
                        return _instance;

                    // Try to load from Resources folder first
                    _instance = Resources.Load<T>(typeof(T).Name);
                    //Debug.Log("instance " + _instance);

                    // If not found by exact type name, try to find derived types in Resources
                    if (_instance == null) {
                        T[] allResourcesOfType = Resources.LoadAll<T>("");
                        if (allResourcesOfType.Length > 0) {
                            _instance = allResourcesOfType[0];
                            //Debug.Log($"Found in Resources by type search: {_instance.GetType().Name}");
                        }
                    }

                    // If not found in Resources, try to find in project assets
                    if (_instance == null) {
#if UNITY_EDITOR
                        //Debug.Log("instance is null, trying to load");
                        string[] guids = AssetDatabase.FindAssets($"t:{typeof(T).Name}");

                        // If exact type not found, search for derived types
                        if (guids.Length == 0) {
                            //Debug.Log($"Exact type {typeof(T).Name} not found, searching for derived types");

                            // Get all ScriptableObject assets and check for type compatibility
                            string[] allGuids = AssetDatabase.FindAssets("t:ScriptableObject");
                            foreach (string guid in allGuids) {
                                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                                var asset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(assetPath);

                                if (asset != null && typeof(T).IsAssignableFrom(asset.GetType())) {
                                    _instance = asset as T;
                                    if (_instance != null) {
                                        //Debug.Log($"Found compatible type: {asset.GetType().Name} for requested type: {typeof(T).Name}");
                                        break;
                                    }
                                }
                            }
                        }
                        else {
                            string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
                            _instance = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                            //Debug.Log("Loaded: " + _instance);
                        }
#endif
                    }

                    // If still not found, create a new instance
                    if (_instance == null) {
                        Debug.LogError("Failed to find instance of " + typeof(T).Name + ". Make sure a ScriptableObject of this type exists in the Resources folder or project assets.");
                    }
                    return _instance;
                }
            }
        }
        #endregion

        #region  Methods
        protected virtual void OnEnable() {
            OnAwake();
        }

        protected virtual void OnAwake() { }
        protected virtual void OnInit() { }
        #endregion
    }
    public abstract class SingletonSO : ScriptableObject {
        #region  Properties
        public static bool Quitting { get; set; }
        #endregion

        #region  Methods
        protected virtual void OnDisable() {
            // Set quitting flag when the application is shutting down
            if (Application.isPlaying) {
                Quitting = true;
            }
        }

        #endregion
    }
}
