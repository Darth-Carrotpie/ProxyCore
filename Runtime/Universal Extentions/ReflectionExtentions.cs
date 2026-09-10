using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace ProxyCore
{
    public static class ReflectionExtentions
    {
        public static PropertyInfo[] GetUnreflectedProperties(this Type type)
        {
            return type.GetProperties()
                  .Where(pi => !Attribute.IsDefined(pi, typeof(DoNotReflectPropertyAttribute)))
                  .ToArray();
        }
    }
    public class DoNotReflectPropertyAttribute : Attribute
    {
    }
}