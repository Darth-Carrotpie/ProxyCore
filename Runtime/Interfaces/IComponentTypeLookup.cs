using System;

namespace ProxyCore {
    /// <summary>
    /// Interface for definitions that can provide component type lookup functionality.
    /// This allows different definition types to participate in registry type lookups
    /// without forcing a specific implementation.
    /// </summary>
    public interface IComponentTypeLookup {
        /// <summary>
        /// Gets the component type associated with this definition for registry lookup.
        /// </summary>
        /// <returns>The component type, or null if no type is associated.</returns>
        Type GetComponentType();
    }
}
