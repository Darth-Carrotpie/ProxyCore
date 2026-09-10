using JetBrains.Annotations;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;
namespace ProxyCore {
#if UNITY_EDITOR
    [InitializeOnLoad]
#endif
    public abstract class SingletonSO<T> : SingletonSO where T : ScriptableObject {
        #region  Fields
        [CanBeNull]
        private static T _instance;

        [NotNull]
        private static readonly object Lock = new object();
        #endregion

#if UNITY_EDITOR
        static SingletonSO() {
            EditorApplication.playModeStateChanged += state => {
                if (state == PlayModeStateChange.EnteredEditMode)
                    _instance = null;
            };
        }
#endif

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

                        // If exact type not found, search for concrete derived types via TypeCache
                        if (guids.Length == 0) {
                            var derivedTypes = TypeCache.GetTypesDerivedFrom<T>();
                            foreach (var type in derivedTypes) {
                                if (type.IsAbstract) continue;
                                string[] derivedGuids = AssetDatabase.FindAssets($"t:{type.Name}");
                                if (derivedGuids.Length > 0) {
                                    string derivedPath = AssetDatabase.GUIDToAssetPath(derivedGuids[0]);
                                    _instance = AssetDatabase.LoadAssetAtPath<T>(derivedPath);
                                    if (_instance != null) break;
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
        protected virtual void OnInit() { }
        #endregion
    }
    public abstract class SingletonSO : ScriptableObject {
        #region  Fields
        /// <summary>
        /// Controls runtime state lifecycle across scene reloads.
        /// When true (default): runtime state (subscriptions, listeners) persists across scene reloads.
        /// When false: OnSceneReload() is called to clear runtime state, forcing fresh initialization.
        /// Note: ScriptableObject assets themselves always persist; this only affects runtime dictionaries/subscriptions.
        /// </summary>
        [SerializeField]
        protected bool _persistent = true;
        #endregion

        #region  Properties
        public static bool Quitting { get; set; }
        #endregion

        #region  Methods
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() {
            Quitting = false;
        }

#if UNITY_EDITOR
        [InitializeOnLoadMethod]
        private static void ResetQuittingOnDomainReload() {
            Quitting = false;
        }
#endif

        protected virtual void OnEnable() {
            Application.quitting += OnApplicationQuitting;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
            OnAwake();
        }

        protected virtual void OnDisable() {
            Application.quitting -= OnApplicationQuitting;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnApplicationQuitting() {
            Quitting = true;
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene oldScene, UnityEngine.SceneManagement.Scene newScene) {
            // Skip if old scene wasn't valid (initial Play Mode entry from editor)
            // Only clear on legitimate scene-to-scene transitions
            if (!_persistent && oldScene.IsValid() && oldScene.isLoaded) {
                OnSceneReload();
            }
        }

        protected virtual void OnAwake() { }

        /// <summary>
        /// Called when transitioning between scenes and _persistent is false.
        /// This fires BEFORE the new scene's objects are enabled, ensuring subscriptions are cleared before new registrations.
        /// Override to clear runtime state (subscriptions, listeners, cached data).
        /// Note: Does NOT fire on initial Play Mode entry, only on legitimate scene-to-scene transitions (SceneManager.LoadScene).
        /// </summary>
        protected virtual void OnSceneReload() { }
        #endregion
    }
}
