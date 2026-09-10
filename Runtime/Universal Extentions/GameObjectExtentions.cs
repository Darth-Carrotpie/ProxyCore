using System.Collections.Generic;
using UnityEngine;

namespace ProxyCore
{
    public static class GameObjectExtensions
    {
        //Destroys all Children Objects which have a component of type
        public static void DestroyAllChildren(this GameObject go, System.Type type)
        {
            List<Component> components = new List<Component>(go.GetComponentsInChildren(type));
            for (int i = components.Count - 1; i >= 0; i--)
            {
                if (components[i].gameObject != go)
                {
                    GameObject.Destroy(components[i].gameObject);
                }
            }
        }
        //Destroys all monos in children except which have a component of type T
        public static void DestroyAllMonosExcept<T>(this GameObject go)
        {
            List<Component> components = new List<Component>(go.GetComponentsInChildren(typeof(MonoBehaviour)));
            for (int i = components.Count - 1; i >= 0; i--)
            {
                //Debug.Log(components[i].GetType()+" its not equal by type: " + typeof(T));
                GameObject.Destroy(components[i]);
            }
        }
    }
}