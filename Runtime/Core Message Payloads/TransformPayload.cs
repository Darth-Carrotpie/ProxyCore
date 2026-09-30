using System;
using UnityEngine;

namespace ProxyCore
{
    /// <summary>
    /// Payload containing a Transform reference.
    /// </summary>
    [Serializable]
    public class TransformPayload : EventMessagePayload<Transform>
    {
        public TransformPayload() { }

        public TransformPayload(Transform value)
        {
            SetValue(value);
        }

        public TransformPayload With(Transform value)
        {
            SetValue(value);
            return this;
        }

        public override string ToDebugString()
        {
            if (!_isSet)
                return $"({GetType().Name}: not set)";

            var val = _value;
            string name = val != null ? val.name : "null";
            return $"(Transform) = {name}";
        }
    }
}
