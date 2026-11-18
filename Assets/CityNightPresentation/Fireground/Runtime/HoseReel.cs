using FirefighterVR.Station;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace CityNightPresentation.Fireground
{
    // Crank handle of the hose reel, built on XRI's own interactable/selection (Grab via the
    // existing Input Actions: controller grip or tracked-hand pinch/grab). Only actual rotation of
    // the grabbing hand around the axle moves the hose; holding Grab alone does nothing.
    // Reel axle = this transform's local Z.
    //  - The crank always turns CLOCKWISE (as seen from the crank side): during the operation it pays the
    //    hose out, during recovery it rewinds it. fullPayoutTurns turns move the whole hose (FiregroundOperation).
    //  - Counter-clockwise, or past the end of travel, is felt as resistance on the controller.
    //  - While grabbed, the avatar's hand is drawn ON the crank knob.
    public sealed class HoseReel : XRBaseInteractable
    {
        public FiregroundOperation operation;
        public Transform drum, coil, knob;
        public AudioSource ratchet;
        public float coreRadius = .1f, fullRadius = .23f;
        [Tooltip("Hand must stay within this distance of the crank knob (interaction range).")]
        public float interactionRange = .3f;
        public float minimumHandRadius = .04f;
        [Tooltip("Legacy (pre clockwise-only crank); kept for serialized data.")]
        public float payoutDirection = 1;

        [Tooltip("Controller push-back when the crank is turned the wrong way or at the end of travel.")]
        [Range(0, 1)] public float resistanceAmplitude = .95f;
        [Min(.02f)] public float resistancePulse = .09f;
        [Tooltip("Seconds for the avatar hand to move onto the crank knob when grabbed.")]
        [Min(.01f)] public float handSnapSeconds = .12f;
        float lastAngle, pending, nextSend, shownPayout = -1, ratchetLevel, lastShown, nextResist, drumTurns, handWeight;
        bool hasAngle, handLeft; float clickTravel;

        // +1 when the crank knob is on the reel's +Z side. Positive local angle = clockwise seen from +Z.
        float CrankSide => knob && transform.InverseTransformPoint(knob.position).z < 0 ? -1 : 1;

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor) || !operation || !operation.LocalMayOperatePump || !operation.HandsOnPhase) return false;
            if (interactor is NearFarInteractor n && n.selectionRegion.Value == NearFarInteractor.Region.Far) return false;
            return !knob || Vector3.Distance(interactor.GetAttachTransform(this).position, knob.position) <= interactionRange;
        }
        protected override void OnSelectEntered(SelectEnterEventArgs args)
        {
            base.OnSelectEntered(args); hasAngle = false; pending = 0;
            handLeft = args.interactorObject is XRBaseInteractor b && b.handedness == InteractorHandedness.Left;
        }
        protected override void OnSelectExited(SelectExitEventArgs args) { base.OnSelectExited(args); Flush(); hasAngle = false; }

        public override void ProcessInteractable(XRInteractionUpdateOrder.UpdatePhase updatePhase)
        {
            base.ProcessInteractable(updatePhase);
            if (updatePhase != XRInteractionUpdateOrder.UpdatePhase.Dynamic || !isSelected) return;
            var interactor = firstInteractorSelecting;
            Vector3 hand = interactor.GetAttachTransform(this).position;
            if (knob && Vector3.Distance(hand, knob.position) > interactionRange * 1.6f && interactionManager)
            { interactionManager.SelectExit(interactor, this); return; }
            Vector3 p = transform.InverseTransformPoint(hand);
            float radius = new Vector2(p.x, p.y).magnitude;
            if (radius < minimumHandRadius) { hasAngle = false; return; }
            float angle = Mathf.Atan2(p.y, p.x);
            if (hasAngle)
            {
                float step = Mathf.DeltaAngle(lastAngle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad;
                float clockwise = step * CrankSide;
                // Clockwise moves the hose the way the current job needs (+ pay out / - rewind).
                int job = operation.AllowedCrankDirection;
                var input = interactor as XRBaseInputInteractor;
                bool wrongWay = clockwise < 0, blocked = job == 0 || operation.CrankBlocked(job);
                if (Mathf.Abs(step) > .0005f && (wrongWay || blocked))
                {
                    // Counter-clockwise, or the hose is already fully out / fully in:
                    // nothing moves and the controller pushes back with a strong, steady buzz.
                    pending = 0; clickTravel = 0;
                    if (Mathf.Abs(step) > .004f && input && Time.unscaledTime >= nextResist) { nextResist = Time.unscaledTime + resistancePulse * .8f; input.SendHapticImpulse(resistanceAmplitude, resistancePulse); }
                }
                else if (clockwise > 0)
                {
                    pending += clockwise * job;
                    // Ratchet feel: one haptic click per ~20 degrees of crank travel.
                    clickTravel += clockwise; if (clickTravel > .35f && input) { clickTravel = 0; input.SendHapticImpulse(.45f, .03f); }
                }
            }
            lastAngle = angle; hasAngle = true;
            if (Time.unscaledTime >= nextSend) Flush();
        }
        void Flush()
        {
            nextSend = Time.unscaledTime + .05f;
            if (Mathf.Abs(pending) > .002f && operation) operation.RequestCrank(pending);
            pending = 0;
        }

        // Avatar hand drawn on the crank knob while grabbed (blends in/out).
        void LateUpdate()
        {
            var flow = StationSessionFlow.Instance; var rig = flow ? flow.localRig : null; var avatar = rig ? rig.avatar : null;
            if (!avatar || !knob) return;
            bool grabbed = isSelected;
            float target = grabbed ? 1 : 0;
            if (handWeight <= 0 && !grabbed) return;
            handWeight = Mathf.MoveTowards(handWeight, target, Time.unscaledDeltaTime / handSnapSeconds);
            var source = handLeft ? avatar.leftSource : avatar.rightSource;
            if (!source) return;
            var offset = handLeft ? avatar.leftGripOffset : avatar.rightGripOffset;
            var wrist = source.rotation * Quaternion.Euler(handLeft ? avatar.leftWristEuler : avatar.rightWristEuler);
            // Same grip as the real controller, translated so the controller sits on the knob.
            Vector3 position = knob.position + (source.TransformPoint(offset) - source.position);
            avatar.SetVisualGrip(handLeft, position, wrist, handWeight);
        }

        public void ShowPayout(float payout, float maxPayout)
        {
            if (shownPayout < 0) shownPayout = payout;
            float before = shownPayout;
            shownPayout = Mathf.Lerp(shownPayout, payout, 1 - Mathf.Exp(-14 * Time.deltaTime));
            // The drum always turns clockwise (both paying out and rewinding), matching the crank hand.
            if (operation) drumTurns += Mathf.Abs(shownPayout - before) / Mathf.Max(.01f, operation.payoutPerTurn);
            if (drum) drum.localRotation = Quaternion.Euler(0, 0, CrankSide * drumTurns * 360f);
            if (coil)
            {
                float remaining = Mathf.Clamp01(1 - payout / Mathf.Max(1, maxPayout));
                float r = Mathf.Sqrt(coreRadius * coreRadius + (fullRadius * fullRadius - coreRadius * coreRadius) * remaining);
                var s = coil.localScale; coil.localScale = new Vector3(r * 2, s.y, r * 2);
            }
            if (ratchet)
            {
                float speed = Mathf.Abs(shownPayout - lastShown) / Mathf.Max(.001f, Time.deltaTime); lastShown = shownPayout;
                ratchetLevel = Mathf.MoveTowards(ratchetLevel, Mathf.Clamp01(speed * .8f), Time.deltaTime * 6);
                ratchet.volume = ratchetLevel * .6f; ratchet.pitch = .8f + Mathf.Clamp01(speed * .3f) * .6f;
                if (ratchetLevel > .01f && !ratchet.isPlaying) ratchet.Play(); else if (ratchetLevel <= .01f && ratchet.isPlaying) ratchet.Stop();
            }
        }
    }
}
