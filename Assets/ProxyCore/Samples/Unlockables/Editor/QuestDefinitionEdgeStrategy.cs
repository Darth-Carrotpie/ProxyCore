using System;
using UnityEditor;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Sample downstream strategy for quest definitions.
    ///
    /// This demonstrates project-level strategy registration and a custom
    /// pass-state label. It currently reuses the default direct-edge condition
    /// behavior until a quest-completion runtime condition is introduced.
    /// </summary>
    [InitializeOnLoad]
    internal static class QuestDefinitionEdgeStrategyRegistrar {
        static QuestDefinitionEdgeStrategyRegistrar() {
            DefinitionEdgeStrategyRegistry.Register(new QuestDefinitionEdgeStrategy());
        }
    }

    internal sealed class QuestDefinitionEdgeStrategy : IDefinitionEdgeStrategy {
        private static readonly DefaultDefinitionEdgeStrategy _defaultStrategy = new();

        public bool CanHandle(Type sourceType) {
            return sourceType != null &&
                   (sourceType == typeof(QuestDefinition) ||
                    sourceType.IsSubclassOf(typeof(QuestDefinition)));
        }

        public string PassStateLabel => "Quest Completed";

        public UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder) {
            return _defaultStrategy.GetOrCreateCondition(source, conditionsFolder);
        }

        public BaseDefinition GetDirectEdgeSource(UnlockCondition condition) {
            return _defaultStrategy.GetDirectEdgeSource(condition);
        }
    }
}
