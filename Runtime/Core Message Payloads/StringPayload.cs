using System;
using UnityEngine;

namespace ProxyCore
{
    /// <summary>
    /// Payload containing a string value.
    /// </summary>
    [Serializable]
    public class StringPayload : EventMessagePayload<string>
    {
        public StringPayload() { }

        public StringPayload(string value)
        {
            SetValue(value);
        }

        public StringPayload With(string value)
        {
            SetValue(value);
            return this;
        }
    }
}
