using System;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

namespace FirefighterVR.Station
{
    public enum CrewMissionStage : byte { None,Exploration,EmergencyAlarm,DispatchReview,MissionAccepted,DriverInTruck,Departure,CityTransition,DrivingMission }
    public enum StationSeatKind : byte { DispatchChair,DriverTruck,AssistantTruck,LeaveChair }
    public struct CrewMissionState : INetworkSerializable,IEquatable<CrewMissionState>
    {
        public CrewMissionStage stage;
        public uint epoch,revision;
        public double deadline,alarmAt,garageAt;
        public float garageFrom;
        public bool garageOpen,garageTask,driverChair,driverTruck,assistantTruck,tabletPicked,tabletPresented,tabletHeld;
        public byte cityBlack,citySeats;
        public Vector3 tabletPosition;
        public Quaternion tabletRotation;
        public FixedString64Bytes dispatchTime;
        public bool Equals(CrewMissionState other)=>revision==other.revision&&epoch==other.epoch&&stage==other.stage;
        public float GarageFraction(double now,float seconds)=>Mathf.MoveTowards(garageFrom,garageOpen?1:0,(float)Math.Max(0,now-garageAt)/Mathf.Max(.1f,seconds));
        public bool CanAccept=>stage==CrewMissionStage.DispatchReview&&driverChair&&tabletPicked&&tabletPresented;
        public bool CanBoardAssistant(double now,float seconds)=>stage==CrewMissionStage.DriverInTruck&&driverTruck&&garageTask&&GarageFraction(now,seconds)>.999f;
        public bool TrySeat(StationSeatKind kind,StationRole role,double now,float doorSeconds,float departureSeconds)
        {
            if(kind==StationSeatKind.DispatchChair&&role==StationRole.Driver&&(stage==CrewMissionStage.EmergencyAlarm||stage==CrewMissionStage.DispatchReview)){driverChair=true;stage=CrewMissionStage.DispatchReview;return true;}
            if(kind==StationSeatKind.LeaveChair&&role==StationRole.Driver&&stage==CrewMissionStage.DispatchReview){driverChair=false;stage=CrewMissionStage.EmergencyAlarm;return true;}
            if(kind==StationSeatKind.DriverTruck&&role==StationRole.Driver&&stage==CrewMissionStage.MissionAccepted){driverTruck=true;driverChair=false;stage=CrewMissionStage.DriverInTruck;return true;}
            if(kind==StationSeatKind.AssistantTruck&&role==StationRole.Assistant&&CanBoardAssistant(now,doorSeconds)){assistantTruck=true;stage=CrewMissionStage.Departure;deadline=now+departureSeconds;return true;}
            return false;
        }
        public bool TryAccept(StationRole role){if(role!=StationRole.Driver||!CanAccept)return false;stage=CrewMissionStage.MissionAccepted;return true;}
        public bool TryGarage(StationRole role,bool open,bool clear,double now,float seconds)
        {
            if(role!=StationRole.Assistant||stage==CrewMissionStage.None||stage>=CrewMissionStage.Departure||(!open&&!clear))return false;
            garageFrom=GarageFraction(now,seconds);garageAt=now;garageOpen=open;if(stage==CrewMissionStage.DriverInTruck)garageTask=open;return true;
        }
        public void NetworkSerialize<T>(BufferSerializer<T> s)where T:IReaderWriter
        {
            s.SerializeValue(ref stage);s.SerializeValue(ref epoch);s.SerializeValue(ref revision);s.SerializeValue(ref deadline);s.SerializeValue(ref alarmAt);s.SerializeValue(ref garageAt);s.SerializeValue(ref garageFrom);
            s.SerializeValue(ref garageOpen);s.SerializeValue(ref garageTask);s.SerializeValue(ref driverChair);s.SerializeValue(ref driverTruck);s.SerializeValue(ref assistantTruck);s.SerializeValue(ref tabletPicked);s.SerializeValue(ref tabletPresented);s.SerializeValue(ref tabletHeld);
            s.SerializeValue(ref cityBlack);s.SerializeValue(ref citySeats);s.SerializeValue(ref tabletPosition);s.SerializeValue(ref tabletRotation);s.SerializeValue(ref dispatchTime);
        }
    }
    public sealed partial class StationSessionFlow
    {
        public StationIncidentDefinition incident;
        [Min(1)] public float explorationSeconds=60,garageTravelSeconds=4,departureSeconds=1.8f;
        public readonly NetworkVariable<CrewMissionState> CrewMission=new NetworkVariable<CrewMissionState>(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        double lastTabletPacket;
        bool cityLoadComplete;
        [SerializeField,TextArea(3,8)] string runtimeMissionDiagnostics;
        float nextMissionDiagnostic;
        string previousMissionDiagnostic;
        FirefighterVR.StationBot.StationSoloBotTest diagnosticSolo;
        public static bool CanBeginStationMission(StationLobbyState lobby,int realPlayers,bool stationReady)=>realPlayers==2&&lobby.BothReady&&lobby.Arrivals==3&&lobby.Phase==StationPhase.StationReady&&stationReady;
        public static bool DispatchDue(CrewMissionState mission,double now)=>mission.stage==CrewMissionStage.Exploration&&now>=mission.deadline;
        public string MissionBlockingReason
        {
            get
            {
                if(SoloMissionActive)return soloFailure.Length>0?soloFailure:"None";
                int count=NetworkManager&&NetworkManager.IsListening?NetworkManager.ConnectedClientsIds.Count:0;
                if(count<2)return "CO-OP WAITING FOR SECOND REAL PLAYER · Play With Bot starts a Solo mission";
                if(count!=2)return "Cooperative station mission requires exactly two real players";
                if(!IsSpawned)return "Existing mission coordinator has not network-spawned";
                var lobby=Lobby.Value;
                if(!lobby.BothReady)return "Waiting for Driver and Assistant assignments / both Ready";
                if(lobby.Arrivals!=3)return "Waiting for station spawn acknowledgements ("+lobby.Arrivals+"/3 bits)";
                if(lobby.Phase!=StationPhase.StationReady)return "Waiting for station Ready completion: "+lobby.Phase;
                var view=StationMissionView.Instance;
                if(!view||!view.isActiveAndEnabled||!view.dispatchSeat||!view.tablet||!view.garage||view.alarmAudio.Length==0)return "Station mission presentation references are not ready";
                if(CrewMission.Value.stage==CrewMissionStage.None&&!Authority)return "Waiting for the existing session authority to start the countdown";
                return "None";
            }
        }
        public string StationMissionStatus
        {
            get
            {
                string block=MissionBlockingReason;if(block!="None")return block+"\nDISPATCH ACCEPTANCE LOCKED · emergency report required";
                var s=MissionState;
                if(s.stage==CrewMissionStage.None)return "Station crew ready · starting synchronized dispatch countdown";
                if(s.stage==CrewMissionStage.Exploration)return (MissionLocalRole==StationRole.Assistant?"ASSISTANT EXPLORATION":"SEATED STANDBY")+" · ALERT IN "+Mathf.Max(0,Mathf.CeilToInt((float)(s.deadline-SharedTime)))+" s"+(MissionLocalRole==StationRole.Driver?" · COMPUTER ACTIVITIES AVAILABLE":"");
                return "DISPATCH CHAIR "+(s.stage==CrewMissionStage.EmergencyAlarm||s.stage==CrewMissionStage.DispatchReview?"AVAILABLE TO DRIVER":"UNAVAILABLE AT THIS STAGE");
            }
        }
        public void UpdateMissionDiagnostics()
        {
            if(Time.unscaledTime<nextMissionDiagnostic)return;nextMissionDiagnostic=Time.unscaledTime+.5f;
            if(!StationSceneSetup.Instance)return;
            if(!diagnosticSolo)diagnosticSolo=FindFirstObjectByType<FirefighterVR.StationBot.StationSoloBotTest>();
            var l=Lobby.Value;var m=MissionState;int count=NetworkManager&&NetworkManager.IsListening?NetworkManager.ConnectedClientsIds.Count:0;
            string session=SoloMissionActive?"SoloHuman"+SoloHumanRole+"Bot"+FirefighterVR.StationBot.StationBotRoles.Opposite(SoloHumanRole):NetworkManager&&NetworkManager.IsListening?"NetworkSession":"OfflineNoNetworkSession";
            int remaining=m.stage==CrewMissionStage.Exploration?Mathf.Max(0,Mathf.CeilToInt((float)(m.deadline-SharedTime))):0;
            string block=MissionBlockingReason;
            runtimeMissionDiagnostics="session="+session+" stage="+m.stage+" phase="+l.Phase+" networkConnectedRealPlayers="+count+" authority="+Authority+" networkSpawned="+IsSpawned+
                " driver="+(l.Driver==StationLobbyState.Unassigned?"none":l.Driver.ToString())+" assistant="+(l.Assistant==StationLobbyState.Unassigned?"none":l.Assistant.ToString())+
                " driverReady="+l.DriverReady+" assistantReady="+l.AssistantReady+" driverSpawnAck="+((l.Arrivals&1)!=0)+" assistantSpawnAck="+((l.Arrivals&2)!=0)+" countdownStarted="+(m.stage!=CrewMissionStage.None)+" remaining="+remaining+
                " soloHumanReady="+SoloMissionActive+" soloBotReady="+(SoloMissionActive&&soloSession&&soloSession.Bot)+" botTask="+SoloTaskStatus+" alarm="+StationMissionView.AlarmRequired(m.stage)+" chair="+StationMissionStatus+" blocking="+block;
            string key=session+"|"+m.stage+"|"+l.Phase+"|"+count+"|"+Authority+"|"+l.Driver+"|"+l.Assistant+"|"+l.DriverReady+"|"+l.AssistantReady+"|"+l.Arrivals+"|"+(remaining/10)+"|"+block;
            if(key!=previousMissionDiagnostic){previousMissionDiagnostic=key;Debug.Log("[StationMission] "+runtimeMissionDiagnostics,this);}
        }
        public CrewMissionState MissionState=>SoloMissionActive?soloMission:CrewMission.Value;
        public bool MissionSessionActive=>SoloMissionActive||IsSpawned;
        public bool StationMissionReady=>SoloMissionActive||IsSpawned&&Lobby.Value.Phase==StationPhase.StationReady;
        public StationRole MissionLocalRole=>SoloMissionActive?SoloHumanRole:IsSpawned?Lobby.Value.RoleOf(NetworkManager.LocalClientId):StationRole.None;
        public bool MissionAuthority=>SoloMissionActive||Authority;
        public bool DrivingMissionReady=>MissionSessionActive&&MissionState.stage==CrewMissionStage.DrivingMission&&MissionState.citySeats==3&&(SoloMissionActive||!DrivingTransition.Value);
        void PublishCrew(CrewMissionState state){state.revision++;if(SoloMissionActive)soloMission=state;else CrewMission.Value=state;}
        bool CrewSender(ulong id,StationRole role)=>Authority&&Connected(id)&&Lobby.Value.Phase==StationPhase.StationReady&&NetworkManager.ConnectedClientsIds.Count==2&&Lobby.Value.RoleOf(id)==role;
        void TickCrewMission()
        {
            if(!SoloMissionActive&&(!Authority||Lobby.Value.Phase!=StationPhase.StationReady||NetworkManager.ConnectedClientsIds.Count!=2))return;
            var state=MissionState;var view=StationMissionView.Instance;
            if(!SoloMissionActive&&(state.stage==CrewMissionStage.None||state.epoch!=Lobby.Value.Epoch))
            {
                bool presentationReady=view&&view.isActiveAndEnabled&&view.dispatchSeat&&view.tablet&&view.garage&&view.alarmAudio.Length>0;
                if(!CanBeginStationMission(Lobby.Value,NetworkManager.ConnectedClientsIds.Count,presentationReady))return;
                state=new CrewMissionState{stage=CrewMissionStage.Exploration,epoch=Lobby.Value.Epoch,deadline=SharedTime+explorationSeconds,garageAt=SharedTime,tabletRotation=Quaternion.identity};
                if(view&&view.tablet){var home=view.tabletHome?view.tabletHome:view.tablet;state.tabletPosition=home.position;state.tabletRotation=home.rotation;}
                PublishCrew(state);
            }
            else if(DispatchDue(state,SharedTime))
            {
                state.stage=CrewMissionStage.EmergencyAlarm;state.alarmAt=SharedTime;state.dispatchTime=DateTime.UtcNow.ToString("HH:mm:ss 'UTC'");PublishCrew(state);
            }
            else if(state.stage==CrewMissionStage.Departure&&SharedTime>=state.deadline)
            {
                if(SoloMissionActive){state.stage=CrewMissionStage.CityTransition;state.cityBlack=state.citySeats=0;PublishCrew(state);StartCoroutine(LoadSoloCity());return;}
                state.stage=CrewMissionStage.CityTransition;state.cityBlack=state.citySeats=0;cityLoadComplete=false;
                DrivingTransition.Value=true;cityLoadDeadline=SharedTime+125;PublishCrew(state);
            }
            else if(state.stage==CrewMissionStage.CityTransition)
            {
                if(SoloMissionActive)return;
                if(state.cityBlack==3&&!drivingRequested&&!cityLoadIssued)RequestDrivingMissionTransition();
                if(cityLoadComplete&&state.citySeats==3)
                {
                    var city=FindFirstObjectByType<CityNightPresentation.Mission.FireTruckMission>();
                    if(city&&city.IsSpawned&&city.controller&&city.controller.Initialized&&city.route&&city.route.waypoints.Length>1)
                    {state.stage=CrewMissionStage.DrivingMission;DrivingTransition.Value=false;PublishCrew(state);}
                }
            }
        }
        public void NotifyStationSeat(StationSeatKind kind){if(SoloMissionActive){var s=soloMission;if(s.TrySeat(kind,SoloHumanRole,SharedTime,garageTravelSeconds,departureSeconds))PublishCrew(s);}else if(IsSpawned)CrewSeatRpc(kind);}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void CrewSeatRpc(StationSeatKind kind,RpcParams p=default)
        {
            var state=CrewMission.Value;var id=p.Receive.SenderClientId;
            var role=Lobby.Value.RoleOf(id);if(!CrewSender(id,role)||role==StationRole.None||!state.TrySeat(kind,role,SharedTime,garageTravelSeconds,departureSeconds))return;
            PublishCrew(state);
        }
        public void AcceptDispatch(){if(SoloMissionActive){var s=soloMission;if(s.TryAccept(SoloHumanRole))PublishCrew(s);}else if(IsSpawned)AcceptDispatchRpc();}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void AcceptDispatchRpc(RpcParams p=default)
        {var state=CrewMission.Value;if(!CrewSender(p.Receive.SenderClientId,StationRole.Driver)||!state.TryAccept(StationRole.Driver))return;PublishCrew(state);}
        public void CommandGarage(bool open){if(SoloMissionActive){if(!soloBotPress&&SoloHumanRole!=StationRole.Assistant)return;var s=soloMission;var v=StationMissionView.Instance;if(s.TryGarage(StationRole.Assistant,open,open||v&&v.DoorClearToClose(),SharedTime,garageTravelSeconds))PublishCrew(s);}else if(IsSpawned)GarageRpc(open);}
        public void SafetyReopenGarage()
        {if(!MissionAuthority)return;var s=MissionState;s.garageFrom=s.GarageFraction(SharedTime,garageTravelSeconds);s.garageAt=SharedTime;s.garageOpen=true;s.garageTask=false;PublishCrew(s);}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void GarageRpc(bool open,RpcParams p=default)
        {
            if(!CrewSender(p.Receive.SenderClientId,StationRole.Assistant))return;
            var state=CrewMission.Value;var view=StationMissionView.Instance;
            if(!state.TryGarage(StationRole.Assistant,open,open||view&&view.DoorClearToClose(),SharedTime,garageTravelSeconds))return;
            PublishCrew(state);
        }
        public void UpdateDispatchTablet(Vector3 position,Quaternion rotation,bool held)
        {if(SoloMissionActive){if(SoloHumanRole!=StationRole.Assistant||SharedTime-lastTabletPacket<.045||soloMission.stage<CrewMissionStage.EmergencyAlarm||soloMission.stage>=CrewMissionStage.CityTransition||!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z)||!float.IsFinite(rotation.x)||!float.IsFinite(rotation.y)||!float.IsFinite(rotation.z)||!float.IsFinite(rotation.w)||position.y<-.5f||position.y>16||Mathf.Abs(position.x)>35||Mathf.Abs(position.z)>35)return;var v=StationMissionView.Instance;if(!v)return;lastTabletPacket=SharedTime;var s=soloMission;RecordTablet(ref s,position,rotation,held,v);PublishCrew(s);}else if(IsSpawned)TabletPoseRpc(position,rotation,held);}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void TabletPoseRpc(Vector3 position,Quaternion rotation,bool held,RpcParams p=default)
        {
            if(!CrewSender(p.Receive.SenderClientId,StationRole.Assistant)||!float.IsFinite(position.x)||!float.IsFinite(position.y)||!float.IsFinite(position.z)||!float.IsFinite(rotation.x)||!float.IsFinite(rotation.y)||!float.IsFinite(rotation.z)||!float.IsFinite(rotation.w)||SharedTime-lastTabletPacket<.045)return;
            var state=CrewMission.Value;if(state.stage<CrewMissionStage.EmergencyAlarm||state.stage>=CrewMissionStage.CityTransition)return;
            var view=StationMissionView.Instance;if(!view||position.y<-.5f||position.y>16||Mathf.Abs(position.x)>35||Mathf.Abs(position.z)>35)return;
            lastTabletPacket=SharedTime;RecordTablet(ref state,position,rotation,held,view);
            PublishCrew(state);
        }
        static void RecordTablet(ref CrewMissionState state,Vector3 position,Quaternion rotation,bool held,StationMissionView view)
        {
            state.tabletPosition=position;state.tabletRotation=rotation.normalized;state.tabletHeld=held;if(held)state.tabletPicked=true;
            if(held&&state.driverChair&&view.dispatchSeat&&(position-(view.dispatchSeat.position+Vector3.up*1.2f)).sqrMagnitude<1.5f*1.5f&&Vector3.Dot(rotation*Vector3.up,(view.dispatchSeat.position+Vector3.up*1.35f-position).normalized)>.15f)state.tabletPresented=true;
        }
        public void AcknowledgeCityBlack(uint epoch){if(IsSpawned)CityBlackRpc(epoch);}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void CityBlackRpc(uint epoch,RpcParams p=default)
        {
            if(!Authority||CrewMission.Value.stage!=CrewMissionStage.CityTransition||CrewMission.Value.epoch!=epoch||!Connected(p.Receive.SenderClientId))return;
            var state=CrewMission.Value;var role=Lobby.Value.RoleOf(p.Receive.SenderClientId);if(role==StationRole.Driver)state.cityBlack|=1;else if(role==StationRole.Assistant)state.cityBlack|=2;else return;PublishCrew(state);
        }
        public void AcknowledgeCitySeat(uint epoch){if(SoloMissionActive){var s=soloMission;if(s.epoch==epoch&&s.stage==CrewMissionStage.CityTransition){s.citySeats|=(byte)(SoloHumanRole==StationRole.Driver?1:2);PublishCrew(s);}}else if(IsSpawned)CitySeatRpc(epoch);}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void CitySeatRpc(uint epoch,RpcParams p=default)
        {
            if(!Authority||CrewMission.Value.stage!=CrewMissionStage.CityTransition||CrewMission.Value.epoch!=epoch||!Connected(p.Receive.SenderClientId))return;
            var state=CrewMission.Value;var role=Lobby.Value.RoleOf(p.Receive.SenderClientId);if(role==StationRole.Driver)state.citySeats|=1;else if(role==StationRole.Assistant)state.citySeats|=2;else return;PublishCrew(state);
        }
    }
}
