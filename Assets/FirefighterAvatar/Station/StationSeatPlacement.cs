using UnityEngine;
using Unity.XR.CoreUtils;
namespace FirefighterVR.Station
{
    public static class StationSeatPlacement
    {
        public static float EyeHeight(FirefighterAvatarDriver avatar)=>avatar?Mathf.Clamp(.59f+avatar.standingEye.y-avatar.standingHips.y,1.05f,1.55f):1.32f;
        public static float EyeAboveHips(FirefighterAvatarDriver avatar)=>avatar?Mathf.Clamp(avatar.standingEye.y-avatar.standingHips.y,.46f,.96f):.73f;
        // Eye sits above the measured hip joint of the real seat (SeatPose), not a fixed height.
        public static Vector3 EyeTarget(Transform seat,FirefighterAvatarDriver avatar)
        {var f=SeatPose.Resolve(seat);var offset=avatar?avatar.standingEye-avatar.standingHips:Vector3.zero;offset.y=f.hipHeight+EyeAboveHips(avatar);return f.floorPoint+Quaternion.Euler(0,f.yaw,0)*offset;}
        // Re-puts the camera at the seated eye point of the seat the avatar currently occupies
        // (used after a headset recenter, which moves the tracking origin under the player).
        public static bool PlaceAtCurrentSeat(XROrigin origin,FirefighterAvatarDriver avatar)
        {
            if(!origin||!origin.Camera||!avatar||!avatar.Seated)return false;
            if(avatar.SeatId!=0&&SeatPose.TryGet(avatar.SeatId,out var pose)){Place(origin,pose.transform,avatar);return true;}
            var yaw=Quaternion.Euler(0,avatar.SeatYaw,0);var offset=avatar.standingEye-avatar.standingHips;offset.y=avatar.SeatHipHeight+EyeAboveHips(avatar);
            var capsule=origin.GetComponent<CharacterController>();if(capsule)capsule.enabled=false;
            origin.MatchOriginUpCameraForward(Vector3.up,yaw*Vector3.forward);
            origin.MoveCameraToWorldLocation(avatar.SeatPosition+yaw*offset);
            return true;
        }
        public static void Place(XROrigin origin,Transform seat,FirefighterAvatarDriver avatar)
        {
            var capsule=origin.GetComponent<CharacterController>();if(capsule)capsule.enabled=false;
            origin.MatchOriginUpCameraForward(Vector3.up,seat.forward);
            origin.MoveCameraToWorldLocation(EyeTarget(seat,avatar));
            if(avatar)avatar.SetSeat(seat);
        }
    }
}
