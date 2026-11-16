using System.Collections.Generic;
using UnityEngine;

namespace FirefighterVR
{
    // Measured seating geometry of one existing seat anchor (chair or vehicle seat).
    // Avatars sit on the real cushion with feet on the real floor; remote peers resolve the
    // same seat by id every frame, so a seated body stays locked to a moving truck.
    public sealed class SeatPose : MonoBehaviour
    {
        public byte id;
        [Tooltip("Hip joint in this seat's local space.")] public Vector3 hipLocal = new Vector3(0, .59f, 0);
        [Tooltip("Floor height under the feet, seat local Y.")] public float floorLocalY;
        [Tooltip("Horizontal distance from the hips to the resting feet.")] public float footForward = .43f;

        public struct Frame { public byte id; public Vector3 floorPoint; public float hipHeight, footForward, yaw; }
        static readonly Dictionary<byte, SeatPose> registry = new Dictionary<byte, SeatPose>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatic() => registry.Clear();
        void OnEnable() { if (id != 0) registry[id] = this; }
        void OnDisable() { if (id != 0 && registry.TryGetValue(id, out var s) && s == this) registry.Remove(id); }
        public static bool TryGet(byte id, out SeatPose pose) => registry.TryGetValue(id, out pose) && pose && pose.isActiveAndEnabled;

        public Frame Current
        {
            get
            {
                var t = transform; Vector3 hip = t.TransformPoint(hipLocal); float floor = t.TransformPoint(new Vector3(0, floorLocalY, 0)).y;
                return new Frame { id = id, floorPoint = new Vector3(hip.x, floor, hip.z), hipHeight = Mathf.Max(.2f, hip.y - floor), footForward = footForward, yaw = t.eulerAngles.y };
            }
        }
        // Seats without a measured pose keep the original behaviour (anchor = floor, hips 0.59 m).
        public static Frame Resolve(Transform seat)
        {
            if (!seat) return new Frame { hipHeight = .59f, footForward = .43f };
            var pose = seat.GetComponent<SeatPose>();
            return pose ? pose.Current : new Frame { floorPoint = seat.position, hipHeight = .59f, footForward = .43f, yaw = seat.eulerAngles.y };
        }
    }
}
