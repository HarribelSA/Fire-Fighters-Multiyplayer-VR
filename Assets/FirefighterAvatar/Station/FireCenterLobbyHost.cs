using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirefighterVR.Station
{
    // S1Test stays the system scene (NetworkManager, XR Origin + avatar, StationSessionFlow, bot).
    // FireCenterLobby is loaded additively and locally on every peer as the visible lobby
    // (environment, stations, screens). It is excluded from NGO scene synchronization so a joining
    // client never loads a second copy, and it is hidden while a mission scene is active.
    [DefaultExecutionOrder(-200)]
    public sealed class FireCenterLobbyHost : MonoBehaviour
    {
        public string lobbyScenePath = "Assets/FireCenter/Scenes/FireCenterLobby.unity";
        public string lobbySceneName = "FireCenterLobby";
        [Tooltip("Merged mode: the lobby content lives inside S1Test under this root (no additive load).")]
        public GameObject lobbyRoot;
        [Tooltip("Visuals of the previous S1Test dispatch room, replaced by FireCenterLobby.")]
        public GameObject[] legacyVisuals = new GameObject[0];
        readonly Dictionary<GameObject, bool> hidden = new Dictionary<GameObject, bool>();
        readonly List<GameObject> roots = new List<GameObject>();
        bool shown = true, verifyHooked;
        NetworkSceneManager hookedManager;
        public static FireCenterLobbyHost Instance { get; private set; }
        public bool LobbyLoaded => lobbyRoot || SceneManager.GetSceneByPath(lobbyScenePath).isLoaded;

        void Awake()
        {
            Instance = this;
            foreach (var g in legacyVisuals) if (g) g.SetActive(false);
            if (lobbyRoot) return; // merged into this scene
            if (!SceneManager.GetSceneByPath(lobbyScenePath).isLoaded) SceneManager.LoadSceneAsync(lobbyScenePath, LoadSceneMode.Additive);
        }
        void OnDestroy() { if (Instance == this) Instance = null; }

        void Update()
        {
            if (lobbyRoot)
            {
                var l = SeatedDispatchLobby.Instance; bool show = !l || l.InLobby;
                if (lobbyRoot.activeSelf != show) lobbyRoot.SetActive(show);
                return;
            }
            HookSceneVerification();
            var scene = SceneManager.GetSceneByPath(lobbyScenePath); if (!scene.isLoaded) return;
            var lobby = SeatedDispatchLobby.Instance;
            bool want = !lobby || lobby.InLobby;
            if (want == shown) return; shown = want;
            if (!want)
            {
                hidden.Clear(); roots.Clear(); scene.GetRootGameObjects(roots);
                foreach (var g in roots) { hidden[g] = g.activeSelf; g.SetActive(false); }
            }
            else { foreach (var p in hidden) if (p.Key) p.Key.SetActive(p.Value); hidden.Clear(); }
        }

        void HookSceneVerification()
        {
            var nm = NetworkManager.Singleton; var sm = nm && nm.IsListening ? nm.SceneManager : null;
            if (sm == null || (verifyHooked && hookedManager == sm)) return;
            var previous = sm.VerifySceneBeforeLoading;
            sm.VerifySceneBeforeLoading = (index, name, mode) => name != lobbySceneName && (previous == null || previous(index, name, mode));
            hookedManager = sm; verifyHooked = true;
        }
    }
}
