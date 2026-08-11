using System;
using System.Collections;
using System.Collections.Generic;
using ProxyCore;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProxyCore
{
    /// <summary>
    /// Example of event chaining - attaching follow-up events to primary events.
    /// Attached events always trigger after main listeners complete.
    /// Useful for patterns like: MakeSomethingHappen -> ThatSomethingHappened
    /// </summary>
    public class EventChain : MonoBehaviour
    {
        // References to EventMessage assets for chaining
        [SerializeField] private EventMessage primaryEvent;
        [SerializeField] private EventMessage followUpEvent;

        private IDisposable _attachment;

        void OnEnable()
        {
            if (primaryEvent != null)
            {
                // Attach a follow-up handler to the primary event
                EventCoordinator.Attach(primaryEvent, OnPrimaryEventAttachment);
            }
        }

        void OnDisable()
        {
            if (primaryEvent != null)
            {
                EventCoordinator.Detach(primaryEvent, OnPrimaryEventAttachment);
            }
        }

        void OnPrimaryEventAttachment(EventMessageData data)
        {
            // You can modify or add to the data before passing it on
            // In this example, we add an int payload
            data.With(new IntPayload(42));

            // Trigger the follow-up event
            if (followUpEvent != null)
            {
                EventCoordinator.TriggerEventInternal(followUpEvent, data);
            }
        }
    }
}
