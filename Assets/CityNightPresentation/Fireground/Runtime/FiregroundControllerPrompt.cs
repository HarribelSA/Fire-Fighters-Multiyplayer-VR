using System.Collections.Generic;
using FirefighterVR.Station;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;

namespace CityNightPresentation.Fireground
{
    // At the fireground both crew members work with the Meta controllers.
    // - Driver / Pump Operator: app-side hand tracking is paused and the avatar uses controller grip/trigger.
    // - Both roles: glowing controller silhouettes float in front of the player ("pick up your controllers")
    //   and stay until the real controllers have been held for holdAfterPickupSeconds, then disappear.
    public sealed class FiregroundControllerPrompt : MonoBehaviour
    {
        public FiregroundOperation op;
        public GameObject ghostRoot;
        public Vector3 offset = new Vector3(0, -.32f, .45f);
        [Tooltip("The silhouettes stay this long after the controllers are picked up, then hide.")]
        [Min(0)] public float holdAfterPickupSeconds = 3;
        [Tooltip("Show the controller silhouettes to the Assistant / nozzle operator as well.")]
        public bool promptAssistant = true;
        readonly List<XRHandSubsystem> stopped = new List<XRHandSubsystem>();
        readonly List<XRHandSubsystem> query = new List<XRHandSubsystem>();
        StationLocalRig engagedRig;
        bool engaged, notified, done;
        float heldSince = -1;

        void Update()
        {
            var flow = StationSessionFlow.Instance; var rig = flow ? flow.localRig : null;
            var role = op ? op.LocalRole : StationRole.None;
            bool want = op && rig && op.LocalDismounted && (role == StationRole.Driver || promptAssistant && role == StationRole.Assistant);
            if (want && !engaged) Engage(rig, role); else if (!want && engaged) Disengage();
            if (!engaged || !ghostRoot) return;
            bool controllers = XRInputModalityManager.currentInputMode.Value == XRInputModalityManager.InputMode.MotionController;
            if (!controllers) { heldSince = -1; done = false; }
            else if (heldSince < 0) { heldSince = Time.unscaledTime; FiregroundOperation.Notify(role == StationRole.Assistant ? "Controllers ready · NOZZLE OPERATOR" : "Controllers ready · PUMP OPERATOR"); }
            if (controllers && Time.unscaledTime - heldSince >= holdAfterPickupSeconds) done = true;
            bool show = !done;
            if (ghostRoot.activeSelf != show) ghostRoot.SetActive(show);
            if (!show || !rig.origin || !rig.origin.Camera) return;
            var cam = rig.origin.Camera.transform; var f = Vector3.ProjectOnPlane(cam.forward, Vector3.up); if (f.sqrMagnitude < .01f) f = cam.up; f.Normalize();
            var yaw = Quaternion.LookRotation(f, Vector3.up);
            ghostRoot.transform.SetPositionAndRotation(cam.position + yaw * offset + Vector3.up * .03f * Mathf.Sin(Time.time * 3), yaw);
        }
        void Engage(StationLocalRig rig, StationRole role)
        {
            engaged = true; done = false; heldSince = -1; engagedRig = rig;
            if (role == StationRole.Driver)
            {
                rig.ControllersOnly = true;
                query.Clear(); SubsystemManager.GetSubsystems(query);
                foreach (var s in query) if (s != null && s.running) { s.Stop(); stopped.Add(s); }
            }
            if (!notified) { notified = true; FiregroundOperation.Notify(role == StationRole.Assistant ? "Nozzle Operator: pick up your Meta controllers" : "Pump Operator: pick up your Meta controllers"); }
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
