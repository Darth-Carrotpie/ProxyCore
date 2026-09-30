using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace ProxyCore.Editor.Graph {
    /// <summary>
    /// Registry for definition-edge strategies used by the unlock dependency graph.
    /// </summary>
    [InitializeOnLoad]
    public static class DefinitionEdgeStrategyRegistry {
        private static readonly List<IDefinitionEdgeStrategy> _strategies = new();
        private static readonly IDefinitionEdgeStrategy _fallback = new DefaultDefinitionEdgeStrategy();

        static DefinitionEdgeStrategyRegistry() {
            Register(_fallback);
        }

        public static void Register(IDefinitionEdgeStrategy strategy) {
            if (strategy == null) return;
            if (_strategies.Any(s => s.GetType() == strategy.GetType())) return;
            _strategies.Add(strategy);
        }

        public static string GetStrategyId(IDefinitionEdgeStrategy strategy) {
            return strategy?.GetType().FullName ?? string.Empty;
        }

        public static bool TryGetStrategyById(string strategyId, out IDefinitionEdgeStrategy strategy) {
            strategy = null;
            if (string.IsNullOrWhiteSpace(strategyId)) return false;

            foreach (var candidate in _strategies) {
                if (GetStrategyId(candidate) == strategyId) {
                    strategy = candidate;
                    return true;
                }
            }

            return false;
        }

        public static List<IDefinitionEdgeStrategy> GetStrategiesForSourceType(Type sourceType) {
            var matches = new List<IDefinitionEdgeStrategy>();
            var seen = new HashSet<string>();

            for (int i = _strategies.Count - 1; i >= 0; i--) {
                var strategy = _strategies[i];
                bool canHandle = false;

                try {
                    canHandle = strategy.CanHandle(sourceType);
                }
                catch {
                    canHandle = false;
                }

                if (!canHandle) continue;

                var id = GetStrategyId(strategy);
                if (seen.Add(id))
                    matches.Add(strategy);
            }

            if (matches.Count == 0)
                matches.Add(_fallback);

            return matches;
        }

        public static IDefinitionEdgeStrategy GetStrategy(Type sourceType) {
            for (int i = _strategies.Count - 1; i >= 0; i--) {
                var strategy = _strategies[i];
                try {
                    if (strategy.CanHandle(sourceType))
                        return strategy;
                }
                catch {
                    // Ignore a bad strategy and continue with remaining candidates.
                }
            }

            return _fallback;
        }

        public static bool TryGetOwningStrategy(UnlockCondition condition, out IDefinitionEdgeStrategy owningStrategy) {
            owningStrategy = null;
            if (condition == null) return false;

            for (int i = _strategies.Count - 1; i >= 0; i--) {
                try {
                    if (_strategies[i].OwnsCondition(condition)) {
                        owningStrategy = _strategies[i];
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        public static bool TryGetDirectEdgeSource(UnlockCondition condition, out BaseDefinition source) {
            source = null;
            if (condition == null) return false;

            for (int i = _strategies.Count - 1; i >= 0; i--) {
                var strategy = _strategies[i];
                BaseDefinition candidate = null;

                try {
                    candidate = strategy.GetDirectEdgeSource(condition);
                }
                catch {
                    // Ignore a bad strategy and continue with remaining candidates.
                }

                if (candidate != null) {
                    source = candidate;
                    return true;
                }
            }

            return false;
        }
    }
}
