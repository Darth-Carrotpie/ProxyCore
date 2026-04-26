using System;
using UnityEditor;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Sample downstream strategy for character definitions.
    ///
    /// This adds a second pass behavior option for CharacterDefinition nodes.
    /// It currently reuses default direct-edge condition creation until a
    /// dedicated character-specific runtime condition is introduced.
    /// </summary>
    [InitializeOnLoad]
    internal static class CharacterDefinitionEdgeStrategyRegistrar {
        static CharacterDefinitionEdgeStrategyRegistrar() {
            DefinitionEdgeStrategyRegistry.Register(new CharacterDefinitionEdgeStrategy());
        }
    }

    internal sealed class CharacterDefinitionEdgeStrategy : IDefinitionEdgeStrategy {
        private static readonly DefaultDefinitionEdgeStrategy _defaultStrategy = new();

        public bool CanHandle(Type sourceType) {
            return sourceType != null &&
                   (sourceType == typeof(CharacterDefinition) ||
                    sourceType.IsSubclassOf(typeof(CharacterDefinition)));
        }

        public string PassStateLabel => "Purchased";

        public UnlockCondition GetOrCreateCondition(BaseDefinition source, string conditionsFolder) {
            return _defaultStrategy.GetOrCreateCondition(source, conditionsFolder);
        }

        public BaseDefinition GetDirectEdgeSource(UnlockCondition condition) {
            return _defaultStrategy.GetDirectEdgeSource(condition);
        }
    }
}
