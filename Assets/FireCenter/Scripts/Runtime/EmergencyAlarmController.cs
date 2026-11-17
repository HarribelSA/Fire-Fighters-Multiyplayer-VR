using System.Collections;
using UnityEngine;

namespace FireCenter
{
    /// <summary>
    /// Lobby audio states:
    ///   Lobby            -> calm background music, fire-station alarm bells silent.
    ///   Dispatch event   -> music fades down, the station's EmergencyBell sources ring (Alarm mixer group).
    ///   After dispatch   -> the lobby scene unloads with the synchronized mission load, which stops the bells.
    ///   Start cancelled  -> bells stop and the music fades back up.
    /// The bells never start on scene load, room creation or joining - only on MissionLauncher.alertStarted.
    /// </summary>
    public class EmergencyAlarmController : MonoBehaviour
    {
        [SerializeField, Tooltip("The fire station EmergencyBell AudioSources (Play On Awake must be off).")]
        AudioSource[] m_AlarmBells;
        [SerializeField, Tooltip("Lobby background music source.")]
        AudioSource m_LobbyMusic;
        [SerializeField, Range(0f, 1f), Tooltip("Music volume multiplier while the alarm sounds.")]
        float m_MusicDuckLevel = 0.12f;
        [SerializeField] float m_FadeSeconds = 1.2f;
        [SerializeField, Tooltip("Night ambience loops (crickets, wind, room tone) faded out when the mission begins.")]
        AudioSource[] m_Ambience;
        [SerializeField] float m_AmbienceFadeSeconds = 2.5f;

        float m_MusicBaseVolume = 1f;
        float[] m_AmbienceBase;
        bool m_AlarmActive;
        Coroutine m_Fade;
        Coroutine m_AmbienceFade;

        public bool alarmActive => m_AlarmActive;

        void Awake()
        {
            if (m_LobbyMusic != null) m_MusicBaseVolume = m_LobbyMusic.volume;
            m_Ambience ??= new AudioSource[0];
            m_AmbienceBase = new float[m_Ambience.Length];
            for (int i = 0; i < m_Ambience.Length; i++) m_AmbienceBase[i] = m_Ambience[i] != null ? m_Ambience[i].volume : 0f;
            // Guarantee silence at load even if a bell prefab instance still has Play On Awake set.
            foreach (var bell in m_AlarmBells)
            {
                if (bell == null) continue;
                bell.playOnAwake = false;
                bell.Stop();
            }
        }

        void Start()
        {
            // Stop again after every Awake has run (Play On Awake triggers during activation).
            foreach (var bell in m_AlarmBells) if (bell != null) bell.Stop();
            if (MissionLauncher.Instance != null)
            {
                MissionLauncher.Instance.alertStarted += StartAlarm;
                MissionLauncher.Instance.missionFailed += OnMissionFailed;
            }
        }

        void OnDestroy()
        {
            if (MissionLauncher.Instance != null)
            {
                MissionLauncher.Instance.alertStarted -= StartAlarm;
                MissionLauncher.Instance.missionFailed -= OnMissionFailed;
            }
        }

        void OnMissionFailed(string reason) => StopAlarm();

        public void StartAlarm()
        {
            if (m_AlarmActive) return;
            m_AlarmActive = true;
            FadeMusic(m_MusicBaseVolume * m_MusicDuckLevel);
            FadeAmbience(0f);
            foreach (var bell in m_AlarmBells)
                if (bell != null && !bell.isPlaying) bell.Play();
        }

        public void StopAlarm()
        {
            if (!m_AlarmActive) return;
            m_AlarmActive = false;
            foreach (var bell in m_AlarmBells) if (bell != null) bell.Stop();
            FadeMusic(m_MusicBaseVolume);
            FadeAmbience(1f);
        }

        void FadeAmbience(float multiplier)
        {
            if (m_Ambience.Length == 0) return;
            if (m_AmbienceFade != null) StopCoroutine(m_AmbienceFade);
            m_AmbienceFade = StartCoroutine(AmbienceRoutine(multiplier));
        }

        IEnumerator AmbienceRoutine(float multiplier)
        {
            var start = new float[m_Ambience.Length];
            for (int i = 0; i < m_Ambience.Length; i++) start[i] = m_Ambience[i] != null ? m_Ambience[i].volume : 0f;
            for (float t = 0f; t < m_AmbienceFadeSeconds; t += Time.unscaledDeltaTime)
            {
                float k = Mathf.SmoothStep(0f, 1f, t / m_AmbienceFadeSeconds);
                for (int i = 0; i < m_Ambience.Length; i++)
                    if (m_Ambience[i] != null) m_Ambience[i].volume = Mathf.Lerp(start[i], m_AmbienceBase[i] * multiplier, k);
                yield return null;
            }
            for (int i = 0; i < m_Ambience.Length; i++)
                if (m_Ambience[i] != null) m_Ambience[i].volume = m_AmbienceBase[i] * multiplier;
            m_AmbienceFade = null;
        }

        void FadeMusic(float target)
        {
            if (m_LobbyMusic == null) return;
            if (m_Fade != null) StopCoroutine(m_Fade);
            m_Fade = StartCoroutine(FadeRoutine(target));
        }

        IEnumerator FadeRoutine(float target)
        {
            float start = m_LobbyMusic.volume;
            for (float t = 0f; t < m_FadeSeconds; t += Time.unscaledDeltaTime)
            {
                m_LobbyMusic.volume = Mathf.Lerp(start, target, t / m_FadeSeconds);
                yield return null;
            }
            m_LobbyMusic.volume = target;
            m_Fade = null;
        }
    }
}
