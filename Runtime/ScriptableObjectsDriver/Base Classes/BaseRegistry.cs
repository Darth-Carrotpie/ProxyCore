using UnityEngine;
using System.Collections.Generic;
using System;

namespace ProxyCore {
    public abstract class BaseRegistry<T> : SingletonSO<BaseRegistry<T>>, IUnlockableCatalog where T : BaseDefinition {
        [Tooltip("List of all definitions")]
        public List<T> definitions = new List<T>();

        protected Dictionary<int, T> _lookup;
        protected Dictionary<Type, T> _typeLookup;

        protected override void OnEnable() {
            base.OnEnable();
            InitializeLookup();
        }

        public virtual void InitializeLookup() {
            _lookup = new Dictionary<int, T>();
            _typeLookup = new Dictionary<Type, T>();

            foreach (var def in definitions) {
                if (def != null && def.IsValidID()) {
                    _lookup[def.ID] = def;
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

        /// <inheritdoc/>
        /// <remarks>
        /// Override to narrow what the unlock system sees. UnlockManager's auto-unlock pass and
        /// stale-key purge both read this, so returning a subset keeps definitions outside that
        /// subset from auto-unlocking and from filling the save file with keys the current save
        /// will never surface.
        /// </remarks>
        public virtual IReadOnlyList<BaseDefinition> GetCatalogDefinitions() {
            if (_lookup == null)
                InitializeLookup();
            return definitions.ConvertAll(d => (BaseDefinition)d).AsReadOnly();
        }

        public virtual T GetDefinition(int id) {
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

        /// <summary>
        /// Called when scene is reloaded and _persistent is false.
        /// Rebuilds lookup dictionaries from definitions to ensure consistency.
        /// </summary>
        protected override void OnSceneReload() {
            base.OnSceneReload();

            // Rebuild lookups from definitions (fast operation, ensures consistency)
            InitializeLookup();
        }

#if UNITY_EDITOR
        [Header("Editor Settings")]
        [Tooltip("Automatically refresh this registry when definitions are created, deleted, or moved")]
        public bool autoRefresh = true;

        /// <summary>
        /// Override this property in derived classes to specify the asset type name for search.
        /// Example: "ResourceCategoryDefinition"
        /// </summary>
        protected virtual string AssetTypeName => typeof(T).Name;

        public virtual void RefreshDefinitions() {
            string[] guids = UnityEditor.AssetDatabase.FindAssets($"t:{AssetTypeName}");
            var found = new List<T>(guids.Length);

            foreach (string guid in guids) {
                string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                T def = UnityEditor.AssetDatabase.LoadAssetAtPath<T>(path);
                if (def != null) {
                    found.Add(def);
                }
            }

            // Only write when the list actually changed. This runs on every definition
            // import once autoRefresh is on, and an unconditional SetDirty/SaveAssets
            // rewrites every registry asset on every import.
            if (!SameDefinitions(definitions, found)) {
                definitions.Clear();
                definitions.AddRange(found);
                UnityEditor.EditorUtility.SetDirty(this);
                UnityEditor.AssetDatabase.SaveAssets();
            }

            // Rebuild either way — the in-memory dictionaries can be stale (domain reload,
            // a definition whose ID was only just persisted) even when the list is identical.
            InitializeLookup();
        }

        /// <summary>
        /// True when both lists hold the same definitions in the same order. A null hole in
        /// <paramref name="current"/> (a deleted asset) always counts as a difference.
        /// </summary>
        public static bool SameDefinitions(List<T> current, List<T> found) {
            if (current == null || current.Count != found.Count)
                return false;

            for (int i = 0; i < found.Count; i++) {
                if (current[i] != found[i])
                    return false;
            }
            return true;
        }
#endif
    }
}
