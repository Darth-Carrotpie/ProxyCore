using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ProxyCore
{
    public static class GenericHelpers
    {
        public static List<PropertyInfo> GetStringProperties<T>()
        {
            var props = typeof(T).GetUnreflectedProperties().Where(a => a.PropertyType == typeof(string)).ToList();
            return props;
        }
    }
}
