using UnityEngine.Events;

namespace ProxyCore
{
    /// <summary>
    /// Unity event that carries EventMessageData payload.
    /// Used internally by the event system for UnityEvent-based listeners.
    /// </summary>
    public class UnityGameEvent : UnityEvent<EventMessageData>
    {

    }
}