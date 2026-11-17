using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
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
        public bool permanentlySeatedDriver=true;
        public bool releaseUnusedAssetsOnReturn=true;
        public enum AssistantMovement { SmoothAndTeleport,Smooth,Teleport }
        public AssistantMovement assistantMovement=AssistantMovement.SmoothAndTeleport;
        StationRole previousProviderRole;
        bool providersWereFrozen=true;
        StationPhase diagnosticPhase;
        uint diagnosticCityEpoch;
        XRRayInteractor[] controllerTeleportRays;
        XRInteractorLineVisual[] controllerTeleportLines;
        // Comfort settings record which providers the player prefers; the movement policy still decides.
        public void SetPreferredEnabled(LocomotionProvider provider,bool on){if(provider&&originals.ContainsKey(provider))originals[provider]=on;}
        public void SetAssistantMovement(int mode){if(LocalRole!=StationRole.Assistant)return;assistantMovement=(AssistantMovement)Mathf.Clamp(mode,0,2);PlayerPrefs.SetInt("Firefighter.AssistantMovement",(int)assistantMovement);}
        public static bool MovementAllowed(LocomotionProvider provider,StationRole role,AssistantMovement mode,bool frozen,bool original,bool seatedDriver)
        {
            if(frozen||role==StationRole.Driver&&seatedDriver)return false;
            string type=provider.GetType().Name;
            if(provider is GrabMoveProvider||type.Contains("TwoHandedGrab")||type.Contains("ClimbProvider")||type.Contains("JumpProvider"))return false;
            if(role==StationRole.Assistant){if(provider is ContinuousMoveProvider)return mode!=AssistantMovement.Teleport;if(provider is TeleportationProvider)return mode!=AssistantMovement.Smooth;}
            return original;
        }
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
        uint blackEpoch;
        uint cityBlackEpoch;
        Coroutine spawnRoutine;
        public bool StationSeated {get;set;}
        // Seated in a vehicle: body locked to the seat; head tracking stays free, locomotion off.
        public bool VehicleSeated {get;set;}
        // Fireground pump operator: Driver uses the Meta controllers instead of hand tracking.
        public bool ControllersOnly {get;set;}
        public bool SeatTransition {get;set;}
        public string SeatedComfortWarning
        {get{if(LocalRole!=StationRole.Driver||!avatar||!avatar.Seated||!origin||!origin.Camera)return "";var offset=Quaternion.Inverse(Quaternion.Euler(0,avatar.SeatYaw,0))*(origin.Camera.transform.position-avatar.SeatPosition);var eye=avatar.standingEye-avatar.standingHips;eye.y=avatar.SeatHipHeight+StationSeatPlacement.EyeAboveHips(avatar);return Vector3.ProjectOnPlane(offset-eye,Vector3.up).magnitude>.45f||Mathf.Abs(offset.y-eye.y)>.35f?"PLEASE RETURN TO YOUR COMFORTABLE SEATED POSITION":"";}}
        readonly Dictionary<Collider,bool> lobbyResetVolumes=new Dictionary<Collider,bool>();
        Vector3 lastSafeGround;
        bool safeGroundKnown;
        public bool InputAvailable { get; private set; }
        public string InputStatus { get; private set; }="Waiting for an active XR runtime.";
        public StationRole LocalRole { get; private set; }
        public bool IsWindowsPcvrHost => Application.platform==RuntimePlatform.WindowsEditor||Application.platform==RuntimePlatform.WindowsPlayer;
        void Awake()
        {
            lobbyPosition=origin.transform.position;lobbyRotation=origin.transform.rotation;
            assistantMovement=(AssistantMovement)Mathf.Clamp(PlayerPrefs.GetInt("Firefighter.AssistantMovement",0),0,2);
            foreach(var p in origin.GetComponentsInChildren<LocomotionProvider>(true))originals[p]=p.enabled;
            controllerTeleportRays=origin.GetComponentsInChildren<XRRayInteractor>(true).Where(r=>r.name.Contains("Teleport")&&!r.GetComponentInParent<StationHandTeleportInput>()).ToArray();controllerTeleportLines=controllerTeleportRays.Select(r=>r.GetComponent<XRInteractorLineVisual>()).ToArray();
            leftSkeletons=modality.leftHand.GetComponentsInChildren<XRHandSkeletonDriver>(true);rightSkeletons=modality.rightHand.GetComponentsInChildren<XRHandSkeletonDriver>(true);
            if(avatar)avatar.BindTrackedFingerSources(leftSkeletons,rightSkeletons);
            if(InSeatedLobby){foreach(var p in originals.Keys)p.enabled=false;var resetter=origin.GetComponent<XRMultiplayer.CharacterResetter>();if(resetter)resetter.enabled=false;}
            // Held objects follow the hand only: no joystick push/pull or rotate of the grab point, so the
            // player must physically walk to where an object has to go.
            foreach(var attach in origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Attachment.InteractionAttachController>(true))attach.useManipulationInput=false;
            fadeBlock=new MaterialPropertyBlock();SetFade(0);
            foreach(var root in gameObject.scene.GetRootGameObjects())foreach(var destroyer in root.GetComponentsInChildren<XRMultiplayer.NetworkObjectDestroyer>(true))foreach(var c in destroyer.GetComponentsInChildren<Collider>(true))if(c.isTrigger)lobbyResetVolumes[c]=c.enabled;
        }
        void Update()
        {
            TransitionDiagnostics.Tick();
            var flow=StationSessionFlow.Instance;
            bool connected=flow&&flow.IsSpawned;
            if(!connected)blackEpoch=0;
            var state=connected?flow.Lobby.Value:StationLobbyState.Initial;
            if(connected&&state.Phase!=diagnosticPhase){diagnosticPhase=state.Phase;if(state.Phase==StationPhase.Fading)TransitionDiagnostics.Begin("multiplayer_station");TransitionDiagnostics.Mark("network_phase:"+state.Phase);}
            if(connected&&flow.MissionState.stage==CrewMissionStage.CityTransition&&diagnosticCityEpoch!=flow.MissionState.epoch){diagnosticCityEpoch=flow.MissionState.epoch;TransitionDiagnostics.Begin("multiplayer_city");}
            LocalRole=flow&&flow.SoloMissionActive?flow.SoloHumanRole:connected?state.RoleOf(flow.NetworkManager.LocalClientId):SeatedDispatchLobby.Instance?SeatedDispatchLobby.Instance.SelectedProfile:StationRole.None;
            if(Time.unscaledTimeAsDouble>=nextInputCheck){nextInputCheck=Time.unscaledTimeAsDouble+.25;CheckInput();if(connected&&inputWasAvailable&&!InputAvailable&&state.IsReady(flow.NetworkManager.LocalClientId)&&state.Editable)flow.SetLocalReady(false);inputWasAvailable=InputAvailable;}
            if(connected&&state.Phase==StationPhase.AwaitingSpawn&&(!placed||placedEpoch!=state.Epoch)&&spawnRoutine==null)spawnRoutine=StartCoroutine(PlaceInStation(flow,state));
            if(connected&&state.Phase==StationPhase.Blocked)RecoverBlockedTransition(flow);
            else{blockedRecoveryDone=false;blockedStationHidden=false;}
            bool transitioning=state.Phase==StationPhase.Fading||state.Phase==StationPhase.Loading||state.Phase==StationPhase.AwaitingSpawn||(connected&&flow.DrivingTransition.Value);
            var viewFade=StationViewFade.Instance;
            if(viewFade){viewFade.HoldScene(transitioning);fade=viewFade.SceneBusy?viewFade.Alpha:0;
                if(connected&&state.Phase==StationPhase.Fading&&viewFade.BlackRendered&&blackEpoch!=state.Epoch){blackEpoch=state.Epoch;flow.AcknowledgeBlack(state.Epoch);}}
            if(connected&&viewFade&&viewFade.BlackRendered&&flow.CrewMission.Value.stage==CrewMissionStage.CityTransition&&cityBlackEpoch!=flow.CrewMission.Value.epoch){cityBlackEpoch=flow.CrewMission.Value.epoch;flow.AcknowledgeCityBlack(cityBlackEpoch);}
            else SetFade(Mathf.MoveTowards(fade,transitioning?1:0,Time.unscaledDeltaTime/(transitioning?.35f:.45f)));
            bool frozen=StationSeated||VehicleSeated||SeatTransition||InSeatedLobby||returningToLobby||transitioning||state.Phase==StationPhase.Blocked||fade>.01f||(state.Phase==StationPhase.StationReady&&!InputAvailable);
            bool inStation=StationSceneSetup.Instance&&SceneManager.GetActiveScene()==StationSceneSetup.Instance.gameObject.scene;
            if(inStation)DisableLobbyResetVolumes();else if(InSeatedLobby)foreach(var c in lobbyResetVolumes)if(c.Key)c.Key.enabled=c.Value;
            foreach(var entry in originals)
            {
                if(!entry.Key)continue;bool allowed=MovementAllowed(entry.Key,LocalRole,assistantMovement,frozen,entry.Value,permanentlySeatedDriver);
                string type=entry.Key.GetType().Name;
                if(LocalRole==StationRole.Assistant&&!frozen)
                {
                    if(type.Contains("TurnProvider")){if(previousProviderRole!=LocalRole||providersWereFrozen)entry.Key.enabled=entry.Value;continue;}
                }
                if(entry.Key.enabled!=allowed)entry.Key.enabled=allowed;
            }
            previousProviderRole=LocalRole;
            providersWereFrozen=frozen;
            bool teleportAllowed=LocalRole==StationRole.Assistant&&!frozen&&assistantMovement!=AssistantMovement.Smooth;
            for(int i=0;i<controllerTeleportRays.Length;i++){if(controllerTeleportRays[i])controllerTeleportRays[i].enabled=teleportAllowed;if(controllerTeleportLines[i])controllerTeleportLines[i].enabled=teleportAllowed;}
            if(avatar)avatar.HandTrackingProfile=LocalRole==StationRole.Driver&&!ControllersOnly;
            if(SeatedComfortWarning.Length>0)InputStatus=SeatedComfortWarning;
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
            if(LocalRole==StationRole.Driver&&ControllersOnly)
            {bool Held(XRNode node){var d=InputDevices.GetDeviceAtXRNode(node);return d.isValid&&d.TryGetFeatureValue(CommonUsages.isTracked,out bool t)&&t;}InputAvailable=Held(XRNode.LeftHand)||Held(XRNode.RightHand)||XRInputModalityManager.currentInputMode.Value==XRInputModalityManager.InputMode.MotionController;InputStatus=InputAvailable?"PUMP OPERATOR · Meta controllers active":"Pick up the Meta controllers";return;}
            if(LocalRole==StationRole.Driver&&SceneManager.GetSceneByName(StationSessionFlow.CitySceneName).isLoaded)
            {InputAvailable=true;InputStatus="City driving: tracked headset active. G29 controls the truck; hand tracking remains independent.";return;}
            if(LocalRole==StationRole.Driver)
            {hands.Clear();SubsystemManager.GetSubsystems(hands);bool station=StationSceneSetup.Instance&&SceneManager.GetActiveScene()==StationSceneSetup.Instance.gameObject.scene;
                InputAvailable=hands.Any(h=>h.running&&h.rightHand.isTracked);
                InputStatus=InputAvailable?"SEATED DRIVER · Hand UI / Grab · G29 driving · locomotion disabled":"Track the right hand for seated UI. The other hand may be occluded.";}
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
        System.Collections.IEnumerator PlaceInStation(StationSessionFlow flow,StationLobbyState state)
        {
            try {
            TransitionDiagnostics.Mark("tracking_and_black_wait");
            yield return null;
            while(flow&&flow.IsSpawned&&flow.Lobby.Value.Phase==StationPhase.AwaitingSpawn&&(!StationSpawnPlacement.TrackingReady(origin)||(StationViewFade.Instance&&!StationViewFade.Instance.BlackRendered)))yield return null;
            if(!flow||!flow.IsSpawned||flow.Lobby.Value.Phase!=StationPhase.AwaitingSpawn||flow.Lobby.Value.Epoch!=state.Epoch)yield break;
            TransitionDiagnostics.Mark("tracking_ready:station");
            var station=StationSceneSetup.Instance;if(!station)yield break;var anchor=station.Spawn(state.RoleOf(flow.NetworkManager.LocalClientId));if(!anchor)yield break;
            foreach(var entry in hiddenStationRoots)if(entry.Key)entry.Key.SetActive(entry.Value);hiddenStationRoots.Clear();
            if(!station.ValidDestination(anchor.position,origin,false)){InputStatus="Spawn clearance check failed. Select Play to retry after clearing the destination.";flow.ReportSpawnFailure(state.Epoch);yield break;}
            // Keep usable lobby UI until this client has a valid destination.
            flow.HideLobbyEnvironment();
            if(station.inspectionCamera)station.inspectionCamera.SetActive(false);
            RememberStationSpawn(anchor.position);StationSpawnPlacement.Place(origin,anchor);
            bool driver=state.RoleOf(flow.NetworkManager.LocalClientId)==StationRole.Driver&&permanentlySeatedDriver;
            var missionView=StationMissionView.Instance;
            if(driver){if(!missionView||!missionView.PlaceDriverAtDispatch()){flow.ReportSpawnFailure(state.Epoch);yield break;}}
            avatar.walkableGroundSurfaces=station.walkableSurfaces;
            SceneManager.SetActiveScene(station.gameObject.scene);
            foreach(var g in sampleInstructionUI)if(g)g.SetActive(false);
            var resetter=origin.GetComponent<XRMultiplayer.CharacterResetter>();if(resetter)resetter.enabled=false;
            int stable=0;
            while(stable<3){yield return null;if(!flow||!flow.IsSpawned||flow.Lobby.Value.Phase!=StationPhase.AwaitingSpawn||flow.Lobby.Value.Epoch!=state.Epoch)yield break;
                if(!StationSpawnPlacement.TrackingReady(origin)){stable=0;continue;}
                bool aligned=driver?Vector3.Distance(origin.Camera.transform.position,StationSeatPlacement.EyeTarget(missionView.dispatchSeat,avatar))<.15f:StationSpawnPlacement.Aligned(origin,anchor);
                if(!aligned){if(driver)missionView.PlaceDriverAtDispatch();else StationSpawnPlacement.Place(origin,anchor);stable=0;}else stable++;}
            if(!driver&&!station.ValidDestination(StationSpawnPlacement.GroundPosition(origin),origin,false)){flow.ReportSpawnFailure(state.Epoch);yield break;}
            placed=true;placedEpoch=state.Epoch;Physics.SyncTransforms();TransitionDiagnostics.Mark("placement_verified:station");flow.AcknowledgeSpawn(state.Epoch);TransitionDiagnostics.Mark("local_spawn_ack_sent");
            } finally {spawnRoutine=null;}
        }
        public void RememberStationSpawn(Vector3 p){lastSafeGround=p;safeGroundKnown=true;DisableLobbyResetVolumes();}
        void DisableLobbyResetVolumes(){foreach(var c in lobbyResetVolumes)if(c.Key)c.Key.enabled=false;}
        public void GuardStationTeleport(TeleportationProvider provider)
        {
            var station=StationSceneSetup.Instance;
            if(!station||SceneManager.GetActiveScene()!=station.gameObject.scene||StationSeated||SeatTransition)return;
            var target=provider.positionTransformation.targetPosition;
            if(station.ValidDestination(target,origin,false)){lastSafeGround=target;safeGroundKnown=true;return;}
            var anchor=station.Spawn(LocalRole==StationRole.None?StationRole.Driver:LocalRole);
            if(!safeGroundKnown&&anchor){lastSafeGround=anchor.position;safeGroundKnown=true;}
            if(safeGroundKnown){provider.positionTransformation.targetPosition=lastSafeGround;Debug.LogWarning("Station rejected a queued non-station teleport to "+target+"; retained "+lastSafeGround+". Check lobby reset/destroyer triggers.",this);}
        }
        void RecoverBlockedTransition(StationSessionFlow flow)
        {
            var viewFade=StationViewFade.Instance;
            if(!blockedRecoveryDone&&viewFade&&!viewFade.BlackRendered){viewFade.HoldManual(true);return;}
            if(!blockedRecoveryDone)
            {
                var lobby=gameObject.scene;if(lobby.IsValid()&&lobby.isLoaded)SceneManager.SetActiveScene(lobby);
                flow.RestoreLobbyEnvironment();RestoreLobbyRig();
                if(SeatedDispatchLobby.Instance)SeatedDispatchLobby.Instance.ResetSeatPlacement();
                blockedRecoveryDone=true;
                if(viewFade)viewFade.HoldManual(false);
            }
            // The station has no NetworkObjects. Hide its local presentation without
            // independently loading/unloading it or disturbing the retained session.
            var station=SceneManager.GetSceneByPath("Assets/FireStation/Scenes/Sample.unity");
            if(!blockedStationHidden&&station.IsValid()&&station.isLoaded)
            {stationRoots.Clear();station.GetRootGameObjects(stationRoots);foreach(var g in stationRoots){if(!hiddenStationRoots.ContainsKey(g))hiddenStationRoots[g]=g.activeSelf;g.SetActive(false);}blockedStationHidden=true;}
        }
        void SetFade(float alpha)
        {
            fade=alpha;if(StationViewFade.Instance)return;if(!fadeRenderer)return;fadeRenderer.gameObject.SetActive(alpha>.001f);
            if(alpha>.001f){float z=Mathf.Max(.05f,origin.Camera.nearClipPlane+.03f);fadeRenderer.transform.localPosition=new Vector3(0,0,z);fadeRenderer.transform.localScale=Vector3.one*z*6;fadeBlock.SetColor("_BaseColor",new Color(0,0,0,alpha));fadeRenderer.SetPropertyBlock(fadeBlock);}
        }
        public void ResetToLobby()
        {
            // The local solo transition already owns this additive station. Delegate cleanup
            // instead of starting a second unload/reposition coroutine after room shutdown.
            var solo=FindFirstObjectByType<FirefighterVR.StationBot.StationSoloBotTest>();
            if(solo&&solo.OwnsStation){solo.ReturnToLobby();return;}
            if((placed||hiddenStationRoots.Count>0||SceneManager.GetSceneByPath("Assets/FireStation/Scenes/Sample.unity").isLoaded)&&!returningToLobby){returningToLobby=true;StartCoroutine(ReturnAfterShutdown());return;}
            RestoreLobbyRig();
        }
        System.Collections.IEnumerator ReturnAfterShutdown()
        {
            var viewFade=StationViewFade.Instance;if(viewFade)yield return viewFade.Cover();
            // Local cleanup is only allowed after the network session has ended.
            // The shared station transition always uses NGO scene management.
            yield return null;
            while(Unity.Netcode.NetworkManager.Singleton&&Unity.Netcode.NetworkManager.Singleton.IsListening)yield return null;
            var station=SceneManager.GetSceneByPath("Assets/FireStation/Scenes/Sample.unity");
            var lobby=SceneManager.GetSceneByPath("Assets/S1Test.unity");
            if(lobby.IsValid()&&lobby.isLoaded)SceneManager.SetActiveScene(lobby);
            var city=SceneManager.GetSceneByName(StationSessionFlow.CitySceneName);
            if(city.IsValid()&&city.isLoaded){var unloadCity=SceneManager.UnloadSceneAsync(city);if(unloadCity!=null)yield return unloadCity;}
            if(station.IsValid()&&station.isLoaded){var unload=SceneManager.UnloadSceneAsync(station);if(unload!=null)yield return unload;}
            var flow=StationSessionFlow.Instance;if(flow)flow.RestoreLobbyEnvironment();
            var cc=origin.GetComponent<CharacterController>();bool enabled=cc&&cc.enabled;if(cc)cc.enabled=false;
            origin.transform.SetPositionAndRotation(lobbyPosition,lobbyRotation);if(cc)cc.enabled=enabled;
            hiddenStationRoots.Clear();returningToLobby=false;RestoreLobbyRig();if(SeatedDispatchLobby.Instance)SeatedDispatchLobby.Instance.ResetSeatPlacement();
            yield return null;
            if(releaseUnusedAssetsOnReturn)yield return TransitionMemory.ReleaseAfterReturn();
            if(viewFade)yield return viewFade.Reveal();
        }
        void RestoreLobbyRig()
        {
            diagnosticCityEpoch=0;diagnosticPhase=StationPhase.Waiting;
            placed=false;placedEpoch=0;cityBlackEpoch=0;StationSeated=SeatTransition=false;LocalRole=StationRole.None;SetFade(0);if(avatar)avatar.walkableGroundSurfaces=System.Array.Empty<Collider>();
            foreach(var p in originals)if(p.Key)p.Key.enabled=p.Value&&!InSeatedLobby;
            var resetter=origin?origin.GetComponent<XRMultiplayer.CharacterResetter>():null;if(resetter)resetter.enabled=!InSeatedLobby;
        }
    }
}
