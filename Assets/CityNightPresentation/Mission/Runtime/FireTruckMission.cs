using System;
using Unity.Netcode;
using UnityEngine;
using FirefighterVR.Station;

namespace CityNightPresentation.Mission
{
    public enum MissionOutcome:byte { InProgress,Success,TimedOut,BotBlocked }
    public struct FireTruckEvaluation:INetworkSerializable,IEquatable<FireTruckEvaluation>
    {
        public uint revision;
        public MissionOutcome outcome;
        public int emergencyActivations,redLightViolations;
        public float sirenSeconds,roofLightSeconds,trafficWaitingSeconds,intersectionDelaySeconds;
        public bool challengeStopRecorded;
        public bool Equals(FireTruckEvaluation other)=>revision==other.revision;
        public void NetworkSerialize<T>(BufferSerializer<T> b)where T:IReaderWriter
        {b.SerializeValue(ref revision);b.SerializeValue(ref outcome);b.SerializeValue(ref emergencyActivations);b.SerializeValue(ref redLightViolations);b.SerializeValue(ref sirenSeconds);b.SerializeValue(ref roofLightSeconds);b.SerializeValue(ref trafficWaitingSeconds);b.SerializeValue(ref intersectionDelaySeconds);b.SerializeValue(ref challengeStopRecorded);}
    }
    public struct CityMissionSnapshot : INetworkSerializable,IEquatable<CityMissionSnapshot>
    {
        public float clock,d0,d1,d2,d3,d4,d5,s0,s1,s2,s3,s4,s5,y0,y1,y2,y3,y4,y5;
        public int signal0,signal1,signal2;
        public byte activeVehicles;
        public float Distance(int i)=>i==0?d0:i==1?d1:i==2?d2:i==3?d3:i==4?d4:d5;
        public float Speed(int i)=>i==0?s0:i==1?s1:i==2?s2:i==3?s3:i==4?s4:s5;
        public float Yield(int i)=>i==0?y0:i==1?y1:i==2?y2:i==3?y3:i==4?y4:y5;
        public void Set(int i,float d,float s,float y){switch(i){case 0:d0=d;s0=s;y0=y;break;case 1:d1=d;s1=s;y1=y;break;case 2:d2=d;s2=s;y2=y;break;case 3:d3=d;s3=s;y3=y;break;case 4:d4=d;s4=s;y4=y;break;default:d5=d;s5=s;y5=y;break;}}
        public void NetworkSerialize<T>(BufferSerializer<T> b)where T:IReaderWriter
        {
            b.SerializeValue(ref clock);b.SerializeValue(ref d0);b.SerializeValue(ref d1);b.SerializeValue(ref d2);b.SerializeValue(ref d3);b.SerializeValue(ref d4);b.SerializeValue(ref d5);
            b.SerializeValue(ref s0);b.SerializeValue(ref s1);b.SerializeValue(ref s2);b.SerializeValue(ref s3);b.SerializeValue(ref s4);b.SerializeValue(ref s5);
            b.SerializeValue(ref y0);b.SerializeValue(ref y1);b.SerializeValue(ref y2);b.SerializeValue(ref y3);b.SerializeValue(ref y4);b.SerializeValue(ref y5);
            b.SerializeValue(ref signal0);b.SerializeValue(ref signal1);b.SerializeValue(ref signal2);
            b.SerializeValue(ref activeVehicles);
        }
        public bool Equals(CityMissionSnapshot other)=>clock.Equals(other.clock)&&signal0==other.signal0&&signal1==other.signal1&&signal2==other.signal2;
    }
    [DefaultExecutionOrder(-100),RequireComponent(typeof(NetworkObject))]
    public sealed class FireTruckMission : NetworkBehaviour
    {
        public FireTruckWheelController controller;
        public CityTrafficManager traffic;
        public CityDrivingRoute route;
        public Transform driverSeat,passengerSeat,incidentBuilding;
        public float redLightPenaltySeconds=10;
        [Min(60)] public float missionTimeLimitSeconds=300;
        [Min(0)] public int emergencyChallengeSignalIndex;
        public readonly NetworkVariable<FireTruckEvaluation> Evaluation=new NetworkVariable<FireTruckEvaluation>(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        FireTruckEvaluation evaluation;
        bool waitingForRed,waitingAtChallenge,wasEvaluationAuthority;
        bool challengeCrossed;
        public FireTruckEvaluation EvaluationState=>UseNetworkState&&!Authority?Evaluation.Value:evaluation;
        public bool HasFailed=>EvaluationState.outcome==MissionOutcome.TimedOut||EvaluationState.outcome==MissionOutcome.BotBlocked;
        public string BotFailureReason{get;private set;}="";
        public float RemainingSeconds=>Mathf.Max(0,missionTimeLimitSeconds-TotalSeconds);
        public string OutcomeReason=>EvaluationState.outcome==MissionOutcome.BotBlocked?BotFailureReason:HasFailed?"Mission time expired before arrival":HasArrived?"Incident stop zone reached safely":"Response in progress";
        public bool EmergencyConditionsSatisfied=>CrewReady&&!HasArrived&&!HasFailed&&controller&&controller.SpeedKmh<1.2f&&NearChallenge()&&(EvaluationState.challengeStopRecorded||StoppedAtRed(true));
        public bool CanActivateEmergency=>LocalRole==StationRole.Driver&&EmergencyConditionsSatisfied;
        public bool offlineDriverTesting;
        public readonly NetworkVariable<bool> Siren=new NetworkVariable<bool>(false,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> Elapsed=new NetworkVariable<float>(0,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<float> Penalties=new NetworkVariable<float>(0,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> Arrived=new NetworkVariable<bool>(false,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<CityMissionSnapshot> TrafficState=new NetworkVariable<CityMissionSnapshot>(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<Vector2> Motion=new NetworkVariable<Vector2>(Vector2.zero,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        Vector4 input=new Vector4(0,0,1,0);
        float inputAt=-10,networkAt,arrivalHold,offlineElapsed,offlinePenalties;
        bool offlineSiren,offlineArrived,started,previousReady;
        Vector3 previousFront;
        readonly float[] penaltyAt={-100,-100,-100};
        public bool Authority
        {get{if(StationSessionFlow.Instance&&StationSessionFlow.Instance.SoloMissionActive)return true;var n=Unity.Netcode.NetworkManager.Singleton;return !n||!n.IsListening||IsSpawned&&HasAuthority&&(!n.DistributedAuthorityMode||n.LocalClient.IsSessionOwner);}}
        public StationRole LocalRole
        {
            get{var flow=StationSessionFlow.Instance;if(flow&&flow.SoloMissionActive)return flow.SoloHumanRole;if(flow&&flow.IsSpawned)return flow.Lobby.Value.RoleOf(flow.NetworkManager.LocalClientId);return offlineDriverTesting?StationRole.Driver:StationRole.None;}
        }
        public bool CrewReady
        {
            get{var f=StationSessionFlow.Instance;if(f&&f.SoloMissionActive)return f.DrivingMissionReady;if(!f||!f.IsSpawned)return offlineDriverTesting&&!NetworkManagerIsListening;
                var s=f.Lobby.Value;bool assigned=!f.DrivingTransition.Value&&s.Phase==StationPhase.StationReady&&s.Driver!=StationLobbyState.Unassigned&&s.Assistant!=StationLobbyState.Unassigned;
                return assigned&&f.DrivingMissionReady&&(!Authority||f.NetworkManager.ConnectedClients.ContainsKey(s.Driver)&&f.NetworkManager.ConnectedClients.ContainsKey(s.Assistant));}
        }
        bool NetworkManagerIsListening=>Unity.Netcode.NetworkManager.Singleton&&Unity.Netcode.NetworkManager.Singleton.IsListening;
        public bool LocalDriverMayControl
        {get{var flow=StationSessionFlow.Instance;return LocalRole==StationRole.Driver&&CrewReady&&!HasArrived&&!HasFailed&&(!NetworkManagerIsListening||IsSpawned||(flow&&flow.SoloMissionActive))&&(!flow||!flow.localRig||flow.localRig.InputAvailable);}}
        bool UseNetworkState=>IsSpawned&&!(StationSessionFlow.Instance&&StationSessionFlow.Instance.SoloMissionActive);
        public bool SirenActive=>UseNetworkState?Siren.Value:offlineSiren;
        public bool HasArrived=>UseNetworkState?Arrived.Value:offlineArrived;
        public float TotalSeconds=>(UseNetworkState?Elapsed.Value+Penalties.Value:offlineElapsed+offlinePenalties);
        void Awake(){if(traffic)traffic.localSimulation=!NetworkManagerIsListening;}
        public override void OnNetworkSpawn(){if(HasAuthority&&NetworkManager.DistributedAuthorityMode)NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner,true);if(traffic)traffic.localSimulation=Authority;}
        public void SubmitDriverInput(Vector4 value)
        {
            if(!LocalDriverMayControl)return;
            if(UseNetworkState)DriverInputRpc(value);else if(Authority)AcceptInput(value);
        }
        public bool BotDriverMayControl=>StationSessionFlow.Instance&&StationSessionFlow.Instance.SoloBotDriverActive&&Authority&&CrewReady&&!HasArrived&&!HasFailed;
        public void SubmitBotDriverInput(Vector4 value){if(BotDriverMayControl)AcceptInput(value);}
        public bool RequestBotEmergency(bool enabled){if(!BotDriverMayControl||enabled&&!EmergencyConditionsSatisfied)return false;ApplyEmergency(enabled);return true;}
        public void FailBotDriver(string reason){if(!BotDriverMayControl)return;BotFailureReason=reason;evaluation.outcome=MissionOutcome.BotBlocked;ApplyEmergency(false);input=new Vector4(0,0,1,0);}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void DriverInputRpc(Vector4 value,RpcParams p=default)
        {var f=StationSessionFlow.Instance;if(Authority&&CrewReady&&f&&f.IsSpawned&&f.Lobby.Value.Driver==p.Receive.SenderClientId)AcceptInput(value);}
        void AcceptInput(Vector4 v)
        {
            if(!float.IsFinite(v.x)||!float.IsFinite(v.y)||!float.IsFinite(v.z)||!float.IsFinite(v.w))return;
            input=new Vector4(Mathf.Clamp(v.x,-1,1),Mathf.Clamp01(v.y),Mathf.Clamp01(v.z),v.w>.5f?1:0);inputAt=Time.unscaledTime;
            if(v.y>.05f)started=true;
        }
        public Vector4 ConsumeDriverInput()=>CrewReady&&!HasArrived&&!HasFailed&&Time.unscaledTime-inputAt<.3f?input:new Vector4(0,0,1,0);
        public void RequestSiren(bool enabled)
        {if(LocalRole!=StationRole.Driver||!CrewReady||enabled&&!CanActivateEmergency)return;if(UseNetworkState)SirenRpc(enabled);else if(Authority)ApplyEmergency(enabled);}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void SirenRpc(bool value,RpcParams p=default)
        {var f=StationSessionFlow.Instance;if(Authority&&CrewReady&&f&&f.IsSpawned&&f.Lobby.Value.RoleOf(p.Receive.SenderClientId)==StationRole.Driver&&(!value||EmergencyConditionsSatisfied))ApplyEmergency(value);}
        void ApplyEmergency(bool enabled)
        {enabled&=!HasArrived&&!HasFailed;if(enabled&&!SirenActive)evaluation.emergencyActivations++;if(UseNetworkState)Siren.Value=enabled;else offlineSiren=enabled;}
        void Update()
        {
            if(!traffic)return;
            if(NetworkManagerIsListening&&!IsSpawned&&!(StationSessionFlow.Instance&&StationSessionFlow.Instance.SoloMissionActive)){traffic.localSimulation=false;return;}
            traffic.localSimulation=Authority;
            if(Authority&&!wasEvaluationAuthority&&UseNetworkState)evaluation=Evaluation.Value;wasEvaluationAuthority=Authority;
            var flow=StationSessionFlow.Instance;if(Authority&&CrewReady&&flow&&flow.DrivingMissionReady)started=true;
            if(!Authority)
            {
                var state=TrafficState.Value;
                if(state.clock<=0)return;
                for(int i=0;i<traffic.vehicles.Length&&i<6;i++)
                {
                    var vehicle=traffic.vehicles[i];bool active=i<state.activeVehicles;
                    if(vehicle.body&&vehicle.body.gameObject.activeSelf!=active)vehicle.body.gameObject.SetActive(active);
                    if(active)traffic.ApplyRemoteVehicle(i,state.Distance(i),state.Speed(i),state.Yield(i),Time.deltaTime);
                }
                for(int i=0;i<traffic.signals.Length&&i<3;i++){int code=i==0?state.signal0:i==1?state.signal1:state.signal2;traffic.signals[i].ApplyReplicatedState(code/3,(CitySignalController.Aspect)(code%3));}
                return;
            }
            if(!CrewReady&&SirenActive){if(UseNetworkState)Siren.Value=false;else offlineSiren=false;}
            if(started&&CrewReady&&!HasArrived&&!HasFailed)
            {
                float dt=Time.deltaTime;if(UseNetworkState)Elapsed.Value+=dt;else offlineElapsed+=dt;
                if(SirenActive){evaluation.sirenSeconds+=dt;evaluation.roofLightSeconds+=dt;}
                if(waitingForRed)evaluation.trafficWaitingSeconds+=dt;if(waitingAtChallenge)evaluation.intersectionDelaySeconds+=dt;
                if(RemainingSeconds<=0){evaluation.outcome=MissionOutcome.TimedOut;ApplyEmergency(false);}
            }
            if(Time.unscaledTime>=networkAt)
            {
                networkAt=Time.unscaledTime+.1f;UpdateEmergency();CheckArrivalAndSignals();
                waitingForRed=StoppedAtRed(false);waitingAtChallenge=StoppedAtRed(true);if(waitingAtChallenge){evaluation.challengeStopRecorded=true;if(traffic.signals.Length>emergencyChallengeSignalIndex)traffic.signals[emergencyChallengeSignalIndex].StartResponseWait();}
                if(traffic.signals.Length>emergencyChallengeSignalIndex){var challenge=traffic.signals[emergencyChallengeSignalIndex];foreach(var head in challenge.heads)if(head.approach==challenge.responseApproach&&head.stopLine){var crossed=head.stopLine.InverseTransformPoint(transform.TransformPoint(new Vector3(0,1,3.8f)));if(crossed.z>=0&&crossed.z<25&&Mathf.Abs(crossed.x)<6&&Vector3.Dot(transform.forward,head.stopLine.forward)>.65f)challengeCrossed=true;}if(challengeCrossed&&(transform.position-challenge.transform.position).sqrMagnitude>35*35)challenge.CompleteResponsePassage();}
                if(UseNetworkState){evaluation.revision++;Evaluation.Value=evaluation;}
                if(UseNetworkState)
                {
                    Motion.Value=new Vector2(controller.AppliedInput.x,Vector3.Dot(transform.forward,controller.GetComponent<Rigidbody>().linearVelocity)*3.6f);
                    var state=new CityMissionSnapshot{clock=traffic.Clock,activeVehicles=(byte)Mathf.Clamp(traffic.activeVehicles,0,traffic.vehicles.Length)};
                    for(int i=0;i<traffic.vehicles.Length&&i<6;i++){var v=traffic.vehicles[i];state.Set(i,v.distance,v.speed,v.yieldOffset);}
                    for(int i=0;i<traffic.signals.Length&&i<3;i++){var s=traffic.signals[i];int code=s.ActiveApproach*3+(int)s.CurrentAspect;if(i==0)state.signal0=code;else if(i==1)state.signal1=code;else state.signal2=code;}
                    TrafficState.Value=state;
                }
            }
        }
        void UpdateEmergency()
        {
            bool active=SirenActive&&!HasArrived&&!HasFailed&&CrewReady;
            CitySignalController priority=null;float best=85;
            foreach(var s in traffic.signals)
            {var d=s.transform.position-transform.position;float distance=d.magnitude;if(active&&distance<best&&Vector3.Dot(transform.forward,d.normalized)>.2f){priority=s;best=distance;}}
            foreach(var s in traffic.signals)
            {
                if(s==priority){Vector3 f=transform.forward;int a=Mathf.Abs(f.x)>Mathf.Abs(f.z)?(f.x>0?0:2):(f.z>0?3:1);s.RequestPriority(a);}else s.ReleasePriority();
            }
            foreach(var v in traffic.vehicles)
            {
                if(!v.body)continue;
                bool outsideArrival=!route.stopZone||Vector3.Distance(v.body.position,route.stopZone.position)>35;
                Vector3 delta=v.body.position-transform.position;
                v.yieldRequested=active&&outsideArrival&&delta.sqrMagnitude<55*55&&Vector3.Dot(transform.forward,delta) > -5;
            }
        }
        void CheckArrivalAndSignals()
        {
            Vector3 front=transform.TransformPoint(new Vector3(0,1,3.8f));
            if(previousReady&&Vector3.Distance(previousFront,front)<20&&started)
            {
                for(int i=0;i<traffic.signals.Length;i++)foreach(var head in traffic.signals[i].heads)
                {
                    if(!head.stopLine||Vector3.Dot(transform.forward,head.stopLine.forward)<.65f)continue;
                    Vector3 before=head.stopLine.InverseTransformPoint(previousFront),now=head.stopLine.InverseTransformPoint(front);
                    if(CrossedStopLine(before,now)&&traffic.signals[i].GetAspect(head.approach)==CitySignalController.Aspect.Red&&Time.unscaledTime-penaltyAt[i]>10)
                    {penaltyAt[i]=Time.unscaledTime;evaluation.redLightViolations++;if(UseNetworkState)Penalties.Value+=redLightPenaltySeconds;else offlinePenalties+=redLightPenaltySeconds;}
                }
            }
            previousFront=front;previousReady=true;
            if(!route.stopZone||HasArrived||HasFailed)return;
            Vector3 p=route.stopZone.InverseTransformPoint(transform.position);
            bool stopped=CrewReady&&started&&IsParked(p,Vector3.Dot(transform.forward,route.stopZone.forward),Vector3.Dot(transform.up,Vector3.up),controller.SpeedKmh);
            arrivalHold=stopped?arrivalHold+.1f:0;
            if(arrivalHold>=2){evaluation.outcome=MissionOutcome.Success;if(UseNetworkState){Arrived.Value=true;Siren.Value=false;}else{offlineArrived=true;offlineSiren=false;}UpdateEmergency();}
        }
        bool NearChallenge()=>traffic&&traffic.signals.Length>0&&traffic.signals[Mathf.Clamp(emergencyChallengeSignalIndex,0,traffic.signals.Length-1)]&&(traffic.signals[Mathf.Clamp(emergencyChallengeSignalIndex,0,traffic.signals.Length-1)].transform.position-transform.position).sqrMagnitude<65*65;
        bool StoppedAtRed(bool designatedOnly)
        {
            if(!CrewReady||HasArrived||HasFailed||!controller||controller.SpeedKmh>=1.2f||!traffic)return false;
            var front=transform.TransformPoint(new Vector3(0,1,3.8f));
            for(int i=0;i<traffic.signals.Length;i++)
            {
                if(designatedOnly&&i!=emergencyChallengeSignalIndex)continue;var signal=traffic.signals[i];if(!signal)continue;
                foreach(var head in signal.heads)if(head.stopLine&&Vector3.Dot(transform.forward,head.stopLine.forward)>.65f&&signal.GetAspect(head.approach)==CitySignalController.Aspect.Red)
                {var p=head.stopLine.InverseTransformPoint(front);if(p.z<=1&&p.z>=-30&&Mathf.Abs(p.x)<5.5f)return true;}
            }
            return false;
        }
        public static bool CrossedStopLine(Vector3 before,Vector3 now)=>before.z<0&&now.z>=0&&Mathf.Abs(Mathf.Lerp(before.x,now.x,-before.z/Mathf.Max(.001f,now.z-before.z)))<4.2f;
        public static bool IsParked(Vector3 local,float headingDot,float upDot,float speedKmh)=>Mathf.Abs(local.x)<.3f&&Mathf.Abs(local.z)<3.5f&&Mathf.Abs(local.y)<1&&speedKmh<1.2f&&headingDot>.94f&&upDot>.95f;
        void OnDisable(){if(traffic){traffic.localSimulation=false;foreach(var s in traffic.signals)if(s)s.ReleasePriority();foreach(var v in traffic.vehicles)v.yieldRequested=false;}}
    }
}
