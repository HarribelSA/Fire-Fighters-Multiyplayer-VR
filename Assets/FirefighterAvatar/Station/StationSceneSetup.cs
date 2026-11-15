using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FirefighterVR.Station
{
    public sealed class StationSceneSetup : MonoBehaviour
    {
        public static StationSceneSetup Instance { get; private set; }
        public Transform driverSpawn,assistantSpawn;
        public Transform botSpawn;
        public Collider[] walkableSurfaces;
        public MeshCollider[] architecturalFloors;
        public GameObject inspectionCamera;
        [Min(0)] public float destinationClearancePadding=.01f;
        readonly Collider[] overlaps=new Collider[64];
        readonly RaycastHit[] sightHits=new RaycastHit[64];
        Vector3[] rayPoints=new Vector3[64];
        XRMultiplayer.XRINetworkPlayer[] players=System.Array.Empty<XRMultiplayer.XRINetworkPlayer>();
        FirefighterVR.StationBot.StationBotFollower[] bots=System.Array.Empty<FirefighterVR.StationBot.StationBotFollower>();
        double nextOccupantCheck;
        readonly Dictionary<MeshCollider,(int[] ends,bool[] floors)> floorMaterials=new Dictionary<MeshCollider,(int[],bool[])>();
        static readonly Vector3[] supportOffsets={Vector3.zero,Vector3.right,Vector3.left,Vector3.forward,Vector3.back,new Vector3(.707f,0,.707f),new Vector3(-.707f,0,.707f),new Vector3(.707f,0,-.707f),new Vector3(-.707f,0,-.707f)};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatic()=>Instance=null;
        void Awake(){using var timing=TransitionDiagnostics.Measure("station_scene_awake");Instance=this;if(FindFirstObjectByType<XROrigin>()&&inspectionCamera)inspectionCamera.SetActive(false);}
        void OnDestroy(){if(Instance==this)Instance=null;}
        public Transform Spawn(StationRole role)=>role==StationRole.Driver?driverSpawn:role==StationRole.Assistant?assistantSpawn:null;
        public float CapsuleRadius(XROrigin rig)=>rig&&rig.TryGetComponent<CharacterController>(out var c)?Mathf.Max(.08f,c.radius*Mathf.Max(rig.Origin.transform.lossyScale.x,rig.Origin.transform.lossyScale.z)+destinationClearancePadding):.28f;
        public float FootprintRadius(Vector3 p,XROrigin rig)=>p.x>15.2f&&p.x<19.6f&&p.z>.65f&&p.z<6.5f&&p.y>.15f?CapsuleRadius(rig):Mathf.Max(.25f,CapsuleRadius(rig));
        public bool ArchitecturalFloor(Vector3 p)
        {
            if(architecturalFloors==null||architecturalFloors.Length==0)return false;
            foreach(var c in architecturalFloors)
            {
                if(!c||!c.Raycast(new Ray(p+Vector3.up*.08f,Vector3.down),out var hit,.16f)||hit.normal.y<.8f||Mathf.Abs(hit.point.y-p.y)>.055f)continue;
                if(!floorMaterials.TryGetValue(c,out var info))
                {
                    var mesh=c.sharedMesh;var renderer=c.GetComponent<Renderer>();var mats=renderer?renderer.sharedMaterials:System.Array.Empty<Material>();
                    var ends=new int[mesh.subMeshCount];var floors=new bool[ends.Length];int end=0;
                    for(int i=0;i<ends.Length;i++){end+=(int)mesh.GetIndexCount(i)/3;ends[i]=end;string name=i<mats.Length&&mats[i]?mats[i].name:"";
                        floors[i]=name.Contains("Floor")||name.Contains("Concrete")||name.Contains("Stair")||name.Contains("Rubber")||name=="MetalPlate";}
                    info=(ends,floors);floorMaterials[c]=info;
                }
                for(int i=0;i<info.ends.Length;i++)if(hit.triangleIndex<info.ends[i]){if(info.floors[i])return true;break;}
            }
            return false;
        }
        public bool HasSupport(Vector3 p,float radius=.28f)
        {
            foreach(var offset in supportOffsets)
            {bool found=false;foreach(var c in walkableSurfaces){if(c&&c.Raycast(new Ray(p+offset*radius+Vector3.up*.12f,Vector3.down),out var hit,.24f)&&hit.normal.y>.8f&&Mathf.Abs(hit.point.y-p.y)<.055f){found=true;break;}}if(!found)return false;}return true;
        }
        bool Ignore(Collider c,XROrigin rig)
        {
            if(!c||c.isTrigger)return true;
            if(rig&&c.transform.IsChildOf(rig.Origin.transform))return true;
            var player=c.GetComponentInParent<XRMultiplayer.XRINetworkPlayer>();
            if(player)return player.IsOwner;
            // Additive lobby architecture can overlap station world coordinates. It is not
            // destination geometry; remote player occupancy is still checked explicitly.
            return c.gameObject.scene!=gameObject.scene;
        }
        public bool Occupied(Vector3 p,XROrigin rig,float radius=.28f)
        {
            if(Time.realtimeSinceStartupAsDouble>=nextOccupantCheck)
            {
                players=FindObjectsByType<XRMultiplayer.XRINetworkPlayer>(FindObjectsSortMode.None);
                bots=FindObjectsByType<FirefighterVR.StationBot.StationBotFollower>(FindObjectsSortMode.None);
                nextOccupantCheck=Time.realtimeSinceStartupAsDouble+.35;
            }
            foreach(var player in players)if(player&&player.IsSpawned&&!player.IsOwner&&player.head)
            {var delta=player.head.position-p;if(delta.y>.5f&&delta.y<2.5f&&Vector3.ProjectOnPlane(delta,Vector3.up).sqrMagnitude<Mathf.Pow(radius+.32f,2))return true;}
            foreach(var bot in bots)if(bot&&bot.isActiveAndEnabled&&bot.gameObject.scene==gameObject.scene)
            {var delta=bot.transform.position-p;if(Mathf.Abs(delta.y)<1&&Vector3.ProjectOnPlane(delta,Vector3.up).sqrMagnitude<Mathf.Pow(radius+.28f,2))return true;}
            return false;
        }
        public bool ClearCapsule(Vector3 p,XROrigin rig)
        {
            float radius=CapsuleRadius(rig);
            float height=rig&&rig.Camera?Mathf.Clamp(rig.CameraInOriginSpaceHeight,1.65f,2.2f):1.8f;
            height=Mathf.Max(height,radius*2+.1f);
            int count=gameObject.scene.GetPhysicsScene().OverlapCapsule(p+Vector3.up*(radius+.045f),p+Vector3.up*(height-radius+.045f),radius,overlaps,~0,QueryTriggerInteraction.Ignore);
            if(count>=overlaps.Length)return false;
            for(int i=0;i<count;i++){var c=overlaps[i];if(Ignore(c,rig))continue;if(System.Array.IndexOf(walkableSurfaces,c)>=0)continue;return false;}return !Occupied(p,rig,radius);
        }
        public bool Reachable(Vector3 p,XROrigin rig)
        {
            if(!rig||!rig.Camera)return false;
            return ReachableFrom(rig.Camera.transform.position,p,Mathf.Clamp(rig.CameraInOriginSpaceHeight,1.2f,2.1f),rig);
        }
        public bool ReachableFrom(Vector3 eye,Vector3 p,float height=1.7f,XROrigin rig=null)
        {
            Vector3 target=p+Vector3.up*height;Vector3 delta=target-eye;
            int count=gameObject.scene.GetPhysicsScene().Raycast(eye,delta.normalized,sightHits,Mathf.Max(0,delta.magnitude-.025f),~0,QueryTriggerInteraction.Ignore);
            if(count>=sightHits.Length)return false;
            for(int i=0;i<count;i++){var h=sightHits[i];if(Ignore(h.collider,rig))continue;return false;}return true;
        }
        public bool IsSafeStairLanding(Vector3 p)=>!(p.x>15.2f&&p.x<19.6f&&p.z>.65f&&p.z<6.5f&&p.y>.15f)||Mathf.Abs(p.y-3.3646f)<.045f||Mathf.Abs(p.y-6.383f)<.045f||Mathf.Abs(p.y-11.005f)<.045f;
        public bool ValidDestination(Vector3 p,XROrigin rig,bool requireSight=true)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)&&IsSafeStairLanding(p)&&ArchitecturalFloor(p)&&HasSupport(p,FootprintRadius(p,rig))&&ClearCapsule(p,rig)&&(!requireSight||Reachable(p,rig));
        public bool ValidRayDestination(RaycastHit destination,XROrigin rig,XRRayInteractor ray)
        {
            if(!ray||destination.normal.y<.8f||!ValidDestination(destination.point,rig))return false;
            if(!ray.GetLinePoints(ref rayPoints,out int count))return false;
            if(!ray.TryGetCurrent3DRaycastHit(out _,out int endpoint))return false;
            for(int i=1;i<count&&i<=endpoint;i++)
            {
                var end=i==endpoint?destination.point:rayPoints[i];var delta=end-rayPoints[i-1];if(delta.sqrMagnitude<.000001f)continue;
                int hits=gameObject.scene.GetPhysicsScene().Raycast(rayPoints[i-1],delta.normalized,sightHits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
                if(hits==sightHits.Length)return false;
                for(int h=0;h<hits;h++)if(!Ignore(sightHits[h].collider,rig)&&Vector3.Distance(sightHits[h].point,destination.point)>.07f)return false;
            }
            return true;
        }
    }
}
