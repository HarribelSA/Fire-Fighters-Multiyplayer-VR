using UnityEngine;
using UnityEngine.XR.Content.Interaction;

namespace CityNightPresentation.Mission
{
    public sealed class FireTruckEmergency : MonoBehaviour
    {
        public FireTruckMission mission;
        public XRLever lever;
        public AudioSource siren;
        public Renderer redLens,blueLens;
        public Material redOn,blueOn,lensOff;
        public Transform pullMount,pullHandle;
        public Vector3 closedLocalPosition=new Vector3(0,0,-.08f),pullAxis=Vector3.back;
        [Min(.05f)]public float pullTravel=.18f;
        public bool BeingPulled {get;set;}
        public float PullValue {get;private set;}
        public string InteractionStatus {get;private set;}="STOP AT THE RED SIGNAL · GRAB AND PULL";
        bool last,warnedAudio;
        float retryAudio;
        int flash=-1;
        void OnEnable(){if(lever)lever.enabled=false;if(siren&&siren.clip&&siren.clip.loadState==AudioDataLoadState.Unloaded)siren.clip.LoadAudioData();SetPullValue(mission&&mission.SirenActive?1:0);}
        public void SetPullValue(float value){PullValue=Mathf.Clamp01(value);if(pullHandle)pullHandle.localPosition=closedLocalPosition+pullAxis.normalized*pullTravel*PullValue;}
        public bool RequestFromPull(bool active)
        {
            if(!mission||mission.LocalRole!=FirefighterVR.Station.StationRole.Driver)return false;
            if(active&&!mission.CanActivateEmergency){InteractionStatus="STOP SAFELY AT THE DESIGNATED RED SIGNAL BEFORE PULLING";return false;}
            mission.RequestSiren(active);InteractionStatus=active?"EMERGENCY PRIORITY REQUESTED":"EMERGENCY RESET";return true;
        }
        public bool RequestFromBotDriver(bool active)
        {if(!mission||!mission.RequestBotEmergency(active))return false;SetPullValue(active?1:0);InteractionStatus=active?"BOT DRIVER · VALIDATED PRIORITY REQUEST":"BOT DRIVER · EMERGENCY RESET";return true;}
        void Update()
        {
            bool active=mission&&mission.SirenActive;
            if(active!=last)
            {
                last=active;if(!BeingPulled)SetPullValue(active?1:0);
                if(siren&&!active)siren.Stop();
            }
            if(lever&&lever.enabled)lever.enabled=false;
            if(active&&Time.unscaledTime>=retryAudio&&siren&&!siren.isPlaying)
            {
                retryAudio=Time.unscaledTime+.5f;
                if(siren.clip&&siren.enabled&&siren.gameObject.activeInHierarchy&&siren.clip.loadState==AudioDataLoadState.Loaded){siren.loop=true;siren.Play();warnedAudio=false;}
                else if(!warnedAudio){warnedAudio=true;Debug.LogWarning("Emergency siren unavailable: check clip load state/source enablement. No playback success claimed.",this);}
            }
            double clock=mission&&mission.IsSpawned?mission.NetworkManager.ServerTime.Time:Time.unscaledTimeAsDouble;
            int phase=active?(int)(clock*6)%2:-1;if(phase==flash)return;flash=phase;
            if(redLens)redLens.sharedMaterial=phase==0?redOn:lensOff;if(blueLens)blueLens.sharedMaterial=phase==1?blueOn:lensOff;
        }
        void OnDisable(){BeingPulled=false;if(siren)siren.Stop();if(redLens)redLens.sharedMaterial=lensOff;if(blueLens)blueLens.sharedMaterial=lensOff;flash=-1;}
    }
}
