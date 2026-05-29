using System;
using UnityEngine;

namespace ProxyCore
{
    /// <summary>
    /// Base class for event message payloads.
    /// Provides common functionality for value storage and retrieval.
    /// Mirrors the pattern from BaseMessage for consistency.
    /// </summary>
    [Serializable]
    public abstract class EventMessagePayloadBase : IEventMessagePayload
    {
        [SerializeField]
        protected bool _isSet;

        public bool IsSet => _isSet;

        public abstract void Reset();
        public abstract object GetValue();
        public abstract Type ValueType { get; }

        public virtual string ToDebugString()
        {
            if (!_isSet)
                return $"({GetType().Name}: not set)";

            var value = GetValue();
            return $"({GetType().Name}) = {value ?? "null"}";
        }

        /// <summary>
        /// Helper method to get a value, throwing if not set.
        /// Mirrors BaseMessage.GetItem pattern.
        /// </summary>
        protected T GetItem<T>(ref T item)
        {
            if (_isSet)
                return item;
            throw new Exception($"No <{typeof(T)}> was set but was requested within {GetType().Name}");
        }

        /// <summary>
        /// Helper method to set a value with fluent return.
        /// Mirrors BaseMessage.WithItem pattern.
        /// </summary>
        protected TPayload WithItem<T, TPayload>(ref T item, T value) where TPayload : EventMessagePayloadBase
        {
            item = value;
            _isSet = true;
            return this as TPayload;
        }
    }

    /// <summary>
    /// Generic base class for single-value payloads.
    /// Simplifies creation of new payload types.
    /// </summary>
    [Serializable]
    public abstract class EventMessagePayload<T> : EventMessagePayloadBase
    {
        [SerializeField]
        protected T _value;

        public T value => GetItem(ref _value);

        public override Type ValueType => typeof(T);

        public override object GetValue() => _isSet ? _value : null;

        public override void Reset()
        {
            _value = default;
            _isSet = false;
        }

        /// <summary>
        /// Sets the value. For use in constructors or fluent chaining.
        /// </summary>
        protected void SetValue(T val)
        {
            _value = val;
            _isSet = true;
        }
    }
}
