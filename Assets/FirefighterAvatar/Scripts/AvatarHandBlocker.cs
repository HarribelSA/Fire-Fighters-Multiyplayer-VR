using System.Collections.Generic;
using UnityEngine;

namespace FirefighterVR
{
    // Marks solid objects (the fire trucks) whose colliders the local avatar's hands may not pass into.
    // While the player stands (not seated), FirefighterAvatarDriver pushes each drawn hand back out of these
    // colliders, so a hand reaching into the truck stops at its surface. Interactables (reel, buttons,
    // nozzle bracket) and triggers are ignored, so the controls on the truck stay reachable.
    public sealed class AvatarHandBlocker : MonoBehaviour
    {
        public static readonly List<Collider> Colliders = new List<Collider>();
        readonly List<Collider> mine = new List<Collider>();
        void OnEnable()
        {
            mine.Clear();
            foreach (var c in GetComponentsInChildren<Collider>(true))
            {
                if (!c || c.isTrigger || c is WheelCollider || c is CharacterController) continue;
                if (c.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>()) continue;
                mine.Add(c); Colliders.Add(c);
            }
        }
        void OnDisable() { foreach (var c in mine) Colliders.Remove(c); mine.Clear(); }

        static SphereCollider probe;
        // Returns the point moved out of every blocking collider it is inside (radius = hand size).
        public static Vector3 PushOut(Vector3 point, float radius)
        {
            if (Colliders.Count == 0) return point;
            if (!probe) { var g = new GameObject("AvatarHandProbe"); g.hideFlags = HideFlags.HideAndDontSave; probe = g.AddComponent<SphereCollider>(); probe.isTrigger = true; g.transform.position = new Vector3(0, -9999, 0); }
            probe.radius = radius;
            for (int pass = 0; pass < 2; pass++)
                foreach (var c in Colliders)
                {
                    if (!c || !c.enabled || !c.gameObject.activeInHierarchy) continue;
                    var b = c.bounds; b.Expand(radius * 2); if (!b.Contains(point)) continue;
                    if (Physics.ComputePenetration(probe, point, Quaternion.identity, c, c.transform.position, c.transform.rotation, out var dir, out float dist)) point += dir * dist;
                }
            return point;
        }
    }
}
