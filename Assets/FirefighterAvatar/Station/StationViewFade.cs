using System.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FirefighterVR.Station
{
    // One local effect using the existing headset overlay and existing teleport provider.
    [DefaultExecutionOrder(190)]
    public sealed class StationViewFade : MonoBehaviour
    {
        public XROrigin origin;
        public Renderer overlay;
        public Material fadeMaterial;
        [Min(.01f)] public float sceneFadeOut = .35f, sceneFadeIn = .45f;
        [Min(.01f)] public float teleportFadeOut = .12f, teleportFadeIn = .18f;
        public float Alpha { get; private set; }
        public bool BlackRendered { get; private set; }
        public bool ManualHold { get; private set; }
        public bool SceneHold { get; private set; }
        // Held by the mission loading screen until its outro has finished (independent of Cover/Reveal).
        public bool ExternalHold { get; set; }
        public bool IsBusy => ManualHold || SceneHold || ExternalHold || teleporting || Alpha > .001f;
        public bool SceneBusy => ManualHold || SceneHold || ExternalHold || (!lastTeleport && Alpha > .001f);
        public static StationViewFade Instance { get; private set; }
        TeleportationProvider teleport;
        MaterialPropertyBlock block;
        bool teleporting;
        float originalDelay;
        void Awake()
        {
            Instance = this; block = new MaterialPropertyBlock();
            if (fadeMaterial) overlay.sharedMaterial = fadeMaterial;
            teleport = origin.GetComponentInChildren<TeleportationProvider>(true);
            if (teleport)
            {
                originalDelay = teleport.delayTime;
                // XRI's own preparation stage delays movement; no replacement provider.
                teleport.delayTime = Mathf.Max(originalDelay, teleportFadeOut + .03f);
                teleport.locomotionStateChanged += TeleportState;
                teleport.beforeStepLocomotion += BeforeTeleportStep;
                teleport.locomotionEnded += TeleportEnded;
            }
            RenderPipelineManager.endCameraRendering += CameraRendered;
            Apply();
        }
        public void HoldScene(bool hold) => SceneHold = hold;
        public void HoldManual(bool hold) => ManualHold = hold;
        public IEnumerator Cover()
        {
            TransitionDiagnostics.Mark("fade_out_requested");
            ManualHold = true;
            while (!BlackRendered) yield return null;
        }
        public IEnumerator Reveal()
        {
            TransitionDiagnostics.Mark("fade_in_requested");
            ManualHold = false;
            while (Alpha > .001f) yield return null;
        }
        void TeleportState(LocomotionProvider provider, LocomotionState state)
        {
            if (state == LocomotionState.Preparing) teleporting = true;
            else if (state == LocomotionState.Idle) teleporting = false;
        }
        void BeforeTeleportStep(LocomotionProvider provider)
        {
            if(provider is TeleportationProvider p){var local=origin.GetComponent<StationLocalRig>();if(local)local.GuardStationTeleport(p);}
            // Fully cover the transformation even if a frame took longer than the fade.
            Alpha = 1; Apply();
        }
        void TeleportEnded(LocomotionProvider provider) => teleporting = false;
        // Safety net: a transition coroutine that was interrupted (scene unloaded, session ended) must never
        // leave the player blind. Manual holds are released after maxManualBlackSeconds of full black, and a
        // teleport that never reported its end after a few seconds. Network/loading holds (SceneHold,
        // ExternalHold) are driven every frame by their owners and are left alone.
        [Min(3)] public float maxManualBlackSeconds = 12, maxTeleportSeconds = 3;
        float manualSince = -1, teleportSince = -1;
        void Watchdog()
        {
            bool manualOnly = ManualHold && !SceneHold && !ExternalHold;
            if (manualOnly && Alpha >= .999f) { if (manualSince < 0) manualSince = Time.unscaledTime; else if (Time.unscaledTime - manualSince > maxManualBlackSeconds) { Debug.LogWarning("[ViewFade] Black screen held too long by an interrupted transition; revealing.", this); ManualHold = false; manualSince = -1; } }
            else manualSince = -1;
            if (teleporting) { if (teleportSince < 0) teleportSince = Time.unscaledTime; else if (Time.unscaledTime - teleportSince > maxTeleportSeconds) { teleporting = false; teleportSince = -1; } }
            else teleportSince = -1;
        }
        void Update()
        {
            Watchdog();
            if(teleport)teleport.delayTime=Mathf.Max(originalDelay,teleportFadeOut+.03f);
            bool scene = SceneHold || ManualHold || ExternalHold;
            bool cover = scene || teleporting;
            float duration = scene ? (cover ? sceneFadeOut : sceneFadeIn) : (teleporting ? teleportFadeOut : sceneFadeIn);
            // Remember the shorter reveal after a teleport while retaining scene reveal timing.
            if (!cover && lastTeleport) duration = teleportFadeIn;
            float previousAlpha=Alpha;
            // A single loading hitch must not skip the whole comfortable reveal in one frame.
            Alpha = Mathf.MoveTowards(Alpha, cover ? 1 : 0, Mathf.Min(Time.unscaledDeltaTime,.05f) / Mathf.Max(.01f, duration));
            if(!cover&&previousAlpha>0&&Alpha==0){TransitionDiagnostics.Mark("fade_in_complete");TransitionDiagnostics.End();}
            if (cover) lastTeleport = !scene;
            if (Alpha < .999f) BlackRendered = false;
            Apply();
        }
        bool lastTeleport;
        void LateUpdate() => Apply();
        void Apply()
        {
            if (!overlay || !origin || !origin.Camera) return;
            overlay.gameObject.SetActive(Alpha > .0001f);
            float z = Mathf.Max(.1f, origin.Camera.nearClipPlane + .05f);
            overlay.transform.localPosition = Vector3.forward * z;
            overlay.transform.localRotation = Quaternion.identity;
            overlay.transform.localScale = Vector3.one * z * 12;
            block ??= new MaterialPropertyBlock();
            block.SetColor("_BaseColor", new Color(0, 0, 0, Alpha)); overlay.SetPropertyBlock(block);
        }
        void CameraRendered(ScriptableRenderContext context, Camera camera)
        { if (origin && camera == origin.Camera && Alpha >= .999f){if(!BlackRendered)TransitionDiagnostics.Mark("black_frame_rendered");BlackRendered = true;} }
        void OnDestroy()
        {
            RenderPipelineManager.endCameraRendering -= CameraRendered;
            if (teleport) { teleport.delayTime = originalDelay; teleport.locomotionStateChanged -= TeleportState; teleport.beforeStepLocomotion -= BeforeTeleportStep; teleport.locomotionEnded -= TeleportEnded; }
            if (Instance == this) Instance = null;
        }
    }
}
