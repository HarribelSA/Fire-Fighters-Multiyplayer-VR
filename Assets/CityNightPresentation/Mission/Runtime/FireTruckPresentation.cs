using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using FirefighterVR.Station;

namespace CityNightPresentation.Mission
{
    public sealed class FireTruckPresentation : MonoBehaviour
    {
        public FireTruckMission mission;
        public TMP_Text windshieldTimer,tabletStatus;
        public TMP_Text tabletDetails;
        public CanvasGroup tabletInteraction;
        public Mesh arrowMesh;
        public Material arrowMaterial;
        public Vector3[] arrowPositions=new Vector3[0];
        public Quaternion[] arrowRotations=new Quaternion[0];
        [Range(10,60)]public float arrowRange=35;
        readonly Matrix4x4[] matrices=new Matrix4x4[128];
        float nextText;
        Camera playerCamera;
        void Update()
        {
            if(!mission)return;
            if(!playerCamera)playerCamera=Camera.main;
            if(Time.unscaledTime>=nextText)
            {
                nextText=Time.unscaledTime+.1f;float t=mission.RemainingSeconds;
                string time=$"{Mathf.FloorToInt(t/60):00}:{Mathf.FloorToInt(t%60):00}";
                if(windshieldTimer)windshieldTimer.text=mission.HasFailed?(mission.EvaluationState.outcome==MissionOutcome.BotBlocked?"BOT BLOCKED":"TIME EXPIRED"):mission.HasArrived?"ARRIVED":time+(mission.LocalRole==StationRole.Driver&&!mission.controller.DrivingDevicePresent?"\nG29 OFFLINE":"");
                var flow=StationSessionFlow.Instance;
                if(windshieldTimer&&flow&&flow.localRig&&flow.localRig.SeatedComfortWarning.Length>0)windshieldTimer.text+="\nPLEASE REMAIN SEATED";
                string location=flow&&flow.incident?flow.incident.location:"MISSION DESTINATION";
                if(tabletStatus)tabletStatus.text=$"NAVIGATION · DISPATCH {(flow&&flow.MissionSessionActive?flow.MissionState.epoch:0)}\n{location}\n{time} LEFT | {(mission.SirenActive?"EMERGENCY PRIORITY":"NORMAL TRAFFIC")}"+(flow&&flow.SoloMissionActive?(flow.SoloBotDriverActive?"\nBOT DRIVER · "+flow.SoloTaskStatus:"\nBOT NAVIGATION · DRIVER PULLS EMERGENCY HANDLE"):"");
                if(tabletDetails)tabletDetails.text=NavigationDetails(time,flow);
                if(tabletInteraction){bool allowed=mission.LocalRole==StationRole.Assistant;tabletInteraction.interactable=allowed;tabletInteraction.blocksRaycasts=allowed;}
            }
            if(!playerCamera||!arrowMesh||!arrowMaterial||mission.LocalRole!=StationRole.Driver||mission.HasArrived||mission.HasFailed)return;
            int count=0;
            for(int i=0;i<arrowPositions.Length;i++)
            {
                if((arrowPositions[i]-playerCamera.transform.position).sqrMagnitude>arrowRange*arrowRange)continue;
                matrices[count++]=Matrix4x4.TRS(arrowPositions[i],arrowRotations[i],Vector3.one);
                if(count==matrices.Length){Draw(count);count=0;}
            }
            if(count>0)Draw(count);
        }
        string NavigationDetails(string time,StationSessionFlow flow)
        {
            if(mission.HasArrived||mission.HasFailed)
            {var e=mission.EvaluationState;return $"{e.outcome}\n{mission.OutcomeReason}\nTIME {mission.TotalSeconds:0} s\nEMERGENCY {e.emergencyActivations} TIMES\nSIREN {e.sirenSeconds:0}s\nROOF LIGHTS {e.roofLightSeconds:0}s\nRED WAIT {e.trafficWaitingSeconds:0}s\nCHALLENGE DELAY {e.intersectionDelaySeconds:0}s\nRED VIOLATIONS {e.redLightViolations}";}
            var route=mission.route;if(!route||route.waypoints.Length<2)return "ROUTE INITIALIZING";
            Vector3 position=mission.transform.position;float nearest=float.PositiveInfinity,along=0,total=0,bestAlong=0;int segment=0;
            for(int i=1;i<route.waypoints.Length;i++)
            {
                var a=route.waypoints[i-1].position;var b=route.waypoints[i].position;var d=b-a;float length=d.magnitude;
                float u=Mathf.Clamp01(Vector3.Dot(position-a,d)/Mathf.Max(.001f,d.sqrMagnitude));float distance=(position-(a+d*u)).sqrMagnitude;
                if(distance<nearest){nearest=distance;segment=i-1;bestAlong=total+length*u;}total+=length;
            }
            along=bestAlong;float remaining=Mathf.Max(0,total-along);float speed=mission.controller.SpeedKmh/3.6f;
            string eta=speed>.5f?Mathf.CeilToInt(remaining/speed)+" s":"—";string turn="ARRIVAL";
            if(segment+2<route.waypoints.Length){var a=route.waypoints[segment+1].position-route.waypoints[segment].position;var b=route.waypoints[segment+2].position-route.waypoints[segment+1].position;float angle=Vector3.SignedAngle(a,b,Vector3.up);turn=Mathf.Abs(angle)<20?"CONTINUE":angle>0?"TURN RIGHT":"TURN LEFT";}
            string state=mission.HasArrived?"ARRIVED":flow&&flow.DrivingMissionReady?"DRIVING":"CREW SYNC";
            return $"{state}\n{remaining:0} m REMAIN\nETA {eta}\nPROGRESS {Mathf.Clamp01(along/Mathf.Max(1,total))*100:0}%\nSEGMENT {segment+1}/{route.waypoints.Length-1}\n{turn}\nWORLD m\nX {position.x:0.0}\nZ {position.z:0.0}\n{(flow&&flow.incident?flow.incident.priority:"RESPONSE")}\n{time}";
        }
        void Draw(int count)=>Graphics.DrawMeshInstanced(arrowMesh,0,arrowMaterial,matrices,count,null,ShadowCastingMode.Off,false,gameObject.layer,playerCamera,LightProbeUsage.Off);
    }
}
