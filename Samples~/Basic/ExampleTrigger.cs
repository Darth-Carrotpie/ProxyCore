using System.Collections;
using System.Collections.Generic;
// Note: Once EventMessage assets are created and code is generated, uncomment the line below:
using ProxyCore;
using ProxyCore.Generated;
using UnityEngine;

/// <summary>
/// Example showing how to trigger events with the new EventMessage system.
/// Before using this sample:
/// 1. Create EventMessage assets via Create > Definitions > Event Message
/// 2. Assign categories to the events
/// 3. If needed, run ProxyCore > Regenerate Event Accessors
/// </summary>
public class ExampleTrigger : MonoBehaviour
{
    float damage = 15f;
    float heal = 52f;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Keypad1))
            SendEventWithUsingClause();

        if (Input.GetKeyDown(KeyCode.Keypad2))
            SendEvent_Simple();
    }

    void SendEventWithUsingClause()
    {
        // with using pattern (auto-sends on dispose) - use TriggerEvent:
        using (var evt = TriggerEvent.Health.DealDamage
            .With(new FloatPayload(damage))
            .With(new StringPayload("Damage: " + damage)))
        {
            if (damage > 100)
            {
                evt.With(new IntPayload((int)damage));
            }
        }
    }

    void SendEvent_Simple()
    {
        // simpler usage cases - use TriggerEvent to send events:
        TriggerEvent.Health.Heal.With(new FloatPayload(heal)).With(new StringPayload("Healed: " + heal)).Send();
    }
}

