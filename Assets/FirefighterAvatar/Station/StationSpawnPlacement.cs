using System.Collections;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR;

namespace FirefighterVR.Station
{
    public static class StationSpawnPlacement
    {
        public static bool TrackingReady(XROrigin origin)
        {
            if (!origin || !origin.Camera || !origin.Camera.isActiveAndEnabled || !origin.Origin) return false;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            return head.isValid && head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked
                && origin.CurrentTrackingOriginMode != TrackingOriginModeFlags.Unknown
                && float.IsFinite(origin.CameraInOriginSpaceHeight) && origin.CameraInOriginSpaceHeight > .4f;
        }
        public static Vector3 GroundPosition(XROrigin origin)
        {
            var up = origin.Origin.transform.up;
            return origin.Camera.transform.position - up * Vector3.Dot(origin.Camera.transform.position - origin.Origin.transform.position, up);
        }
        public static void Place(XROrigin origin, Transform anchor)
        { Place(origin,anchor.position,anchor.rotation); }
        public static void Place(XROrigin origin, Vector3 position, Quaternion rotation)
        {
            var controller = origin.GetComponent<CharacterController>(); bool was = controller && controller.enabled;
            if (controller) controller.enabled = false;
            origin.MatchOriginUpCameraForward(Vector3.up, rotation*Vector3.forward);
            // Move the actual XROrigin.Origin root, preserving HMD tracking and floor offset.
            origin.Origin.transform.position += position - GroundPosition(origin);
            Physics.SyncTransforms();
            if (controller) controller.enabled = was;
        }
        public static bool Aligned(XROrigin origin, Transform anchor, float tolerance = .06f) => Vector3.Distance(GroundPosition(origin), anchor.position) <= tolerance;
        public static IEnumerator WaitForTracking(XROrigin origin)
        { while (!TrackingReady(origin)) yield return null; }
    }
}
