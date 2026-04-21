using System;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Strategy for definition-to-definition direct edges in the unlock graph.
    /// Implementations define how condition assets are created/resolved per source definition type.
    /// </summary>
    public interface IDefinitionEdgeStrategy {
        bool CanHandle(Type sourceType);
        string PassStateLabel { get; }
        UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder);
        BaseDefinition GetDirectEdgeSource(UnlockCondition condition);
    }
}
