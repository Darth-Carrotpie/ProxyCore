using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProxyCore
{

    public static class MonoBehaviourExtensions
    {

        //Enable/Disable a whole List:
        public static void EnableAll(MonoBehaviour[] listToChangeState, bool state)
        {
            listToChangeState.ToList().EnableAll(state);
        }
        public static void EnableAll(this List<MonoBehaviour> listToChangeState, bool state)
        {
            foreach (MonoBehaviour item in listToChangeState)
            {
                item.enabled = state;
            }
        }
    }
}