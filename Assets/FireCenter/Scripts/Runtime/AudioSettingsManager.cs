using System;
using UnityEngine;
using UnityEngine.Audio;
using XRMultiplayer;

namespace FireCenter
{
    public enum VolumeCategory
    {
        Master = 0,
        Music = 1,
        SFX = 2,
        Environment = 3,
        VoiceChat = 4,
        Alarm = 5,
    }

    /// <summary>
    /// Applies the local player's volume preferences to the Audio Mixer groups and stores them in
    /// PlayerPrefs. Settings are never synchronized to other players.
    /// Voice chat (Vivox) is rendered outside the Unity mixer, so the Voice Chat slider also drives
    /// the existing <see cref="VoiceChatManager"/> output volume when voice chat is active.
    /// </summary>
    public class AudioSettingsManager : MonoBehaviour
    {
        public static AudioSettingsManager Instance { get; private set; }

        [SerializeField] AudioMixer m_Mixer;

        [Header("Exposed mixer parameters")]
        [SerializeField] string m_MasterParam = "MainVolume";
        [SerializeField] string m_MusicParam = "MusicVolume";
        [SerializeField] string m_SfxParam = "SFXVolume";
        [SerializeField] string m_EnvironmentParam = "EnvironmentVolume";
        [SerializeField] string m_VoiceParam = "VoiceVolume";
        [SerializeField] string m_AlarmParam = "AlarmVolume";

        [Header("Defaults (0-1)")]
        [SerializeField] float m_DefaultMaster = 0.9f;
        [SerializeField] float m_DefaultMusic = 0.5f;
        [SerializeField] float m_DefaultSfx = 0.8f;
        [SerializeField] float m_DefaultEnvironment = 0.6f;
        [SerializeField] float m_DefaultVoice = 0.8f;
        [SerializeField] float m_DefaultAlarm = 0.7f;

        readonly float[] m_Values = new float[6];
        bool m_Muted;
        public bool isMuted => m_Muted;
        public event Action<bool> muteChanged;

        public event Action<VolumeCategory, float> volumeChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;

            m_Values[0] = LocalSettings.GetFloat(Key(VolumeCategory.Master), m_DefaultMaster);
            m_Values[1] = LocalSettings.GetFloat(Key(VolumeCategory.Music), m_DefaultMusic);
            m_Values[2] = LocalSettings.GetFloat(Key(VolumeCategory.SFX), m_DefaultSfx);
            m_Values[3] = LocalSettings.GetFloat(Key(VolumeCategory.Environment), m_DefaultEnvironment);
            m_Values[4] = LocalSettings.GetFloat(Key(VolumeCategory.VoiceChat), m_DefaultVoice);
            m_Values[5] = LocalSettings.GetFloat(Key(VolumeCategory.Alarm), m_DefaultAlarm);
            m_Muted = LocalSettings.GetInt("Volume.Muted", 0) == 1;
        }

        void Start()
        {
            // AudioMixer.SetFloat is ignored during Awake, so apply in Start.
            for (int i = 0; i < m_Values.Length; i++)
                Apply((VolumeCategory)i, m_Values[i]);
            ApplyMute();

            XRINetworkGameManager.Connected.Subscribe(OnConnectedChanged);
        }

        void OnDestroy()
        {
            XRINetworkGameManager.Connected.Unsubscribe(OnConnectedChanged);
            if (Instance == this) Instance = null;
        }

        void OnConnectedChanged(bool connected)
        {
            // Re-apply voice volume once Vivox has joined a channel.
            if (connected) Invoke(nameof(ReapplyVoice), 5f);
        }

        void ReapplyVoice() => Apply(VolumeCategory.VoiceChat, m_Values[(int)VolumeCategory.VoiceChat]);

        public float GetVolume(VolumeCategory category) => m_Values[(int)category];

        public void SetVolume(VolumeCategory category, float value01)
        {
            value01 = Mathf.Clamp01(value01);
            m_Values[(int)category] = value01;
            Apply(category, value01);
            LocalSettings.SetFloat(Key(category), value01);
            LocalSettings.Save();
            volumeChanged?.Invoke(category, value01);
        }

        // UnityEvent-friendly setters for sliders.
        public void SetMaster(float v) => SetVolume(VolumeCategory.Master, v);
        public void SetMusic(float v) => SetVolume(VolumeCategory.Music, v);
        public void SetSfx(float v) => SetVolume(VolumeCategory.SFX, v);
        public void SetEnvironment(float v) => SetVolume(VolumeCategory.Environment, v);
        public void SetVoiceChat(float v) => SetVolume(VolumeCategory.VoiceChat, v);
        public void SetAlarm(float v) => SetVolume(VolumeCategory.Alarm, v);

        /// <summary>Mutes all game audio (and incoming voice) without losing the slider values.</summary>
        public void SetMuted(bool muted)
        {
            m_Muted = muted;
            LocalSettings.SetInt("Volume.Muted", muted ? 1 : 0);
            LocalSettings.Save();
            ApplyMute();
            muteChanged?.Invoke(muted);
        }

        void ApplyMute()
        {
            AudioListener.volume = m_Muted ? 0f : 1f;
            ApplyVoiceChatVolume(m_Muted ? 0f : m_Values[(int)VolumeCategory.VoiceChat]);
        }

        void Apply(VolumeCategory category, float value01)
        {
            string param = ParamFor(category);
            if (m_Mixer != null && !string.IsNullOrEmpty(param))
            {
                if (!m_Mixer.SetFloat(param, LinearToDecibel(value01)))
                    Debug.LogWarning($"[FireCenter] Audio mixer parameter '{param}' is not exposed on {m_Mixer.name}.", this);
            }

            if (category == VolumeCategory.VoiceChat && !m_Muted)
                ApplyVoiceChatVolume(value01);
        }

        static void ApplyVoiceChatVolume(float value01)
        {
            // Only touch Vivox if the existing voice-chat system is present and active (it is disabled in LocalOnly sessions).
            var voice = FindAnyObjectByType<VoiceChatManager>();
            if (voice == null || !voice.isActiveAndEnabled) return;
            if (!XRINetworkGameManager.Connected.Value) return;
            try
            {
                // VoiceChatManager clamps to its configured output range (default -10..10 dB).
                voice.SetOutputVolume(Mathf.Lerp(-10f, 10f, value01));
            }
            catch (Exception e)
            {
                // Vivox not initialized yet (e.g. not logged in) - the value is re-applied on connect.
                Debug.Log($"[FireCenter] Voice volume deferred: {e.Message}");
            }
        }

        string ParamFor(VolumeCategory category)
        {
            switch (category)
            {
                case VolumeCategory.Master: return m_MasterParam;
                case VolumeCategory.Music: return m_MusicParam;
                case VolumeCategory.SFX: return m_SfxParam;
                case VolumeCategory.Environment: return m_EnvironmentParam;
                case VolumeCategory.VoiceChat: return m_VoiceParam;
                case VolumeCategory.Alarm: return m_AlarmParam;
            }
            return null;
        }

        static string Key(VolumeCategory c) => "Volume." + c;

        public static float LinearToDecibel(float v) => v <= 0.0001f ? -80f : Mathf.Log10(v) * 20f;
    }
}
