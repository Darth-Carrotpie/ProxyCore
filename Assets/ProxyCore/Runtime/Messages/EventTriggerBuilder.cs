using System;

namespace ProxyCore
{
    /// <summary>
    /// Builder for triggering events with fluent payload API.
    /// Implements IDisposable for auto-send pattern.
    /// </summary>
    public class EventTriggerBuilder : IDisposable
    {
        private readonly EventMessage _eventMessage;
        private EventMessageData _data;
        private bool _sent;

        public EventTriggerBuilder(EventMessage eventMessage)
        {
            _eventMessage = eventMessage;
            _data = EventMessageData.Create();
            _sent = false;
        }

        /// <summary>
        /// Adds a payload to the event data.
        /// </summary>
        public EventTriggerBuilder With(IEventMessagePayload payload)
        {
            if (_sent)
            {
                throw new InvalidOperationException("Cannot add payloads after event has been sent");
            }
            _data.With(payload);
            return this;
        }

        /// <summary>
        /// Triggers the event with all added payloads.
        /// Auto-releases the EventMessageData after invocation.
        /// </summary>
        public void Send()
        {
            if (_sent) return;
            _sent = true;

            EventCoordinatorNew.TriggerEventInternal(_eventMessage, _data);
            // Data is released by TriggerEventInternal after invocation
            _data = null;
        }

        /// <summary>
        /// Disposes the builder, sending the event if not already sent.
        /// Enables 'using' pattern for exception-safe event triggering.
        /// </summary>
        public void Dispose()
        {
            if (!_sent)
            {
                Send();
            }
        }
    }
}
