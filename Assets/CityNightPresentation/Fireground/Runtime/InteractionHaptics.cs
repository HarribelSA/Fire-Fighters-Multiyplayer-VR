using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace CityNightPresentation.Fireground
{
    // Controller pulse when a fireground control is pressed / grabbed / released.
    [RequireComponent(typeof(XRBaseInteractable))]
    public sealed class InteractionHaptics : MonoBehaviour
    {
        [Range(0, 1)] public float pressAmplitude = .7f, releaseAmplitude = .25f;
        public float duration = .08f;
        void Awake()
        {
            var i = GetComponent<XRBaseInteractable>();
            i.selectEntered.AddListener(a => Pulse(a.interactorObject, pressAmplitude));
            i.selectExited.AddListener(a => Pulse(a.interactorObject, releaseAmplitude));
        }
        void Pulse(IXRInteractor interactor, float amplitude) { if (amplitude > 0 && interactor is XRBaseInputInteractor input) input.SendHapticImpulse(amplitude, duration); }
    }
}
