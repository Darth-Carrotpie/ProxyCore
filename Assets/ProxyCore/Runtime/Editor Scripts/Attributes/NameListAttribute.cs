using UnityEngine;
namespace ProxyCore
{
    public class NameListAttribute : PropertyAttribute
    {
        public System.Type propType;

        public string[] FullList
        {
            get;
            private set;
        }

        public NameListAttribute(System.Type aType, string methodName)
        {
            var method = aType.GetMethod(methodName);
            if (method != null)
            {
                FullList = method.Invoke(null, null) as string[];
            }
            else
            {
                Debug.LogError("NO SUCH METHOD " + methodName + " FOR " + aType);
            }
            propType = aType;
        }
    }

    public class ObjectListAttribute : PropertyAttribute
    {
        public System.Type propType;

        //objects have to have a field called "name", this field will be used for dropdown list;
        public object[] FullList
        {
            get;
            private set;
        }

        public ObjectListAttribute(System.Type aType, string methodName)
        {
            var method = aType.GetMethod(methodName);
            if (method != null)
            {
                FullList = method.Invoke(null, null) as object[];
            }
            else
            {
                Debug.LogError("NO SUCH METHOD " + methodName + " FOR " + aType);
            }
            propType = aType;
        }
    }
}