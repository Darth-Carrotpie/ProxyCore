using UnityEngine;

namespace ProxyCore
{

    public static class MathExtensions
    {
        public static Vector3Int ConvertToVector3(this Vector3 vec3)
        {
            return new Vector3Int((int)vec3.x, (int)vec3.y, (int)vec3.z);
        }
        public static int Vector3ToCellCount(this Vector3 vec3)
        {
            Vector3Int intVec = vec3.ConvertToVector3();
            return intVec.x * intVec.y * intVec.z;
        }
        public static int Vector3ToCellCount(this Vector3Int intVec)
        {
            return intVec.x * intVec.y * intVec.z;
        }
    }
}