using System.Collections.Generic;
using UnityEngine;
using Unity.XR.CoreUtils;

namespace FirefighterVR.Station
{
    public sealed class StationSceneSetup : MonoBehaviour
    {
        public static StationSceneSetup Instance { get; private set; }
        public Transform driverSpawn,assistantSpawn;
        public Collider[] walkableSurfaces;
        public GameObject inspectionCamera;
        readonly Collider[] overlaps=new Collider[64];
        readonly RaycastHit[] sightHits=new RaycastHit[64];
        static readonly Vector3[] supportOffsets={Vector3.zero,Vector3.right,Vector3.left,Vector3.forward,Vector3.back};
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatic()=>Instance=null;
        void Awake(){Instance=this;if(FindFirstObjectByType<XROrigin>()&&inspectionCamera)inspectionCamera.SetActive(false);}
        void OnDestroy(){if(Instance==this)Instance=null;}
        public Transform Spawn(StationRole role)=>role==StationRole.Driver?driverSpawn:role==StationRole.Assistant?assistantSpawn:null;
        public bool HasSupport(Vector3 p,float radius=.28f)
        {
            foreach(var offset in supportOffsets)
            {bool found=false;foreach(var c in walkableSurfaces){if(c&&c.Raycast(new Ray(p+offset*radius+Vector3.up*.12f,Vector3.down),out var hit,.24f)&&hit.normal.y>.8f&&Mathf.Abs(hit.point.y-p.y)<.055f){found=true;break;}}if(!found)return false;}return true;
        }
        public bool ClearCapsule(Vector3 p,XROrigin rig)
        {
            float radius=rig&&rig.TryGetComponent<CharacterController>(out var controller)?Mathf.Max(.25f,controller.radius):.28f;
            float height=rig&&rig.Camera?Mathf.Clamp(rig.Camera.transform.position.y-rig.transform.position.y,.9f,2.2f):1.8f;
            height=Mathf.Max(height,radius*2+.1f);
            int count=Physics.OverlapCapsuleNonAlloc(p+Vector3.up*(radius+.045f),p+Vector3.up*(height-radius+.045f),radius,overlaps,~0,QueryTriggerInteraction.Ignore);
            if(count>=overlaps.Length)return false;
            for(int i=0;i<count;i++){var c=overlaps[i];if(!c)continue;if(rig&&c.transform.IsChildOf(rig.transform))continue;if(System.Array.IndexOf(walkableSurfaces,c)>=0)continue;var player=c.GetComponentInParent<XRMultiplayer.XRINetworkPlayer>();if(player&&player.IsOwner)continue;return false;}return true;
        }
        public bool Reachable(Vector3 p,XROrigin rig)
        {
            if(!rig||!rig.Camera)return false;
            Vector3 eye=rig.Camera.transform.position,target=p+Vector3.up*.12f;Vector3 delta=target-eye;
            int count=Physics.RaycastNonAlloc(eye,delta.normalized,sightHits,Mathf.Max(0,delta.magnitude-.08f),~0,QueryTriggerInteraction.Ignore);
            if(count>=sightHits.Length)return false;
            for(int i=0;i<count;i++){var h=sightHits[i];if(h.collider.transform.IsChildOf(rig.transform)||System.Array.IndexOf(walkableSurfaces,h.collider)>=0)continue;return false;}return true;
        }
        public bool ValidDestination(Vector3 p,XROrigin rig,bool requireSight=true)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z)&&HasSupport(p)&&ClearCapsule(p,rig)&&(!requireSight||Reachable(p,rig));
    }
}
