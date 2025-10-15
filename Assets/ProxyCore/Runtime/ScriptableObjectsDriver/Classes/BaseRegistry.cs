using UnityEngine;
using System.Collections.Generic;
using System;

namespace ProxyCore {
    public abstract class BaseRegistry<T> : SingletonSO<BaseRegistry<T>> where T : BaseDefinition {
        [Tooltip("List of all definitions")]
        public List<T> definitions = new List<T>();

        protected Dictionary<string, T> _lookup;
        protected Dictionary<Type, T> _typeLookup;

        protected override void OnEnable() {
            InitializeLookup();
        }

        public virtual void InitializeLookup() {
            _lookup = new Dictionary<string, T>();
            _typeLookup = new Dictionary<Type, T>();

            foreach (var def in definitions) {
                if (def != null && def.IsValidID()) {
                    _lookup[def.ID.ToString()] = def;
                }

                if (def != null) {
                    var componentType = def.GetComponentType();
                    if (componentType != null) {
                        _typeLookup[componentType] = def;
                    }
                }
            }
        }

        public virtual IReadOnlyList<T> GetAllDefinitions() {
            if (_lookup == null)
                InitializeLookup();
            return definitions.AsReadOnly();
        }

        public virtual T GetDefinition(string id) {
            if (_lookup == null)
                InitializeLookup();
            _lookup.TryGetValue(id, out var def);
            return def;
        }

        public virtual T GetDefinition(Type componentType) {
            if (_typeLookup == null)
                InitializeLookup();

            _typeLookup.TryGetValue(componentType, out var def);
            return def;
        }

#if UNITY_EDITOR
        /// <summary>
        /// Override this property in derived classes to specify the asset type name for search.
        /// Example: "ResourceCategoryDefinition"
        /// </summary>
        protected virtual string AssetTypeName => typeof(T).Name;

        public virtual void RefreshDefinitions() {
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{AssetTypeName}");
            definitions.Clear();

            foreach (string guid in guids) {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                T def = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                if (def != null) {
                    definitions.Add(def);
                }
            }

            InitializeLookup();
            UnityEditor.EditorUtility.SetDirty(this);
            UnityEditor.AssetDatabase.SaveAssets();
        }
#endif
    }
}
