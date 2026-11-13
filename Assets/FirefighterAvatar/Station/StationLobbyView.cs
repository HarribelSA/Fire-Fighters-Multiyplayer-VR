using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FirefighterVR.Station
{
    public sealed class StationLobbyView : MonoBehaviour
    {
        public Canvas canvas;
        public TMP_Text status,driverStatus,assistantStatus,countdown;
        public Button chooseDriver,chooseAssistant,driverReady,assistantReady;
        public Transform head;
        public StationLocalRig rig;
        void Awake()
        {
            chooseDriver.onClick.AddListener(()=>StationSessionFlow.Instance?.ChooseRole(StationRole.Driver));
            chooseAssistant.onClick.AddListener(()=>StationSessionFlow.Instance?.ChooseRole(StationRole.Assistant));
            driverReady.onClick.AddListener(Toggle);assistantReady.onClick.AddListener(Toggle);
        }
        void Toggle(){var f=StationSessionFlow.Instance;if(f&&f.IsSpawned)f.SetLocalReady(!f.Lobby.Value.IsReady(f.NetworkManager.LocalClientId));}
        void LateUpdate()
        {
            if(head){var forward=Vector3.ProjectOnPlane(head.forward,Vector3.up).normalized;if(forward.sqrMagnitude>.1f){var p=head.position+forward*1.4f+Vector3.down*.12f;transform.SetPositionAndRotation(Vector3.Lerp(transform.position,p,1-Mathf.Exp(-Time.unscaledDeltaTime*8)),Quaternion.LookRotation(forward,Vector3.up));}}
            var f=StationSessionFlow.Instance;bool connected=f&&f.IsSpawned;var state=connected?f.Lobby.Value:StationLobbyState.Initial;
            ulong local=connected?f.NetworkManager.LocalClientId:StationLobbyState.Unassigned;
            canvas.enabled=state.Phase!=StationPhase.StationReady || (rig&&!rig.InputAvailable);
            chooseDriver.interactable=connected&&state.Editable&&(state.Driver==StationLobbyState.Unassigned||state.Driver==local);
            chooseAssistant.interactable=connected&&state.Editable&&(state.Assistant==StationLobbyState.Unassigned||state.Assistant==local);
            driverReady.interactable=connected&&state.Editable&&state.Driver==local&&(state.DriverReady||(rig&&rig.InputAvailable));
            assistantReady.interactable=connected&&state.Editable&&state.Assistant==local&&(state.AssistantReady||(rig&&rig.InputAvailable));
            driverStatus.text="PLAYER 1 — DRIVER / HANDS\n"+Describe(state.Driver,state.DriverReady,local);
            assistantStatus.text="PLAYER 2 — ASSISTANT / CONTROLLERS\n"+Describe(state.Assistant,state.AssistantReady,local);
            countdown.text=state.Phase==StationPhase.Countdown?Mathf.Clamp(Mathf.CeilToInt((float)(state.Deadline-f.SharedTime)),0,10).ToString():state.Phase==StationPhase.Fading?"0":"";
            if(!connected)status.text="Join the same room using the existing room menu.";
            else if(state.Phase==StationPhase.Blocked)status.text="Team transition interrupted. Reconnect from the lobby.\n"+(f.Failure??"");
            else if(state.Phase==StationPhase.Loading||state.Phase==StationPhase.AwaitingSpawn||state.Phase==StationPhase.Fading)status.text="Loading the station for both firefighters…";
            else if(state.Phase==StationPhase.StationReady)status.text=rig?rig.InputStatus:"Waiting for local XR tracking.";
            else if(state.Phase==StationPhase.Countdown)status.text="Both ready — select Ready again to cancel.";
            else if(state.IsReady(local))status.text="Waiting for the other player...";
            else if(state.RoleOf(local)==StationRole.None)status.text="Choose your role. Each role belongs to one firefighter.";
            else status.text=rig&&rig.InputAvailable?"Ready when you are. Both roles must be connected.":rig?rig.InputStatus:"Waiting for XR setup.";
        }
        static string Describe(ulong id,bool ready,ulong local)=>id==StationLobbyState.Unassigned?"Not connected / role unassigned":(id==local?"You — ":"Other firefighter — ")+(ready?"Ready":"Not Ready");
    }
}
