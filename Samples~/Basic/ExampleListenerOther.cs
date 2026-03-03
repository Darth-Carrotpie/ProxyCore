using System;
using System.Collections;
using System.Collections.Generic;
using ProxyCore;
// Note: Once EventMessage assets are created and code is generated, uncomment:
// using ProxyCore.Generated;
using UnityEngine;

/// <summary>
/// Example showing multiple event subscriptions in one MonoBehaviour.
/// Demonstrates listening to multiple events and proper cleanup.
/// </summary>
public class ExampleListenerOther : MonoBehaviour
{

    // References to EventMessage assets (drag in Inspector)
    [SerializeField] private EventMessage errorEvent;
    [SerializeField] private EventMessage resourceEvent;

    // Subscription handles for cleanup
    private IDisposable _errorSubscription;
    private IDisposable _resourceSubscription;

    void OnEnable()
    {
        if (errorEvent != null)
        {
            _errorSubscription = new EventListenBuilder(errorEvent).Do(OnErrorShow);
        }

        if (resourceEvent != null)
        {
            // Can listen to the same event with any number of Listeners independently
            _resourceSubscription = new EventListenBuilder(resourceEvent).Do(OnAddResource);
        }
    }

    void OnDisable()
    {
        _errorSubscription?.Dispose();
        _resourceSubscription?.Dispose();
    }

    void OnAddResource(EventMessageData data)
    {
        // Access custom payloads
        var intPayload = data.TryGet<IntPayload>();
        if (intPayload != null)
        {
            Debug.Log($"Resource amount: {intPayload.value}");
        }
    }

    void OnErrorShow(EventMessageData data)
    {
        var stringPayload = data.TryGet<StringPayload>();
        if (stringPayload != null)
        {
            Debug.LogError($"Error: {stringPayload.value}");
        }
    }
}
