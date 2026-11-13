using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Unity.XR.CoreUtils;
using UnityEngine.SceneManagement;

namespace FirefighterVR.Station
{
    [DefaultExecutionOrder(200)]
    public sealed class StationLocalRig : MonoBehaviour
    {
        public XROrigin origin;
        public FirefighterAvatarDriver avatar;
        public Transform leftController,rightController;
        public XRInputModalityManager modality;
        public StationLobbyView view;
        public Renderer fadeRenderer;
        public GameObject[] sampleInstructionUI;
        readonly List<XRHandSubsystem> hands=new List<XRHandSubsystem>();
        readonly List<XRDisplaySubsystem> displays=new List<XRDisplaySubsystem>();
        readonly Dictionary<LocomotionProvider,bool> originals=new Dictionary<LocomotionProvider,bool>();
        readonly Dictionary<GameObject,bool> hiddenStationRoots=new Dictionary<GameObject,bool>();
        readonly List<GameObject> stationRoots=new List<GameObject>();
        XRHandSkeletonDriver[] leftSkeletons,rightSkeletons;
        bool blockedRecoveryDone,blockedStationHidden;
        bool InSeatedLobby=>SeatedDispatchLobby.Instance?SeatedDispatchLobby.Instance.InLobby:SceneManager.GetActiveScene().path=="Assets/S1Test.unity";
        MaterialPropertyBlock fadeBlock;
        uint placedEpoch;
        bool placed,inputWasAvailable;
        bool returningToLobby;
        Vector3 lobbyPosition;
        Quaternion lobbyRotation;
        double nextInputCheck;
        float fade;
        public bool InputAvailable { get; private set; }
        public string InputStatus { get; private set; }="Waiting for an active XR runtime.";
        public StationRole LocalRole { get; private set; }
        public bool IsWindowsPcvrHost => Application.platform==RuntimePlatform.WindowsEditor||Application.platform==RuntimePlatform.WindowsPlayer;
        void Awake()
        {
            lobbyPosition=origin.transform.position;lobbyRotation=origin.transform.rotation;
            foreach(var p in origin.GetComponentsInChildren<LocomotionProvider>(true))originals[p]=p.enabled;
            leftSkeletons=modality.leftHand.GetComponentsInChildren<XRHandSkeletonDriver>(true);rightSkeletons=modality.rightHand.GetComponentsInChildren<XRHandSkeletonDriver>(true);
            if(InSeatedLobby){foreach(var p in originals.Keys)p.enabled=false;var resetter=origin.GetComponent<XRMultiplayer.CharacterResetter>();if(resetter)resetter.enabled=false;}
            fadeBlock=new MaterialPropertyBlock();SetFade(0);
        }
        void Update()
        {
            var flow=StationSessionFlow.Instance;
            bool connected=flow&&flow.IsSpawned;
            var state=connected?flow.Lobby.Value:StationLobbyState.Initial;
            LocalRole=connected?state.RoleOf(flow.NetworkManager.LocalClientId):StationRole.None;
            if(Time.unscaledTimeAsDouble>=nextInputCheck){nextInputCheck=Time.unscaledTimeAsDouble+.25;CheckInput();if(connected&&inputWasAvailable&&!InputAvailable&&state.IsReady(flow.NetworkManager.LocalClientId)&&state.Editable)flow.SetLocalReady(false);inputWasAvailable=InputAvailable;}
            if(connected&&state.Phase==StationPhase.AwaitingSpawn&&(!placed||placedEpoch!=state.Epoch))PlaceInStation(flow,state);
            if(connected&&state.Phase==StationPhase.Blocked)RecoverBlockedTransition(flow);
            else{blockedRecoveryDone=false;blockedStationHidden=false;}
            bool transitioning=state.Phase==StationPhase.Fading||state.Phase==StationPhase.Loading||state.Phase==StationPhase.AwaitingSpawn;
            float target=transitioning?1:0;
            if(state.Phase==StationPhase.Fading)target=Mathf.Clamp01(1-(float)(state.Deadline-flow.SharedTime)/.65f);
            SetFade(Mathf.MoveTowards(fade,target,Time.unscaledDeltaTime*4));
            bool frozen=InSeatedLobby||returningToLobby||transitioning||state.Phase==StationPhase.Blocked||fade>.01f||(state.Phase==StationPhase.StationReady&&!InputAvailable);
            foreach(var entry in originals)
            {
                if(!entry.Key)continue;bool allowed=entry.Value&&!frozen;
                if(LocalRole==StationRole.Driver){string type=entry.Key.GetType().Name;if(entry.Key is ContinuousMoveProvider||entry.Key is GrabMoveProvider||type.Contains("TurnProvider")||type.Contains("JumpProvider")||type.Contains("ClimbProvider")||type.Contains("TwoHandedGrab"))allowed=false;}
                if(entry.Key.enabled!=allowed)entry.Key.enabled=allowed;
            }
            UpdateAvatarHands();
        }
        void CheckInput()
        {
            // The assigned role persists independently of USB devices and platform.
            // Driver readiness requires Windows PCVR; G29 presence is a separate capability.
            if(LocalRole==StationRole.Driver&&!IsWindowsPcvrHost){InputAvailable=false;InputStatus="Driver requires Windows PCVR with tracked hands. This device is running "+Application.platform+".";return;}
            displays.Clear();SubsystemManager.GetSubsystems(displays);bool display=displays.Any(d=>d.running);
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);bool headTracked=head.isValid&&head.TryGetFeatureValue(CommonUsages.isTracked,out bool tracked)&&tracked;
            if(!display||!headTracked){InputAvailable=false;InputStatus="No tracked headset from the active XR runtime. Connect the headset before Ready.";return;}
            if(LocalRole==StationRole.Driver)
            {hands.Clear();SubsystemManager.GetSubsystems(hands);InputAvailable=hands.Any(h=>h.running&&h.leftHand.isTracked&&h.rightHand.isTracked);InputStatus=InputAvailable?"Live XR hand tracking available — teleportation role.":"The active runtime is not providing both tracked hands. Enable supported PCVR hand tracking before Ready.";}
            else if(LocalRole==StationRole.Assistant)
            {bool Good(XRNode node){var d=InputDevices.GetDeviceAtXRNode(node);return d.isValid&&d.TryGetFeatureValue(CommonUsages.isTracked,out bool t)&&t;}InputAvailable=Good(XRNode.LeftHand)&&Good(XRNode.RightHand)&&XRInputModalityManager.currentInputMode.Value==XRInputModalityManager.InputMode.MotionController;InputStatus=InputAvailable?"Tracked controllers available — existing joystick / turning actions preserved.":"Both motion controllers must be tracked before Ready.";}
            else{InputAvailable=false;InputStatus="Select Driver (hands) or Assistant (controllers).";}
        }
        void UpdateAvatarHands()
        {
            if(!avatar)return;
            bool handMode=XRInputModalityManager.currentInputMode.Value==XRInputModalityManager.InputMode.TrackedHand;
            Transform Skeleton(XRHandSkeletonDriver[] drivers){foreach(var d in drivers)if(d&&d.isActiveAndEnabled)return d.rootTransform;return null;}
            var left=handMode?Skeleton(leftSkeletons):leftController;var right=handMode?Skeleton(rightSkeletons):rightController;
            if(left)avatar.leftSource=left;if(right)avatar.rightSource=right;
        }
        void PlaceInStation(StationSessionFlow flow,StationLobbyState state)
        {
            var station=StationSceneSetup.Instance;if(!station)return;var anchor=station.Spawn(LocalRole);if(!anchor)return;
            foreach(var entry in hiddenStationRoots)if(entry.Key)entry.Key.SetActive(entry.Value);hiddenStationRoots.Clear();
            if(!station.ValidDestination(anchor.position,origin,false)){InputStatus="Spawn clearance check failed. Select Play to retry after clearing the destination.";flow.ReportSpawnFailure(state.Epoch);return;}
            // Keep usable lobby UI until this client has a valid destination.
            flow.HideLobbyEnvironment();
            if(station.inspectionCamera)station.inspectionCamera.SetActive(false);
            var cc=origin.GetComponent<CharacterController>();bool ccWas=cc&&cc.enabled;if(cc)cc.enabled=false;
            // Preserve tracked camera pose and the existing camera-floor offset.
            origin.MatchOriginUpCameraForward(Vector3.up,anchor.forward);
            Vector3 horizontal=Vector3.ProjectOnPlane(origin.Camera.transform.position-origin.transform.position,Vector3.up);
            origin.transform.position=anchor.position-horizontal;
            if(cc)cc.enabled=ccWas;
            avatar.walkableGroundSurfaces=station.walkableSurfaces;
            SceneManager.SetActiveScene(station.gameObject.scene);
            foreach(var g in sampleInstructionUI)if(g)g.SetActive(false);
            var resetter=origin.GetComponent<XRMultiplayer.CharacterResetter>();if(resetter)resetter.enabled=false;
            placed=true;placedEpoch=state.Epoch;
            Physics.SyncTransforms();flow.AcknowledgeSpawn(state.Epoch);
        }
        void RecoverBlockedTransition(StationSessionFlow flow)
        {
            if(!blockedRecoveryDone)
            {
                var lobby=gameObject.scene;if(lobby.IsValid()&&lobby.isLoaded)SceneManager.SetActiveScene(lobby);
                flow.RestoreLobbyEnvironment();RestoreLobbyRig();
                if(SeatedDispatchLobby.Instance)SeatedDispatchLobby.Instance.ResetSeatPlacement();
                blockedRecoveryDone=true;
            }
            // The station has no NetworkObjects. Hide its local presentation without
            // independently loading/unloading it or disturbing the retained session.
            var station=SceneManager.GetSceneByPath("Assets/FireStation/Scenes/Sample.unity");
            if(!blockedStationHidden&&station.IsValid()&&station.isLoaded)
            {stationRoots.Clear();station.GetRootGameObjects(stationRoots);foreach(var g in stationRoots){if(!hiddenStationRoots.ContainsKey(g))hiddenStationRoots[g]=g.activeSelf;g.SetActive(false);}blockedStationHidden=true;}
        }
        void SetFade(float alpha)
        {
            fade=alpha;if(!fadeRenderer)return;fadeRenderer.gameObject.SetActive(alpha>.001f);
            if(alpha>.001f){float z=Mathf.Max(.05f,origin.Camera.nearClipPlane+.03f);fadeRenderer.transform.localPosition=new Vector3(0,0,z);fadeRenderer.transform.localScale=Vector3.one*z*6;fadeBlock.SetColor("_BaseColor",new Color(0,0,0,alpha));fadeRenderer.SetPropertyBlock(fadeBlock);}
        }
        public void ResetToLobby()
        {
            if((placed||hiddenStationRoots.Count>0||SceneManager.GetSceneByPath("Assets/FireStation/Scenes/Sample.unity").isLoaded)&&!returningToLobby){returningToLobby=true;StartCoroutine(ReturnAfterShutdown());return;}
            RestoreLobbyRig();
        }
        System.Collections.IEnumerator ReturnAfterShutdown()
        {
            // Local cleanup is only allowed after the network session has ended.
            // The shared station transition always uses NGO scene management.
            yield return null;
            while(Unity.Netcode.NetworkManager.Singleton&&Unity.Netcode.NetworkManager.Singleton.IsListening)yield return null;
            var station=SceneManager.GetSceneByPath("Assets/FireStation/Scenes/Sample.unity");
            var lobby=SceneManager.GetSceneByPath("Assets/S1Test.unity");
            if(lobby.IsValid()&&lobby.isLoaded)SceneManager.SetActiveScene(lobby);
            if(station.IsValid()&&station.isLoaded){var unload=SceneManager.UnloadSceneAsync(station);if(unload!=null)yield return unload;}
            var flow=StationSessionFlow.Instance;if(flow)flow.RestoreLobbyEnvironment();
            var cc=origin.GetComponent<CharacterController>();bool enabled=cc&&cc.enabled;if(cc)cc.enabled=false;
            origin.transform.SetPositionAndRotation(lobbyPosition,lobbyRotation);if(cc)cc.enabled=enabled;
            hiddenStationRoots.Clear();returningToLobby=false;RestoreLobbyRig();if(SeatedDispatchLobby.Instance)SeatedDispatchLobby.Instance.ResetSeatPlacement();
        }
        void RestoreLobbyRig()
        {
            placed=false;placedEpoch=0;LocalRole=StationRole.None;SetFade(0);if(avatar)avatar.walkableGroundSurfaces=System.Array.Empty<Collider>();
            foreach(var p in originals)if(p.Key)p.Key.enabled=p.Value&&!InSeatedLobby;
            var resetter=origin?origin.GetComponent<XRMultiplayer.CharacterResetter>():null;if(resetter)resetter.enabled=!InSeatedLobby;
        }
    }
}
