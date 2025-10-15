using System;
using System.Collections.Generic;
using UnityEngine;
//#pragma warning disable 0414
//idea for upgrade: this could be composite, made out of generic MessagePage or smth like it, whithin which would contain isSet states and other things if needed;
namespace ProxyCore {
    public class GameMessage : BaseMessage {
        public static GameMessage Write() {
            return new GameMessage();
        }

        //These are example message fields, can have a lot of these but of course not memory efficient.
        //Priority for a messaging/event system is convenience though...
        //If it gets blown up with a larger scale project, you can theoretically split it into multiple event systems to handle different things, though that might complicate things.
        private string _strMessage;
        private bool strMessageSet; // must be private bool
        public string strMessage { get { return base.GetItem(ref _strMessage, strMessageSet); } }
        public GameMessage WithStringMessage(string value) => base.WithItem<string>(ref _strMessage, value, ref strMessageSet);

        private Transform _transform; //must not be type of bool (if bool needed, use int)
        //the bool variable has to have identical name +"Set" added at the end, for proper message debug printing.
        //See more about debug logic within BaseMessage.ToString() method.
        private bool transformSet;

        public Transform transform { get { return base.GetItem(ref _transform, transformSet); } }
        public GameMessage WithTransform(Transform value) => base.WithItem<Transform>(ref _transform, value, ref transformSet);

        //Can also send a custom object for better packing and readability, a preferred way. And even a List as well.
        //private List<CustomObject> _targetCustomObjects;
        //private bool targetCustomObjectsSet;
        //public List<CustomObject> targetCustomObjects { get { return base.GetItem(ref _targetCustomObjects, targetCustomObjectsSet); } }
        //public GameMessage WithTargetCustomObjects(List<CustomObject> value) => base.WithItem<List<CustomObject>>(ref _targetCustomObjects, value, ref targetCustomObjectsSet);

        //Use an int type if you need a bool, i.e. 0 or 1 and just convert it to bool in a listener via (bool)value
        private int _intMessage;
        private bool intMessageSet;
        public int intMessage { get { return base.GetItem(ref _intMessage, transformSet); } }
        public GameMessage WithIntMessage(int value) => base.WithItem<int>(ref _intMessage, value, ref transformSet);

        private float _deltaFloat;
        private bool deltaFloatSet;
        public float deltaFloat { get { return base.GetItem(ref _deltaFloat, deltaFloatSet); } }
        public GameMessage WithDeltaFloat(float value) => base.WithItem<float>(ref _deltaFloat, value, ref deltaFloatSet);

        private List<MonoBehaviour> _monoRefsList;
        private bool monoRefsListSet;
        public List<MonoBehaviour> monoRefsList { get { return base.GetItem(ref _monoRefsList, monoRefsListSet); } }
        public MonoBehaviour monoRef<T>() {
            foreach (MonoBehaviour mono in monoRefsList) {
                if (mono is T) {
                    return mono;
                }
            }
            ;
            //throw new Exception("No <" + typeof(T) + "> was not set in monoRefsList but was requested within GameMessage: " + (ToString()));
            return null; //null is also a value, need it for de-assigning stuff;
        }
        public GameMessage WithMonoRefsList(List<MonoBehaviour> value) => base.WithItem<List<MonoBehaviour>>(ref _monoRefsList, value, ref monoRefsListSet);
    }
}
