using System;
using System.Collections;
using System.Collections.Generic;
using ProxyCore;
// Note: Once EventMessage assets are created and code is generated, uncomment:
// using ProxyCore.Generated;
using UnityEngine;

/// <summary>
/// Example showing how to listen to events with the new EventMessage system.
/// Listeners receive EventMessageData containing typed payloads.
/// The IDisposable subscription pattern ensures proper cleanup.
/// </summary>
public class ExampleListener : MonoBehaviour
{

    // Reference to an EventMessage asset (drag in Inspector)
    [SerializeField] private EventMessage winEvent;

    // Subscription handle for proper cleanup
    private IDisposable _winSubscription;

    void OnEnable()
    {
        if (winEvent != null)
        {
            // New way to listen using EventMessage reference:
            _winSubscription = new EventListenBuilder(winEvent).Do(OnWinReceived);

            // Once code is generated, you can also use:
            // _winSubscription = ListenEvent.Examples.Win.Do(OnWinReceived);
        }
    }

    void OnDisable()
    {
        // Always dispose subscriptions to prevent memory leaks
        _winSubscription?.Dispose();
    }

    void OnWinReceived(EventMessageData data)
    {
        // Get typed payloads from the event data
        var transformPayload = data.TryGet<TransformPayload>();
        var stringPayload = data.TryGet<StringPayload>();

        if (transformPayload != null)
        {
            Debug.Log($"Transform: {transformPayload.value.name}");
        }

        if (stringPayload != null)
        {
            // Event starts the Coroutine which does the work:
            StartCoroutine(DoWorkMethod(stringPayload.value));
        }
    }

    IEnumerator DoWorkMethod(string message)
    {
        int framesToWork = 2;
        int step = 0;
        while (step < framesToWork)
        {
            Debug.Log($"Doing work over multiple frames... working on frame: {step}, message: {message}", this);
            step++;
            yield return null;
        }
        Debug.Log("Work complete!");
    }
}
