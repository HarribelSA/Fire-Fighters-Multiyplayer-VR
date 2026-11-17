using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UI;
using XRMultiplayer;

namespace FireCenter
{
    /// <summary>
    /// "CIVIL DEFENSE - MISSION CONTROL" on the central shared monitor (shown while connected to a room).
    /// RED and BLUE operator cards with separate Connection and Ready indicators, SWITCH ROLES (host-coordinated,
    /// hardware-validated), START MISSION (host only) and LEAVE. Every value comes from the synchronized
    /// <see cref="LobbyPlayerState"/> NetworkVariables, so both clients show the same state in real time.
    /// </summary>
    public class MissionControlPanel : MonoBehaviour
    {
        [System.Serializable]
        public class OperatorCard
        {
            public Graphic[] colorGraphics;
            public TMP_Text title;
            public TMP_Text playerName;
            public Image connectionDot;
            public TMP_Text connectionText;
            public TMP_Text roleText;
            public Image readyDot;
            public TMP_Text readyText;
            public TMP_Text hardwareText;
        }

        [SerializeField] TMP_Text m_RoomInfo;
        [SerializeField] OperatorCard m_Red = new();
        [SerializeField] OperatorCard m_Blue = new();
        [SerializeField] TMP_Text m_Banner;
        [SerializeField] Image m_BannerBackground;
        [SerializeField] Button m_SwitchRoles;
        [SerializeField] Button m_StartMission;
        [SerializeField] TMP_Text m_StartLabel;
        [SerializeField] TMP_Text m_Message;

        static readonly Color k_BannerIdle = new(0.12f, 0.14f, 0.18f, 1f);
        static readonly Color k_BannerReady = new(0.12f, 0.36f, 0.20f, 1f);

        float m_AllReadySince = -1f;
        float m_MessageUntil;
        float m_NextRefresh;

        void Start()
        {
            if (m_SwitchRoles) m_SwitchRoles.onClick.AddListener(OnSwitch);
            if (m_StartMission) m_StartMission.onClick.AddListener(() => MissionLauncher.Instance?.RequestStartMission());
            LobbyPlayerState.anyStateChanged += Refresh;
            LobbyPlayerState.roleSwitchRejected += OnSwitchRejected;
            LobbyPlayerState.roleSwitchApplied += OnSwitchApplied;
            if (MissionLauncher.Instance != null) MissionLauncher.Instance.missionFailed += ShowMessage;
            Refresh();
        }

        void OnDestroy()
        {
            LobbyPlayerState.anyStateChanged -= Refresh;
            LobbyPlayerState.roleSwitchRejected -= OnSwitchRejected;
            LobbyPlayerState.roleSwitchApplied -= OnSwitchApplied;
            if (MissionLauncher.Instance != null) MissionLauncher.Instance.missionFailed -= ShowMessage;
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + 0.25f;
            Refresh();
        }

        void OnSwitch()
        {
            // Show the local validation result immediately; the host re-validates authoritatively.
            string error = LobbyPlayerState.ValidateRoleSwitch();
            if (error != null) { ShowMessage(error); return; }
            ShowMessage("Switching roles...");
            LobbyPlayerState.RequestRoleSwitch();
        }

        void OnSwitchRejected(string reason) => ShowMessage(reason);
        void OnSwitchApplied() => ShowMessage("<color=#4CDB6B>Station switched.</color>");

        void ShowMessage(string text)
        {
            if (m_Message == null) return;
            m_Message.text = text;
            m_MessageUntil = Time.unscaledTime + 6f;
        }

        void Refresh()
        {
            var nm = NetworkManager.Singleton;
            string room = XRINetworkGameManager.ConnectedRoomName.Value;
            if (string.IsNullOrEmpty(room) || room == "Local Room") room = LobbySessionData.RoomName;
            if (m_RoomInfo) m_RoomInfo.text = $"{room}   |   Operators {LobbyPlayerState.All.Count}/{LobbySessionData.MaxPlayers}";

            FillCard(m_Red, PlayerColor.Red);
            FillCard(m_Blue, PlayerColor.Blue);

            bool busy = MissionLauncher.Instance != null && MissionLauncher.Instance.sequenceRunning;
            string blocker = MissionLauncher.ValidateOperators();
            bool allReady = blocker == null;
            if (allReady && m_AllReadySince < 0f) m_AllReadySince = Time.unscaledTime;
            if (!allReady) m_AllReadySince = -1f;

            bool isHost = LobbyPlayerState.IsLocalHostAuthority(nm);
            if (m_Banner)
            {
                if (busy) m_Banner.text = "DISPATCH IN PROGRESS";
                else if (allReady) m_Banner.text = Time.unscaledTime - m_AllReadySince < 1.5f ? "BOTH OPERATORS READY" : "MISSION READY TO START";
                else m_Banner.text = blocker.ToUpperInvariant();
            }
            if (m_BannerBackground) m_BannerBackground.color = allReady ? k_BannerReady : k_BannerIdle;

            if (m_SwitchRoles) m_SwitchRoles.interactable = !busy && LobbyPlayerState.ValidateRoleSwitch() == null; // open until the room is complete, then locked
            if (m_StartMission)
            {
                m_StartMission.interactable = !busy && LobbyPlayerState.Local != null && (allReady || !LobbyPlayerState.Local.isReady);
            }
            if (m_StartLabel) m_StartLabel.text = LobbyPlayerState.Local != null && LobbyPlayerState.Local.isReady ? "WAITING FOR TEAM" : "READY MISSION";

            if (m_Message && Time.unscaledTime > m_MessageUntil) m_Message.text = "";
        }

        static void FillCard(OperatorCard card, PlayerColor color)
        {
            var p = LobbyPlayerState.ByColor(color);
            var c = StationColors.Of(color);
            foreach (var g in card.colorGraphics) if (g != null) g.color = new Color(c.r, c.g, c.b, g.color.a);
            bool connected = p != null;
            if (card.title) card.title.text = $"{StationColors.Label(color)} OPERATOR" + (p != null && p.isSessionHost ? "  <size=70%><color=#FFC857>HOST</color></size>" : "") + (p != null && p == LobbyPlayerState.Local ? "  <size=70%><color=#A0D8FF>YOU</color></size>" : "");
            if (card.playerName) card.playerName.text = connected ? p.playerName : "<color=#6E7682>No operator</color>";
            if (card.connectionDot) card.connectionDot.color = connected ? StationColors.Ok : StationColors.Bad;
            if (card.connectionText) card.connectionText.text = connected ? "Connected" : "Disconnected";
            if (card.roleText) card.roleText.text = connected ? RoleRules.RoleLabel(p.role) + (p.role == PlayerRole.Driver ? "  <size=75%><color=#A0A8B4>G29</color></size>" : "  <size=75%><color=#A0A8B4>VR controllers</color></size>") : "-";
            bool ready = connected && p.isReady;
            if (card.readyDot) card.readyDot.color = ready ? StationColors.Ok : StationColors.Bad;
            if (card.readyText) card.readyText.text = ready ? "Ready" : "Not Ready";
            if (card.hardwareText)
                card.hardwareText.text = connected
                    ? (p.SupportsRole(p.role) ? $"<color=#8A93A0>{p.HardwareSummary()}</color>" : $"<color=#FFB347>Hardware does not support {RoleRules.RoleLabel(p.role)}: {p.HardwareSummary()}</color>")
                    : "";
        }
    }
}
