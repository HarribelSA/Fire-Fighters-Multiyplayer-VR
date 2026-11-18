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
        [Tooltip("Real-time emergency lights (no shadows) driven with the roof lenses.")] public Light[] redLights=new Light[0],blueLights=new Light[0];
        public float lightIntensity=9;
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
            if(active&&!mission.CanActivateEmergency)
            {
                // Tell the Driver why the handle sprang back (previously this was silent).
                string why=!mission.CrewReady?"crew not ready yet":mission.HasArrived?"already arrived":mission.HasFailed?"mission stopped":mission.controller&&mission.controller.SpeedKmh>=1.2f?"truck still moving ("+mission.controller.SpeedKmh.ToString("0")+" km/h)":"not stopped at the designated red light";
                InteractionStatus="SIREN UNAVAILABLE · "+why.ToUpperInvariant();
                Debug.Log("[Siren] Pull rejected: "+why,this);
                var hud=XRMultiplayer.PlayerHudNotification.Instance;if(hud)hud.ShowText(mission.sirenAnytime?"SIREN UNAVAILABLE · "+why.ToUpperInvariant():"SIREN: STOP COMPLETELY AT THE RED LIGHT, THEN PULL",4);
                return false;
            }
            mission.RequestSiren(active);InteractionStatus=active?"EMERGENCY PRIORITY REQUESTED":"EMERGENCY RESET";Debug.Log("[Siren] Pull "+(active?"accepted: emergency ON":"released: emergency OFF"),this);return true;
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
            int phase=active?Pattern(clock):-1;if(phase==flash)return;flash=phase;
            if(redLens)redLens.sharedMaterial=phase==1?redOn:lensOff;if(blueLens)blueLens.sharedMaterial=phase==2?blueOn:lensOff;
            foreach(var l in redLights)if(l){l.enabled=phase==1;l.intensity=lightIntensity;}foreach(var l in blueLights)if(l){l.enabled=phase==2;l.intensity=lightIntensity;}
        }
        // Civil-defence style double flash: red-red, blue-blue, shared server clock so both peers match.
        static int Pattern(double clock){double t=clock%.8;return t<.08||(t>=.14&&t<.22)?1:(t>=.4&&t<.48)||(t>=.54&&t<.62)?2:0;}
        void OnDisable(){BeingPulled=false;foreach(var l in redLights)if(l)l.enabled=false;foreach(var l in blueLights)if(l)l.enabled=false;if(siren)siren.Stop();if(redLens)redLens.sharedMaterial=lensOff;if(blueLens)blueLens.sharedMaterial=lensOff;flash=-1;}
    }
}
