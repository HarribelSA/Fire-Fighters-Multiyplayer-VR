using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
namespace FirefighterVR.Station
{
    public sealed class StationMissionSeatAnchor : TeleportationAnchor
    {
        public StationMissionView view;
        public StationSeatKind kind;
        public GameObject indicator;
        public override bool IsSelectableBy(IXRSelectInteractor interactor)=>view&&view.Eligible(kind)&&base.IsSelectableBy(interactor);
        public override bool IsHoverableBy(IXRHoverInteractor interactor)=>view&&view.Eligible(kind)&&base.IsHoverableBy(interactor);
        protected override bool GenerateTeleportRequest(IXRInteractor interactor,RaycastHit hit,ref TeleportRequest request)
        {if(!view||!base.GenerateTeleportRequest(interactor,hit,ref request))return false;return view.QueueSeat(kind);}
        public void SetVisible(bool visible){if(indicator)indicator.SetActive(visible);foreach(var c in colliders)if(c)c.enabled=visible;}
    }
}
