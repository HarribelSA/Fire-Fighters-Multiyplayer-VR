using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace CityNightPresentation.Fireground
{
    // Existing XRI XRGrabInteractable: Grab = existing select action, Trigger = existing activate action.
    // The holder simulates the nozzle locally and streams its pose; others follow the stream or the
    // authoritative resting/docked pose.
    [RequireComponent(typeof(XRGrabInteractable))]
    public sealed class HoseNozzle : MonoBehaviour, IXRSelectFilter
    {
        public FiregroundOperation operation;
        public NozzleLeash leash;
        public Transform muzzle, hoseAttach;
        public ParticleSystem stream, mist;
        public AudioSource sprayAudio;
        [Range(5, 30)] public float poseRate = 20;

        XRGrabInteractable grab;
        IXRSelectInteractor holder;
        Vector3 remotePos; Quaternion remoteRot = Quaternion.identity; bool hasRemote;
        float nextPose, grabTime, sprayLevel, nextHaptic;
        bool spraying;

        public bool LocallyHeld { get; private set; }
        public bool LocalTrigger { get; private set; }
        public Vector3 HoseAttachLocal => hoseAttach ? transform.InverseTransformPoint(hoseAttach.position) : Vector3.zero;
        public bool canProcess => isActiveAndEnabled;
        public bool Process(IXRSelectInteractor interactor, IXRSelectInteractable interactable) => LocallyHeld || (operation && operation.CanLocalGrabNozzle);

        void Awake()
        {
            grab = GetComponent<XRGrabInteractable>();
            grab.selectFilters.Add(this);
            grab.selectEntered.AddListener(Grabbed);
            grab.selectExited.AddListener(Released);
            grab.activated.AddListener(_ => SetTrigger(true));
            grab.deactivated.AddListener(_ => SetTrigger(false));
            if (stream && operation) { var m = stream.main; m.startSpeedMultiplier = operation.streamSpeed; }
            SetSpray(false, true);
        }
        void Grabbed(SelectEnterEventArgs args)
        {
            LocallyHeld = true; holder = args.interactorObject; grabTime = Time.unscaledTime; hasRemote = false;
            if (operation) operation.NozzleGrabbed();
        }
        void Released(SelectExitEventArgs args)
        {
            if (!LocallyHeld) return;
            LocallyHeld = false; LocalTrigger = false; holder = null;
            if (!operation) return;
            Vector3 p = transform.position; Quaternion q = transform.rotation;
            if (operation.CanDock(p)) { p = operation.nozzleDock.position; q = operation.nozzleDock.rotation; }
            else
            {
                // Drop and lie flat on the ground, barrel horizontal.
                Vector3 g = operation.GroundBelow(p);
                Vector3 f = Vector3.ProjectOnPlane(transform.forward, Vector3.up); if (f.sqrMagnitude < 1e-4f) f = Vector3.forward;
                q = Quaternion.LookRotation(f.normalized, Vector3.up); p = g + Vector3.up * .06f;
            }
            transform.SetPositionAndRotation(p, q);
            operation.NozzleReleased(p, q);
        }
        void SetTrigger(bool pressed)
        {
            if (!LocallyHeld) return; LocalTrigger = pressed;
            if (operation) operation.NozzleTrigger(pressed);
        }
        public void ReceiveRemotePose(Vector3 p, Quaternion q) { remotePos = p; remoteRot = q; hasRemote = true; }

        void Update()
        {
            if (!operation) return;
            var s = operation.Current; float dt = Time.deltaTime;
            if (LocallyHeld)
            {
                // Authority rejected or another peer owns the nozzle: let go cleanly.
                if (Time.unscaledTime - grabTime > 1.5f && !operation.LocalHoldsNozzle && grab.interactionManager && holder != null)
                { grab.interactionManager.SelectExit(holder, grab); return; }
                if (Time.unscaledTime >= nextPose) { nextPose = Time.unscaledTime + 1f / poseRate; operation.SendNozzlePose(transform.position, transform.rotation); }
                if (leash && leash.Tension > .5f && Time.unscaledTime >= nextHaptic && holder is XRBaseInputInteractor input)
                { nextHaptic = Time.unscaledTime + .15f; input.SendHapticImpulse(.25f + .5f * leash.Tension, .06f); }
            }
            else if (s.Held)
            {
                if (hasRemote) { float k = 1 - Mathf.Exp(-18 * dt); transform.SetPositionAndRotation(Vector3.Lerp(transform.position, remotePos, k), Quaternion.Slerp(transform.rotation, remoteRot, k)); }
            }
            else
            {
                Vector3 p = s.nozzleDocked && operation.nozzleDock ? operation.nozzleDock.position : s.nozzlePosition;
                Quaternion q = s.nozzleDocked && operation.nozzleDock ? operation.nozzleDock.rotation : s.nozzleRotation;
                if (s.nozzleDocked || (p - transform.position).sqrMagnitude > 4) transform.SetPositionAndRotation(p, q);
                else { float k = 1 - Mathf.Exp(-12 * dt); transform.SetPositionAndRotation(Vector3.Lerp(transform.position, p, k), Quaternion.Slerp(transform.rotation, q, k)); }
            }
            bool want = LocallyHeld ? s.pumpOn && LocalTrigger && operation.HandsOnPhase : s.Spraying;
            SetSpray(want, false);
            sprayLevel = Mathf.MoveTowards(sprayLevel, want ? 1 : 0, dt * 5);
            if (sprayAudio) { sprayAudio.volume = .7f * sprayLevel; if (sprayLevel > .01f && !sprayAudio.isPlaying) sprayAudio.Play(); else if (sprayLevel <= .01f && sprayAudio.isPlaying) sprayAudio.Stop(); }
            if (want && LocallyHeld && holder is XRBaseInputInteractor feedback && Time.unscaledTime >= nextHaptic)
            { nextHaptic = Time.unscaledTime + .1f; feedback.SendHapticImpulse(.12f, .08f); }
        }
        void SetSpray(bool on, bool force)
        {
            if (on == spraying && !force) return; spraying = on;
            foreach (var ps in new[] { stream, mist })
            {
                if (!ps) continue; var e = ps.emission; e.enabled = on;
                if (on && !ps.isPlaying) ps.Play();
            }
        }
    }
}
