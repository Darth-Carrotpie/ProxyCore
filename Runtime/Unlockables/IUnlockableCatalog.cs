using System.Collections.Generic;

namespace ProxyCore
{
    /// <summary>
    /// Implemented by any container that can supply definitions to the UnlockManager
    /// for automatic prerequisite evaluation.
    ///
    /// BaseRegistry&lt;T&gt; implements this interface so that any registry can be registered
    /// on the UnlockManager without needing to know the concrete definition type.
    /// </summary>
    public interface IUnlockableCatalog
    {
        /// <summary>
        /// Returns all definitions held by this catalog as base-typed items.
        /// The UnlockManager filters for IUnlockable + IHasPrerequisites at evaluation time.
        /// </summary>
        IReadOnlyList<BaseDefinition> GetCatalogDefinitions();
    }
}
