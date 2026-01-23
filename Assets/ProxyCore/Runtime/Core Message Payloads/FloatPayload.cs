using System;
using UnityEngine;

namespace ProxyCore
{
    /// <summary>
    /// Payload containing a float value.
    /// </summary>
    [Serializable]
    public class FloatPayload : EventMessagePayload<float>
    {
        public FloatPayload() { }

        public FloatPayload(float value)
        {
            SetValue(value);
        }

        public FloatPayload With(float value)
        {
            SetValue(value);
            return this;
        }
    }
}
