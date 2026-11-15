using UnityEngine;
using Unity.XR.CoreUtils;
namespace FirefighterVR.Station
{
    public static class StationSeatPlacement
    {
        public static float EyeHeight(FirefighterAvatarDriver avatar)=>avatar?Mathf.Clamp(.59f+avatar.standingEye.y-avatar.standingHips.y,1.05f,1.55f):1.32f;
        public static Vector3 EyeTarget(Transform seat,FirefighterAvatarDriver avatar)
        {var offset=avatar?avatar.standingEye-avatar.standingHips:Vector3.zero;offset.y=EyeHeight(avatar);return seat.position+seat.rotation*offset;}
        public static void Place(XROrigin origin,Transform seat,FirefighterAvatarDriver avatar)
        {
            var capsule=origin.GetComponent<CharacterController>();if(capsule)capsule.enabled=false;
            origin.MatchOriginUpCameraForward(Vector3.up,seat.forward);
            origin.MoveCameraToWorldLocation(EyeTarget(seat,avatar));
            if(avatar){avatar.Seated=true;avatar.SeatPosition=seat.position;avatar.SeatYaw=seat.eulerAngles.y;}
        }
    }
}
