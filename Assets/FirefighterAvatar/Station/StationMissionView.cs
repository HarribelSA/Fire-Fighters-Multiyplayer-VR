using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FirefighterVR.Station
{
    [DefaultExecutionOrder(260)]
    public sealed class StationMissionView : MonoBehaviour,IXRSelectFilter
    {
        public static StationMissionView Instance {get;private set;}
        public Transform dispatchSeat,chairApproach,driverSeat,passengerSeat,driverApproach,passengerApproach,tablet,tabletHome;
        public XRGrabInteractable tabletGrab;
        public Animator garage;
        public Collider[] curtainColliders;
        public Bounds garageSweep;
        public AudioSource[] alarmAudio;
        public Animator[] alarmAnimators;
        public TMP_Text computerReport,tabletReport,objective;
        public UnityEngine.UI.Button acceptButton,exitChairButton;
        public StationMissionSeatAnchor[] seatTargets;
        StationLocalRig rig;
        TeleportationProvider teleport;
        Transform localSeat;
        Rigidbody tabletBody;
        float nextUI,nextPose,lastGarage=-1;
        double playedAlarm=-1;
        bool pending,busy,hooks;
        bool alarmActive;
        uint automaticChairEpoch;
        AnimationClip[] alarmClips;
        StationSeatKind pendingKind;
        readonly Collider[] overlaps=new Collider[64];
        XRMultiplayer.XRINetworkPlayer[] players=System.Array.Empty<XRMultiplayer.XRINetworkPlayer>();
        StationSessionFlow Flow=>StationSessionFlow.Instance;
        public StationRole LocalRole=>Flow?Flow.MissionLocalRole:StationRole.None;
        public bool canProcess=>isActiveAndEnabled;
        public bool Process(IXRSelectInteractor interactor,IXRSelectInteractable interactable)=>Flow&&Flow.MissionSessionActive&&LocalRole==StationRole.Assistant&&Flow.MissionState.stage>=CrewMissionStage.EmergencyAlarm&&Flow.MissionState.stage<CrewMissionStage.Departure;
        void Awake()
        {
            using var timing=TransitionDiagnostics.Measure("station_mission_view_awake");
            Instance=this;
            foreach(var audio in alarmAudio)if(audio){audio.Stop();audio.playOnAwake=false;}
            foreach(var animator in alarmAnimators)if(animator)animator.enabled=false;
            SetAlarmVisuals(false,0);
            if(garage)garage.speed=0;
        }
        void Start()
        {
            using var timing=TransitionDiagnostics.Measure("station_mission_view_start");
            rig=Flow?Flow.localRig:SeatedDispatchLobby.Instance?SeatedDispatchLobby.Instance.rig:null;
            if(rig)teleport=rig.origin.GetComponentInChildren<TeleportationProvider>(true);
            foreach(var target in seatTargets)if(target&&teleport)target.teleportationProvider=teleport;
            tabletBody=tablet?tablet.GetComponent<Rigidbody>():null;
            Wire();
            if(acceptButton)acceptButton.onClick.AddListener(()=>{if(Flow&&localSeat==dispatchSeat)Flow.AcceptDispatch();});
            if(exitChairButton)exitChairButton.onClick.AddListener(LeaveChair);
            players=FindObjectsByType<XRMultiplayer.XRINetworkPlayer>(FindObjectsSortMode.None);
        }
        void OnEnable(){Instance=this;Wire();}
        void Wire(){if(hooks||!teleport)return;teleport.locomotionEnded+=TeleportEnded;if(tabletGrab)tabletGrab.selectFilters.Add(this);hooks=true;}
        public bool PlaceDriverAtDispatch()
        {
            if(!rig)rig=Flow?Flow.localRig:SeatedDispatchLobby.Instance?SeatedDispatchLobby.Instance.rig:null;
            if(!rig||!dispatchSeat)return false;
            StationSeatPlacement.Place(rig.origin,dispatchSeat,rig.avatar);rig.StationSeated=true;localSeat=dispatchSeat;return true;
        }
        public void ConfirmSeatedDispatch()
        {
            var flow=Flow;if(!flow||!flow.MissionSessionActive||LocalRole!=StationRole.Driver||localSeat!=dispatchSeat||!flow.StationMissionReady)return;
            var state=flow.MissionState;if(state.stage!=CrewMissionStage.EmergencyAlarm||state.driverChair||automaticChairEpoch==state.epoch)return;
            automaticChairEpoch=state.epoch;flow.NotifyStationSeat(StationSeatKind.DispatchChair);
        }
        public bool Eligible(StationSeatKind kind)
        {
            if(!Flow||!Flow.MissionSessionActive||!Flow.StationMissionReady||pending||busy)return false;
            var s=Flow.MissionState;
            if(rig&&rig.permanentlySeatedDriver&&LocalRole==StationRole.Driver)return false;
            if(kind==StationSeatKind.DispatchChair)return LocalRole==StationRole.Driver&&(s.stage==CrewMissionStage.EmergencyAlarm||s.stage==CrewMissionStage.DispatchReview)&&!s.driverChair;
            if(kind==StationSeatKind.DriverTruck)return LocalRole==StationRole.Driver&&s.stage==CrewMissionStage.MissionAccepted;
            if(kind==StationSeatKind.AssistantTruck)return LocalRole==StationRole.Assistant&&s.CanBoardAssistant(Flow.SharedTime,Flow.garageTravelSeconds);
            return LocalRole==StationRole.Driver&&s.stage==CrewMissionStage.DispatchReview&&localSeat==dispatchSeat;
        }
        public bool QueueSeat(StationSeatKind kind)
        {if(!Eligible(kind))return false;pending=true;pendingKind=kind;return true;}
        void TeleportEnded(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider provider)
        {if(pending&&!busy){pending=false;StartCoroutine(EnterSeat(pendingKind));}}
        IEnumerator EnterSeat(StationSeatKind kind)
        {
            TransitionDiagnostics.Begin("seat_"+kind);
            if(!rig)yield break;busy=true;rig.SeatTransition=true;
            var fade=StationViewFade.Instance;if(fade)yield return fade.Cover();
            var anchor=kind==StationSeatKind.DispatchChair?dispatchSeat:kind==StationSeatKind.DriverTruck?driverSeat:passengerSeat;
            if(!anchor){rig.SeatTransition=false;busy=false;if(fade)yield return fade.Reveal();yield break;}
            var cc=rig.origin.GetComponent<CharacterController>();if(cc)cc.enabled=false;
            StationSeatPlacement.Place(rig.origin,anchor,rig.avatar);
            localSeat=anchor;rig.StationSeated=true;rig.SeatTransition=false;
            UpdateAvatar();Physics.SyncTransforms();
            while(!StationSpawnPlacement.TrackingReady(rig.origin))yield return null;
            if(Flow)Flow.NotifyStationSeat(kind);
            // Keep the local view covered until the authority confirms this action.
            // A close-door command arriving during passenger boarding must not leave
            // a locally seated passenger while the shared mission rejects boarding.
            while(Flow&&Flow.MissionSessionActive&&Flow.StationMissionReady)
            {
                var state=Flow.MissionState;
                bool accepted=kind==StationSeatKind.DispatchChair?state.driverChair:kind==StationSeatKind.DriverTruck?state.driverTruck:state.assistantTruck;
                if(accepted)break;
                if(kind==StationSeatKind.AssistantTruck&&!state.CanBoardAssistant(Flow.SharedTime,Flow.garageTravelSeconds))
                {
                    localSeat=null;rig.StationSeated=false;rig.avatar.Seated=false;
                    StationSpawnPlacement.Place(rig.origin,passengerApproach);if(cc)cc.enabled=true;rig.RememberStationSpawn(passengerApproach.position);break;
                }
                yield return null;
            }
            if(fade)yield return fade.Reveal();busy=false;
        }
        public void LeaveChair(){if(Eligible(StationSeatKind.LeaveChair))StartCoroutine(ExitChair());}
        IEnumerator ExitChair()
        {
            busy=true;rig.SeatTransition=true;var fade=StationViewFade.Instance;if(fade)yield return fade.Cover();
            localSeat=null;rig.StationSeated=false;rig.avatar.Seated=false;
            StationSpawnPlacement.Place(rig.origin,chairApproach);var cc=rig.origin.GetComponent<CharacterController>();if(cc)cc.enabled=true;
            rig.RememberStationSpawn(chairApproach.position);rig.SeatTransition=false;if(Flow)Flow.NotifyStationSeat(StationSeatKind.LeaveChair);
            if(fade)yield return fade.Reveal();busy=false;
        }
        void Update()
        {
            // Additive station initialization can precede the local rig binding.
            // Retry the existing binding instead of leaving seat completion unhooked.
            if(!teleport){rig=Flow?Flow.localRig:SeatedDispatchLobby.Instance?SeatedDispatchLobby.Instance.rig:null;if(rig){teleport=rig.origin.GetComponentInChildren<TeleportationProvider>(true);foreach(var target in seatTargets)if(target&&teleport)target.teleportationProvider=teleport;Wire();}}
            var flow=Flow;var state=flow&&flow.MissionSessionActive?flow.MissionState:default;
            bool mission=flow&&flow.MissionSessionActive&&state.stage!=CrewMissionStage.None;
            ConfirmSeatedDispatch();
            if(mission&&state.stage==CrewMissionStage.MissionAccepted&&LocalRole==StationRole.Driver&&localSeat==dispatchSeat&&!busy)StartCoroutine(EnterSeat(StationSeatKind.DriverTruck));
            float fraction=mission?state.GarageFraction(flow.SharedTime,flow.garageTravelSeconds):0;
            if(garage&&Mathf.Abs(fraction-lastGarage)>.0001f){garage.Play("Door03_Open",0,fraction);garage.Update(0);lastGarage=fraction;}
            foreach(var c in curtainColliders)if(c)c.enabled=fraction<.999f;
            bool sounding=mission&&AlarmRequired(state.stage);
            if(sounding&&playedAlarm!=state.alarmAt)
            {
                playedAlarm=state.alarmAt;
                foreach(var audio in alarmAudio)if(audio&&audio.clip){audio.loop=true;audio.time=(float)((flow.SharedTime-state.alarmAt)%audio.clip.length);audio.Play();}
            }
            if(!sounding&&alarmActive)foreach(var audio in alarmAudio)if(audio)audio.Stop();
            if(sounding||alarmActive)SetAlarmVisuals(sounding,sounding?flow.SharedTime-state.alarmAt:0);
            alarmActive=sounding;
            bool assistant=mission&&LocalRole==StationRole.Assistant;
            if(tabletBody)tabletBody.isKinematic=!assistant||(tabletGrab&&tabletGrab.isSelected)||state.stage<CrewMissionStage.EmergencyAlarm;
            if(mission&&tablet&&state.stage>=CrewMissionStage.EmergencyAlarm)
            {
                if(assistant&&Time.unscaledTime>=nextPose)
                {nextPose=Time.unscaledTime+.1f;flow.UpdateDispatchTablet(tablet.position,tablet.rotation,tabletGrab&&tabletGrab.isSelected);}
                else if(!assistant){tablet.SetPositionAndRotation(Vector3.Lerp(tablet.position,state.tabletPosition,1-Mathf.Exp(-18*Time.deltaTime)),Quaternion.Slerp(tablet.rotation,state.tabletRotation,1-Mathf.Exp(-18*Time.deltaTime)));}
            }
            if(Time.unscaledTime<nextUI){UpdateAvatar();return;}nextUI=Time.unscaledTime+.1f;
            if(mission&&!state.garageOpen&&fraction>.001f&&flow.MissionAuthority&&!DoorClearToClose())flow.SafetyReopenGarage();
            foreach(var target in seatTargets)if(target)target.SetVisible(Eligible(target.kind));
            string report=BuildReport(flow,state);
            if(computerReport)computerReport.text=report;if(tabletReport)tabletReport.text=report;
            if(objective)objective.text=Objective(state,flow)+(rig&&rig.SeatedComfortWarning.Length>0?"\n"+rig.SeatedComfortWarning:"");
            if(acceptButton)acceptButton.interactable=mission&&LocalRole==StationRole.Driver&&localSeat==dispatchSeat&&state.CanAccept;
            if(exitChairButton)exitChairButton.gameObject.SetActive(mission&&Eligible(StationSeatKind.LeaveChair));
            UpdateAvatar();
        }
        public static string Objective(CrewMissionState s,StationSessionFlow f)
        {
            if(f&&(s.stage==CrewMissionStage.None||s.stage==CrewMissionStage.Exploration))return f.StationMissionStatus;
            if(f&&f.SoloMissionActive&&s.stage>=CrewMissionStage.EmergencyAlarm&&s.stage<CrewMissionStage.Departure)return (f.SoloHumanRole==StationRole.Assistant?"HUMAN ASSISTANT: TABLET · GARAGE UP · PASSENGER BOARDING":s.stage==CrewMissionStage.EmergencyAlarm?"DRIVER: REMAIN SEATED · WAIT FOR BOT TABLET":s.stage==CrewMissionStage.DispatchReview?s.tabletPresented?"DRIVER: ACCEPT DISPATCH":"DRIVER: WAIT FOR BOT TABLET PRESENTATION":"DRIVER: AUTOMATIC SEATED TRANSFER")+"\nBOT: "+f.SoloTaskStatus;
            switch(s.stage)
            {
                case CrewMissionStage.Exploration:return "EXPLORE THE STATION · TELEPORTATION ONLY\nDispatch standby: "+Mathf.Max(0,Mathf.CeilToInt((float)(s.deadline-(f?f.SharedTime:0))))+" s";
                case CrewMissionStage.EmergencyAlarm:return "EMERGENCY DISPATCH\nDRIVER: SEATED DISPATCH · ASSISTANT: TAKE TABLET";
                case CrewMissionStage.DispatchReview:return s.tabletPresented?"DRIVER: REVIEW AND ACCEPT MISSION":"ASSISTANT: PRESENT TABLET TO THE SEATED DRIVER";
                case CrewMissionStage.MissionAccepted:return "DRIVER: AUTOMATIC SEATED TRANSFER TO TRUCK";
                case CrewMissionStage.DriverInTruck:return s.CanBoardAssistant(f.SharedTime,f.garageTravelSeconds)?"ASSISTANT: BOARD THE PASSENGER SEAT":"ASSISTANT: PRESS GREEN · WAIT FOR GARAGE TO OPEN";
                case CrewMissionStage.Departure:return "CREW SEATED · DEPARTURE STAND BY";
                case CrewMissionStage.CityTransition:return "PREPARING CITY · REMAIN SEATED";
                default:return "STATION EXPLORATION";
            }
        }
        public static string BuildReport(StationSessionFlow f,CrewMissionState s)
        {
            if(!f||!f.incident||s.stage<CrewMissionStage.EmergencyAlarm)return "EMERGENCY OPERATIONS\n\n"+(f?f.StationMissionStatus:"Mission coordinator unavailable")+"\n\nDriver · Hand Tracking / G29\nAssistant · Quest Controllers or Solo Bot";
            var d=f.incident;return "<b>EMERGENCY DISPATCH</b>\n"+d.incidentType+"\n"+d.location+"\nPRIORITY: "+d.priority+" · "+s.dispatchTime+"\n\n"+d.description+"\n\nUNIT: "+d.assignedVehicle+"\nDRIVER / ASSISTANT ASSIGNED\nSTATUS: "+s.stage+"\n"+(s.tabletPresented?"TABLET PRESENTED":"ASSISTANT: PRESENT TABLET");
        }
        void UpdateAvatar(){// Only while the station is the active scene: in the city/fireground the truck seat or standing pose owns the body.
            if(localSeat&&rig&&rig.avatar&&UnityEngine.SceneManagement.SceneManager.GetActiveScene()!=gameObject.scene){localSeat=null;return;}
            if(localSeat&&rig&&rig.avatar)rig.avatar.SetSeat(localSeat);}
        void LateUpdate()=>UpdateAvatar();
        public bool DoorClearToClose()
        {
            var box=garageSweep;box.Expand(.3f);
            if(rig&&rig.origin.Camera&&box.Contains(rig.origin.Camera.transform.position))return false;
            foreach(var p in players)if(p&&p.IsSpawned&&p.head&&box.Contains(p.head.position))return false;
            int count=gameObject.scene.GetPhysicsScene().OverlapBox(box.center,box.extents,overlaps,Quaternion.identity,~0,QueryTriggerInteraction.Ignore);
            if(count==overlaps.Length)return false;
            for(int i=0;i<count;i++)if(overlaps[i]&&overlaps[i].attachedRigidbody&&(!garage||!overlaps[i].transform.IsChildOf(garage.transform)))return false;
            return true;
        }
        public void GarageOpen(){if(Flow)Flow.CommandGarage(true);}
        public void GarageClose(){if(Flow)Flow.CommandGarage(false);}
        // Bell sound and flashing lights run together from the alarm until the truck departs.
        public static bool AlarmRequired(CrewMissionStage stage)=>stage>=CrewMissionStage.EmergencyAlarm&&stage<CrewMissionStage.Departure;
        void SetAlarmVisuals(bool active,double elapsed)
        {
            // Sample the original looping bell, lens and point-light clips against
            // shared mission time. No second flashing clock or network event.
            if(alarmClips==null||alarmClips.Length!=alarmAnimators.Length)
            {
                alarmClips=new AnimationClip[alarmAnimators.Length];
                for(int i=0;i<alarmAnimators.Length;i++)if(alarmAnimators[i]&&alarmAnimators[i].runtimeAnimatorController)
                {var clips=alarmAnimators[i].runtimeAnimatorController.animationClips;if(clips.Length>0)alarmClips[i]=clips[0];}
            }
            for(int i=0;i<alarmAnimators.Length;i++)
            {
                var animator=alarmAnimators[i];if(!animator||!alarmClips[i])continue;
                animator.enabled=true;animator.speed=0;
                var clip=alarmClips[i];float phase=active?(float)((elapsed/Mathf.Max(.001f,clip.length))%1):0;
                animator.Play(clip.name,0,phase);animator.Update(0);
                var light=animator.GetComponent<Light>();if(light)light.enabled=active;
                animator.enabled=active;
            }
        }
        void OnDisable()
        {
            if(busy&&StationViewFade.Instance)StationViewFade.Instance.HoldManual(false);
            foreach(var audio in alarmAudio)if(audio)audio.Stop();
            SetAlarmVisuals(false,0);alarmActive=false;
            if(hooks){if(teleport)teleport.locomotionEnded-=TeleportEnded;if(tabletGrab)tabletGrab.selectFilters.Remove(this);hooks=false;}
            if(rig&&(!Flow||!Flow.MissionSessionActive||Flow.Lobby.Value.Phase==StationPhase.Blocked)){localSeat=null;rig.StationSeated=rig.SeatTransition=false;}
            if(Instance==this)Instance=null;
        }
    }
}
