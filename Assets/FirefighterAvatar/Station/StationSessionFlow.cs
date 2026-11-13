using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirefighterVR.Station
{
    // Added to the existing firefighter coordinator NetworkObject in S1Test.
    // S1Test stays loaded additively so all existing ownership, IK and session objects survive.
    public sealed class StationSessionFlow : NetworkBehaviour
    {
        public static StationSessionFlow Instance { get; private set; }
        public readonly NetworkVariable<StationLobbyState> Lobby = new NetworkVariable<StationLobbyState>(StationLobbyState.Initial,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        public GameObject[] lobbyOnlyRoots;
        public StationLocalRig localRig;
        public string stationSceneName="Sample";
        public readonly NetworkVariable<FixedString128Bytes> SharedFailure=new NetworkVariable<FixedString128Bytes>(default,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        public readonly NetworkVariable<bool> SpawnRetryAvailable=new NetworkVariable<bool>(false,NetworkVariableReadPermission.Everyone,NetworkVariableWritePermission.Owner);
        public string Failure => SharedFailure.Value.ToString();
        public bool CanRetrySpawn => IsSpawned&&Lobby.Value.Phase==StationPhase.Blocked&&SpawnRetryAvailable.Value;
        public double SharedTime => IsSpawned ? NetworkManager.ServerTime.Time : 0;
        public bool Authority => IsSpawned && HasAuthority && (!NetworkManager.DistributedAuthorityMode || NetworkManager.LocalClient.IsSessionOwner);
        uint issuedEpoch;
        bool wasAuthority;
        double nextMembershipCheck;
        bool[] lobbyActiveStates;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatic()=>Instance=null;
        void Awake(){Instance=this;lobbyActiveStates=lobbyOnlyRoots.Select(g=>g&&g.activeSelf).ToArray();}
        public override void OnNetworkSpawn()
        {
            Instance=this;
            NetworkManager.OnClientDisconnectCallback+=Disconnected;
            NetworkManager.SceneManager.OnLoadEventCompleted+=LoadCompleted;
            if(Authority && NetworkManager.DistributedAuthorityMode)NetworkObject.SetOwnershipStatus(NetworkObject.OwnershipStatus.SessionOwner,true);
            wasAuthority=Authority;
            if(Authority){Lobby.Value=StationLobbyState.Initial;issuedEpoch=0;SharedFailure.Value=default;SpawnRetryAvailable.Value=false;}
        }
        public override void OnNetworkDespawn()
        {
            if(NetworkManager){NetworkManager.OnClientDisconnectCallback-=Disconnected;if(NetworkManager.SceneManager!=null)NetworkManager.SceneManager.OnLoadEventCompleted-=LoadCompleted;}
            if(localRig)localRig.ResetToLobby();
        }
        public override void OnDestroy(){if(Instance==this)Instance=null;base.OnDestroy();}
        bool Connected(ulong id)=>NetworkManager.ConnectedClientsIds.Contains(id);
        public void ChooseRole(StationRole role){if(IsSpawned)ClaimRoleRpc(role);}
        public void SetLocalReady(bool ready){if(IsSpawned)ReadyRpc(ready,localRig&&localRig.InputAvailable);}
        public void AcknowledgeSpawn(uint epoch){if(IsSpawned)SpawnedRpc(epoch);}
        public void ReportSpawnFailure(uint epoch){if(IsSpawned)SpawnFailedRpc(epoch);}
        public void RequestSpawnRetry(){if(IsSpawned)RetrySpawnRpc();}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void SpawnFailedRpc(uint epoch,RpcParams p=default)
        {if(Authority&&Connected(p.Receive.SenderClientId)&&Lobby.Value.RoleOf(p.Receive.SenderClientId)!=StationRole.None&&Lobby.Value.Phase==StationPhase.AwaitingSpawn&&Lobby.Value.Epoch==epoch)Block("Spawn clearance failed. Clear the destination, then select Play to reset Ready.");}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void RetrySpawnRpc(RpcParams p=default)
        {
            if(!Authority||!CanRetrySpawn||!Connected(p.Receive.SenderClientId)||Lobby.Value.RoleOf(p.Receive.SenderClientId)==StationRole.None||NetworkManager.ConnectedClientsIds.Count!=2)return;
            var state=Lobby.Value;state.DriverReady=state.AssistantReady=false;state.Arrivals=0;state.CancelCountdown();Lobby.Value=state;SharedFailure.Value=default;
        }
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void ClaimRoleRpc(StationRole role,RpcParams p=default)
        {if(!Authority||!Connected(p.Receive.SenderClientId))return;var state=Lobby.Value;if(state.Claim(p.Receive.SenderClientId,role))Lobby.Value=state;}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void ReadyRpc(bool ready,bool inputAvailable,RpcParams p=default)
        {if(!Authority||!Connected(p.Receive.SenderClientId))return;var state=Lobby.Value;if(state.SetReady(p.Receive.SenderClientId,ready,inputAvailable,NetworkManager.ConnectedClientsIds.Count,SharedTime))Lobby.Value=state;}
        [Rpc(SendTo.Authority,InvokePermission=RpcInvokePermission.Everyone)]
        void SpawnedRpc(uint epoch,RpcParams p=default)
        {
            if(!Authority||!Connected(p.Receive.SenderClientId))return;var state=Lobby.Value;
            if(state.Phase!=StationPhase.AwaitingSpawn||state.Epoch!=epoch)return;
            if(p.Receive.SenderClientId==state.Driver)state.Arrivals|=1;else if(p.Receive.SenderClientId==state.Assistant)state.Arrivals|=2;else return;
            if(state.Arrivals==3&&NetworkManager.ConnectedClientsIds.Count==2){state.Phase=StationPhase.StationReady;state.Deadline=0;}
            Lobby.Value=state;
        }
        void Disconnected(ulong id)
        {if(!Authority)return;var state=Lobby.Value;state.Remove(id);Lobby.Value=state;SharedFailure.Value="A firefighter disconnected. Use Exit / Leave Room to reconnect safely.";}
        void Update()
        {
            if(!IsSpawned)return;
            if(Authority&&!wasAuthority){var promoted=Lobby.Value;if(promoted.Phase==StationPhase.Countdown){promoted.DriverReady=promoted.AssistantReady=false;promoted.CancelCountdown();Lobby.Value=promoted;}}
            wasAuthority=Authority;if(!Authority)return;
            var state=Lobby.Value;
            if(Time.unscaledTimeAsDouble>=nextMembershipCheck){nextMembershipCheck=Time.unscaledTimeAsDouble+.2;if(state.Driver!=StationLobbyState.Unassigned&&!Connected(state.Driver))state.Remove(state.Driver);if(state.Assistant!=StationLobbyState.Unassigned&&!Connected(state.Assistant))state.Remove(state.Assistant);if(NetworkManager.ConnectedClientsIds.Count!=2&&state.Phase==StationPhase.Countdown)state.CancelCountdown();if(!state.Equals(Lobby.Value))Lobby.Value=state;}
            if(state.Phase==StationPhase.Countdown&&SharedTime>=state.Deadline)
            {
                if(!state.BothReady||NetworkManager.ConnectedClientsIds.Count!=2){state.CancelCountdown();Lobby.Value=state;return;}
                state.Phase=StationPhase.Fading;state.Deadline=SharedTime+.75;Lobby.Value=state;
            }
            else if(state.Phase==StationPhase.Fading&&SharedTime>=state.Deadline)
            {
                if(NetworkManager.ConnectedClientsIds.Count!=2){Block("Both players must remain connected.");return;}
                if(SpawnRetryAvailable.Value&&SceneManager.GetSceneByName(stationSceneName).isLoaded)
                {state.Phase=StationPhase.AwaitingSpawn;state.Arrivals=0;state.Deadline=SharedTime+20;Lobby.Value=state;return;}
                state.Phase=StationPhase.Loading;state.Deadline=SharedTime+125;Lobby.Value=state;
                if(issuedEpoch==state.Epoch)return;issuedEpoch=state.Epoch;
                var result=NetworkManager.SceneManager.LoadScene(stationSceneName,LoadSceneMode.Additive);
                if(result!=SceneEventProgressStatus.Started)Block("Station load could not start: "+result);
            }
            else if((state.Phase==StationPhase.Loading||state.Phase==StationPhase.AwaitingSpawn)&&SharedTime>=state.Deadline)Block("Both station clients did not finish loading/spawning. Reconnect from the lobby.");
        }
        void LoadCompleted(string scene,LoadSceneMode mode,List<ulong> completed,List<ulong> timedOut)
        {
            if(!Authority||scene!=stationSceneName||Lobby.Value.Phase!=StationPhase.Loading)return;
            var state=Lobby.Value;
            if(timedOut.Count!=0||!completed.Contains(state.Driver)||!completed.Contains(state.Assistant)||NetworkManager.ConnectedClientsIds.Count!=2){Block("Station loading was incomplete; gameplay remains locked.");return;}
            SpawnRetryAvailable.Value=true;state.Phase=StationPhase.AwaitingSpawn;state.Arrivals=0;state.Deadline=SharedTime+20;Lobby.Value=state;
        }
        void Block(string reason){SharedFailure.Value=reason;var state=Lobby.Value;state.Phase=StationPhase.Blocked;state.Arrivals=0;state.Deadline=0;Lobby.Value=state;Debug.LogWarning(reason,this);}
        public void HideLobbyEnvironment()
        {foreach(var g in lobbyOnlyRoots)if(g)g.SetActive(false);}
        public void RestoreLobbyEnvironment()
        {for(int i=0;i<lobbyOnlyRoots.Length;i++)if(lobbyOnlyRoots[i])lobbyOnlyRoots[i].SetActive(lobbyActiveStates[i]);}
    }
}
