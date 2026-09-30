using System;

namespace ProxyCore
{
    /// <summary>
    /// Interface for modular event message payloads.
    /// Each payload is a self-contained data fragment that can be attached to an event.
    /// </summary>
    public interface IEventMessagePayload
    {
        /// <summary>
        /// Whether this payload has been set with a value.
        /// </summary>
        bool IsSet { get; }

        /// <summary>
        /// Resets the payload to its unset state.
        /// </summary>
        void Reset();

        /// <summary>
        /// Gets the underlying value as an object.
        /// </summary>
        object GetValue();

        /// <summary>
        /// Returns a debug-friendly string representation of the payload.
        /// </summary>
        string ToDebugString();

        /// <summary>
        /// Gets the type of value this payload contains.
        /// </summary>
        Type ValueType { get; }
    }
}
