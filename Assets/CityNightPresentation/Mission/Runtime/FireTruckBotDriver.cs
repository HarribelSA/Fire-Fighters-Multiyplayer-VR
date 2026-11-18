using FirefighterVR.Station;
using UnityEngine;

namespace CityNightPresentation.Mission
{
    // Solo-only input adapter. Never moves the truck transform or changes vehicle physics.
    [DefaultExecutionOrder(-95)]
    public sealed class FireTruckBotDriver:MonoBehaviour
    {
        public FireTruckMission mission;
        public FireTruckEmergency emergency;
        [Range(3,16)]public float cruiseMetresPerSecond=11;
        public float blockedSeconds=40;
        public string Status{get;private set;}="Waiting for crew gates";
        public float Progress{get;private set;}
        Vector3[] points;
        float[] distances;
        int segment;
        float nextTick,lastProgressAt,previousProgress;
        bool started,requestedEmergency;
        Vector4 input=new Vector4(0,0,1,0);
        readonly RaycastHit[] hits=new RaycastHit[12];
        public static float Steering(Vector3 localTarget,float wheelbase,float maximumAngle)
        {return Mathf.Clamp(Mathf.Atan2(2*wheelbase*localTarget.x,Mathf.Max(1,localTarget.x*localTarget.x+localTarget.z*localTarget.z))*Mathf.Rad2Deg/Mathf.Max(1,maximumAngle),-1,1);}
        public static float StoppingSpeed(float distance,float deceleration=2)=>Mathf.Sqrt(2*deceleration*Mathf.Max(0,distance));
        bool Initialize()
        {
            var route=mission.route;if(!route||route.waypoints.Length<2||!route.stopZone)return false;
            points=new Vector3[route.waypoints.Length];distances=new float[points.Length];
            for(int i=0;i<points.Length;i++){if(!route.waypoints[i])return false;points[i]=route.waypoints[i].position;if(i>0)distances[i]=distances[i-1]+Vector3.ProjectOnPlane(points[i]-points[i-1],Vector3.up).magnitude;}
            started=true;lastProgressAt=Time.unscaledTime;return true;
        }
        void FixedUpdate()
        {
            if(!mission||!mission.BotDriverMayControl){input=new Vector4(0,0,1,0);Status=mission&&mission.HasFailed?mission.OutcomeReason:mission&&mission.HasArrived?"Incident arrival confirmed":"Waiting for crew gates";return;}
            if(!started&&!Initialize()){mission.FailBotDriver("Missing existing route or stop zone");return;}
            if(Time.unscaledTime>=nextTick){nextTick=Time.unscaledTime+.08f;Compute();}
            mission.SubmitBotDriverInput(input);
        }
        Vector3 Sample(float distance)
        {int i=segment;while(i<points.Length-2&&distances[i+1]<distance)i++;return Vector3.Lerp(points[i],points[i+1],Mathf.InverseLerp(distances[i],distances[i+1],distance));}
        void Compute()
        {
            var position=transform.position;float nearest=float.PositiveInfinity;int best=segment;float progress=Progress;
            for(int i=Mathf.Max(0,segment-1);i<Mathf.Min(points.Length-1,segment+6);i++)
            {var a=Vector3.ProjectOnPlane(position-points[i],Vector3.up);var d=Vector3.ProjectOnPlane(points[i+1]-points[i],Vector3.up);float t=Mathf.Clamp01(Vector3.Dot(a,d)/Mathf.Max(.001f,d.sqrMagnitude));float error=(a-d*t).sqrMagnitude;if(error<nearest){nearest=error;best=i;progress=Mathf.Lerp(distances[i],distances[i+1],t);}}
            segment=best;Progress=Mathf.Max(Progress,progress);
            if(nearest>64){mission.FailBotDriver("Truck left the validated route corridor");input=new Vector4(0,0,1,0);return;}
            float speed=mission.controller.SpeedKmh/3.6f;float look=Mathf.Clamp(4+speed*.7f,4,9);var target=Sample(Progress+look);var local=transform.InverseTransformPoint(target);local.y=0;
            float desired=cruiseMetresPerSecond;var future=Sample(Progress+18)-Sample(Progress+7);if(Vector3.Angle(transform.forward,Vector3.ProjectOnPlane(future,Vector3.up))>15)desired=Mathf.Min(desired,5f);
            float free=float.PositiveInfinity;bool signalWait=false;
            var front=transform.TransformPoint(new Vector3(0,1,3.8f));
            foreach(var signal in mission.traffic.signals)foreach(var head in signal.heads)
            {
                if(!head.stopLine||Vector3.Dot(transform.forward,head.stopLine.forward)<.65f)continue;
                var p=head.stopLine.InverseTransformPoint(front);
                if(p.z<.15f&&p.z>-45&&Mathf.Abs(p.x)<5.5f&&signal.GetAspect(head.approach)!=CitySignalController.Aspect.Green){free=Mathf.Min(free,Mathf.Max(0,-p.z-1));signalWait=true;}
            }
            foreach(var vehicle in mission.traffic.vehicles)if(vehicle.body&&vehicle.body.gameObject.activeInHierarchy)
            {var p=transform.InverseTransformPoint(vehicle.body.position);if(p.z>0&&Mathf.Abs(p.x)<2.7f&&Mathf.Abs(p.y)<3)free=Mathf.Min(free,Mathf.Max(0,p.z-5-vehicle.length*.5f-2));}
            if(mission.traffic.obstacleMask.value!=0)
            {
                int n=Physics.SphereCastNonAlloc(transform.position+Vector3.up*1.15f+transform.forward*4.5f,.9f,transform.forward,hits,Mathf.Min(22,4+speed*speed/4),mission.traffic.obstacleMask,QueryTriggerInteraction.Ignore);
                for(int i=0;i<n;i++)if(hits[i].collider&&!hits[i].collider.transform.IsChildOf(transform))free=Mathf.Min(free,Mathf.Max(0,hits[i].distance-2));
            }
            if(!requestedEmergency&&mission.EmergencyConditionsSatisfied&&emergency){requestedEmergency=emergency.RequestFromBotDriver(true);}
            var stop=mission.route.stopZone;var final=stop.InverseTransformPoint(position);float remaining=distances[distances.Length-1]-Progress;
            if(remaining<14)
            {
                target=stop.position+stop.forward*Mathf.Clamp(-final.z*.35f,0,3);local=transform.InverseTransformPoint(target);local.y=0;
                desired=Mathf.Min(desired,Mathf.Clamp(-final.z*.65f,0,2));
                if(final.z>-.15f)desired=0;
            }
            desired=Mathf.Min(desired,StoppingSpeed(free));
            float steer=Steering(local,4.8f,mission.controller.steeringAngle);float throttle=Mathf.Clamp01((desired-speed)*.4f);float brake=Mathf.Clamp01((speed-desired)*.55f);
            if(desired<.15f){throttle=0;brake=1;}
            input=new Vector4(steer,throttle,brake,0);
            Status=signalWait&&desired<.2f?"Stopped at signal Â· waiting for safe clearance":remaining<14?"Aligning with incident stop zone":"Following existing route";
            if(Progress>previousProgress+.5f){previousProgress=Progress;lastProgressAt=Time.unscaledTime;}
            if(signalWait||remaining<4&&speed<.4f)lastProgressAt=Time.unscaledTime;
            if(Time.unscaledTime-lastProgressAt>blockedSeconds){mission.FailBotDriver("Bot Driver blocked for "+blockedSeconds+" seconds; no teleport recovery");input=new Vector4(0,0,1,0);Status="BLOCKED Â· STOPPED SAFELY";}
        }
        void OnDisable(){if(mission&&mission.BotDriverMayControl)mission.SubmitBotDriverInput(new Vector4(0,0,1,0));}
    }
}
