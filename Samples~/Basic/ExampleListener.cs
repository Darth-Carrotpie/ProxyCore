using System;
using System.Collections;
using System.Collections.Generic;
using ProxyCore;
using ProxyCore.Generated;
using UnityEngine;

/// <summary>
/// Example showing how to listen to events with the new EventMessage system.
/// Listeners receive EventMessageData containing typed payloads.
/// The IDisposable subscription pattern ensures proper cleanup.
/// </summary>
public class ExampleListener : MonoBehaviour
{
    // Subscription handles for proper cleanup
    private IDisposable _healSubscription;
    private IDisposable _damageSubscription;

    void OnEnable()
    {
        // Subscribe to Health events from ExampleTrigger
        _healSubscription = ListenEvent.Health.Heal.Do(OnHeal);
        _damageSubscription = ListenEvent.Health.DealDamage.Do(OnDealDamage);
    }

    void OnDisable()
    {
        // Always dispose subscriptions to prevent memory leaks
        _healSubscription?.Dispose();
        _damageSubscription?.Dispose();
    }

    void OnHeal(EventMessageData data)
    {
        // Get typed payloads matching what ExampleTrigger sends
        var floatPayload = data.TryGet<FloatPayload>();
        var stringPayload = data.TryGet<StringPayload>();

        if (floatPayload != null)
        {
            Debug.Log($"Heal amount: {floatPayload.value}");
        }

        if (stringPayload != null)
        {
            Debug.Log($"Heal message: {stringPayload.value}");
        }
    }

    void OnDealDamage(EventMessageData data)
    {
        // Get typed payloads matching what ExampleTrigger sends
        var floatPayload = data.TryGet<FloatPayload>();
        var stringPayload = data.TryGet<StringPayload>();
        var intPayload = data.TryGet<IntPayload>(); // Only present if damage > 100

        if (floatPayload != null)
        {
            Debug.Log($"Damage received: {floatPayload.value}");
        }

        if (stringPayload != null)
        {
            // Start coroutine to handle damage over multiple frames if needed
            StartCoroutine(ProcessDamage(stringPayload.value));
        }

        if (intPayload != null)
        {
            Debug.Log($"Critical damage! Integer value: {intPayload.value}");
        }
    }

    IEnumerator ProcessDamage(string message)
    {
        int framesToWork = 2;
        int step = 0;
        while (step < framesToWork)
        {
            Debug.Log($"Processing damage... frame: {step}, message: {message}", this);
            step++;
            yield return null;
        }
        Debug.Log("Damage processing complete!");
    }
}
