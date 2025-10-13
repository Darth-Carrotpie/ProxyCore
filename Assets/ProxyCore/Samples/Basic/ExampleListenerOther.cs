using System.Collections;
using System.Collections.Generic;
using ProxyCore;
using UnityEngine;

public class ExampleListenerOther : MonoBehaviour {
    void Start() {
        EventCoordinator.StartListening(EventName.Examples.ShowError(), OnErrorShow);
        //Can listen to the same event with any number of Listeners independently, they will all receive an identical Message
        EventCoordinator.StartListening(EventName.Examples.AddResource(), OnAddResource);
    }

    void OnAddResource(GameMessage msg) {
        foreach (CustomObject ob in msg.targetCustomObjects) {
            Debug.Log(ob.value);
        }
    }

    void OnErrorShow(GameMessage msg) {
        //Event was attached so it will be always executed after the main one.
        Debug.Log(msg.strMessage);
    }
}
