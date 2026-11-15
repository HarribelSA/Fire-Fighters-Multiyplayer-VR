using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FirefighterVR.Station
{
    // Dedicated upward floor triangles only. Architectural walls/ceilings never become teleport areas.
    public sealed class StationTeleportArea : TeleportationArea
    {
        XROrigin rig;
        readonly System.Collections.Generic.Dictionary<XRRayInteractor,(int frame,Vector3 point,bool valid)> validity=new System.Collections.Generic.Dictionary<XRRayInteractor,(int,Vector3,bool)>();
        bool CachedValid(XRRayInteractor ray,RaycastHit hit)
        {
            if(validity.TryGetValue(ray,out var cached)&&cached.frame==Time.frameCount&&(cached.point-hit.point).sqrMagnitude<.000001f)return cached.valid;
            bool result=StationSceneSetup.Instance&&StationSceneSetup.Instance.ValidRayDestination(hit,rig,ray);
            validity[ray]=(Time.frameCount,hit.point,result);return result;
        }
        protected override bool GenerateTeleportRequest(IXRInteractor interactor,RaycastHit hit,ref TeleportRequest request)
        {
            if(!rig)rig=FindFirstObjectByType<XROrigin>();
            if(!StationSceneSetup.Instance||!(interactor is XRRayInteractor ray)||!StationSceneSetup.Instance.ValidRayDestination(hit,rig,ray))return false;
            return base.GenerateTeleportRequest(interactor,hit,ref request);
        }
        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if(!base.IsSelectableBy(interactor))return false;
            if(!rig)rig=FindFirstObjectByType<XROrigin>();
            if(interactor is XRRayInteractor ray&&ray.TryGetCurrent3DRaycastHit(out var hit))return CachedValid(ray,hit);
            return false;
        }
        public override bool IsHoverableBy(IXRHoverInteractor interactor)
        {
            if(!base.IsHoverableBy(interactor))return false;
            if(!rig)rig=FindFirstObjectByType<XROrigin>();
            return interactor is XRRayInteractor ray&&ray.TryGetCurrent3DRaycastHit(out var hit)&&CachedValid(ray,hit);
        }
    }
}
