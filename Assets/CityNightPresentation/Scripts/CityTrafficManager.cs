using System;
using UnityEngine;

namespace CityNightPresentation
{
    public sealed class CityTrafficManager : MonoBehaviour
    {
        [Serializable] public sealed class Vehicle
        {
            public Transform body;
            public CityTrafficPath path;
            public float distance;
            public float speed;
            [Min(1)] public float length = 5;
            [Min(1)] public float cruiseSpeed = 7;
            public bool externalHold; // Generic future authority/yielding adapter; no emergency rules.
            public bool yieldRequested;
            public float yieldOffset;
            [NonSerialized] public CitySignalController reserved;
            [NonSerialized] public float releaseAt;
            [NonSerialized] public float visualDistance;
            [NonSerialized] public float nextObstacleQuery;
            [NonSerialized] public float obstacleFree = float.PositiveInfinity;
            [NonSerialized] public bool remoteInitialized;
        }
        [Range(0,12)] public int activeVehicles = 6;
        [Range(10,50)] public int simulationHz = 20;
        [Min(2)] public float minimumGap = 4;
        [Min(.1f)] public float acceleration = 1.4f;
        [Min(.1f)] public float braking = 4;
        public bool localSimulation = true;
        public CitySignalController[] signals = new CitySignalController[0];
        public Vehicle[] vehicles = new Vehicle[0];
        public LayerMask obstacleMask;
        public Transform emergencyVehicle;
        [Min(100)] public float distantSimulationRange = 250;
        public float Clock { get; private set; }
        public event Action SimulationStepped;
        float accumulator;
        Camera viewer;
        float nextCameraCheck;
        readonly System.Collections.Generic.HashSet<CityTrafficPath> initializingPaths=new System.Collections.Generic.HashSet<CityTrafficPath>();
        public void Initialize()
        {
            initializingPaths.Clear();
            using var timing=FirefighterVR.Station.TransitionDiagnostics.Measure("traffic_paths_and_poses_initialize");
            foreach (var v in vehicles)
            {
                if (!v.path || !v.body) continue;
                if(initializingPaths.Add(v.path))v.path.Initialize(); v.speed = 0; v.reserved = null; v.visualDistance = v.distance;
                v.obstacleFree = float.PositiveInfinity; v.nextObstacleQuery = 0;
                v.remoteInitialized=false;
                ApplyPose(v,v.distance);
            }
            foreach(var s in signals) if(s) s.SetClock(Clock);
        }
        void Start() { Initialize(); }
        void Update()
        {
            if (!localSimulation) return;
            if (Time.unscaledTime > nextCameraCheck) { viewer = Camera.main; nextCameraCheck = Time.unscaledTime + 1; }
            accumulator += Mathf.Min(Time.deltaTime,.15f);
            float step=1f/Mathf.Max(10,simulationHz);
            int guard=0;
            while(accumulator>=step && guard++<4) { SimulateStep(step); accumulator-=step; }
            for(int i=0;i<vehicles.Length;i++)
            {
                var v=vehicles[i]; if(!v.body||!v.path||i>=activeVehicles)continue;
                bool far=viewer && (v.body.position-viewer.transform.position).sqrMagnitude>distantSimulationRange*distantSimulationRange;
                if(far && Time.frameCount%4!=0)continue;
                v.visualDistance=Mathf.Lerp(v.visualDistance,v.distance,1-Mathf.Exp(-14*Time.deltaTime*(far?4:1)));
                ApplyPose(v,v.visualDistance);
            }
        }
        void ApplyPose(Vehicle v,float d)
        {
            v.path.Sample(d,out var p,out var f);
            v.path.Sample(d+3,out var ahead,out _);
            if((ahead-p).sqrMagnitude>.01f)f=(ahead-p).normalized;
            p+=Vector3.Cross(Vector3.up,f).normalized*v.yieldOffset;
            v.body.SetPositionAndRotation(p,Quaternion.LookRotation(f,Vector3.up));
        }
        public void ApplyRemoteVehicle(int i,float distance,float speed,float offset,float dt)
        {
            if(i<0||i>=vehicles.Length)return;var v=vehicles[i];if(!v.body||!v.path)return;
            v.distance=distance;v.speed=speed;
            if(!v.remoteInitialized){v.visualDistance=distance;v.yieldOffset=offset;v.remoteInitialized=true;}
            else{v.yieldOffset=Mathf.Lerp(v.yieldOffset,offset,1-Mathf.Exp(-10*dt));v.visualDistance=Mathf.Lerp(v.visualDistance,distance,1-Mathf.Exp(-12*dt));}
            ApplyPose(v,v.visualDistance);
        }
        // Public deterministic step permits editor validation and a future authoritative adapter.
        public void SimulateStep(float dt)
        {
            if(dt<=0||dt>.2f) return;
            Clock+=dt;
            foreach(var s in signals)if(s)
            {
                s.occupiedApproach=-1;
                foreach(var v in vehicles)if(v.reserved==s&&v.path)foreach(var gate in v.path.gates)if(gate.signal==s){s.occupiedApproach=gate.approach;break;}
                if(emergencyVehicle&&Vector3.ProjectOnPlane(emergencyVehicle.position-s.transform.position,Vector3.up).sqrMagnitude<25*25)
                {
                    Vector3 f=emergencyVehicle.forward;int a=Mathf.Abs(f.x)>Mathf.Abs(f.z)?(f.x>0?0:2):(f.z>0?3:1);
                    bool inside=false;foreach(var h in s.heads)if(h.approach==a&&h.stopLine){var p=h.stopLine.InverseTransformPoint(emergencyVehicle.TransformPoint(new Vector3(0,1,3.8f)));if(p.z>=0&&p.z<30&&Mathf.Abs(p.x)<5.5f)inside=true;}
                    if(inside)s.occupiedApproach=s.occupiedApproach>=0&&s.occupiedApproach!=a?-2:a;
                }
                s.SetClock(Clock);
            }
            int count=Mathf.Min(activeVehicles,vehicles.Length);
            for(int i=0;i<vehicles.Length;i++)if(vehicles[i].body && vehicles[i].body.gameObject.activeSelf!=(i<count))vehicles[i].body.gameObject.SetActive(i<count);
            for(int i=0;i<count;i++)
            {
                var v=vehicles[i];if(!v.body||!v.path||v.path.Length<1)continue;
                if(v.reserved && v.distance>=v.releaseAt)v.reserved=null;
                float free=float.PositiveInfinity;
                for(int j=0;j<count;j++)
                {
                    var other=vehicles[j];if(i==j||!other.body)continue;
                    if(other.path!=v.path)
                    {
                        // Prefix vehicles eventually share the existing loop's asphalt.
                        // Keep following separation even when their path references differ.
                        v.path.Sample(v.distance,out var ownPoint,out var ownForward);var delta=other.body.position-ownPoint;
                        float along=Vector3.Dot(delta,ownForward);float side=Mathf.Abs(Vector3.Dot(delta,Vector3.Cross(Vector3.up,ownForward)));
                        if(along>0&&side<2.2f&&Mathf.Abs(delta.y)<1.5f)free=Mathf.Min(free,along-(v.length+other.length)*.5f-minimumGap);
                        continue;
                    }
                    float gap=v.path.ForwardDistance(v.distance,other.distance)-(v.length+other.length)*.5f-minimumGap;
                    if(gap<free)free=gap;
                }
                for(int g=0;g<v.path.gates.Length;g++)
                {
                    var gate=v.path.gates[g];if(!gate.signal)continue;
                    float ahead=v.path.DistanceToGate(g,v.distance);
                    // Gate distance is a center stopping coordinate, already behind the painted line.
                    if(ahead>40 || v.reserved==gate.signal)continue;
                    bool occupied=false;
                    for(int j=0;j<count;j++)if(j!=i&&vehicles[j].reserved==gate.signal){occupied=true;break;}
                    // Yielded approach traffic stays on the verified shoulder while the truck passes,
                    // even after its signal becomes green. Do not reserve/merge back into the truck lane.
                    bool yieldingAtLine=v.yieldRequested&&v.path.YieldClearance(v.distance)>=3;
                    if(!gate.signal.Allows(gate.approach)||occupied||yieldingAtLine)free=Mathf.Min(free,ahead);
                    else if(ahead<=Mathf.Max(1,v.speed*dt+.5f))
                    {
                        v.reserved=gate.signal;
                        v.releaseAt=v.distance+ahead+Mathf.Max(25,gate.clearanceDistance);
                    }
                }
                // Low-frequency, bounded obstacle check includes future truck colliders and static obstructions.
                if(obstacleMask.value!=0)
                {
                    if(Clock>=v.nextObstacleQuery)
                    {
                        v.path.Sample(v.distance,out var pos,out var dir);
                        bool far=viewer && (pos-viewer.transform.position).sqrMagnitude>distantSimulationRange*distantSimulationRange;
                        v.nextObstacleQuery=Clock+(far?.25f:.1f);
                        float range=Mathf.Min(20,minimumGap+v.speed*v.speed/(2*Mathf.Max(.1f,braking)));
                        v.obstacleFree=Physics.SphereCast(pos+Vector3.up*1.1f+dir*(v.length*.5f+.5f),.45f,dir,out var hit,range,obstacleMask,QueryTriggerInteraction.Ignore)?Mathf.Max(0,hit.distance-minimumGap):float.PositiveInfinity;
                    }
                    free=Mathf.Min(free,v.obstacleFree);
                }
                float desired=v.externalHold?0:Mathf.Min(v.cruiseSpeed,v.path.speedLimit);
                float yieldTarget=v.yieldRequested&&v.reserved==null?v.path.YieldClearance(v.distance):0;
                float nextYield=Mathf.MoveTowards(v.yieldOffset,yieldTarget,dt*1.1f);
                v.path.Sample(v.distance,out var roadPoint,out var roadForward);var roadRight=Vector3.Cross(Vector3.up,roadForward).normalized;var shifted=roadPoint+roadRight*nextYield;
                bool sideClear=true;
                for(int j=0;j<count;j++)
                {var other=vehicles[j];if(i==j||!other.body)continue;var delta=other.body.position-shifted;if(Mathf.Abs(delta.y)>1.5f)continue;if(Mathf.Abs(Vector3.Dot(delta,roadForward))<(v.length+other.length)*.5f+.4f&&Mathf.Abs(Vector3.Dot(delta,roadRight))<2.15f){sideClear=false;break;}}
                if(sideClear)v.yieldOffset=nextYield;
                if(v.yieldRequested&&v.reserved==null)desired=Mathf.Min(desired,yieldTarget>=3&&v.yieldOffset>=yieldTarget-.1f?0:2.5f);
                // Brake before turns by comparing forward tangents 12 m ahead.
                v.path.Sample(v.distance,out _,out var now);
                v.path.Sample(v.distance+12,out _,out var future);
                if(Vector3.Angle(now,future)>20)desired=Mathf.Min(desired,3.5f);
                if(!float.IsPositiveInfinity(free))desired=Mathf.Min(desired,Mathf.Sqrt(2*braking*Mathf.Max(0,free-.15f)));
                v.speed=Mathf.MoveTowards(v.speed,desired,(desired<v.speed?braking:acceleration)*dt);
                float move=v.speed*dt;
                if(!float.IsPositiveInfinity(free))move=Mathf.Min(move,Mathf.Max(0,free));
                if(move<.0001f)v.speed=0;
                v.distance+=move;
                if(!float.IsPositiveInfinity(v.obstacleFree))v.obstacleFree=Mathf.Max(0,v.obstacleFree-move);
            }
            SimulationStepped?.Invoke();
        }
        public void ResetClock(float seconds=0){Clock=Mathf.Max(0,seconds);foreach(var s in signals)if(s)s.SetClock(Clock);}
    }
}
