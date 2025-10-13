using System.Collections;
using System.Collections.Generic;
using ProxyCore;
using UnityEngine;
using UnityEngine.SceneManagement;
namespace ProxyCore
{
    public class EventChain : MonoBehaviour
    {
        //This is used for passing the message further for another event.
        //Functionally it still happens in the same frame, but if to avoid confusion and improve readability we need to use a different event name
        //Attached events will be always triggered after main ones, but ".Attach" can be done only once.
        //It could be later improved, but for now there was never an actual need for more than one, i.e.
        //1. MakeSomethingHappen
        //2. ThatSomethingHappened

        void Start()
        {
            //EventCoordinator.Attach(EventName.Network.PlayerJoined(), OnPlayerJoinedAttachment);
            //EventCoordinator.Attach(EventName.UI.ShowScoreScreen(), OnShowScoreScreenAttachment);
        }

        void OnDestroy()
        {
            //EventCoordinator.Detach(EventName.Network.PlayerJoined(), OnPlayerJoinedAttachment);
            //EventCoordinator.Detach(EventName.UI.ShowScoreScreen(), OnShowScoreScreenAttachment);
        }

        void OnShowScoreScreenAttachment(GameMessage msg)
        {
            //If we wanted, we can write some additional information to the message before passing, i.e.
            GameMessage newMsg = msg.WithIntMessage(42);
            //Then we write a trigger, like we normally would, but with a different name
            //EventCoordinator.TriggerEvent(EventName.UI.ScoreScreenShown(), newMsg);
        }

        void OnPlayerJoinedAttachment(GameMessage msg)
        {
            EventCoordinator.TriggerEvent(EventName.Input.Menus.ShowSettings(), msg);
        }
    }
}