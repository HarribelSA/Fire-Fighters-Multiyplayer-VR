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
        [Tooltip("Rebuild the navigation arrows at load from the CURRENT route, ending exactly at the incident stop zone.")]
        public bool rebuildArrowsFromRoute=true;
        [Range(.5f,4)]public float arrowSpacing=1;
        void Start(){if(rebuildArrowsFromRoute)RebuildArrows();}
        public Vector3 ArrivalTarget
        {
            get
            {
                if(!mission)return Vector3.zero;
                if(mission.arrivalZone)return mission.arrivalZone.transform.TransformPoint(mission.arrivalZone.center);
                var r=mission.route;return r&&r.stopZone?r.stopZone.position:r&&r.arrivalPoint?r.arrivalPoint.position:mission.transform.position;
            }
        }
        // Builds road-conformed arrows along the route, trimmed at the point closest to the incident stop zone
        // and finished with a final leg straight into it, so the last arrow points at the exact parking spot.
        public void RebuildArrows()
        {
            var route=mission?mission.route:null;if(!route||route.waypoints==null||route.waypoints.Length<2)return;
            var path=new System.Collections.Generic.List<Vector3>();foreach(var w in route.waypoints)if(w)path.Add(w.position);if(path.Count<2)return;
            Vector3 target=ArrivalTarget;
            // Closest point on the polyline to the arrival target: the route ends there.
            int bestSeg=path.Count-2;float bestT=1,best=float.PositiveInfinity;
            for(int i=0;i<path.Count-1;i++)
            {
                Vector3 a=path[i],d=path[i+1]-a;float t=Mathf.Clamp01(Vector3.Dot(target-a,d)/Mathf.Max(.0001f,d.sqrMagnitude));
                float e=Vector3.ProjectOnPlane(target-(a+d*t),Vector3.up).sqrMagnitude;if(e<best-.01f){best=e;bestSeg=i;bestT=t;}
            }
            var trimmed=path.GetRange(0,bestSeg+1);trimmed.Add(Vector3.Lerp(path[bestSeg],path[bestSeg+1],bestT));
            if(Mathf.Sqrt(best)>.5f)trimmed.Add(target);
            var points=new System.Collections.Generic.List<Vector3>();var rotations=new System.Collections.Generic.List<Quaternion>();float carry=0;
            for(int i=0;i<trimmed.Count-1;i++)
            {
                Vector3 a=trimmed[i],b=trimmed[i+1];var flat=Vector3.ProjectOnPlane(b-a,Vector3.up);float length=flat.magnitude;if(length<.01f)continue;
                var rot=Quaternion.LookRotation(flat/length,Vector3.up);
                for(float d=carry;d<length;d+=arrowSpacing){var p=Vector3.Lerp(a,b,d/length);p.y=Ground(p)+.075f;points.Add(p);rotations.Add(rot);}
                carry=Mathf.Repeat(carry-length,arrowSpacing);
            }
            if(points.Count==0)return;
            arrowPositions=points.ToArray();arrowRotations=rotations.ToArray();
        }
        readonly RaycastHit[] groundHits=new RaycastHit[12];
        float Ground(Vector3 p)
        {
            int n=Physics.RaycastNonAlloc(p+Vector3.up*6,Vector3.down,groundHits,14,~0,QueryTriggerInteraction.Ignore);
            // Road surface = the static hit nearest the route height (ignores signal arms, trees and the truck).
            float y=p.y,bestGap=2.5f;
            for(int i=0;i<n;i++){var c=groundHits[i].collider;if(!c||c.attachedRigidbody&&!c.attachedRigidbody.isKinematic||mission&&c.transform.IsChildOf(mission.transform))continue;float gap=Mathf.Abs(groundHits[i].point.y-p.y);if(gap<bestGap){bestGap=gap;y=groundHits[i].point.y;}}
            return y;
        }
        void Update()
        {
            if(!mission)return;
            if(!playerCamera)playerCamera=Camera.main;
            if(Time.unscaledTime>=nextText)
            {
                nextText=Time.unscaledTime+.1f;
                // The response timer is hidden from the crew; it is only revealed on the debrief dashboard.
                string time=mission.SirenActive?"EMERGENCY RESPONSE":"EN ROUTE";
                if(windshieldTimer)windshieldTimer.text=mission.HasFailed?(mission.EvaluationState.outcome==MissionOutcome.BotBlocked?"BOT BLOCKED":"MISSION STOPPED"):mission.HasArrived?"ARRIVED":time+(mission.LocalRole==StationRole.Driver&&!mission.controller.DrivingDevicePresent?"\nG29 OFFLINE":"");
                var flow=StationSessionFlow.Instance;
                if(windshieldTimer&&flow&&flow.localRig&&flow.localRig.SeatedComfortWarning.Length>0)windshieldTimer.text+="\nPLEASE REMAIN SEATED";
                if(windshieldTimer&&CityNightPresentation.Fireground.MissionDebrief.Active)windshieldTimer.text="";
                string location=flow&&flow.incident?flow.incident.location:"MISSION DESTINATION";
                var fireground=CityNightPresentation.Fireground.FiregroundOperation.Instance;
                var fg=fireground?fireground.Current:default;
                if(windshieldTimer&&fireground&&fg.phase==CityNightPresentation.Fireground.FiregroundPhase.ArrivalHold)windshieldTimer.text=$"ARRIVED\nDISMOUNT IN {Mathf.CeilToInt(fireground.SecondsRemaining)}";
                string type=flow&&flow.incident?flow.incident.incidentType.ToUpperInvariant():"STRUCTURE FIRE";
                string priority=flow&&flow.incident?flow.incident.priority:"HIGH";
                string siren=mission.SirenActive?"<color=#FF4A3D>SIREN ● ON</color>":"<color=#9AA4AE>SIREN ○ OFF</color>";
                if(tabletStatus)tabletStatus.text=$"<b>CIVIL DEFENCE · DISPATCH {(flow&&flow.MissionSessionActive?flow.MissionState.epoch:0)}</b>   <color=#FFB020>PRIORITY {priority}</color>\n{type} · {location}\n{siren} · {(mission.SirenActive?"EMERGENCY PRIORITY":"NORMAL TRAFFIC")}"+(flow&&flow.SoloMissionActive?(flow.SoloBotDriverActive?"\nBOT DRIVER · "+flow.SoloTaskStatus:"\nBOT NAVIGATION · DRIVER PULLS EMERGENCY HANDLE"):"");
                if(tabletDetails)tabletDetails.text=fireground&&fg.phase!=CityNightPresentation.Fireground.FiregroundPhase.Inactive?"<b>MISSION STATUS</b>\n"+fireground.PanelStatus(fg):NavigationDetails(time,flow);
                if(tabletInteraction){bool allowed=mission.LocalRole==StationRole.Assistant;tabletInteraction.interactable=allowed;tabletInteraction.blocksRaycasts=allowed;}
            }
            // Route arrows are shown to the whole crew (Driver and passenger).
            if(!playerCamera||!arrowMesh||!arrowMaterial||mission.LocalRole==StationRole.None||mission.HasArrived||mission.HasFailed)return;
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
            {var e=mission.EvaluationState;return $"{e.outcome}\n{mission.OutcomeReason}\nEMERGENCY {e.emergencyActivations} TIMES\nSIREN {e.sirenSeconds:0}s\nRED VIOLATIONS {e.redLightViolations}\n<color=#A0A8B4>Full time report after the operation</color>";}
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
            return $"{state}\n{remaining:0} m REMAIN\nETA {eta}\nPROGRESS {Mathf.Clamp01(along/Mathf.Max(1,total))*100:0}%\nSEGMENT {segment+1}/{route.waypoints.Length-1}\n{turn}\nWORLD m\nX {position.x:0.0}\nZ {position.z:0.0}\n{(flow&&flow.incident?flow.incident.priority:"RESPONSE")}";
        }
        void Draw(int count)=>Graphics.DrawMeshInstanced(arrowMesh,0,arrowMaterial,matrices,count,null,ShadowCastingMode.Off,false,gameObject.layer,playerCamera,LightProbeUsage.Off);
    }
}
