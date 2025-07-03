using System;
using NorthShore.Player;
using UnityEngine;

namespace UnityJigs.Extensions
{
    public static class ColliderUtils
    {
        public static int OverlapCollider(
            Collider collider,
            Collider[] results,
            int layerMask = ~0,
            QueryTriggerInteraction qti = QueryTriggerInteraction.UseGlobal,
            bool showDebug = false)
        {
            switch (collider)
            {
                case BoxCollider b:
                    var bCentre = b.transform.TransformPoint(b.center);
                    var bExtents = Vector3.Scale(b.transform.TransformVector(Vector3.one), b.size * .5f);
                    var orientation = b.transform.rotation;
                    if (showDebug) GizmoDrawer.DrawBox(bCentre, bExtents, orientation);
                    return Physics.OverlapBoxNonAlloc(bCentre, bExtents, results, orientation, layerMask, qti);
                case SphereCollider s:
                    var sCentre = s.transform.TransformPoint(s.center);
                    var sRadius = s.transform.lossyScale.x * s.radius;
                    if (showDebug) GizmoDrawer.DrawSphere(sCentre, sRadius);
                    return Physics.OverlapSphereNonAlloc(sCentre, sRadius, results, layerMask, qti);
                case CapsuleCollider c:
                    var cPt1 = c.transform.TransformPoint(c.center + c.height * .5f * Vector3.up);
                    var cPt2 = c.transform.TransformPoint(c.center + c.height * .5f * Vector3.up);
                    var cRadius = c.transform.lossyScale.x * c.radius;
                    if (showDebug) GizmoDrawer.DrawCapsule(cPt1, cPt2, cRadius);
                    return Physics.OverlapCapsuleNonAlloc(cPt1, cPt2, cRadius, results, layerMask, qti);
                default: throw new NotSupportedException("Collider type not supported:" + collider.GetType().Name);
            }
        }
    }
}
