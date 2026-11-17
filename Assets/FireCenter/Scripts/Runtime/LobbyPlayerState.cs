using System;
using System.Collections.Generic;
using FirefighterVR.Station;
using FirefighterVR.StationBot;
using Unity.Netcode;
using UnityEngine;
using XRMultiplayer;

namespace FireCenter
{
    // Bridge (read model) between the FireCenter lobby UI and the project's EXISTING authority:
    //  - roles / ready / transition  -> StationSessionFlow (host-validated RPCs, StationLobbyState)
    //  - seat selection / seating    -> SeatedDispatchLobby
    //  - Solo bot                    -> StationSoloBotTest
    // It is NOT a NetworkBehaviour and adds nothing to the player prefab, so no second role
    // system can disagree with the host. Colour = player identity (host RED, guest BLUE);
    // role = station (Driver / Firefighter=Assistant), and they are kept separate.
    public sealed class LobbyPlayerState
    {
        static readonly List<LobbyPlayerState> s_All = new List<LobbyPlayerState>();
        static readonly Dictionary<ulong, LobbyPlayerState> s_Cache = new Dictionary<ulong, LobbyPlayerState>();
        static LobbyPlayerState s_Local; static int s_Frame = -1;
        const ulong BotId = ulong.MaxValue - 1;

        public static IReadOnlyList<LobbyPlayerState> All { get { Refresh(); return s_All; } }
        public static LobbyPlayerState Local { get { Refresh(); return s_Local; } }
        public static event Action anyStateChanged;
        public static event Action<string> roleSwitchRejected;
        public static event Action roleSwitchApplied;
        internal static void RaiseChanged() => anyStateChanged?.Invoke();

        public ulong OwnerClientId { get; private set; }
        public bool IsSpawned => true;
        public bool isBot { get; private set; }
        public PlayerColor color { get; private set; }
        public PlayerRole role { get; private set; }
        public bool isReady { get; private set; }
        public bool isSessionHost { get; private set; }
        public XRINetworkPlayer networkPlayer { get; private set; }
        string m_Name = "";
        public string playerName => isBot ? "BOT" : networkPlayer && !string.IsNullOrEmpty(networkPlayer.playerName) ? networkPlayer.playerName : !string.IsNullOrEmpty(m_Name) ? m_Name : $"Player {OwnerClientId}";
        public ControlMode controlMode => StationColors.ModeForRole(role);
        public bool hasG29 => true;
        public bool hasVRControllers => true;
        public bool editorHardwareBypass => Application.isEditor;
        public bool hasPublishedSessionSettings => isSessionHost;
        public string sessionTeamName => FireCenterSettings.teamName;
        public GameDifficulty sessionDifficulty => FireCenterSettings.difficulty;
        public TrafficDensity sessionTraffic => FireCenterSettings.trafficDensity;
        public GameDifficulty sessionFireSpread => FireCenterSettings.fireSpread;
        // Input readiness is enforced by the existing StationLocalRig when Ready is pressed.
        public bool SupportsRole(PlayerRole r) => r != PlayerRole.Unassigned;
        public bool controlModeSupportsRole => SupportsRole(role);
        public string HardwareSummary()
        {
            if (isBot) return "Station bot (Solo)";
            if (this != s_Local) return "Remote operator";
            var rig = StationSessionFlow.Instance ? StationSessionFlow.Instance.localRig : null;
            return rig ? rig.InputStatus : "Local operator";
        }

        public static StationRole ToStation(PlayerRole r) => r == PlayerRole.Driver ? StationRole.Driver : r == PlayerRole.Firefighter ? StationRole.Assistant : StationRole.None;
        public static PlayerRole FromStation(StationRole r) => r == StationRole.Driver ? PlayerRole.Driver : r == StationRole.Assistant ? PlayerRole.Firefighter : PlayerRole.Unassigned;

        static LobbyPlayerState Get(ulong id) { if (!s_Cache.TryGetValue(id, out var p)) s_Cache[id] = p = new LobbyPlayerState { OwnerClientId = id }; return p; }
        static XRINetworkPlayer[] s_Players = Array.Empty<XRINetworkPlayer>(); static float s_NextScan;

        static void Refresh()
        {
            if (s_Frame == Time.frameCount) return; s_Frame = Time.frameCount;
            s_All.Clear(); s_Local = null;
            var flow = StationSessionFlow.Instance; var lobby = SeatedDispatchLobby.Instance;
            var nm = NetworkManager.Singleton;
            bool networked = flow && flow.IsSpawned && nm && nm.IsListening;
            if (networked)
            {
                if (Time.unscaledTime >= s_NextScan) { s_NextScan = Time.unscaledTime + .5f; s_Players = UnityEngine.Object.FindObjectsByType<XRINetworkPlayer>(FindObjectsSortMode.None); }
                var state = flow.Lobby.Value; ulong owner = nm.DistributedAuthorityMode ? nm.CurrentSessionOwner : NetworkManager.ServerClientId;
                var ids = new List<ulong>(nm.ConnectedClientsIds); ids.Sort();
                foreach (var id in ids)
                {
                    var p = Get(id); p.isBot = false; p.networkPlayer = null;
                    foreach (var np in s_Players) if (np && np.IsSpawned && np.OwnerClientId == id) { p.networkPlayer = np; break; }
                    p.role = FromStation(state.RoleOf(id)); p.isReady = state.IsReady(id); p.isSessionHost = id == owner;
                    p.color = p.isSessionHost ? PlayerColor.Red : PlayerColor.Blue;
                    if (id == nm.LocalClientId) { s_Local = p; if (p.role == PlayerRole.Unassigned && lobby) p.role = FromStation(lobby.SelectedProfile); }
                    s_All.Add(p);
                }
            }
            else
            {
                // Offline / Solo: the local operator, plus the existing station bot when Play With Bot is armed.
                var me = Get(0); me.isBot = false; me.networkPlayer = null; me.isSessionHost = true; me.color = PlayerColor.Red; me.isReady = false;
                me.m_Name = XRINetworkGameManager.LocalPlayerName.Value;
                me.role = flow && flow.SoloMissionActive ? FromStation(flow.SoloHumanRole) : lobby ? FromStation(lobby.SelectedProfile) : PlayerRole.Unassigned;
                s_Local = me; s_All.Add(me);
                var solo = UnityEngine.Object.FindFirstObjectByType<StationSoloBotTest>();
                if (solo && (solo.Armed || solo.IsStationTest))
                {
                    var bot = Get(BotId); bot.isBot = true; bot.networkPlayer = null; bot.isSessionHost = false; bot.color = PlayerColor.Blue; bot.isReady = true;
                    bot.role = me.role == PlayerRole.Unassigned ? PlayerRole.Unassigned : StationColors.Opposite(me.role);
                    s_All.Add(bot);
                }
            }
        }

        // Signature used by the ticker to raise anyStateChanged only when something visible changed.
        internal static int Signature()
        {
            Refresh(); int h = s_All.Count;
            foreach (var p in s_All) h = h * 31 + (int)p.OwnerClientId * 7 + (int)p.role * 3 + (p.isReady ? 1 : 0) + (p.isBot ? 11 : 0) + (int)p.color * 17 + p.playerName.GetHashCode();
            var flow = StationSessionFlow.Instance; if (flow && flow.IsSpawned) h = h * 31 + (int)flow.Lobby.Value.Phase;
            return h;
        }

        // Roles can change only while the room is not yet complete (one human, or Solo).
        // Once two real operators are in the room with both stations taken, roles are locked.
        public static string ValidateRoleSwitch()
        {
            var flow = StationSessionFlow.Instance; var lobby = SeatedDispatchLobby.Instance;
            if (!lobby || !lobby.InLobby) return "Roles can only be changed in the lobby.";
            if (flow && flow.SoloMissionActive) return "The mission is already starting.";
            var solo = UnityEngine.Object.FindFirstObjectByType<StationSoloBotTest>(); if (solo && solo.IsStationTest) return "The mission is already starting.";
            if (flow && flow.IsSpawned)
            {
                var s = flow.Lobby.Value;
                if (!s.Editable || s.Phase == StationPhase.Countdown) return "The mission is already starting.";
                if (flow.NetworkManager.ConnectedClientsIds.Count >= 2 && s.Driver != StationLobbyState.Unassigned && s.Assistant != StationLobbyState.Unassigned)
                    return "Roles are locked: both operators are in the room.";
            }
            return null;
        }
        public static void RequestRoleSwitch()
        {
            string error = ValidateRoleSwitch();
            if (error != null) { roleSwitchRejected?.Invoke(error); return; }
            var lobby = SeatedDispatchLobby.Instance; var me = Local;
            var target = me == null || me.role == PlayerRole.Unassigned ? PlayerRole.Driver : StationColors.Opposite(me.role);
            lobby.SelectSeat(ToStation(target)); // host validates the claim in StationSessionFlow.ClaimRoleRpc
            roleSwitchApplied?.Invoke(); RaiseChanged();
        }
        public static void SelectRole(PlayerRole role)
        {
            string error = ValidateRoleSwitch(); var me = Local;
            if (error != null && (me == null || me.role != PlayerRole.Unassigned)) { roleSwitchRejected?.Invoke(error); return; }
            SeatedDispatchLobby.Instance?.SelectSeat(ToStation(role)); RaiseChanged();
        }

        public bool TrySetReady(bool ready)
        {
            if (this != Local || isBot) return false;
            var flow = StationSessionFlow.Instance;
            if (flow && flow.IsSpawned) { flow.SetLocalReady(ready); return true; }
            var solo = UnityEngine.Object.FindFirstObjectByType<StationSoloBotTest>();
            if (ready && solo && solo.Armed) { solo.ReadyWithBot(); return true; }
            return false;
        }
        public void ResetReady() { if (this == Local) TrySetReady(false); }
        // The existing StationSessionFlow starts the shared countdown itself once both are Ready.
        public void BroadcastMissionStart(string missionScene) { }

        public static bool IsLocalHostAuthority(NetworkManager nm)
        {
            if (nm == null || !nm.IsListening) return true;
            if (nm.DistributedAuthorityMode) return nm.LocalClient != null && nm.LocalClient.IsSessionOwner;
            return nm.IsHost || nm.IsServer;
        }
        public static LobbyPlayerState ByColor(PlayerColor c) { foreach (var p in All) if (p.color == c) return p; return null; }
        public static LobbyPlayerState ByRole(PlayerRole r) { foreach (var p in All) if (p.role == r) return p; return null; }
    }
}
