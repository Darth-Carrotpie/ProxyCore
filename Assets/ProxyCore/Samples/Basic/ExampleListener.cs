using System.Collections;
using System.Collections.Generic;
using ProxyCore;
using UnityEngine;

//This Mono shows how the event can be listened.
//Whatever event is triggered from anywhere, if the name matches, it will be run if you have a listening method attached via StartListening(messageName, OnEventReceivedMethod)
//It will be run same frame as it was triggered (it's not an async buffer!), so be careful not to oversize the event chain.
//To off-load work into multiple frames of final tasks, you can run them in a Coroutine which you start within OnEventReceivedMethod
public class ExampleListener : MonoBehaviour {
    void Start() {
        //EventCoordinator.StartListening(EventName.UI.ShowScoreScreen(), OnScoreShowReceived);
        //tip:
        //if unsure what triggers/listens the event, just select the whole event name chain (in this case - "EventName.UI.ShowScoreScreen()")
        //and to a ctrl+shift+F in VS Code to find all triggers throughout the project.
        EventCoordinator.StartListening(EventName.Examples.AddResource(), OnScoreScreenShown);
    }

    void OnScoreShowReceived(GameMessage msg) {
        Debug.Log(msg.transform);
        //Event starts the Coroutine which does the work:
        StartCoroutine(DoWorkMethod(msg.strMessage));
    }

    IEnumerator DoWorkMethod(string showScore) {
        int framesToWork = 2;
        int step = 0;
        while (step < framesToWork) {
            Debug.Log("Doing work over multiple frames... working on frame: " + step, this);
            step++;
            yield return new WaitForFixedUpdate();
        }
        Debug.Log(showScore);
    }

    void OnScoreScreenShown(GameMessage msg) {
        //Event was attached so it will be always executed after the main one.
        Debug.Log(msg.strMessage);
    }
}
