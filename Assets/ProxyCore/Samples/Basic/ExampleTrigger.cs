using System.Collections;
using System.Collections.Generic;
using ProxyCore;
using UnityEngine;
public class ExampleTrigger : MonoBehaviour {

    void Update() {
        if (Input.GetKeyDown(KeyCode.Space)) {
            string scoreMessageString = "You Win!";
            //This is how you trigger an event call. Be careful about the order, if you want other events to follow up use EventChain example of how they can be attached.
            //Samples should contain their own message objects, not residing in the core package.
            EventCoordinator.TriggerEvent(EventName.Examples.Win(), GameMessage.Write().WithStringMessage(scoreMessageString).WithTransform(transform).WithIntMessage(7));
        }

        //This trigger should throw an error, see more at example listener on Why
        if (Input.GetKeyDown(KeyCode.E)) {
            string scoreMessageString = "Error test!";
            //Samples should contain their own message objects, not residing in the core package.
            EventCoordinator.TriggerEvent(EventName.Examples.ShowError(), GameMessage.Write().WithStringMessage(scoreMessageString));
        }

        //Send a list of custom objects
        if (Input.GetKeyDown(KeyCode.L)) {
            string scoreMessageString = "Custom object value.....!...!";
            CustomObject newObj1 = new CustomObject(scoreMessageString + "  1!");
            CustomObject newObj2 = new CustomObject(scoreMessageString + "    2!");
            List<CustomObject> list = new List<CustomObject>();
            list.Add(newObj1);
            list.Add(newObj2);
            //EventCoordinator.TriggerEvent(EventName.Examples.AddResource(), GameMessage.Write().WithTargetCustomObjects(list));
        }
    }
}
