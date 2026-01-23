using System;

namespace ProxyCore
{
    /// <summary>
    /// Builder for listening to events.
    /// Returns an IDisposable subscription handle for easy cleanup.
    /// </summary>
    public class EventListenBuilder
    {
        private readonly EventMessage _eventMessage;

        public EventListenBuilder(EventMessage eventMessage)
        {
            _eventMessage = eventMessage;
        }

        /// <summary>
        /// Registers a listener for this event.
        /// Returns an IDisposable that removes the listener when disposed.
        /// </summary>
        public IDisposable Do(Action<EventMessageData> callback)
        {
            EventCoordinatorNew.StartListening(_eventMessage, callback);
            return new EventSubscription(_eventMessage, callback);
        }
    }

    /// <summary>
    /// Represents an active event subscription.
    /// Disposing removes the listener from the event.
    /// </summary>
    public class EventSubscription : IDisposable
    {
        private readonly EventMessage _eventMessage;
        private readonly Action<EventMessageData> _callback;
        private bool _disposed;

        public EventSubscription(EventMessage eventMessage, Action<EventMessageData> callback)
        {
            _eventMessage = eventMessage;
            _callback = callback;
            _disposed = false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            EventCoordinatorNew.StopListening(_eventMessage, _callback);
        }
    }
}
