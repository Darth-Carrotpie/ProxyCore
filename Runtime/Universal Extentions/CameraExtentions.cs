using UnityEngine;

namespace ProxyCore
{

    public static class CameraExtensions
    {
        public static bool IsVisibleFrom(this Renderer renderer, Camera camera)
        {
            var planes = GeometryUtility.CalculateFrustumPlanes(camera);
            return GeometryUtility.TestPlanesAABB(planes, renderer.bounds);
        }
    }
}