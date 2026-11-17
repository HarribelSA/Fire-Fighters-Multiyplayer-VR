using System.Collections.Generic;
using FirefighterVR;
using FirefighterVR.Station;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;

namespace CityNightPresentation.Mission
{
    [DefaultExecutionOrder(240)]
    public sealed class FireTruckSeating : MonoBehaviour
    {
        public FireTruckMission mission;
        public float seatedEyeHeight=1.35f;
        XROrigin origin;
        FirefighterAvatarDriver avatar;
        CharacterController capsule;
        Transform seat;
        Vector3 lastPosition;
        Quaternion lastRotation;
        bool capsuleWasEnabled;
        readonly Dictionary<LocomotionProvider,bool> locomotion=new Dictionary<LocomotionProvider,bool>();
        float retryAt;
        uint seatAcknowledged;
        bool presentationActivated;
        public Collider chassisCollider;
        readonly List<Collider> ignored=new List<Collider>();
        readonly Dictionary<GameObject,bool> hiddenStation=new Dictionary<GameObject,bool>();
        readonly Dictionary<Light,bool> hiddenLobbyLights=new Dictionary<Light,bool>();
        void ActivatePresentation()
        {
            if(presentationActivated)return;presentationActivated=true;
            using var timing=TransitionDiagnostics.Measure("city_presentation_hide_retained_scenes");
            // The existing station is retained additively; hide only its static scene instances locally.
            var station=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/FireStation/Scenes/Sample.unity");
            if(station.IsValid()&&station.isLoaded)foreach(var g in station.GetRootGameObjects())
            {
                if(g.GetComponentInChildren<Unity.Netcode.NetworkObject>(true)||g.GetComponentInChildren<XROrigin>(true)||g.GetComponentInChildren<FirefighterVR.StationBot.StationBotFollower>(true))continue;
                hiddenStation[g]=g.activeSelf;g.SetActive(false);
            }
            var lobby=UnityEngine.SceneManagement.SceneManager.GetSceneByPath("Assets/S1Test.unity");
            if(lobby.IsValid()&&lobby.isLoaded)foreach(var g in lobby.GetRootGameObjects())foreach(var l in g.GetComponentsInChildren<Light>(true))
                if(l.type==LightType.Directional&&!l.GetComponentInParent<XROrigin>()){hiddenLobbyLights[l]=l.enabled;l.enabled=false;}
            if(gameObject.scene.IsValid()&&gameObject.scene.isLoaded)UnityEngine.SceneManagement.SceneManager.SetActiveScene(gameObject.scene);
        }
        public void ReleaseForFireground()=>Release();
        void Update()
        {
            // Fireground dismount hands the rig back to normal standing tracking/locomotion.
            var fireground=CityNightPresentation.Fireground.FiregroundOperation.Instance;
            if(fireground&&fireground.LocalDismounted&&!CityNightPresentation.Fireground.MissionDebrief.CrewSeated){Release();return;}
            var session=StationSessionFlow.Instance;
            bool authorized=mission&&((mission.offlineDriverTesting&&(!Unity.Netcode.NetworkManager.Singleton||!Unity.Netcode.NetworkManager.Singleton.IsListening))||(session&&session.MissionSessionActive&&session.MissionState.stage>=CrewMissionStage.CityTransition));
            if(!authorized){Release();return;}
            ActivatePresentation();
            var role=mission?mission.LocalRole:StationRole.None;
            var target=role==StationRole.Driver?mission.driverSeat:role==StationRole.Assistant?mission.passengerSeat:null;
            if(!target){Release();return;}
            if(!origin&&Time.unscaledTime>=retryAt)
            {
                retryAt=Time.unscaledTime+1;
                var local=StationSessionFlow.Instance?StationSessionFlow.Instance.localRig:null;
                origin=local?local.origin:FindFirstObjectByType<XROrigin>();avatar=local?local.avatar:FindFirstObjectByType<FirefighterAvatarDriver>();
            }
            if(!origin||!origin.Camera)return;
            if(seat!=target)
            {
                Release(false);seat=target;
                foreach(var p in origin.GetComponentsInChildren<LocomotionProvider>(true))locomotion[p]=p.enabled;
                capsule=origin.GetComponent<CharacterController>();capsuleWasEnabled=capsule&&capsule.enabled;if(capsule)capsule.enabled=false;
                if(chassisCollider)foreach(var c in origin.GetComponentsInChildren<Collider>(true))if(c&&c!=chassisCollider){Physics.IgnoreCollision(c,chassisCollider,true);ignored.Add(c);}
                StationSeatPlacement.Place(origin,seat,avatar);
                lastPosition=seat.position;lastRotation=seat.rotation;
            }
            FollowSeat();
            foreach(var p in locomotion.Keys)if(p)p.enabled=false;
            if(capsule)capsule.enabled=false;
            if(avatar)avatar.SetSeat(seat);var seatRig=StationSessionFlow.Instance?StationSessionFlow.Instance.localRig:null;if(seatRig){seatRig.VehicleSeated=true;seatRig.StationSeated=false;}
            var flow=StationSessionFlow.Instance;
            if(flow&&flow.MissionSessionActive&&flow.MissionState.stage==CrewMissionStage.CityTransition&&seatAcknowledged!=flow.MissionState.epoch&&StationSpawnPlacement.TrackingReady(origin)&&Vector3.Distance(origin.Camera.transform.position,StationSeatPlacement.EyeTarget(seat,avatar))<.15f)
            {seatAcknowledged=flow.MissionState.epoch;TransitionDiagnostics.Mark("local_city_tracking_and_seat_verified");flow.AcknowledgeCitySeat(seatAcknowledged);}
        }
        void FollowSeat()
        {
            if(!seat||!origin)return;Quaternion delta=seat.rotation*Quaternion.Inverse(lastRotation);
            origin.transform.SetPositionAndRotation(seat.position+delta*(origin.transform.position-lastPosition),delta*origin.transform.rotation);
            lastPosition=seat.position;lastRotation=seat.rotation;
        }
        void LateUpdate(){if(!seat)return;FollowSeat();if(seat&&avatar)avatar.SetSeat(seat);}
        void Release(bool clear=true)
        {
            if(!seat)return;
            if(chassisCollider)foreach(var c in ignored)if(c)Physics.IgnoreCollision(c,chassisCollider,false);ignored.Clear();
            foreach(var p in locomotion)if(p.Key)p.Key.enabled=p.Value;locomotion.Clear();if(capsule)capsule.enabled=capsuleWasEnabled;
            if(avatar)avatar.Seated=false;var releaseRig=StationSessionFlow.Instance?StationSessionFlow.Instance.localRig:null;if(releaseRig)releaseRig.VehicleSeated=false;seat=null;if(clear){origin=null;avatar=null;}
        }
        void OnDisable(){Release();foreach(var p in hiddenStation)if(p.Key)p.Key.SetActive(p.Value);hiddenStation.Clear();foreach(var p in hiddenLobbyLights)if(p.Key)p.Key.enabled=p.Value;hiddenLobbyLights.Clear();}
    }
}
