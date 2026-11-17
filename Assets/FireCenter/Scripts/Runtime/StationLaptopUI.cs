using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using XRMultiplayer;

namespace FireCenter
{
    /// <summary>
    /// The computer screen of one lobby station (seat). The seat belongs to a ROLE; the theme (RED / BLUE) follows
    /// the colour of the player sitting there. Interactive only for the local player sitting at this seat; on the
    /// other player's screen it shows a read-only status view. Every control is wired to a real system:
    /// AudioSettingsManager (mixer), VoiceChatManager (microphone), FireCenterSettings -> ComfortSettingsApplier (XR
    /// turning / height), SteeringCalibrationProcessor (G29), LobbyPlayerState (Ready, network-synced).
    /// </summary>
    public class StationLaptopUI : MonoBehaviour
    {
        [Header("Seat")]
        [SerializeField] PlayerRole m_SeatRole = PlayerRole.Firefighter;

        [Header("Theme (recoloured with the occupant's colour)")]
        [SerializeField] Graphic[] m_ThemeGraphics;
        [SerializeField] TMP_Text m_Title;
        [SerializeField] TMP_Text m_RoleChip;

        [Header("Tabs")]
        [SerializeField] Button[] m_TabButtons;
        [SerializeField] GameObject[] m_Sections;
        [SerializeField] GameObject m_InteractiveRoot;
        [SerializeField] GameObject m_ObserverRoot;
        [SerializeField] TMP_Text m_ObserverText;

        [Header("Status tab")]
        [SerializeField] TMP_Text m_PlayerName;
        [SerializeField] Image m_ConnectionDot;
        [SerializeField] TMP_Text m_ConnectionText;
        [SerializeField] TMP_Text m_RoleText;
        [SerializeField] TMP_Text m_HardwareText;
        [SerializeField] SegmentedControl m_Language;
        [SerializeField] TMP_Text m_LanguageNote;

        [Header("Audio tab (Master, Music, SFX, Ambience, Voice Chat)")]
        [SerializeField] Slider[] m_VolumeSliders = new Slider[5];
        [SerializeField] TMP_Text[] m_VolumeValues = new TMP_Text[5];
        [SerializeField] SegmentedControl m_Microphone;      // LIVE | MUTED
        [SerializeField] TMP_Text m_MicrophoneNote;

        [Header("Comfort tab")]
        [SerializeField] SegmentedControl m_TurnStyle;       // SNAP | SMOOTH
        [SerializeField] Slider m_TurnSensitivity;           // 1..6
        [SerializeField] TMP_Text m_TurnSensitivityValue;
        [SerializeField] Slider m_Height;                    // -6..6 (x5 cm)
        [SerializeField] TMP_Text m_HeightValue;

        [Header("Driving tab (Driver seat only)")]
        [SerializeField] Slider m_SteerSensitivity;          // 1..8 (x0.25)
        [SerializeField] TMP_Text m_SteerSensitivityValue;
        [SerializeField] Slider m_Deadzone;                  // 0..6 (x0.05)
        [SerializeField] TMP_Text m_DeadzoneValue;
        [SerializeField] Button m_Calibrate;
        [SerializeField] Button m_ResetCalibration;
        [SerializeField] TMP_Text m_CalibrationText;
        [SerializeField] Image m_G29Dot;
        [SerializeField] TMP_Text m_G29Text;
        [SerializeField] RectTransform m_SteerBar;           // centred bar, width follows -1..1
        [SerializeField] Image m_ThrottleFill;
        [SerializeField] Image m_BrakeFill;

        [Header("Ready")]
        [SerializeField] Button m_ReadyButton;
        [SerializeField] TMP_Text m_ReadyLabel;
        [SerializeField] TMP_Text m_ReadyHint;

        static readonly Color k_TabOff = new(0.13f, 0.15f, 0.19f, 1f);
        static readonly Color k_ReadyOff = new(0.20f, 0.23f, 0.28f, 1f);

        int m_Tab;
        float m_NextRefresh;
        VoiceChatManager m_Voice;
        InputAction m_Steer, m_Throttle, m_Brake;
        float m_SteerBarHalfWidth = 200f;

        void Awake()
        {
            for (int i = 0; i < m_TabButtons.Length; i++)
            {
                int captured = i;
                if (m_TabButtons[i] != null) m_TabButtons[i].onClick.AddListener(() => ShowTab(captured));
            }
            if (m_SteerBar != null && m_SteerBar.parent is RectTransform track) m_SteerBarHalfWidth = track.rect.width / 2f;
        }

        void Start()
        {
            // Audio
            for (int i = 0; i < m_VolumeSliders.Length; i++)
            {
                var s = m_VolumeSliders[i];
                if (s == null) continue;
                var cat = (VolumeCategory)i;
                s.minValue = 0f; s.maxValue = 1f;
                int idx = i;
                s.onValueChanged.AddListener(v => { AudioSettingsManager.Instance?.SetVolume(cat, v); SetPercent(idx, v); });
            }
            if (m_Microphone != null) m_Microphone.valueChanged += i => SetMicMuted(i == 1);

            // Status
            if (m_Language != null) m_Language.valueChanged += i => FireCenterSettings.language = (GameLanguage)i;

            // Comfort
            if (m_TurnStyle != null) m_TurnStyle.valueChanged += i => FireCenterSettings.turnStyle = (TurnStyle)i;
            Bind(m_TurnSensitivity, v => { FireCenterSettings.snapTurnAngle = v * 15f; FireCenterSettings.smoothTurnSpeed = v * 30f; });
            Bind(m_Height, v => FireCenterSettings.heightOffset = v * 0.05f);

            // Driving
            Bind(m_SteerSensitivity, v => { FireCenterSettings.g29SteeringSensitivity = v * 0.25f; SteeringCalibrationProcessor.ApplyToSteeringAction(); });
            Bind(m_Deadzone, v => { FireCenterSettings.steeringDeadzone = v * 0.05f; SteeringCalibrationProcessor.ApplyToSteeringAction(); });
            if (m_Calibrate != null) m_Calibrate.onClick.AddListener(CalibrateCenter);
            if (m_ResetCalibration != null) m_ResetCalibration.onClick.AddListener(() => { FireCenterSettings.steeringCenterOffset = 0f; SteeringCalibrationProcessor.ApplyToSteeringAction(); RefreshDriving(); });

            // Ready
            if (m_ReadyButton != null) m_ReadyButton.onClick.AddListener(ToggleReady);

            var im = InputModeManager.Instance;
            if (im != null)
            {
                m_Steer = im.FindAction("DriverControls/Steering");
                m_Throttle = im.FindAction("DriverControls/Accelerate");
                m_Brake = im.FindAction("DriverControls/Brake");
            }
            SteeringCalibrationProcessor.ApplyToSteeringAction();

            LobbyPlayerState.anyStateChanged += Refresh;
            if (InputDeviceProbe.Instance != null) InputDeviceProbe.Instance.statusChanged += Refresh;
            LoadValues();
            ShowTab(0);
            Refresh();
        }

        void OnDestroy()
        {
            LobbyPlayerState.anyStateChanged -= Refresh;
            if (InputDeviceProbe.Instance != null) InputDeviceProbe.Instance.statusChanged -= Refresh;
        }

        void Update()
        {
            UpdateLiveDrivingInput();
            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + 0.3f;
            Refresh();
        }

        // ------------------------------------------------------------------ view

        void ShowTab(int index)
        {
            m_Tab = index;
            for (int i = 0; i < m_Sections.Length; i++)
                if (m_Sections[i] != null) m_Sections[i].SetActive(i == index);
            ApplyTheme(StationSeats.Get(m_SeatRole).color);
        }

        void ApplyTheme(PlayerColor color)
        {
            var c = StationColors.Of(color);
            foreach (var g in m_ThemeGraphics) if (g != null) g.color = new Color(c.r, c.g, c.b, g.color.a);
            for (int i = 0; i < m_TabButtons.Length; i++)
                if (m_TabButtons[i] != null && m_TabButtons[i].targetGraphic != null)
                    m_TabButtons[i].targetGraphic.color = i == m_Tab ? c : k_TabOff;
        }

        void Refresh()
        {
            var occ = StationSeats.Get(m_SeatRole);
            ApplyTheme(occ.color);
            string colorLabel = StationColors.Label(occ.color);
            if (m_Title) m_Title.text = $"FIRE CENTER - STATION {colorLabel}";
            if (m_RoleChip) m_RoleChip.text = RoleRules.RoleLabel(m_SeatRole).ToUpperInvariant();

            bool interactive = occ.isLocal;
            if (m_InteractiveRoot) m_InteractiveRoot.SetActive(interactive);
            if (m_ObserverRoot) m_ObserverRoot.SetActive(!interactive);
            if (!interactive)
            {
                if (m_ObserverText)
                    m_ObserverText.text = occ.present && occ.state != null
                        ? $"<size=120%><b>{occ.state.playerName}</b></size>\n{RoleRules.RoleLabel(occ.state.role)}\n\n{(occ.state.isReady ? "<color=#4CDB6B>READY</color>" : "<color=#ED4238>NOT READY</color>")}\n\n<size=70%><color=#8A93A0>This station's settings are controlled on {colorLabel}'s headset.</color></size>"
                        : $"<color=#8A93A0>STATION {colorLabel}\nWaiting for operator...</color>";
                return;
            }

            // Status tab
            var me = LobbyPlayerState.Local;
            bool connected = StationSeats.IsConnected();
            if (m_PlayerName) m_PlayerName.text = me != null ? me.playerName : XRINetworkGameManager.LocalPlayerName.Value;
            if (m_ConnectionDot) m_ConnectionDot.color = connected ? StationColors.Ok : StationColors.Bad;
            if (m_ConnectionText) m_ConnectionText.text = connected ? "Connected" : "Not connected - create or join a room on the centre monitor";
            if (m_RoleText) m_RoleText.text = $"{RoleRules.RoleLabel(m_SeatRole)}  <size=75%><color=#A0A8B4>({(m_SeatRole == PlayerRole.Driver ? "Logitech G29 operator" : "VR controller operator")})</color></size>";
            if (m_HardwareText) m_HardwareText.text = HardwareLine();
            if (m_LanguageNote) m_LanguageNote.text = FireCenterSettings.language == GameLanguage.Arabic ? "<color=#FFB347>Saved - Arabic UI text not translated yet</color>" : "Saved";

            // Microphone (only real when the existing Vivox voice chat is active)
            if (m_Voice == null) m_Voice = FindAnyObjectByType<VoiceChatManager>();
            bool voiceAvailable = m_Voice != null && m_Voice.isActiveAndEnabled && XRINetworkGameManager.CurrentSessionType == XRMultiplayer.SessionType.DistributedAuthority && connected;
            if (m_Microphone != null)
            {
                m_Microphone.interactable = voiceAvailable;
                m_Microphone.SetValueWithoutNotify(m_Voice != null && m_Voice.selfMuted.Value ? 1 : 0);
            }
            if (m_MicrophoneNote) m_MicrophoneNote.text = voiceAvailable ? "" : "<color=#FFB347>Voice chat unavailable (not connected / local network mode)</color>";

            RefreshDriving();
            RefreshReady();
        }

        string HardwareLine()
        {
            var probe = InputDeviceProbe.Instance;
            bool g29 = probe != null && probe.g29Detected;
            bool ctrl = probe != null && probe.vrControllersPresent;
            var me = LobbyPlayerState.Local;
            string line = m_SeatRole == PlayerRole.Driver
                ? (g29 ? "<color=#4CDB6B>Logitech G29 connected</color>" : "<color=#FFB347>Logitech G29 not detected</color>")
                : (ctrl ? "<color=#4CDB6B>VR controllers detected</color>" : "<color=#FFB347>VR controllers not detected</color>");
            bool hasHardware = m_SeatRole == PlayerRole.Driver ? g29 : ctrl;
            if (me != null && me.editorHardwareBypass && !hasHardware)
                line += "  <size=80%><color=#A0A8B4>(Editor test mode: allowed without hardware)</color></size>";
            return line;
        }

        void RefreshDriving()
        {
            var probe = InputDeviceProbe.Instance;
            bool g29 = probe != null && probe.g29Detected;
            if (m_G29Dot) m_G29Dot.color = g29 ? StationColors.Ok : StationColors.Bad;
            if (m_G29Text) m_G29Text.text = g29 ? $"G29 connected  <size=75%><color=#A0A8B4>{probe.g29DeviceName}</color></size>" : "G29 not connected - wheel settings are saved and apply when it is plugged in";
            if (m_Calibrate) m_Calibrate.interactable = g29;
            if (m_CalibrationText) m_CalibrationText.text = $"Centre offset: {FireCenterSettings.steeringCenterOffset:+0.000;-0.000;0.000}" + (g29 ? "  (hold the wheel straight, then CALIBRATE)" : "");
        }

        void RefreshReady()
        {
            var me = LobbyPlayerState.Local;
            bool connected = me != null && StationSeats.IsConnected();
            bool ready = connected && me.isReady;
            bool supported = connected && me.SupportsRole(me.role);
            bool busy = MissionLauncher.Instance != null && MissionLauncher.Instance.sequenceRunning;
            if (m_ReadyButton)
            {
                m_ReadyButton.interactable = connected && !busy && (ready || supported);
                if (m_ReadyButton.targetGraphic) m_ReadyButton.targetGraphic.color = ready ? StationColors.Ok : k_ReadyOff;
            }
            if (m_ReadyLabel) m_ReadyLabel.text = ready ? "READY  -  TAP TO CANCEL" : "READY MISSION";
            if (m_ReadyHint)
                m_ReadyHint.text = !connected ? "Join a room to get ready."
                    : !supported ? $"<color=#FFB347>This computer does not have the hardware for the {RoleRules.RoleLabel(me.role)} role.</color>"
                    : ready ? "<color=#4CDB6B>You are ready. Waiting for the other operator.</color>" : "";
        }

        // ------------------------------------------------------------------ actions

        void ToggleReady()
        {
            var me = LobbyPlayerState.Local;
            if (me == null) return;
            me.TrySetReady(!me.isReady);
            RefreshReady();
        }

        void SetMicMuted(bool muted)
        {
            if (m_Voice == null) m_Voice = FindAnyObjectByType<VoiceChatManager>();
            if (m_Voice != null && m_Voice.isActiveAndEnabled) m_Voice.ToggleSelfMute(true, muted);
        }

        void CalibrateCenter()
        {
            var probe = InputDeviceProbe.Instance;
            float raw = probe != null ? probe.ReadRawSteering() : float.NaN;
            if (float.IsNaN(raw)) return;
            FireCenterSettings.steeringCenterOffset = -raw;
            SteeringCalibrationProcessor.ApplyToSteeringAction();
            RefreshDriving();
        }

        void UpdateLiveDrivingInput()
        {
            if (m_SeatRole != PlayerRole.Driver || m_Tab != 3 || m_Steer == null) return;
            float steer = m_Steer.enabled ? m_Steer.ReadValue<float>() : 0f;
            if (m_SteerBar)
            {
                m_SteerBar.anchoredPosition = new Vector2(steer >= 0f ? 0f : steer * m_SteerBarHalfWidth, 0f);
                m_SteerBar.sizeDelta = new Vector2(Mathf.Abs(steer) * m_SteerBarHalfWidth, m_SteerBar.sizeDelta.y);
            }
            if (m_ThrottleFill) m_ThrottleFill.fillAmount = m_Throttle != null && m_Throttle.enabled ? Mathf.Clamp01(m_Throttle.ReadValue<float>()) : 0f;
            if (m_BrakeFill) m_BrakeFill.fillAmount = m_Brake != null && m_Brake.enabled ? Mathf.Clamp01(m_Brake.ReadValue<float>()) : 0f;
        }

        // ------------------------------------------------------------------ values

        void LoadValues()
        {
            var audio = AudioSettingsManager.Instance;
            for (int i = 0; i < m_VolumeSliders.Length; i++)
            {
                if (m_VolumeSliders[i] == null) continue;
                float v = audio != null ? audio.GetVolume((VolumeCategory)i) : 0.8f;
                m_VolumeSliders[i].SetValueWithoutNotify(v);
                SetPercent(i, v);
            }
            m_Language?.SetValueWithoutNotify((int)FireCenterSettings.language);
            m_TurnStyle?.SetValueWithoutNotify((int)FireCenterSettings.turnStyle);
            Set(m_TurnSensitivity, Mathf.Clamp(Mathf.Round(FireCenterSettings.snapTurnAngle / 15f), 1, 6));
            Set(m_Height, Mathf.Round(FireCenterSettings.heightOffset / 0.05f));
            Set(m_SteerSensitivity, Mathf.Round(FireCenterSettings.g29SteeringSensitivity / 0.25f));
            Set(m_Deadzone, Mathf.Round(FireCenterSettings.steeringDeadzone / 0.05f));
            UpdateLabels();
        }

        void UpdateLabels()
        {
            if (m_TurnSensitivityValue && m_TurnSensitivity)
                m_TurnSensitivityValue.text = FireCenterSettings.turnStyle == TurnStyle.Snap ? $"{m_TurnSensitivity.value * 15f:0} deg" : $"{m_TurnSensitivity.value * 30f:0} deg/s";
            if (m_HeightValue && m_Height) m_HeightValue.text = $"{m_Height.value * 5f:+0;-0;0} cm";
            if (m_SteerSensitivityValue && m_SteerSensitivity) m_SteerSensitivityValue.text = $"{m_SteerSensitivity.value * 0.25f:0.00}x";
            if (m_DeadzoneValue && m_Deadzone) m_DeadzoneValue.text = $"{m_Deadzone.value * 5f:0}%";
        }

        void SetPercent(int i, float v)
        {
            if (i < m_VolumeValues.Length && m_VolumeValues[i] != null) m_VolumeValues[i].text = $"{Mathf.RoundToInt(v * 100f)}%";
        }

        void Bind(Slider s, System.Action<float> onChange)
        {
            if (s == null) return;
            s.onValueChanged.AddListener(v => { onChange(v); UpdateLabels(); });
        }

        static void Set(Slider s, float v) { if (s != null) s.SetValueWithoutNotify(v); }
    }
}
