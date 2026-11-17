using System;
using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireCenter
{
    /// <summary>Phases of the synchronized Lobby -> Loading Scene -> Mission transition (each maps to a real scene event).</summary>
    public enum TransitionPhase
    {
        None,
        Dispatch,            // dispatch alert in the lobby
        SyncingOperators,    // loading scene shown, waiting until every client has loaded it
        LoadingMission,      // mission scene loading (additive, synchronized)
        PreparingPlayers,    // mission loaded locally; rig placed on the role spawn, waiting for all clients
        MissionReady,        // every client finished loading the mission
        Entering,            // fading into the mission / loading scene unloading
    }

    /// <summary>
    /// START MISSION flow (host = session owner drives every scene operation through Netcode's synchronized
    /// NetworkSceneManager, clients follow automatically - nothing is loaded independently per client):
    ///   1. host validates connections, roles, readiness and hardware
    ///   2. all: dispatch tone + emergency alert, lobby music/ambience fade, black fade
    ///   3. host: load the Loading Scene (Single)            -> lobby unloads, animated loading screen appears
    ///   4. host: when every client has it, load the mission (Additive, async)
    ///   5. all:  mission loaded locally -> made active, rig placed on the role spawn
    ///   6. all:  when every client has the mission -> "Mission ready", fade out
    ///   7. host: unload the Loading Scene (synchronized)    -> all fade into the mission
    /// The XR rig, managers, roles, colours and settings persist (DontDestroyOnLoad / player objects).
    /// If an additive load is rejected by the network topology, it falls back to a synchronized Single load.
    /// </summary>
    public class MissionLauncher : MonoBehaviour
    {
        public static MissionLauncher Instance { get; private set; }

        [SerializeField, Tooltip("Scene name of the mission. Must be in Build Profiles / Build Settings.")]
        string m_MissionSceneName = "FirefightingMission";
        [SerializeField, Tooltip("The animated Loading Scene shown between the lobby and the mission.")]
        string m_LoadingSceneName = "UITranstion";
        [SerializeField] AudioSource m_DispatchAudioSource;
        [SerializeField] AudioClip m_DispatchClip;
        [SerializeField, Tooltip("How long the emergency alert is shown before the fade starts.")]
        float m_AlertDuration = 3.5f;
        [SerializeField] float m_FadeDuration = 1.5f;
        [SerializeField, Tooltip("Minimum time the loading screen is visible before the mission load starts (presentation only).")]
        float m_MinLoadingScreenTime = 1.5f;
        [SerializeField, Tooltip("How long 'Mission ready' is shown before entering the mission.")]
        float m_ReadyHoldTime = 1.2f;

        public string missionSceneName => m_MissionSceneName;
        public string loadingSceneName => m_LoadingSceneName;
        public bool sequenceRunning { get; private set; }
        public TransitionPhase phase { get; private set; }

        /// <summary>UI hook: show the incoming emergency alert on the computer screen.</summary>
        public event Action alertStarted;
        /// <summary>UI hook: a validation or load step failed (message for the status line).</summary>
        public event Action<string> missionFailed;
        /// <summary>Loading screen hook: the transition phase changed.</summary>
        public event Action<TransitionPhase> phaseChanged;

        float m_LoadingSceneShownAt;
        AsyncOperation m_MissionLoadOp;
        bool m_MissionLoadRequested;
        bool m_UsedSingleFallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(this); return; }
            Instance = this;
        }

        public static string PhaseText(TransitionPhase p)
        {
            switch (p)
            {
                case TransitionPhase.SyncingOperators: return "Synchronizing multiplayer...";
                case TransitionPhase.LoadingMission: return "Loading mission area...";
                case TransitionPhase.PreparingPlayers: return "Preparing equipment...";
                case TransitionPhase.MissionReady: return "Mission ready...";
                case TransitionPhase.Entering: return "Entering mission...";
                default: return "Connecting operators...";
            }
        }

        void SetPhase(TransitionPhase p)
        {
            phase = p;
            phaseChanged?.Invoke(p);
        }

        // ---- Host side ---------------------------------------------------------------------

        /// <summary>Returns null if the mission can start, otherwise the reason it cannot.</summary>
        public static string ValidateStart()
        {
            var nm = NetworkManager.Singleton;
            if (nm == null || !nm.IsListening || !nm.IsConnectedClient) return "Not connected to a session.";
            if (!LobbyPlayerState.IsLocalHostAuthority(nm)) return "Only the Host can start the mission.";
            return ValidateOperators();
        }

        /// <summary>
        /// Mission start conditions shared by every client (no host check): both operators connected,
        /// valid roles (one Driver, one Firefighter) supported by each operator's hardware, both Ready.
        /// </summary>
        public static string ValidateOperators()
        {
            var nm = NetworkManager.Singleton;
            // Solo: the local operator plus the existing station bot (Play With Bot).
            if (nm == null || !nm.IsListening)
            {
                var all = LobbyPlayerState.All; var me = LobbyPlayerState.Local;
                if (me == null || me.role == PlayerRole.Unassigned) return "Select your station first.";
                if (all.Count < 2) return "Create a room for two operators, or choose Play With Bot.";
                return null;
            }
            if (!nm.IsConnectedClient) return "Not connected to a session.";
            var players = LobbyPlayerState.All;
            if (players.Count < LobbySessionData.MaxPlayers) return "Waiting for the second operator to connect.";

            bool hasDriver = false, hasFirefighter = false;
            foreach (var p in players)
            {
                if (p == null || !p.IsSpawned) return "An operator connection is not ready yet.";
                if (p.role == PlayerRole.Unassigned) return $"{StationColors.Label(p.color)} has no role yet.";
                if (LobbySessionData.enforceControllerCompatibility && !p.SupportsRole(p.role))
                    return $"{StationColors.Label(p.color)}'s computer does not support the {RoleRules.RoleLabel(p.role)} role.";
                if (!p.isReady) return $"Waiting for {StationColors.Label(p.color)} to be ready.";
                hasDriver |= p.role == PlayerRole.Driver;
                hasFirefighter |= p.role == PlayerRole.Firefighter;
            }
            if (!hasDriver || !hasFirefighter) return "Roles not valid: one Driver and one Firefighter are required.";
            return null;
        }

        /// <summary>Called by the START MISSION button (host only).</summary>
        // Start = confirm Ready in the EXISTING flow: StationSessionFlow starts the synchronized countdown,
        // fade and additive station load once both stations are Ready (Solo: the existing bot test entry).
        public void RequestStartMission()
        {
            if (sequenceRunning) return;
            var me = LobbyPlayerState.Local;
            string blocker = ValidateOperators();
            bool networked = NetworkManager.Singleton != null && NetworkManager.Singleton.IsListening;
            if (me != null && (blocker == null || (networked && !me.isReady)))
            { if (!me.TrySetReady(true)) missionFailed?.Invoke("Ready was not accepted: check headset and controller tracking."); return; }
            if (blocker != null) { missionFailed?.Invoke(blocker); return; }
            if (blocker == null) return;

            string error = ValidateStart();
            if (error != null) { missionFailed?.Invoke(error); return; }

            foreach (var scene in new[] { m_MissionSceneName, m_LoadingSceneName })
            {
                if (!Application.CanStreamedLevelBeLoaded(scene))
                {
                    missionFailed?.Invoke($"Scene '{scene}' is not in the build scene list.");
                    return;
                }
            }

            LobbyPlayerState.Local.BroadcastMissionStart(m_MissionSceneName);
        }

        // ---- All clients -------------------------------------------------------------------

        /// <summary>Invoked on every client by <see cref="LobbyPlayerState"/>'s RPC.</summary>
        public void PlayMissionSequence(string missionScene, ulong hostClientId)
        {
            if (sequenceRunning) return; // prevents duplicate transitions / duplicate loading screens
            m_MissionSceneName = missionScene;
            StartCoroutine(MissionSequence());
        }

        IEnumerator MissionSequence()
        {
            sequenceRunning = true;
            m_MissionLoadRequested = false;
            m_UsedSingleFallback = false;
            m_MissionLoadOp = null;
            SetPhase(TransitionPhase.Dispatch);

            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null) nm.SceneManager.OnSceneEvent += OnSceneEvent;

            if (m_DispatchAudioSource != null && m_DispatchClip != null)
                m_DispatchAudioSource.PlayOneShot(m_DispatchClip);

            alertStarted?.Invoke();
            yield return new WaitForSecondsRealtime(m_AlertDuration);

            if (ScreenFader.Instance != null)
                yield return ScreenFader.Instance.FadeOut(m_FadeDuration);

            if (LobbyPlayerState.IsLocalHostAuthority(nm))
            {
                // Re-validate right before loading - a player may have dropped during the alert.
                if (LobbyPlayerState.All.Count < LobbySessionData.MaxPlayers)
                {
                    Abort("An operator disconnected. Mission start cancelled.");
                    yield break;
                }

                var status = nm.SceneManager.LoadScene(m_LoadingSceneName, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started)
                {
                    // Without the loading scene, go straight to the mission (still synchronized).
                    Debug.LogWarning($"[FireCenter] Loading scene could not be loaded ({status}); loading the mission directly.");
                    m_UsedSingleFallback = true;
                    status = nm.SceneManager.LoadScene(m_MissionSceneName, LoadSceneMode.Single);
                    if (status != SceneEventProgressStatus.Started) Abort($"Could not load the mission scene ({status}).");
                }
            }
            // Clients wait: Netcode loads the scenes for them; OnSceneEvent drives the rest.
        }

        void OnSceneEvent(SceneEvent e)
        {
            var nm = NetworkManager.Singleton;
            if (nm == null) return;
            bool isHost = LobbyPlayerState.IsLocalHostAuthority(nm);
            bool local = e.ClientId == nm.LocalClientId;

            switch (e.SceneEventType)
            {
                case SceneEventType.Load when local && e.SceneName == m_MissionSceneName:
                    m_MissionLoadOp = e.AsyncOperation;   // real async progress of this client's mission load
                    break;

                case SceneEventType.LoadComplete when local && e.SceneName == m_LoadingSceneName:
                    m_LoadingSceneShownAt = Time.unscaledTime;
                    SetPhase(TransitionPhase.SyncingOperators);
                    break;

                case SceneEventType.LoadEventCompleted when e.SceneName == m_LoadingSceneName:
                    LogTimeouts(e.SceneName, e.ClientsThatTimedOut);
                    SetPhase(TransitionPhase.LoadingMission);
                    if (isHost) StartCoroutine(LoadMissionAfterMinimumTime());
                    break;

                case SceneEventType.LoadComplete when local && e.SceneName == m_MissionSceneName:
                    if (e.LoadSceneMode == LoadSceneMode.Additive)
                    {
                        var mission = SceneManager.GetSceneByName(m_MissionSceneName);
                        if (mission.IsValid()) SceneManager.SetActiveScene(mission);
                    }
                    SetPhase(TransitionPhase.PreparingPlayers);
                    break;

                case SceneEventType.LoadEventCompleted when e.SceneName == m_MissionSceneName:
                    LogTimeouts(e.SceneName, e.ClientsThatTimedOut);
                    SetPhase(TransitionPhase.MissionReady);
                    StartCoroutine(EnterMission(isHost, e.LoadSceneMode == LoadSceneMode.Single || m_UsedSingleFallback));
                    break;

                case SceneEventType.UnloadComplete when local && e.SceneName == m_LoadingSceneName:
                    Finish();
                    break;
            }
        }

        IEnumerator LoadMissionAfterMinimumTime()
        {
            if (m_MissionLoadRequested) yield break;
            m_MissionLoadRequested = true;
            float wait = m_MinLoadingScreenTime - (Time.unscaledTime - m_LoadingSceneShownAt);
            if (wait > 0f) yield return new WaitForSecondsRealtime(wait);

            var nm = NetworkManager.Singleton;
            var status = nm.SceneManager.LoadScene(m_MissionSceneName, LoadSceneMode.Additive);
            if (status != SceneEventProgressStatus.Started)
            {
                Debug.LogWarning($"[FireCenter] Additive mission load not started ({status}); using a synchronized Single load.");
                m_UsedSingleFallback = true;
                status = nm.SceneManager.LoadScene(m_MissionSceneName, LoadSceneMode.Single);
                if (status != SceneEventProgressStatus.Started) Abort($"Could not load the mission scene ({status}).");
            }
        }

        /// <summary>
        /// REAL transition progress (0..1) for the loading indicator, built only from actual events:
        /// loading scene shown, this client's async mission load (AsyncOperation.progress), mission loaded locally,
        /// and every client finished. It reaches 1 only after ALL clients have the mission scene.
        /// </summary>
        public float realProgress
        {
            get
            {
                switch (phase)
                {
                    case TransitionPhase.SyncingOperators: return 0.08f;
                    case TransitionPhase.LoadingMission:
                        float op = m_MissionLoadOp != null ? Mathf.Clamp01(m_MissionLoadOp.progress / 0.9f) : 0f;
                        return 0.12f + 0.63f * op;
                    case TransitionPhase.PreparingPlayers: return 0.85f;
                    case TransitionPhase.MissionReady:
                    case TransitionPhase.Entering: return 1f;
                    default: return 0f;
                }
            }
        }

        IEnumerator EnterMission(bool isHost, bool singleMode)
        {
            // Let the loading wheel finish its fill to 100% (it may only do so now that every client is ready),
            // then hold briefly. Bounded, so a missing loading screen can never stall the transition.
            float waited = 0f;
            while (waited < 3f && !LoadingScreenController.IsDisplayComplete)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            yield return new WaitForSecondsRealtime(Mathf.Min(0.5f, m_ReadyHoldTime));
            SetPhase(TransitionPhase.Entering);
            LobbyPlayerState.Local?.ResetReady();

            if (singleMode)
            {
                // The loading scene was replaced by the Single load; ScreenFader fades in automatically.
                Finish();
                yield break;
            }

            if (ScreenFader.Instance != null) yield return ScreenFader.Instance.FadeOut(0.8f);

            if (isHost)
            {
                var nm = NetworkManager.Singleton;
                var loading = SceneManager.GetSceneByName(m_LoadingSceneName);
                if (loading.IsValid() && loading.isLoaded)
                {
                    var status = nm.SceneManager.UnloadScene(loading);
                    if (status != SceneEventProgressStatus.Started)
                    {
                        Debug.LogWarning($"[FireCenter] Could not unload the loading scene ({status}).");
                        Finish();
                    }
                }
                else Finish();
            }
        }

        void Finish()
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null) nm.SceneManager.OnSceneEvent -= OnSceneEvent;
            sequenceRunning = false;
            SetPhase(TransitionPhase.None);
            ScreenFader.Instance?.FadeIn(1.0f);
        }

        static void LogTimeouts(string scene, List<ulong> timedOut)
        {
            if (timedOut != null && timedOut.Count > 0)
                Debug.LogWarning($"[FireCenter] {timedOut.Count} client(s) timed out loading {scene}.");
        }

        void Abort(string reason)
        {
            var nm = NetworkManager.Singleton;
            if (nm != null && nm.SceneManager != null) nm.SceneManager.OnSceneEvent -= OnSceneEvent;
            sequenceRunning = false;
            SetPhase(TransitionPhase.None);
            missionFailed?.Invoke(reason);
            ScreenFader.Instance?.FadeIn(0.8f);
        }
    }
}
