using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace CityNightPresentation.Fireground
{
    // XRI grab transformer that runs after the general grab transformer and keeps the nozzle's
    // hose coupling inside the paid-out hose length. The player's hand is never moved.
    public sealed class NozzleLeash : XRBaseGrabTransformer
    {
        public FiregroundOperation operation;
        public HoseNozzle nozzle;
        public float Tension { get; private set; }

        public override void Process(XRGrabInteractable grabInteractable, XRInteractionUpdateOrder.UpdatePhase updatePhase, ref Pose targetPose, ref Vector3 localScale)
        {
            if (!operation || !operation.hoseOutlet || !nozzle) return;
            Vector3 anchor = operation.hoseOutlet.position;
            float limit = Mathf.Max(.25f, operation.PredictedPayout * .97f);
            Vector3 offset = targetPose.rotation * Vector3.Scale(nozzle.HoseAttachLocal, nozzle.transform.lossyScale);
            Vector3 coupling = targetPose.position + offset, d = coupling - anchor; float dist = d.magnitude;
            Tension = Mathf.Clamp01((dist - limit * .92f) / Mathf.Max(.05f, limit * .08f));
            if (dist > limit && operation.LocalHoldsNozzle) { operation.RequestPullOut(dist / .97f + .35f); limit = Mathf.Max(limit, operation.PredictedPayout * .97f); }
            if (dist > limit) targetPose.position = anchor + d / dist * limit - offset;
        }
    }
}
