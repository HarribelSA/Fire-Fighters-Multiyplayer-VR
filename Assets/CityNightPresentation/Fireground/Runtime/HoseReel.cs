using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace CityNightPresentation.Fireground
{
    // Crank handle of the hose reel, built on XRI's own interactable/selection (Grab via the
    // existing Input Actions: controller grip or tracked-hand pinch/grab). Only actual rotation of
    // the grabbing hand around the axle produces payout; holding Grab alone does nothing.
    // Reel axle = this transform's local Z. Drum rotation is derived from the replicated payout.
    public sealed class HoseReel : XRBaseInteractable
    {
        public FiregroundOperation operation;
        public Transform drum, coil, knob;
        public AudioSource ratchet;
        public float coreRadius = .1f, fullRadius = .23f;
        [Tooltip("Hand must stay within this distance of the crank knob (interaction range).")]
        public float interactionRange = .3f;
        public float minimumHandRadius = .04f;
        [Tooltip("+1: counter-clockwise (seen from the crank side) pays hose out.")]
        public float payoutDirection = 1;

        float lastAngle, pending, nextSend, shownPayout = -1, ratchetLevel, lastShown;
        bool hasAngle;

        public override bool IsSelectableBy(IXRSelectInteractor interactor)
        {
            if (!base.IsSelectableBy(interactor) || !operation || !operation.LocalMayOperatePump || !operation.HandsOnPhase) return false;
            if (interactor is NearFarInteractor n && n.selectionRegion.Value == NearFarInteractor.Region.Far) return false;
            return !knob || Vector3.Distance(interactor.GetAttachTransform(this).position, knob.position) <= interactionRange;
        }
        protected override void OnSelectEntered(SelectEnterEventArgs args) { base.OnSelectEntered(args); hasAngle = false; pending = 0; }
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
            if (hasAngle) pending += Mathf.DeltaAngle(lastAngle * Mathf.Rad2Deg, angle * Mathf.Rad2Deg) * Mathf.Deg2Rad * payoutDirection;
            lastAngle = angle; hasAngle = true;
            if (Time.unscaledTime >= nextSend) Flush();
        }
        void Flush()
        {
            nextSend = Time.unscaledTime + .05f;
            if (Mathf.Abs(pending) > .002f && operation) operation.RequestCrank(pending);
            pending = 0;
        }

        public void ShowPayout(float payout, float maxPayout)
        {
            if (shownPayout < 0) shownPayout = payout;
            shownPayout = Mathf.Lerp(shownPayout, payout, 1 - Mathf.Exp(-14 * Time.deltaTime));
            if (drum && operation) drum.localRotation = Quaternion.Euler(0, 0, payoutDirection * shownPayout / operation.payoutPerTurn * 360f);
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
