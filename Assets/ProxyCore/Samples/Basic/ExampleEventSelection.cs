using System.Collections;
using System.Collections.Generic;
using ProxyCore;
using UnityEngine;
#if UNITY_EDITOR
using ProxyCore.Editor;
#endif

/// <summary>
/// Example showing how to trigger an event selected from a dropdown in the Inspector.
/// With the new system, you can directly reference an EventMessage asset.
/// </summary>
public class ExampleEventSelection : MonoBehaviour
{

    // Direct reference to an EventMessage asset - drag and drop in Inspector
    [SerializeField]
    [Tooltip("Select an EventMessage asset to trigger")]
    private EventMessage selectedEvent;

    void OnEnable()
    {
        if (selectedEvent != null)
        {
            // Trigger the selected event with empty data
            new EventTriggerBuilder(selectedEvent).Send();
        }
    }
}
