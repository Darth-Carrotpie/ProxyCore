using System.Collections;
using System.Collections.Generic;
using ProxyCore;
// Note: Once EventMessage assets are created and code is generated, uncomment the line below:
// using ProxyCore.Generated;
using UnityEngine;

/// <summary>
/// Example showing how to trigger events with the new EventMessage system.
/// Before using this sample:
/// 1. Create EventMessage assets via Create > Definitions > Event Message
/// 2. Assign categories to the events
/// 3. Run Tools > ProxyCore > Regenerate Event Accessors
/// 4. Uncomment the using ProxyCore.Generated and the trigger code below
/// </summary>
public class ExampleTrigger : MonoBehaviour
{

    // Reference to an EventMessage asset (drag in Inspector)
    [SerializeField] private EventMessage winEvent;
    [SerializeField] private EventMessage errorEvent;

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.Space) && winEvent != null)
        {
            // New way to trigger events using direct EventMessage reference:
            new EventTriggerBuilder(winEvent)
                .With(new StringPayload("You Win!"))
                .With(new TransformPayload(transform))
                .With(new IntPayload(7))
                .Send();

            // Or with using pattern (auto-sends on dispose):
            // using (new EventTriggerBuilder(winEvent)
            //     .With(new StringPayload("You Win!"))
            //     .With(new IntPayload(7))) { }

            // Once code is generated, you can also use:
            // TriggerEvent.Examples.Win
            //     .With(new StringPayload("You Win!"))
            //     .With(new IntPayload(7))
            //     .Send();
        }

        if (Input.GetKeyDown(KeyCode.E) && errorEvent != null)
        {
            new EventTriggerBuilder(errorEvent)
                .With(new StringPayload("Error test!"))
                .Send();
        }

        // Example with MonoRefs payload
        if (Input.GetKeyDown(KeyCode.L))
        {
            // You can pass multiple MonoBehaviours in a single payload
            // var refs = new MonoRefsPayload(component1, component2);
            // new EventTriggerBuilder(someEvent).With(refs).Send();
        }
    }
}
