using System.Collections.Generic;
using UnityEngine;

namespace ProxyCore
{
    public static class PhysicsExtensions
    {
        //collider extensions:
        public static List<Transform> GetBoundBoxColliders(this Collider myCollider, Collider[] theirColliders)
        {
            List<Transform> collided = new List<Transform>();
            foreach (Collider theirCol in theirColliders)
            {
                if (theirCol.bounds.Intersects(myCollider.bounds))
                {
                    if (theirCol.transform.parent.gameObject != myCollider.transform.parent.gameObject && //for Targetable,
                        theirCol.gameObject != myCollider.transform.parent.gameObject)
                    { //for BuildableSpaceMarker?
                        Vector3 dir;
                        float dist;
                        if (Physics.ComputePenetration(myCollider, myCollider.transform.position, myCollider.transform.rotation,
                                theirCol, theirCol.transform.position, theirCol.transform.rotation,
                                out dir, out dist))
                        {
                            collided.Add(theirCol.transform.parent);
                            //Debug.Log(myCollider.transform.parent.name+" adding "+theirCol.transform.parent.name);
                        }
                    }
                }
            }
            return collided;
        }
        //this could be still usable somewhere, i.e. on weird shaped bullet computations, instead of raycast
        /*     public static List<Transform> SweepForColliders(this Rigidbody myRigidbody, LayerMask layerMask){
                List<Transform> collided = new List<Transform>();
                RaycastHit[] hits = myRigidbody.SweepTestAll(Vector3.forward, 0.1f, QueryTriggerInteraction.UseGlobal);
                Debug.Log(myRigidbody.transform.parent.name+"hits: "+hits.Length);
                foreach (RaycastHit hit in hits) {
                    if (((1<<hit.collider.gameObject.layer) & layerMask) != 0) {
                        if (hit.collider.transform.parent.gameObject != myRigidbody.transform.parent.gameObject && //for Targetable,
                            hit.collider.gameObject != myRigidbody.transform.parent.gameObject){//for BuildableSpaceMarker?
                            collided.Add(hit.collider.transform.parent);
                            Debug.Log(myRigidbody.transform.parent.name+" adding "+hit.collider.transform.parent.name);
                        }
                    }
                }
                return collided;
            } */
        /*     public static bool HasComponent<T>(this Component component) where T : Component
            {
                return component.GetComponent() != null;
            } */
    }
}