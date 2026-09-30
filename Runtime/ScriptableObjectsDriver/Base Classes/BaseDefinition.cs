using System;
namespace ProxyCore {
    public abstract class BaseDefinition : ScriptableObjectWithID, IComponentTypeLookup {
        /// <summary>
        /// Virtual method for getting component type associated with this definition.
        /// Override in derived classes to provide specific component type lookup.
        /// </summary>
        /// <returns>The component type, or null if no type is associated.</returns>
        public virtual Type GetComponentType() {
            return null;
        }

#if UNITY_EDITOR
        private void OnEnable() {
            // Generate a new ID if this is a new asset
            if (!IsValidID()) {
                SetIndex(IDGenerator.GenerateID());
            }
        }
#endif
    }
}
