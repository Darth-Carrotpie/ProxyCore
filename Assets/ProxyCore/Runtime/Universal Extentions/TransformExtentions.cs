using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ProxyCore
{
    public static class TransformExtensions
    {
        public static Transform FindNearest(this List<Transform> transformList, Transform target)
        {
            if (transformList.Count == 0)
                return null;
            Transform nearest = transformList[0];
            foreach (Transform tr in transformList)
            {
                float prevDistance = (nearest.transform.position - target.position).magnitude;
                float thisDistance = (tr.position - target.position).magnitude;
                if (thisDistance < prevDistance)
                    nearest = tr;
            }
            return nearest;
        }
        public static void ResetTransformation(this Transform trans)
        {
            trans.position = Vector3.zero;
            trans.localRotation = Quaternion.identity;
            trans.localScale = new Vector3(1, 1, 1);
        }
        //Hierarchy helpers for the messaging system:
        public static bool IsSibling(this Transform trans, Transform otherTransform)
        {
            if (trans.parent.GetComponentsInChildren<Transform>().Contains(otherTransform))
                return true;
            else return false;
        }
        public static T GetComponentInSiblings<T>(this Transform trans)
        {
            T c = trans.parent.gameObject.GetComponentInChildren<T>();
            if (c != null)
                return c;
            else return default(T);
        }
    }

}