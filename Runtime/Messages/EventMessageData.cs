using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ProxyCore
{
    /// <summary>
    /// Container for event message payloads.
    /// Provides fluent API for adding payloads and typed retrieval.
    /// Uses object pooling to minimize GC allocations.
    /// </summary>
    public class EventMessageData
    {
        private static readonly Stack<EventMessageData> _pool = new Stack<EventMessageData>();
        private static readonly object _poolLock = new object();

        private readonly Dictionary<Type, IEventMessagePayload> _payloads = new Dictionary<Type, IEventMessagePayload>();
        private bool _isReleased;

        /// <summary>
        /// Gets an EventMessageData instance from the pool.
        /// </summary>
        public static EventMessageData Create()
        {
            lock (_poolLock)
            {
                if (_pool.Count > 0)
                {
                    var data = _pool.Pop();
                    data._isReleased = false;
                    return data;
                }
            }
            return new EventMessageData();
        }

        /// <summary>
        /// Returns this instance to the pool for reuse.
        /// Called automatically after event invocation.
        /// </summary>
        public void Release()
        {
            if (_isReleased) return;

            _isReleased = true;
            _payloads.Clear();

            lock (_poolLock)
            {
                _pool.Push(this);
            }
        }

        /// <summary>
        /// Adds a payload to the data container.
        /// Replaces existing payload of the same type if present.
        /// </summary>
        public EventMessageData With(IEventMessagePayload payload)
        {
            if (payload == null)
            {
                Debug.LogWarning("Attempted to add null payload to EventMessageData");
                return this;
            }

            var type = payload.GetType();
            _payloads[type] = payload;
            return this;
        }

        /// <summary>
        /// Gets a payload of the specified type.
        /// Throws if the payload is not present.
        /// </summary>
        public T Get<T>() where T : class, IEventMessagePayload
        {
            if (_payloads.TryGetValue(typeof(T), out var payload))
            {
                return payload as T;
            }
            throw new Exception($"Payload of type {typeof(T).Name} was not set but was requested in EventMessageData");
        }

        /// <summary>
        /// Tries to get a payload of the specified type.
        /// Returns null if not present.
        /// </summary>
        public T TryGet<T>() where T : class, IEventMessagePayload
        {
            if (_payloads.TryGetValue(typeof(T), out var payload))
            {
                return payload as T;
            }
            return null;
        }

        /// <summary>
        /// Checks if a payload of the specified type is present.
        /// </summary>
        public bool Has<T>() where T : class, IEventMessagePayload
        {
            return _payloads.ContainsKey(typeof(T));
        }

        /// <summary>
        /// Gets all payload types currently in this data container.
        /// </summary>
        public IEnumerable<Type> GetPayloadTypes()
        {
            return _payloads.Keys;
        }

        /// <summary>
        /// Gets all payloads currently in this data container.
        /// </summary>
        public IEnumerable<IEventMessagePayload> GetAllPayloads()
        {
            return _payloads.Values;
        }

        /// <summary>
        /// Returns a debug-friendly string representation of all payloads.
        /// </summary>
        public override string ToString()
        {
            if (_payloads.Count == 0)
                return "(empty)";

            var sb = new StringBuilder();
            foreach (var payload in _payloads.Values)
            {
                if (sb.Length > 0) sb.Append("\n");
                sb.Append(payload.ToDebugString());
            }
            return sb.ToString();
        }
    }
}
