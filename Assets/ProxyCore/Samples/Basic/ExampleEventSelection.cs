using System.Collections;
using System.Collections.Generic;
using ProxyCore;
using UnityEngine;
#if UNITY_EDITOR
using ProxyCore.Editor;
#endif
public class ExampleEventSelection : MonoBehaviour {
    //By adding this editor-only attribute, you will enable a selection of event from all events that get exported via EventName.Get()
    //Might be very useful for your game designers!
#if UNITY_EDITOR
    [StringInList(typeof(PropertyDrawersHelper), "AllEventNames")]
#endif
    public string eventName = "";

    void OnEnable() {
        //If there are no listeners, it will be skipped from call stack and not shown in debug!
        EventCoordinator.TriggerEvent(eventName, GameMessage.Write());
    }
}
