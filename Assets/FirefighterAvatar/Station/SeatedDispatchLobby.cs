using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Samples.SpatialKeyboard;
using XRMultiplayer;

namespace FirefighterVR.Station
{
    [DefaultExecutionOrder(210)]
    public sealed class SeatedDispatchLobby : MonoBehaviour
    {
        [Serializable] public sealed class Console
        {
            public GameObject main,create,join,settings;
            public UnityEngine.CanvasGroup controls;
            public TMP_InputField roomName,roomCode;
            public TMP_Text feedback,role;
            public UnityEngine.UI.Button ready,seat;
            public UnityEngine.UI.Slider master,music,sfx;
        }
        public static SeatedDispatchLobby Instance {get;private set;}
        public StationLocalRig rig;
        public Transform leftSeat,rightSeat;
        public Console left,right;
        public TMP_Text shared;
        public AudioMixer mixer;
        public AudioSource[] musicSources;
        public AudioSource menuMusic;
        public GameObject[] legacyUI;
        [Tooltip("Standing spot before a station is chosen (FireCenterLobby DefaultSpawnPoint).")] public Transform defaultSpot;
        StationRole preference=StationRole.None,positioned=StationRole.None;
        public StationRole SelectedProfile=>preference;
        bool headAligned,wasInLobby;
        static byte SeatPoseId(Transform t){var p=t?t.GetComponent<FirefighterVR.SeatPose>():null;return p?p.id:(byte)255;}
        bool entryPlaced;
        bool explicitPreference;
        float nextClaim;
        float nextStatus;
        float nextPlayerCache;
        XRINetworkPlayer[] playerCache=Array.Empty<XRINetworkPlayer>();
        public bool InLobby=>SceneManager.GetActiveScene().path=="Assets/S1Test.unity";
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatics()=>Instance=null;
        void Awake(){Instance=this;Bind(left,StationRole.Driver);Bind(right,StationRole.Assistant);foreach(var g in legacyUI)if(g)g.SetActive(false);var n=PlayerHudNotification.Instance;if(n){n.stationaryMode=()=>InLobby;n.interceptText=message=>{if(!InLobby)return false;left.feedback.text=right.feedback.text=message;return true;};}}
        void OnEnable(){positioned=StationRole.None;headAligned=false;entryPlaced=false;}
        void OnDisable(){if(rig&&rig.avatar&&(!rig.permanentlySeatedDriver||rig.LocalRole!=StationRole.Driver))rig.avatar.Seated=false;}
        void OnDestroy(){if(Instance==this)Instance=null;var s=FindFirstObjectByType<SessionManager>();if(s)s.OnSessionFailed-=Failed;var n=PlayerHudNotification.Instance;if(n){n.stationaryMode=null;n.interceptText=null;}}
        void Start(){var s=FindFirstObjectByType<SessionManager>();if(s)s.OnSessionFailed+=Failed;}
        void Failed(string message){left.feedback.text=right.feedback.text=message;}
        void Bind(Console c,StationRole role)
        {
            c.seat.onClick.AddListener(()=>SelectSeat(role));
            c.ready.onClick.AddListener(()=>{var f=StationSessionFlow.Instance;if(f&&f.IsSpawned&&f.Lobby.Value.RoleOf(f.NetworkManager.LocalClientId)==role)f.SetLocalReady(!f.Lobby.Value.IsReady(f.NetworkManager.LocalClientId));});
            c.master.onValueChanged.AddListener(v=>{mixer.SetFloat("MainVolume",Db(v));SyncSliders(c.master==left.master?right.master:left.master,v);});
            c.sfx.onValueChanged.AddListener(v=>{mixer.SetFloat("SFXVolume",Db(v));SyncSliders(c.sfx==left.sfx?right.sfx:left.sfx,v);});
            c.music.onValueChanged.AddListener(v=>{foreach(var a in musicSources)if(a)a.volume=v;SyncSliders(c.music==left.music?right.music:left.music,v);});
            foreach(var field in new[]{c.roomName,c.roomCode}){var d=field.GetComponent<XRKeyboardDisplay>();if(d)d.inputField=field;}
        }
        static float Db(float v)=>v<=.0001f?-80:Mathf.Log10(v)*20;
        static void SyncSliders(UnityEngine.UI.Slider s,float v)=>s.SetValueWithoutNotify(v);
        public void SelectSeat(StationRole role)
        {var f=StationSessionFlow.Instance;if(f&&f.IsSpawned){var s=f.Lobby.Value;ulong owner=role==StationRole.Driver?s.Driver:s.Assistant;if(owner!=StationLobbyState.Unassigned&&owner!=f.NetworkManager.LocalClientId){left.feedback.text=right.feedback.text="Profile already reserved. Select the remaining control profile.";return;}}explicitPreference=true;preference=role;if(f&&f.IsSpawned)f.ChooseRole(role);}
        public void Page(int side,string page)
        {var c=side==0?left:right;c.main.SetActive(page=="main");c.create.SetActive(page=="create");c.join.SetActive(page=="join");c.settings.SetActive(page=="settings");}
        public void Play(int side)
        {var f=StationSessionFlow.Instance;if(f&&f.IsSpawned){if(f.CanRetrySpawn)f.RequestSpawnRetry();Page(side,"main");return;}Page(side,"create");}
        public void ResetSeatPlacement(){positioned=StationRole.None;headAligned=false;entryPlaced=false;}
        public void Create(int side)
        {var c=side==0?left:right;var n=XRINetworkGameManager.Instance;if(!n){c.feedback.text="Session system is initializing.";return;}c.feedback.text="Creating room…";n.CreateNewLobby(c.roomName.text.Trim(),true,2);}
        public void Join(int side)
        {var c=side==0?left:right;string code=c.roomCode.text.Trim();if(string.IsNullOrWhiteSpace(code)){c.feedback.text="Enter a room code first.";return;}var n=XRINetworkGameManager.Instance;if(!n){c.feedback.text="Session system is initializing.";return;}c.feedback.text="Joining room…";n.JoinLobbyByCode(code);}
        public void CopyCode(){string code=XRINetworkGameManager.ConnectedRoomCode;if(!string.IsNullOrEmpty(code))GUIUtility.systemCopyBuffer=code;}
        public void Exit()
        {var n=XRINetworkGameManager.Instance;if(n&&Unity.Netcode.NetworkManager.Singleton&&Unity.Netcode.NetworkManager.Singleton.IsListening){n.Disconnect();return;}
        #if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying=false;
        #else
            Application.Quit();
        #endif
        }
        public void KeyVolume(int side,int channel,float delta)
        {var c=side==0?left:right;var s=channel==0?c.master:channel==1?c.music:c.sfx;s.value=Mathf.Clamp01(s.value+delta);}
        void Update()
        {
            if(!InLobby)
            {
                if(menuMusic&&menuMusic.isPlaying)menuMusic.Stop();
                // Leaving the lobby: release the lobby chair unless a mission seat (dispatch/truck) took over.
                if(wasInLobby){wasInLobby=false;entryPlaced=false;if(rig&&rig.avatar&&(rig.avatar.SeatId==SeatPoseId(leftSeat)||rig.avatar.SeatId==SeatPoseId(rightSeat)))rig.avatar.Seated=false;}
                return;
            }
            wasInLobby=true;
            foreach(var g in legacyUI)if(g&&g.activeSelf)g.SetActive(false);
            var resetter=rig.origin.GetComponent<CharacterResetter>();if(resetter)resetter.enabled=false;
            var f=StationSessionFlow.Instance;bool connected=f&&f.IsSpawned;var state=connected?f.Lobby.Value:StationLobbyState.Initial;
            if(!explicitPreference&&(!connected||state.RoleOf(f.NetworkManager.LocalClientId)==StationRole.None)){var mode=XRInputModalityManager.currentInputMode.Value;if(mode==XRInputModalityManager.InputMode.MotionController)preference=StationRole.Assistant;else if(mode==XRInputModalityManager.InputMode.TrackedHand)preference=StationRole.Driver;}
            ulong id=connected?f.NetworkManager.LocalClientId:StationLobbyState.Unassigned;
            var role=connected?state.RoleOf(id):preference;
            if(connected&&role==StationRole.None&&Time.unscaledTime>nextClaim)
            {nextClaim=Time.unscaledTime+1;var desired=AvailableProfile(state,id,explicitPreference?preference:StationRole.None,explicitPreference);if(desired!=StationRole.None){f.ChooseRole(desired);if(!explicitPreference)preference=desired;}else if(preference!=StationRole.None)left.feedback.text=right.feedback.text="Select a control profile. An explicit occupied profile is never reassigned silently.";}
            if(role==StationRole.None)role=preference;
            var anchor=role==StationRole.Driver?leftSeat:rightSeat;
            var head=InputDevices.GetDeviceAtXRNode(XRNode.Head);bool tracked=head.TryGetFeatureValue(CommonUsages.isTracked,out bool t)&&t;
            if(!entryPlaced||positioned!=role||(!headAligned&&tracked))
            {
                // Both stations are seated (Driver and Firefighter/Assistant); no role yet = standing at the default spot.
                if(role!=StationRole.None)StationSeatPlacement.Place(rig.origin,anchor,rig.avatar);
                else {var cc=rig.origin.GetComponent<CharacterController>();if(cc)cc.enabled=false;if(defaultSpot)StationSpawnPlacement.Place(rig.origin,defaultSpot);else StationSpawnPlacement.Place(rig.origin,(leftSeat.position+rightSeat.position)*.5f,leftSeat.rotation);}
                positioned=role;headAligned=tracked;entryPlaced=true;
            }
            if(role!=StationRole.None)rig.avatar.SetSeat(anchor);else rig.avatar.Seated=false;
            if(Time.unscaledTime<nextStatus)return;nextStatus=Time.unscaledTime+.1f;
            if(connected&&Time.unscaledTime>=nextPlayerCache){nextPlayerCache=Time.unscaledTime+1;playerCache=FindObjectsByType<XRINetworkPlayer>(FindObjectsSortMode.None);}
            ApplyConsole(left,StationRole.Driver,role,state,id,connected);ApplyConsole(right,StationRole.Assistant,role,state,id,connected);
            string code=connected?XRINetworkGameManager.ConnectedRoomCode:"—";
            string name=connected?XRINetworkGameManager.ConnectedRoomName.Value:"NO ACTIVE ROOM";
            shared.text="EMERGENCY RESPONSE TEAM\n<size=22>SAUDI CIVIL DEFENSE · OPERATIONS</size>\n\n<size=25>"+Safe(name)+"\nROOM CODE  "+Safe(code)+"</size>\n\n"+Describe(state.Driver,state.DriverReady,"DRIVER · HAND TRACKING + G29")+"\n\n"+Describe(state.Assistant,state.AssistantReady,"ASSISTANT · QUEST CONTROLLERS")+"\n\n<color=#78E7B9>"+(state.Phase==StationPhase.Countdown?"MISSION STARTING IN "+Mathf.Clamp(Mathf.CeilToInt((float)(state.Deadline-f.SharedTime)),0,10)+"…":state.Phase==StationPhase.Blocked?"TEAM DISCONNECTED · LEAVE ROOM":state.Phase>=StationPhase.Fading?"PREPARING STATION…":"WAITING FOR BOTH PLAYERS")+"</color>";
        }
        void ApplyConsole(Console c,StationRole owner,StationRole role,StationLobbyState state,ulong id,bool connected)
        {
            bool mine=role==owner;c.controls.interactable=mine;c.controls.blocksRaycasts=mine;c.controls.alpha=mine?1:.45f;
            ulong occupant=(owner==StationRole.Driver?state.Driver:state.Assistant);c.seat.interactable=state.Editable&&(!connected||occupant==StationLobbyState.Unassigned||occupant==id);
            c.ready.interactable=mine&&connected&&state.RoleOf(id)==owner&&state.Editable&&(state.IsReady(id)||rig.InputAvailable);
            c.role.text=(owner==StationRole.Driver?"DRIVER · HAND TRACKING + G29":"ASSISTANT · QUEST CONTROLLERS")+"\n"+(mine?rig.InputStatus:"Reserved for the other control profile")+(mine&&owner==StationRole.Assistant?"\nMovement: "+rig.assistantMovement:"");
            if(connected&&mine)c.feedback.text=state.Phase==StationPhase.Blocked?Safe(StationSessionFlow.Instance.Failure)+"\n"+(StationSessionFlow.Instance.CanRetrySpawn?"Select Play to reset Ready and retry.":"Use Exit / Leave Room, then reconnect."):"Room: "+Safe(XRINetworkGameManager.ConnectedRoomName.Value)+"  Code: "+Safe(XRINetworkGameManager.ConnectedRoomCode)+"\n"+(state.IsReady(id)?"Waiting for the other player…":"Connected · press Ready when prepared.");
            if(connected&&mine&&state.RoleOf(id)==StationRole.None)c.feedback.text="SELECT CONTROL PROFILE BEFORE READY\nAvailable: "+(state.Driver==StationLobbyState.Unassigned?"Hand Tracking + G29 · Driver":"")+(state.Driver==StationLobbyState.Unassigned&&state.Assistant==StationLobbyState.Unassigned?" / ":"")+(state.Assistant==StationLobbyState.Unassigned?"Quest Controllers · Assistant":"");
        }
        static string Safe(string value)=>string.IsNullOrEmpty(value)?"—":value.Replace("<","‹").Replace(">","›");
        public static StationRole AvailableProfile(StationLobbyState state,ulong id,StationRole selected,bool explicitSelection)
        {
            if(selected!=StationRole.None&&(selected==StationRole.Driver?state.Driver:state.Assistant)==id)return selected;
            if(selected!=StationRole.None&&(selected==StationRole.Driver?state.Driver:state.Assistant)==StationLobbyState.Unassigned)return selected;
            if(explicitSelection)return StationRole.None;
            if(state.Driver!=StationLobbyState.Unassigned&&state.Assistant==StationLobbyState.Unassigned)return StationRole.Assistant;
            if(state.Assistant!=StationLobbyState.Unassigned&&state.Driver==StationLobbyState.Unassigned)return StationRole.Driver;
            return StationRole.None;
        }
        string Describe(ulong id,bool ready,string label)
        {if(id==StationLobbyState.Unassigned)return "<size=24>"+label+"\nAVAILABLE · Not Ready</size>";var player=playerCache.FirstOrDefault(p=>p&&p.IsSpawned&&p.OwnerClientId==id);return "<size=24>"+label+"\n"+Safe(player?player.playerName:"Firefighter")+" · Connected · "+(ready?"READY":"Not Ready")+"</size>";}
    }
}
