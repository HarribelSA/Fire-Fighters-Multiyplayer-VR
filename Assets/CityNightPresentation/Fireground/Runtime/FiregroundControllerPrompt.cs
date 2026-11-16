using System.Collections.Generic;
using FirefighterVR.Station;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace CityNightPresentation.Fireground
{
    // At the fireground the Driver / Pump Operator switches from hand tracking to the Meta controllers:
    // app-side hand tracking is paused, the avatar uses controller grip/trigger animation, and glowing
    // controller silhouettes float in front of the player until real controllers are tracked.
    public sealed class FiregroundControllerPrompt : MonoBehaviour
    {
        public FiregroundOperation op;
        public GameObject ghostRoot;
        public Vector3 offset = new Vector3(0, -.32f, .45f);
        readonly List<XRHandSubsystem> stopped = new List<XRHandSubsystem>();
        readonly List<XRHandSubsystem> query = new List<XRHandSubsystem>();
        StationLocalRig engagedRig;
        bool engaged, notified;

        void Update()
        {
            var flow = StationSessionFlow.Instance; var rig = flow ? flow.localRig : null;
            bool want = op && rig && op.LocalDismounted && op.LocalRole == StationRole.Driver;
            if (want && !engaged) Engage(rig); else if (!want && engaged) Disengage();
            if (!engaged || !ghostRoot) return;
            bool controllers = XRInputModalityManager.currentInputMode.Value == XRInputModalityManager.InputMode.MotionController;
            if (ghostRoot.activeSelf == controllers) ghostRoot.SetActive(!controllers);
            if (controllers || !rig.origin || !rig.origin.Camera) return;
            var cam = rig.origin.Camera.transform; var f = Vector3.ProjectOnPlane(cam.forward, Vector3.up); if (f.sqrMagnitude < .01f) f = cam.up; f.Normalize();
            var yaw = Quaternion.LookRotation(f, Vector3.up);
            ghostRoot.transform.SetPositionAndRotation(cam.position + yaw * offset + Vector3.up * .03f * Mathf.Sin(Time.time * 3), yaw);
        }
        void Engage(StationLocalRig rig)
        {
            engaged = true; engagedRig = rig; rig.ControllersOnly = true;
            query.Clear(); SubsystemManager.GetSubsystems(query);
            foreach (var s in query) if (s != null && s.running) { s.Stop(); stopped.Add(s); }
            if (!notified) { notified = true; FiregroundOperation.Notify("Pump Operator: pick up your Meta controllers"); }
        }
        void Disengage()
        {
            engaged = false; if (engagedRig) engagedRig.ControllersOnly = false; engagedRig = null;
            foreach (var s in stopped) if (s != null && !s.running) s.Start();
            stopped.Clear(); if (ghostRoot) ghostRoot.SetActive(false);
        }
        void OnDisable() { if (engaged) Disengage(); }
    }
}
