using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TMPro;
using Unity.Netcode;
using Unity.Services.Multiplayer;
using UnityEngine;
using UnityEngine.UI;
using XRMultiplayer;

namespace FireCenter
{
    /// <summary>
    /// Drives the computer-monitor UI of the dispatch center: main menu, room browser, settings,
    /// waiting room, exit confirmation and the incoming-emergency alert. All networking goes through
    /// the template's existing <see cref="XRINetworkGameManager"/> / <see cref="SessionManager"/>.
    /// </summary>
    public class LobbyMenuController : MonoBehaviour
    {
        [Header("Panels")]
        [SerializeField] GameObject m_MainPanel;
        [SerializeField] GameObject m_JoinPanel;
        [SerializeField] GameObject m_SettingsPanel;
        [SerializeField] GameObject m_WaitingRoomPanel;
        [SerializeField] GameObject m_ExitDialog;
        [SerializeField] GameObject m_AlertPanel;

        [Header("Main menu")]
        [SerializeField] Button m_CreateRoomButton;
        [SerializeField] Button m_JoinRoomButton;
        [SerializeField] Button m_SettingsButton;
        [SerializeField] Button m_ExitButton;
        [SerializeField, Tooltip("Solo: play with the existing station bot in the other station.")] Button m_PlayWithBotButton;
        [SerializeField] TMP_Text m_MainStatusText;
        [SerializeField] TMP_Text m_PlayerNameText;

        [Header("Join room")]
        [SerializeField] RectTransform m_RoomListContainer;
        [SerializeField] Button m_RoomEntryTemplate;
        [SerializeField] Button m_RefreshRoomsButton;
        [SerializeField] Button m_JoinBackButton;
        [SerializeField] TMP_Text m_JoinStatusText;

        [Header("Settings")]
        [SerializeField] Button m_PcvrModeButton;
        [SerializeField] Button m_QuestModeButton;
        [SerializeField] TMP_Text m_DeviceStatusText;
        [SerializeField] Slider[] m_VolumeSliders = new Slider[5];
        [SerializeField] TMP_Text[] m_VolumeValueTexts = new TMP_Text[5];
        [SerializeField] Button m_SettingsBackButton;

        [Header("Waiting room")]
        [SerializeField] TMP_Text m_RoomNameText;
        [SerializeField] TMP_Text m_PlayerCountText;
        [SerializeField] TMP_Text m_ConnectionText;
        [SerializeField] PlayerRowUI[] m_PlayerRows = new PlayerRowUI[2];
        [SerializeField] Button m_ReadyButton;
        [SerializeField] TMP_Text m_ReadyButtonLabel;
        [SerializeField] Button m_StartMissionButton;
        [SerializeField] TMP_Text m_WaitingHintText;
        [SerializeField] Button m_LeaveRoomButton;
        [SerializeField] Button m_WaitingSettingsButton;

        [Header("Exit dialog")]
        [SerializeField] Button m_ExitYesButton;
        [SerializeField] Button m_ExitCancelButton;

        [Header("Footer")]
        [SerializeField] Image m_ControllerIcon;
        [SerializeField] TMP_Text m_ControllerLabel;
        [SerializeField] TMP_Text m_FooterConnectionText;
        [SerializeField] Sprite m_PcvrIcon;
        [SerializeField] Sprite m_QuestIcon;

        [Header("Monitor placement")]
        [SerializeField, Tooltip("Passive standby screen shown on the workstation the menu is not on.")] Transform m_StandbyScreen;
        [SerializeField, Tooltip("Screen transform of workstation 1 (Driver).")] Transform m_DriverScreenAnchor;
        [SerializeField, Tooltip("Screen transform of workstation 2 (Firefighter).")] Transform m_FirefighterScreenAnchor;

        [Header("Feedback")]
        [SerializeField] AudioSource m_UiAudio;
        [SerializeField] AudioClip m_ClickClip;

        static readonly Color k_Selected = new(0.86f, 0.24f, 0.16f, 1f);
        static readonly Color k_Unselected = new(0.18f, 0.21f, 0.26f, 1f);

        GameObject m_PanelBeforeSettings;
        GameObject m_PanelBeforeExit;
        readonly List<Button> m_RoomEntries = new();
        float m_NextRefresh;
        bool m_Busy;
        bool m_WasConnected;

        // ------------------------------------------------------------------ lifecycle

        void Start()
        {
            Wire(m_CreateRoomButton, CreateRoom);
            Wire(m_JoinRoomButton, OpenJoinPanel);
            Wire(m_SettingsButton, OpenSettings);
            Wire(m_ExitButton, OpenExitDialog);
            Wire(m_PlayWithBotButton, PlayWithBot);
            Wire(m_RefreshRoomsButton, RefreshRoomList);
            Wire(m_JoinBackButton, () => ShowPanel(m_MainPanel));
            Wire(m_PcvrModeButton, () => InputModeManager.Instance?.SetMode(ControlMode.PCVR_G29_HandTracking));
            Wire(m_QuestModeButton, () => InputModeManager.Instance?.SetMode(ControlMode.MetaQuestControllers));
            Wire(m_SettingsBackButton, CloseSettings);
            Wire(m_ReadyButton, ToggleReady);
            Wire(m_StartMissionButton, () => MissionLauncher.Instance?.RequestStartMission());
            Wire(m_LeaveRoomButton, LeaveRoom);
            Wire(m_WaitingSettingsButton, OpenSettings);
            Wire(m_ExitYesButton, ConfirmExit);
            Wire(m_ExitCancelButton, CloseExitDialog);

            SetupVolumeSliders();

            if (m_RoomEntryTemplate != null) m_RoomEntryTemplate.gameObject.SetActive(false);
            if (m_AlertPanel != null) m_AlertPanel.SetActive(false);
            if (m_ExitDialog != null) m_ExitDialog.SetActive(false);

            if (InputModeManager.Instance != null)
            {
                InputModeManager.Instance.modeChanged += OnControlModeChanged;
                OnControlModeChanged(InputModeManager.Instance.currentMode);
            }
            if (InputDeviceProbe.Instance != null) InputDeviceProbe.Instance.statusChanged += RefreshDeviceStatus;
            RefreshDeviceStatus();

            if (MissionLauncher.Instance != null)
            {
                MissionLauncher.Instance.alertStarted += ShowAlert;
                MissionLauncher.Instance.missionFailed += OnMissionFailed;
            }
            if (PlayerSpawnManager.Instance != null) PlayerSpawnManager.Instance.localPlayerPlaced += MoveToRoleMonitor;

            XRINetworkGameManager.CurrentConnectionState.Subscribe(OnConnectionStateChanged);
            SessionManager.status.Subscribe(OnSessionStatus);
            XRINetworkGameManager.LocalPlayerName.Subscribe(OnLocalNameChanged);
            if (XRINetworkGameManager.Instance != null)
                XRINetworkGameManager.Instance.OnConnectionFailedAction += OnConnectionFailed;
            LobbyPlayerState.anyStateChanged += RefreshWaitingRoom;

            OnLocalNameChanged(XRINetworkGameManager.LocalPlayerName.Value);
            OnConnectionStateChanged(XRINetworkGameManager.CurrentConnectionState.Value);
            ShowPanel(IsConnected() ? m_WaitingRoomPanel : m_MainPanel);
        }

        void OnDestroy()
        {
            if (InputModeManager.Instance != null) InputModeManager.Instance.modeChanged -= OnControlModeChanged;
            if (InputDeviceProbe.Instance != null) InputDeviceProbe.Instance.statusChanged -= RefreshDeviceStatus;
            if (MissionLauncher.Instance != null)
            {
                MissionLauncher.Instance.alertStarted -= ShowAlert;
                MissionLauncher.Instance.missionFailed -= OnMissionFailed;
            }
            if (PlayerSpawnManager.Instance != null) PlayerSpawnManager.Instance.localPlayerPlaced -= MoveToRoleMonitor;
            XRINetworkGameManager.CurrentConnectionState.Unsubscribe(OnConnectionStateChanged);
            SessionManager.status.Unsubscribe(OnSessionStatus);
            XRINetworkGameManager.LocalPlayerName.Unsubscribe(OnLocalNameChanged);
            if (XRINetworkGameManager.Instance != null)
                XRINetworkGameManager.Instance.OnConnectionFailedAction -= OnConnectionFailed;
            LobbyPlayerState.anyStateChanged -= RefreshWaitingRoom;
        }

        void Update()
        {
            // Light polling keeps the waiting room correct even if a callback is missed (e.g. name arrives late).
            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + 0.25f;
            if (m_WaitingRoomPanel != null && m_WaitingRoomPanel.activeSelf) RefreshWaitingRoom();
            RefreshMainButtons();
        }

        // ------------------------------------------------------------------ helpers

        void Wire(Button button, Action action)
        {
            if (button == null) return;
            button.onClick.AddListener(() =>
            {
                if (m_UiAudio != null && m_ClickClip != null) m_UiAudio.PlayOneShot(m_ClickClip);
                action();
            });
        }

        void ShowPanel(GameObject panel)
        {
            foreach (var p in new[] { m_MainPanel, m_JoinPanel, m_SettingsPanel, m_WaitingRoomPanel })
                if (p != null) p.SetActive(p == panel);
        }

        GameObject CurrentPanel()
        {
            foreach (var p in new[] { m_MainPanel, m_JoinPanel, m_SettingsPanel, m_WaitingRoomPanel })
                if (p != null && p.activeSelf) return p;
            return m_MainPanel;
        }

        static bool IsConnected() =>
            XRINetworkGameManager.CurrentConnectionState.Value == XRINetworkGameManager.ConnectionState.Connected;

        static bool IsLocalOnly() => XRINetworkGameManager.CurrentSessionType == XRMultiplayer.SessionType.LocalOnly;

        void SetStatus(string text)
        {
            if (m_MainStatusText) m_MainStatusText.text = text;
            if (m_JoinStatusText) m_JoinStatusText.text = text;
        }

        // ------------------------------------------------------------------ connection

        void OnConnectionStateChanged(XRINetworkGameManager.ConnectionState state)
        {
            string label;
            switch (state)
            {
                case XRINetworkGameManager.ConnectionState.Authenticating: label = "<color=#FFB347>Authenticating...</color>"; break;
                case XRINetworkGameManager.ConnectionState.Authenticated: label = "<color=#8A93A0>Online - not in a room</color>"; break;
                case XRINetworkGameManager.ConnectionState.Connecting: label = "<color=#FFB347>Connecting...</color>"; break;
                case XRINetworkGameManager.ConnectionState.Connected: label = "<color=#7CFC8A>Connected</color>"; break;
                default: label = "<color=#FF6B6B>Offline</color>"; break;
            }
            if (m_FooterConnectionText) m_FooterConnectionText.text = label;

            bool connected = state == XRINetworkGameManager.ConnectionState.Connected;
            if (connected && !m_WasConnected)
            {
                m_Busy = false;
                ShowPanel(m_WaitingRoomPanel);
                if (m_WaitingHintText) m_WaitingHintText.text = "";
            }
            else if (!connected && m_WasConnected)
            {
                m_Busy = false;
                if (m_AlertPanel) m_AlertPanel.SetActive(false);
                ShowPanel(m_MainPanel);
                SetStatus("Disconnected from the room.");
                PlayerSpawnManager.Instance?.ReturnToDefaultSpawn();
            }
            else if (state == XRINetworkGameManager.ConnectionState.Authenticated)
            {
                m_Busy = false;
            }
            m_WasConnected = connected;
            RefreshMainButtons();
        }

        void OnSessionStatus(string status)
        {
            if (!string.IsNullOrEmpty(status) && m_Busy) SetStatus(status);
        }

        void OnConnectionFailed(string reason)
        {
            m_Busy = false;
            SetStatus($"<color=#FF6B6B>{reason}</color>");
            RefreshMainButtons();
        }

        void OnLocalNameChanged(string newName)
        {
            if (m_PlayerNameText) m_PlayerNameText.text = $"Operator: <b>{newName}</b>";
        }

        void RefreshMainButtons()
        {
            var state = XRINetworkGameManager.CurrentConnectionState.Value;
            bool canConnect = !m_Busy && (state == XRINetworkGameManager.ConnectionState.Authenticated ||
                                          (IsLocalOnly() && state != XRINetworkGameManager.ConnectionState.Connecting && state != XRINetworkGameManager.ConnectionState.Connected));
            if (m_CreateRoomButton) m_CreateRoomButton.interactable = canConnect;
            if (m_JoinRoomButton) m_JoinRoomButton.interactable = canConnect;
            if (m_RefreshRoomsButton) m_RefreshRoomsButton.interactable = canConnect;
            if (m_PlayWithBotButton) m_PlayWithBotButton.interactable = !m_Busy && !IsConnected();
        }

        // ------------------------------------------------------------------ create / join

        void CreateRoom()
        {
            var gm = XRINetworkGameManager.Instance;
            if (gm == null) return;
            m_Busy = true;
            SetStatus($"Creating room <b>{LobbySessionData.RoomName}</b>...");

            if (IsLocalOnly())
            {
                if (!gm.HostLocalConnection())
                {
                    m_Busy = false;
                    SetStatus("<color=#FF6B6B>Could not start a local host.</color>");
                }
            }
            else
            {
                gm.CreateNewLobby(LobbySessionData.RoomName, false, LobbySessionData.MaxPlayers);
            }
            RefreshMainButtons();
        }

        void OpenJoinPanel()
        {
            ShowPanel(m_JoinPanel);
            RefreshRoomList();
        }

        async void RefreshRoomList()
        {
            ClearRoomEntries();

            if (IsLocalOnly())
            {
                if (m_JoinStatusText) m_JoinStatusText.text = "Local network mode (no internet). Join the host on this network:";
                AddRoomEntry($"{LobbySessionData.RoomName}  -  Local Host", () =>
                {
                    m_Busy = true;
                    SetStatus("Joining local host...");
                    if (!XRINetworkGameManager.Instance.JoinLocalConnection())
                    {
                        m_Busy = false;
                        SetStatus("<color=#FF6B6B>Could not reach the local host.</color>");
                    }
                });
                return;
            }

            if (m_JoinStatusText) m_JoinStatusText.text = "Searching for dispatch rooms...";
            try
            {
                var results = await MultiplayerService.Instance.QuerySessionsAsync(new QuerySessionsOptions());
                if (this == null) return;
                int found = 0;
                foreach (var info in results.Sessions)
                {
                    if (info.AvailableSlots <= 0) continue;
                    if (!SessionManager.CanJoinLobby(info)) continue;
                    var captured = info;
                    AddRoomEntry($"{info.Name}   <size=75%>({info.MaxPlayers - info.AvailableSlots}/{info.MaxPlayers})</size>", () => JoinSession(captured));
                    if (++found >= 5) break;
                }
                if (m_JoinStatusText)
                    m_JoinStatusText.text = found == 0 ? "No open rooms found. Create one, or refresh." : "Select a room to join:";
            }
            catch (Exception e)
            {
                if (m_JoinStatusText) m_JoinStatusText.text = $"<color=#FF6B6B>Room search failed:</color> {e.Message}";
            }
        }

        void JoinSession(ISessionInfo info)
        {
            m_Busy = true;
            SetStatus($"Joining <b>{info.Name}</b>...");
            XRINetworkGameManager.Instance.JoinLobbySpecific(info);
            RefreshMainButtons();
        }

        void ClearRoomEntries()
        {
            foreach (var b in m_RoomEntries) if (b != null) Destroy(b.gameObject);
            m_RoomEntries.Clear();
        }

        void AddRoomEntry(string label, Action onClick)
        {
            if (m_RoomEntryTemplate == null || m_RoomListContainer == null) return;
            var entry = Instantiate(m_RoomEntryTemplate, m_RoomListContainer);
            entry.gameObject.SetActive(true);
            var text = entry.GetComponentInChildren<TMP_Text>(true);
            if (text) text.text = label;
            Wire(entry, onClick);
            m_RoomEntries.Add(entry);
        }

        static FirefighterVR.StationBot.StationSoloBotTest SoloBot => FindFirstObjectByType<FirefighterVR.StationBot.StationSoloBotTest>();
        // Solo: arm the EXISTING bot test (bot takes the other station) and show the waiting room with it.
        void PlayWithBot()
        {
            var solo = SoloBot; if (solo == null) { SetStatus("<color=#FF6B6B>Bot system unavailable.</color>"); return; }
            if (!solo.Armed) solo.ToggleBotMode();
            if (solo.Armed) { ShowPanel(m_WaitingRoomPanel); RefreshWaitingRoom(); } else SetStatus("<color=#FFB347>Play With Bot is available in the lobby with one player only.</color>");
        }
        void LeaveRoom()
        {
            var solo = SoloBot;
            if (solo != null && solo.Armed && !IsConnected()) { solo.ToggleBotMode(); ShowPanel(m_MainPanel); return; }
            if (XRINetworkGameManager.Instance != null) XRINetworkGameManager.Instance.Disconnect();
        }

        // ------------------------------------------------------------------ waiting room

        void ToggleReady()
        {
            var local = LobbyPlayerState.Local;
            if (local == null) return;
            if (!local.TrySetReady(!local.isReady) && m_WaitingHintText)
                m_WaitingHintText.text = $"<color=#FF6B6B>Your controller ({RoleRules.ControlModeLabel(local.controlMode)}) does not support the {RoleRules.RoleLabel(local.role)} role. Change it in Settings.</color>";
        }

        void RefreshWaitingRoom()
        {
            if (m_WaitingRoomPanel == null) return;
            var nm = NetworkManager.Singleton;
            var players = LobbyPlayerState.All;

            string roomName = XRINetworkGameManager.ConnectedRoomName.Value;
            if (string.IsNullOrEmpty(roomName) || roomName == "Local Room") roomName = LobbySessionData.RoomName;
            if (m_RoomNameText) m_RoomNameText.text = $"Room Name: <b>{roomName}</b>";
            if (m_PlayerCountText) m_PlayerCountText.text = $"Players: <b>{players.Count}/{LobbySessionData.MaxPlayers}</b>";
            if (m_ConnectionText)
                m_ConnectionText.text = IsConnected() ? "Connection: <color=#7CFC8A>Connected</color>" : "Connection: <color=#FFB347>Connecting...</color>";

            // Driver first, then Firefighter, then anyone else.
            var ordered = new List<LobbyPlayerState>(players);
            ordered.Sort((a, b) => RoleOrder(a.role).CompareTo(RoleOrder(b.role)));

            for (int i = 0; i < m_PlayerRows.Length; i++)
            {
                var row = m_PlayerRows[i];
                if (row == null) continue;
                if (i < ordered.Count)
                {
                    var p = ordered[i];
                    bool isHost = nm != null && (nm.DistributedAuthorityMode ? p.OwnerClientId == nm.CurrentSessionOwner : p.OwnerClientId == NetworkManager.ServerClientId);
                    row.Show(i, p, isHost, p == LobbyPlayerState.Local);
                }
                else
                {
                    row.ShowEmpty(i, i == 0 ? PlayerRole.Driver : PlayerRole.Firefighter);
                }
            }

            var local = LobbyPlayerState.Local;
            bool isHostLocal = LobbyPlayerState.IsLocalHostAuthority(nm);
            bool sequence = MissionLauncher.Instance != null && MissionLauncher.Instance.sequenceRunning;

            if (m_ReadyButton)
            {
                m_ReadyButton.interactable = local != null && !sequence &&
                                             (local.isReady || !LobbySessionData.enforceControllerCompatibility || local.controlModeSupportsRole);
                SetButtonColor(m_ReadyButton, local != null && local.isReady ? new Color(0.2f, 0.6f, 0.3f) : k_Unselected);
            }
            if (m_ReadyButtonLabel) m_ReadyButtonLabel.text = local != null && local.isReady ? "READY (CANCEL)" : "MARK READY";

            string blocker = MissionLauncher.ValidateStart();
            if (m_StartMissionButton)
            {
                m_StartMissionButton.gameObject.SetActive(true);
                m_StartMissionButton.interactable = isHostLocal && blocker == null && !sequence;
            }

            if (m_WaitingHintText && !sequence)
            {
                if (local != null && !local.controlModeSupportsRole)
                    m_WaitingHintText.text = $"<color=#FFB347>{RoleRules.RoleLabel(local.role)} needs {RoleRules.ControlModeLongLabel(RoleRules.PreferredModeFor(local.role))}. Open Settings to switch.</color>";
                else if (!isHostLocal)
                    m_WaitingHintText.text = "Only the Host can start the mission.";
                else
                    m_WaitingHintText.text = blocker ?? "<color=#7CFC8A>All crews ready - START MISSION.</color>";
            }
        }

        static int RoleOrder(PlayerRole role) => role == PlayerRole.Driver ? 0 : role == PlayerRole.Firefighter ? 1 : 2;

        void OnMissionFailed(string reason)
        {
            if (m_AlertPanel) m_AlertPanel.SetActive(false);
            if (m_WaitingHintText) m_WaitingHintText.text = $"<color=#FF6B6B>{reason}</color>";
        }

        void ShowAlert()
        {
            if (m_AlertPanel) m_AlertPanel.SetActive(true);
            if (m_ExitDialog) m_ExitDialog.SetActive(false);
        }

        // ------------------------------------------------------------------ settings

        void OpenSettings()
        {
            m_PanelBeforeSettings = CurrentPanel();
            InputDeviceProbe.Instance?.Refresh();
            ShowPanel(m_SettingsPanel);
        }

        void CloseSettings() => ShowPanel(m_PanelBeforeSettings != null && m_PanelBeforeSettings != m_SettingsPanel
            ? m_PanelBeforeSettings
            : (IsConnected() ? m_WaitingRoomPanel : m_MainPanel));

        void OnControlModeChanged(ControlMode mode)
        {
            if (m_ControllerIcon)
            {
                m_ControllerIcon.sprite = mode == ControlMode.PCVR_G29_HandTracking ? m_PcvrIcon : m_QuestIcon;
                m_ControllerIcon.enabled = m_ControllerIcon.sprite != null;
            }
            if (m_ControllerLabel) m_ControllerLabel.text = RoleRules.ControlModeLongLabel(mode);
            SetButtonColor(m_PcvrModeButton, mode == ControlMode.PCVR_G29_HandTracking ? k_Selected : k_Unselected);
            SetButtonColor(m_QuestModeButton, mode == ControlMode.MetaQuestControllers ? k_Selected : k_Unselected);
            RefreshDeviceStatus();
        }

        void RefreshDeviceStatus()
        {
            if (m_DeviceStatusText == null) return;
            m_DeviceStatusText.text = InputDeviceProbe.Instance != null ? InputDeviceProbe.Instance.BuildStatusText() : "";
        }

        static void SetButtonColor(Button button, Color color)
        {
            if (button == null) return;
            if (button.targetGraphic) button.targetGraphic.color = color;
        }

        void SetupVolumeSliders()
        {
            var audio = AudioSettingsManager.Instance;
            for (int i = 0; i < m_VolumeSliders.Length; i++)
            {
                var slider = m_VolumeSliders[i];
                if (slider == null) continue;
                var category = (VolumeCategory)i;
                slider.minValue = 0f;
                slider.maxValue = 1f;
                slider.SetValueWithoutNotify(audio != null ? audio.GetVolume(category) : 1f);
                UpdateVolumeLabel(i, slider.value);
                int index = i;
                slider.onValueChanged.AddListener(v =>
                {
                    AudioSettingsManager.Instance?.SetVolume(category, v);
                    UpdateVolumeLabel(index, v);
                });
            }
        }

        void UpdateVolumeLabel(int index, float value)
        {
            if (index < m_VolumeValueTexts.Length && m_VolumeValueTexts[index] != null)
                m_VolumeValueTexts[index].text = $"{Mathf.RoundToInt(value * 100f)}%";
        }

        // ------------------------------------------------------------------ monitor placement

        void MoveToRoleMonitor(PlayerRole role)
        {
            bool firefighter = role == PlayerRole.Firefighter;
            Transform anchor = firefighter ? m_FirefighterScreenAnchor : m_DriverScreenAnchor;
            Transform other = firefighter ? m_DriverScreenAnchor : m_FirefighterScreenAnchor;
            if (anchor == null) return;
            transform.SetPositionAndRotation(anchor.position, anchor.rotation);
            if (m_StandbyScreen != null && other != null)
                m_StandbyScreen.SetPositionAndRotation(other.position, other.rotation);
        }

        // ------------------------------------------------------------------ exit

        void OpenExitDialog()
        {
            m_PanelBeforeExit = CurrentPanel();
            if (m_ExitDialog) m_ExitDialog.SetActive(true);
        }

        void CloseExitDialog()
        {
            if (m_ExitDialog) m_ExitDialog.SetActive(false);
            if (m_PanelBeforeExit) ShowPanel(m_PanelBeforeExit);
        }

        async void ConfirmExit()
        {
            if (m_ExitYesButton) m_ExitYesButton.interactable = false;
            if (m_ExitCancelButton) m_ExitCancelButton.interactable = false;
            SetStatus("Disconnecting...");
            await DisconnectSafely();
            QuitApplication();
        }

        static async Task DisconnectSafely()
        {
            var gm = XRINetworkGameManager.Instance;
            var nm = NetworkManager.Singleton;
            try
            {
                if (gm != null && (XRINetworkGameManager.Connected.Value || (nm != null && nm.IsListening)))
                {
                    if (IsLocalOnly()) gm.LeaveLocalConnection();
                    else await gm.DisconnectAsync();
                }
                else if (nm != null && nm.IsListening)
                {
                    nm.Shutdown();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[FireCenter] Disconnect before exit failed: {e.Message}");
            }
        }

        static void QuitApplication()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
