using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProxyCore
{
    /// <summary>
    /// Payload containing a list of MonoBehaviour references.
    /// Useful for passing multiple component references in a single payload.
    /// </summary>
    [Serializable]
    public class MonoRefsPayload : EventMessagePayload<List<MonoBehaviour>>
    {
        public MonoRefsPayload() { }

        public MonoRefsPayload(List<MonoBehaviour> value)
        {
            SetValue(value);
        }

        public MonoRefsPayload(params MonoBehaviour[] values)
        {
            SetValue(new List<MonoBehaviour>(values));
        }

        public MonoRefsPayload With(List<MonoBehaviour> value)
        {
            SetValue(value);
            return this;
        }

        public MonoRefsPayload With(params MonoBehaviour[] values)
        {
            SetValue(new List<MonoBehaviour>(values));
            return this;
        }

        /// <summary>
        /// Gets the first MonoBehaviour of the specified type from the list.
        /// Returns null if not found.
        /// </summary>
        public T GetRef<T>() where T : MonoBehaviour
        {
            if (!_isSet || _value == null) return null;

            foreach (var mono in _value)
            {
                if (mono is T typed)
                {
                    return typed;
                }
            }
            return null;
        }

        /// <summary>
        /// Gets all MonoBehaviours of the specified type from the list.
        /// </summary>
        public List<T> GetAllRefs<T>() where T : MonoBehaviour
        {
            var result = new List<T>();
            if (!_isSet || _value == null) return result;

            foreach (var mono in _value)
            {
                if (mono is T typed)
                {
                    result.Add(typed);
                }
            }
            return result;
        }

        public override string ToDebugString()
        {
            if (!_isSet)
                return $"({GetType().Name}: not set)";

            if (_value == null || _value.Count == 0)
                return $"(MonoRefsList) = []";

            var names = new List<string>();
            foreach (var mono in _value)
            {
                names.Add(mono != null ? $"{mono.GetType().Name}({mono.name})" : "null");
            }
            return $"(MonoRefsList) = [{string.Join(", ", names)}]";
        }
    }
}
