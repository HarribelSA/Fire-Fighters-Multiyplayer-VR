using TMPro;
using Unity.Netcode;
using UnityEngine.InputSystem;
using UnityEngine;
using UnityEngine.UI;

namespace FireCenter
{
    /// <summary>
    /// The tabbed Settings panel (Audio / VR Comfort / Gameplay / General) inside the lobby monitor canvas.
    /// Volume sliders, the controller-mode buttons and the Back button stay wired by <see cref="LobbyMenuController"/>
    /// exactly as before; this component drives the tabs and every additional setting:
    ///  - Mute (AudioSettingsManager), VR comfort (FireCenterSettings -> ComfortSettingsApplier),
    ///  - mission settings (host-controlled while connected), language, team name, G29 sensitivity.
    /// Everything is stored locally; mission settings are published by the host via LobbyPlayerState.
    /// </summary>
    public class SettingsPanelController : MonoBehaviour
    {
        [Header("Tabs (Audio, VR Comfort, Gameplay, General)")]
        [SerializeField] Button[] m_TabButtons = new Button[4];
        [SerializeField] GameObject[] m_Sections = new GameObject[4];

        [Header("Audio")]
        [SerializeField] SegmentedControl m_Mute;

        [Header("VR Comfort")]
        [SerializeField] SegmentedControl m_TurnStyle;
        [SerializeField] Slider m_SnapAngle;            // 1..6  -> 15..90 degrees
        [SerializeField] TMP_Text m_SnapAngleValue;
        [SerializeField] Slider m_SmoothTurnSpeed;      // 2..12 -> 30..180 deg/s
        [SerializeField] TMP_Text m_SmoothTurnSpeedValue;
        [SerializeField] Slider m_MoveSpeed;            // 2..8  -> 1.0..4.0 m/s
        [SerializeField] TMP_Text m_MoveSpeedValue;
        [SerializeField] SegmentedControl m_Teleport;   // ON | OFF
        [SerializeField] SegmentedControl m_Vignette;   // ON | OFF
        [SerializeField] TMP_Text m_VignetteStatus;
        [SerializeField] Slider m_Height;               // -6..6 -> -30..+30 cm
        [SerializeField] TMP_Text m_HeightValue;
        [SerializeField] TMP_Text m_ComfortNote;

        [Header("Gameplay (host-controlled in a session)")]
        [SerializeField] SegmentedControl m_Difficulty;
        [SerializeField] SegmentedControl m_Traffic;
        [SerializeField] SegmentedControl m_FireSpread;
        [SerializeField] TMP_Text m_GameplayNote;

        [Header("General")]
        [SerializeField] SegmentedControl m_Language;
        [SerializeField] TMP_Text m_LanguageNote;
        [SerializeField] TMP_InputField m_TeamName;
        [SerializeField] TMP_Text m_TeamStatus;
        [SerializeField] Slider m_G29Sensitivity;       // 1..8 -> 0.25..2.0
        [SerializeField] TMP_Text m_G29Value;
        [SerializeField] TMP_Text m_G29Status;

        static readonly Color k_TabOn = new(0.86f, 0.24f, 0.16f, 1f);
        static readonly Color k_TabOff = new(0.14f, 0.16f, 0.2f, 1f);
        float m_NextRefresh;

        void Awake()
        {
            for (int i = 0; i < m_TabButtons.Length; i++)
            {
                int captured = i;
                if (m_TabButtons[i] != null) m_TabButtons[i].onClick.AddListener(() => ShowTab(captured));
            }
            ShowTab(0);
        }

        void Start()
        {
            // Audio
            if (m_Mute != null) m_Mute.valueChanged += i => AudioSettingsManager.Instance?.SetMuted(i == 1);

            // VR comfort
            if (m_TurnStyle != null) m_TurnStyle.valueChanged += i => { FireCenterSettings.turnStyle = (TurnStyle)i; RefreshComfortVisibility(); };
            Bind(m_SnapAngle, v => FireCenterSettings.snapTurnAngle = v * 15f);
            Bind(m_SmoothTurnSpeed, v => FireCenterSettings.smoothTurnSpeed = v * 15f);
            Bind(m_MoveSpeed, v => FireCenterSettings.moveSpeed = v * 0.5f);
            Bind(m_Height, v => FireCenterSettings.heightOffset = v * 0.05f);
            if (m_Teleport != null) m_Teleport.valueChanged += i => FireCenterSettings.teleportEnabled = i == 0;
            if (m_Vignette != null) m_Vignette.valueChanged += i => FireCenterSettings.vignetteEnabled = i == 0;

            // Gameplay (stored locally; the host publishes them for the session)
            if (m_Difficulty != null) m_Difficulty.valueChanged += i => FireCenterSettings.difficulty = (GameDifficulty)i;
            if (m_Traffic != null) m_Traffic.valueChanged += i => FireCenterSettings.trafficDensity = (TrafficDensity)i;
            if (m_FireSpread != null) m_FireSpread.valueChanged += i => FireCenterSettings.fireSpread = (GameDifficulty)i;

            // General
            if (m_Language != null) m_Language.valueChanged += i => { FireCenterSettings.language = (GameLanguage)i; RefreshLanguageNote(); };
            if (m_TeamName != null)
            {
                m_TeamName.characterLimit = 40;
                m_TeamName.onEndEdit.AddListener(s => { if (CanEditMissionSettings()) FireCenterSettings.teamName = s; });
            }
            Bind(m_G29Sensitivity, v => { FireCenterSettings.g29SteeringSensitivity = v * 0.25f; ApplyG29Sensitivity(); });

            if (InputDeviceProbe.Instance != null) InputDeviceProbe.Instance.statusChanged += RefreshG29;
            LobbyPlayerState.anyStateChanged += RefreshMissionControls;

            LoadValues();
            ApplyG29Sensitivity();
        }

        void OnDestroy()
        {
            if (InputDeviceProbe.Instance != null) InputDeviceProbe.Instance.statusChanged -= RefreshG29;
            LobbyPlayerState.anyStateChanged -= RefreshMissionControls;
        }

        void OnEnable()
        {
            if (Time.frameCount > 1) LoadValues();
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextRefresh) return;
            m_NextRefresh = Time.unscaledTime + 0.5f;
            RefreshMissionControls();
        }

        // ------------------------------------------------------------------ tabs

        public void ShowTab(int index)
        {
            for (int i = 0; i < m_Sections.Length; i++)
                if (m_Sections[i] != null) m_Sections[i].SetActive(i == index);
            for (int i = 0; i < m_TabButtons.Length; i++)
                if (m_TabButtons[i] != null && m_TabButtons[i].targetGraphic != null)
                    m_TabButtons[i].targetGraphic.color = i == index ? k_TabOn : k_TabOff;
        }

        // ------------------------------------------------------------------ load / refresh

        void LoadValues()
        {
            var audio = AudioSettingsManager.Instance;
            m_Mute?.SetValueWithoutNotify(audio != null && audio.isMuted ? 1 : 0);

            m_TurnStyle?.SetValueWithoutNotify((int)FireCenterSettings.turnStyle);
            Set(m_SnapAngle, Mathf.Round(FireCenterSettings.snapTurnAngle / 15f));
            Set(m_SmoothTurnSpeed, Mathf.Round(FireCenterSettings.smoothTurnSpeed / 15f));
            Set(m_MoveSpeed, Mathf.Round(FireCenterSettings.moveSpeed / 0.5f));
            Set(m_Height, Mathf.Round(FireCenterSettings.heightOffset / 0.05f));
            m_Teleport?.SetValueWithoutNotify(FireCenterSettings.teleportEnabled ? 0 : 1);
            m_Vignette?.SetValueWithoutNotify(FireCenterSettings.vignetteEnabled ? 0 : 1);

            m_Language?.SetValueWithoutNotify((int)FireCenterSettings.language);
            Set(m_G29Sensitivity, Mathf.Round(FireCenterSettings.g29SteeringSensitivity / 0.25f));

            UpdateValueLabels();
            RefreshComfortVisibility();
            RefreshLanguageNote();
            RefreshMissionControls();
            RefreshG29();
        }

        void UpdateValueLabels()
        {
            if (m_SnapAngleValue && m_SnapAngle) m_SnapAngleValue.text = $"{m_SnapAngle.value * 15f:0} deg";
            if (m_SmoothTurnSpeedValue && m_SmoothTurnSpeed) m_SmoothTurnSpeedValue.text = $"{m_SmoothTurnSpeed.value * 15f:0} deg/s";
            if (m_MoveSpeedValue && m_MoveSpeed) m_MoveSpeedValue.text = $"{m_MoveSpeed.value * 0.5f:0.0} m/s";
            if (m_HeightValue && m_Height) m_HeightValue.text = $"{m_Height.value * 5f:+0;-0;0} cm";
            if (m_G29Value && m_G29Sensitivity) m_G29Value.text = $"{m_G29Sensitivity.value * 0.25f:0.00}x";
        }

        void RefreshComfortVisibility()
        {
            var applier = ComfortSettingsApplier.Instance;
            bool smooth = FireCenterSettings.turnStyle == TurnStyle.Smooth;
            if (m_SnapAngle) m_SnapAngle.interactable = !smooth;
            if (m_SmoothTurnSpeed) m_SmoothTurnSpeed.interactable = smooth;
            if (m_VignetteStatus)
                m_VignetteStatus.text = applier == null || applier.hasVignette ? "" : "<color=#FFB347>Not available on this XR rig</color>";
            if (m_Vignette != null && applier != null) m_Vignette.interactable = applier.hasVignette;
            if (m_ComfortNote)
                m_ComfortNote.text = "Lobby uses <b>teleportation only</b>; move speed applies where walking is enabled (mission areas).";
        }

        void RefreshLanguageNote()
        {
            if (m_LanguageNote == null) return;
            m_LanguageNote.text = FireCenterSettings.language == GameLanguage.Arabic
                ? "<color=#FFB347>Saved. Arabic UI text is not translated yet.</color>"
                : "Saved.";
        }

        bool CanEditMissionSettings()
        {
            var nm = NetworkManager.Singleton;
            bool connected = nm != null && nm.IsListening && nm.IsConnectedClient;
            return !connected || LobbyPlayerState.IsLocalHostAuthority(nm);
        }

        void RefreshMissionControls()
        {
            bool canEdit = CanEditMissionSettings();
            var host = MissionSettings.Host;
            bool showHost = !canEdit && host != null;

            void Show(SegmentedControl c, int local, int fromHost)
            {
                if (c == null) return;
                c.interactable = canEdit;
                c.SetValueWithoutNotify(showHost ? fromHost : local);
            }
            Show(m_Difficulty, (int)FireCenterSettings.difficulty, host != null ? (int)host.sessionDifficulty : 0);
            Show(m_Traffic, (int)FireCenterSettings.trafficDensity, host != null ? (int)host.sessionTraffic : 0);
            Show(m_FireSpread, (int)FireCenterSettings.fireSpread, host != null ? (int)host.sessionFireSpread : 0);

            if (m_TeamName != null)
            {
                m_TeamName.interactable = canEdit;
                if (!m_TeamName.isFocused)
                    m_TeamName.SetTextWithoutNotify(showHost ? host.sessionTeamName : FireCenterSettings.teamName);
            }
            string who = canEdit ? (NetworkManager.Singleton != null && NetworkManager.Singleton.IsConnectedClient ? "You are the Host: changes apply to this session." : "Saved locally; applied when you host a room.")
                                 : "<color=#FFB347>Set by the Host.</color>";
            if (m_TeamStatus) m_TeamStatus.text = who;
            if (m_GameplayNote)
                m_GameplayNote.text = who + "\n<size=85%><color=#A0A8B4>Stored for the mission systems (traffic, fire spread and difficulty logic are not implemented yet).</color></size>";
        }

        void RefreshG29()
        {
            var probe = InputDeviceProbe.Instance;
            bool available = probe != null && probe.g29Detected;
            if (m_G29Sensitivity) m_G29Sensitivity.interactable = available;
            if (m_G29Status)
                m_G29Status.text = available ? "<color=#7CFC8A>G29 detected</color>" : "<color=#FFB347>Unavailable - Logitech G29 not detected</color>";
        }

        /// <summary>Applies the sensitivity as a scale processor override on the G29 steering binding(s).</summary>
        static void ApplyG29Sensitivity() => SteeringCalibrationProcessor.ApplyToSteeringAction();

        // ------------------------------------------------------------------ helpers

        void Bind(Slider s, System.Action<float> onChange)
        {
            if (s == null) return;
            s.onValueChanged.AddListener(v => { onChange(v); UpdateValueLabels(); });
        }

        static void Set(Slider s, float v) { if (s != null) s.SetValueWithoutNotify(v); }
    }
}
