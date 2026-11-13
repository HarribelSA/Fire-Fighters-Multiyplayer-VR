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
        protected override bool GenerateTeleportRequest(IXRInteractor interactor,RaycastHit hit,ref TeleportRequest request)
        {
            if(!rig)rig=FindFirstObjectByType<XROrigin>();
            if(!StationSceneSetup.Instance||hit.normal.y<.8f||!StationSceneSetup.Instance.ValidDestination(hit.point,rig))return false;
            return base.GenerateTeleportRequest(interactor,hit,ref request);
        }
        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if(!base.IsSelectableBy(interactor))return false;
            if(!rig)rig=FindFirstObjectByType<XROrigin>();
            if(interactor is XRRayInteractor ray&&ray.TryGetCurrent3DRaycastHit(out var hit))return StationSceneSetup.Instance&&StationSceneSetup.Instance.ValidDestination(hit.point,rig);
            return false;
        }
    }
}
