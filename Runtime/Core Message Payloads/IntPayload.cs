using System;
using UnityEngine;

namespace ProxyCore
{
    /// <summary>
    /// Payload containing an integer value.
    /// Use this for bool-like values as well (0 or 1).
    /// </summary>
    [Serializable]
    public class IntPayload : EventMessagePayload<int>
    {
        public IntPayload() { }

        public IntPayload(int value)
        {
            SetValue(value);
        }

        public IntPayload With(int value)
        {
            SetValue(value);
            return this;
        }
    }
}
