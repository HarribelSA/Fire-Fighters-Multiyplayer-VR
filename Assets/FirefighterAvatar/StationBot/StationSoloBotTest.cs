using System;
using System.Collections;
using System.Collections.Generic;
using FirefighterVR.Station;
using TMPro;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR;

namespace FirefighterVR.StationBot
{
    [DefaultExecutionOrder(230)]
    public sealed class StationSoloBotTest : MonoBehaviour
    {
        public const string StationPath = "Assets/FireStation/Scenes/Sample.unity";
        public SeatedDispatchLobby lobby;
        public StationBotFollower botPrefab;
        public StationBotFollower Bot { get; private set; }
        public StationRole HumanRole { get; private set; }
        public bool IsStationTest { get; private set; }
        public bool Armed { get; private set; }
        public bool OwnsStation => IsStationTest || busy;
        // The scene load is finished once the eyes start opening (Reveal), so the loading screen can complete.
        public bool Transitioning => busy && !revealing;
        bool revealing;
        public string Status { get; private set; } = "PLAY WITH BOT";
        readonly List<UnityEngine.UI.Button> play = new List<UnityEngine.UI.Button>();
        readonly List<UnityEngine.UI.Button> ready = new List<UnityEngine.UI.Button>();
        readonly Dictionary<GameObject, bool> hidden = new Dictionary<GameObject, bool>();
        readonly bool[] readyCovered = new bool[2], readyWasActive = new bool[2];
        bool busy, ownsStation, quitting, returnRequested;
        Scene stationScene, lobbyScene;
        Collider[] previousGround;
        GameObject stationPanel;
        TMP_Text stationStatus;
        float nextMissionStatus;
        public static int RealPlayerCount
        {
            get { var n = NetworkManager.Singleton; return n && n.IsListening ? Math.Max(1, n.ConnectedClientsIds.Count) : 1; }
        }
        StationPhase Phase => StationSessionFlow.Instance && StationSessionFlow.Instance.IsSpawned ? StationSessionFlow.Instance.Lobby.Value.Phase : StationPhase.Waiting;
        bool Available => StationBotRoles.CanUseBot(RealPlayerCount, Phase);
        void Start()
        {
            lobbyScene = gameObject.scene;
            AddControls(lobby.left); AddControls(lobby.right);
        }
        UnityEngine.UI.Button CopyButton(UnityEngine.UI.Button source, Transform parent, string name, Vector2 position, Vector2 size, string label)
        {
            var button = Instantiate(source, parent); button.name = name;
            // Cloned buttons must never retain the protected multiplayer Ready listeners.
            button.onClick = new UnityEngine.UI.Button.ButtonClickedEvent();
            button.gameObject.SetActive(true);
            var rect = (RectTransform)button.transform; rect.anchoredPosition = position; rect.sizeDelta = size;
            var text = button.GetComponentInChildren<TMP_Text>(true); text.text = label; text.fontSize = 23;
            var textRect = text.rectTransform; textRect.sizeDelta = size - Vector2.one * 8;
            return button;
        }
        void AddControls(SeatedDispatchLobby.Console console)
        {
            var b = CopyButton(console.ready, console.ready.transform.parent, "Solo_PlayWithBot", new Vector2(0, -333), new Vector2(600, 34), "PLAY WITH BOT");
            b.onClick.AddListener(ToggleBotMode); play.Add(b);
            b = CopyButton(console.ready, console.ready.transform.parent, "Solo_Ready", ((RectTransform)console.ready.transform).anchoredPosition, ((RectTransform)console.ready.transform).sizeDelta, "READY · STATION BOT TEST");
            b.onClick.AddListener(ReadyWithBot); b.gameObject.SetActive(false); ready.Add(b);
        }
        public void ToggleBotMode()
        {
            if (busy || IsStationTest || !Available || !lobby.InLobby) return;
            Armed = !Armed; Status = Armed ? "CANCEL BOT TEST" : "PLAY WITH BOT";
        }
        void LateUpdate()
        {
            UpdateLobbyBot();
            if(IsStationTest&&stationStatus&&stationStatus.gameObject.activeInHierarchy&&Time.unscaledTime>=nextMissionStatus)
            {nextMissionStatus=Time.unscaledTime+.25f;var flow=StationSessionFlow.Instance;stationStatus.text="SOLO · HUMAN "+HumanRole+" / BOT "+StationBotRoles.Opposite(HumanRole)+"\n"+(flow&&flow.SoloMissionActive?flow.StationMissionStatus+"\nBOT: "+flow.SoloTaskStatus:Status);}
            if(returnRequested&&IsStationTest&&!busy)StartCoroutine(ReturnRoutine());
            if (IsStationTest && !busy && !stationScene.isLoaded) StartCoroutine(ReturnRoutine());
            if (RealPlayerCount >= 2)
            {
                Armed = false;
                // Remove the local visual immediately; never take a real player's slot.
                if (Bot) { Bot.gameObject.SetActive(false); Destroy(Bot.gameObject); Bot = null; }
                if (IsStationTest && !busy) StartCoroutine(ReturnRoutine());
            }
            if (!Available && !IsStationTest) Armed = false;
            for (int i = 0; i < play.Count; ++i)
            {
                var console = i == 0 ? lobby.left : lobby.right;
                play[i].gameObject.SetActive(lobby.InLobby);
                play[i].interactable = Available && !busy && console.main.activeSelf;
                play[i].GetComponentInChildren<TMP_Text>().text = RealPlayerCount >= 2 ? "TWO PLAYERS · BOT DISABLED" : Status;
                bool soloReady = Armed && lobby.InLobby && Available;
                ready[i].gameObject.SetActive(soloReady);
                var human=lobby.SelectedProfile;
                ready[i].interactable = !busy&&human!=StationRole.None;
                ready[i].GetComponentInChildren<TMP_Text>().text = human!=StationRole.None?"READY · HUMAN "+human.ToString().ToUpperInvariant()+" / BOT "+StationBotRoles.Opposite(human).ToString().ToUpperInvariant():"SELECT A CONTROL PROFILE FOR SOLO";
                // Presentation only. The original button and all its listeners remain intact.
                if (soloReady && !readyCovered[i])
                { readyWasActive[i] = console.ready.gameObject.activeSelf; console.ready.gameObject.SetActive(false); readyCovered[i] = true; }
                else if (!soloReady && readyCovered[i])
                { console.ready.gameObject.SetActive(readyWasActive[i]); readyCovered[i] = false; }
            }
        }
        // Lobby presence of the EXISTING bot prefab while Play With Bot is armed: seated at the station the
        // human did not take, re-seated when the human switches station, removed on entry/cancel/second player.
        public StationBotFollower LobbyBot { get; private set; }
        Transform lobbySeat;
        void UpdateLobbyBot()
        {
            var human = lobby ? lobby.SelectedProfile : StationRole.None;
            bool want = Armed && !busy && !IsStationTest && lobby && lobby.InLobby && botPrefab && RealPlayerCount < 2 && human != StationRole.None;
            if (!want) { if (LobbyBot) { Destroy(LobbyBot.gameObject); LobbyBot = null; lobbySeat = null; } return; }
            var seat = StationBotRoles.Opposite(human) == StationRole.Driver ? lobby.leftSeat : lobby.rightSeat;
            // A station change re-creates the bot from its neutral pose (seated bone axes are captured unseated).
            if (LobbyBot && lobbySeat != seat) { Destroy(LobbyBot.gameObject); LobbyBot = null; }
            if (!LobbyBot) { LobbyBot = Instantiate(botPrefab, seat.position, seat.rotation); LobbyBot.name = "Lobby Station Bot"; SceneManager.MoveGameObjectToScene(LobbyBot.gameObject, lobbyScene); LobbyBot.InitializeLobby(human); LobbyBot.Seat(seat); lobbySeat = seat; }
        }
        public void ReadyWithBot()
        {
            if (!Armed || busy || !Available || !lobby.InLobby || !botPrefab) return;
            var head = InputDevices.GetDeviceAtXRNode(XRNode.Head);
            if (!head.isValid || !head.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) || !tracked)
            { Status = "CONNECT TRACKED HEADSET · THEN READY"; return; }
            HumanRole = lobby.SelectedProfile;
            if (HumanRole == StationRole.None) { Status = "SELECT EXISTING CONTROL MODE FIRST"; return; }
            if(lobby.rig.LocalRole!=HumanRole||!lobby.rig.InputAvailable){Status="WAIT FOR SELECTED PROFILE TRACKING · THEN READY";return;}
            StartCoroutine(EnterStation());
        }
        IEnumerator EnterStation()
        {
            TransitionDiagnostics.Begin("solo_station");
            busy = true; Status = "PREPARING STATION…";
            returnRequested = false;
            var fade = StationViewFade.Instance;
            if (!fade) { Fail("LOCAL TRANSITION OVERLAY MISSING"); yield break; }
            yield return fade.Cover();
            while (Available && Armed && !StationSpawnPlacement.TrackingReady(lobby.rig.origin)) yield return null;
            if (!Available || !Armed) { Fail("USE NORMAL TWO-PLAYER READY"); fade.HoldManual(false); yield break; }
            // Do not send Ready RPCs, create a room/client, or load any mission scene.
            stationScene = SceneManager.GetSceneByPath(StationPath);
            ownsStation = !stationScene.isLoaded;
            if (ownsStation)
            {
                TransitionDiagnostics.Mark("local_load_requested:station");
                var load = SceneManager.LoadSceneAsync(StationPath, LoadSceneMode.Additive);
                if (load == null) { Fail("STATION LOAD FAILED"); fade.HoldManual(false); yield break; }
                yield return TransitionDiagnostics.WaitForLoad(load); stationScene = SceneManager.GetSceneByPath(StationPath);
            }
            if (!Available || !Armed) { yield return AbortLoad("SECOND PLAYER CONNECTED · USE NORMAL READY"); yield break; }
            var station = FindSetup(stationScene);
            if (!station || !lobby.rig || !lobby.rig.origin || !lobby.rig.avatar) { yield return AbortLoad("STATION REFERENCES MISSING"); yield break; }
            var anchor = station.Spawn(HumanRole);
            var other = station.botSpawn ? station.botSpawn : station.Spawn(StationBotRoles.Opposite(HumanRole));
            if (!anchor || !other || !station.ValidDestination(anchor.position, lobby.rig.origin, false)) { yield return AbortLoad("HUMAN SPAWN CLEARANCE FAILED"); yield break; }
            // Check the bot's standing footprint before moving the existing human rig.
            var nav = new StationBotNavigator(station, lobby.rig.origin.transform, transform);
            if (!nav.Ground(other.position, out var botFloor) || !nav.Clear(botFloor) || Vector3.Distance(anchor.position, botFloor) < 1.2f)
            { yield return AbortLoad("BOT SPAWN CLEARANCE FAILED"); yield break; }
            previousGround = lobby.rig.avatar.walkableGroundSurfaces;
            MoveHuman(anchor);
            var missionView=StationMissionView.Instance;
            if(!missionView||(HumanRole==StationRole.Driver&&!missionView.PlaceDriverAtDispatch())){yield return AbortLoad("DISPATCH SEAT REFERENCES MISSING");yield break;}
            lobby.rig.avatar.walkableGroundSurfaces = station.walkableSurfaces;
            lobby.rig.avatar.Seated = HumanRole==StationRole.Driver;
            if(HumanRole==StationRole.Assistant){lobby.rig.StationSeated=false;var cc=lobby.rig.origin.GetComponent<CharacterController>();if(cc)cc.enabled=true;}
            if (station.inspectionCamera) station.inspectionCamera.SetActive(false);
            var flow = StationSessionFlow.Instance;
            if (flow) foreach (var root in flow.lobbyOnlyRoots) if (root) { hidden[root] = root.activeSelf; root.SetActive(false); }
            SceneManager.SetActiveScene(stationScene);
            int stable = 0;
            while (stable < 3)
            {
                yield return null;
                if (!Available || !Armed || returnRequested) { yield return AbortLoad("RETURNING TO LOBBY"); yield break; }
                if (!StationSpawnPlacement.TrackingReady(lobby.rig.origin)) { stable = 0; continue; }
                if(HumanRole==StationRole.Driver){if(Vector3.Distance(lobby.rig.origin.Camera.transform.position,StationSeatPlacement.EyeTarget(missionView.dispatchSeat,lobby.rig.avatar))>.15f){missionView.PlaceDriverAtDispatch();stable=0;}else stable++;}
                else if(Vector3.Distance(StationSpawnPlacement.GroundPosition(lobby.rig.origin),anchor.position)<.25f)stable++;
                else{MoveHuman(anchor);stable=0;}
            }
            if(HumanRole==StationRole.Driver&&!lobby.rig.StationSeated){yield return AbortLoad("DRIVER SEATING VERIFICATION FAILED");yield break;}
            Bot = Instantiate(botPrefab, botFloor, other.rotation);
            SceneManager.MoveGameObjectToScene(Bot.gameObject, stationScene);
            Bot.name = "StationBot_" + StationBotRoles.Opposite(HumanRole);
            Bot.Initialize(station, lobby.rig.origin.transform, lobby.rig.origin.Camera.transform, HumanRole);
            TransitionDiagnostics.Mark("human_placement_and_bot_initialized");
            if(Bot.Role==StationRole.Driver)Bot.Seat(missionView.dispatchSeat);
            IsStationTest = true; Armed = false; Status = "STATION TEST ACTIVE";
            if(!flow||!flow.BeginSoloMission(this))Status="SOLO MISSION BLOCKED · CHECK STATION REFERENCES";
            CreateStationPanel(anchor);
            Physics.SyncTransforms();
            revealing = true; yield return fade.Reveal(); revealing = false; busy = false;
        }
        public static StationSceneSetup FindSetup(Scene scene)
        {
            if (!scene.IsValid() || !scene.isLoaded) return null;
            foreach (var root in scene.GetRootGameObjects()) { var setup = root.GetComponentInChildren<StationSceneSetup>(true); if (setup) return setup; }
            return null;
        }
        void MoveHuman(Transform anchor)
        {
            lobby.rig.RememberStationSpawn(anchor.position);
            StationSpawnPlacement.Place(lobby.rig.origin, anchor);
        }
        void CreateStationPanel(Transform anchor)
        {
            stationPanel = new GameObject("StationBotTest_Status", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster));
            SceneManager.MoveGameObjectToScene(stationPanel, stationScene);
            var canvas = stationPanel.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = lobby.rig.origin.Camera;
            var rect = (RectTransform)stationPanel.transform; rect.sizeDelta = new Vector2(640, 190); rect.localScale = Vector3.one * .0014f;
            rect.position = anchor.position + anchor.forward * 2.2f + anchor.right * 1.5f + Vector3.up * 1.5f;
            rect.rotation = Quaternion.LookRotation(rect.position - lobby.rig.origin.Camera.transform.position);
            var button = CopyButton(lobby.left.ready, rect, "ReturnToLobby", new Vector2(0, -60), new Vector2(600, 55), "RETURN TO LOBBY");
            button.onClick.AddListener(ReturnToLobby);
            var label = Instantiate(button.GetComponentInChildren<TMP_Text>(), rect); label.name = "Roles";
            stationStatus=label;
            label.rectTransform.anchoredPosition = new Vector2(0, 35); label.rectTransform.sizeDelta = new Vector2(620, 110);
            label.raycastTarget = false; label.fontSize=20;label.text = "SOLO MISSION · HUMAN "+HumanRole+" / BOT "+StationBotRoles.Opposite(HumanRole)+"\n60-second dispatch countdown starts automatically";
        }
        public void ReturnToLobby()
        {
            returnRequested = true;
            if(busy) { Armed = false; return; }
            if(IsStationTest)StartCoroutine(ReturnRoutine());
        }
        IEnumerator ReturnRoutine()
        {
            busy = true;
            var flow=StationSessionFlow.Instance;bool city=flow&&flow.SoloCityLoaded;if(flow)flow.EndSoloMission();
            var fade = StationViewFade.Instance;
            if (fade) yield return fade.Cover();
            if (Bot) { Bot.gameObject.SetActive(false); Destroy(Bot.gameObject); Bot = null; }
            if(city){var loaded=SceneManager.GetSceneByName(StationSessionFlow.CitySceneName);if(loaded.isLoaded)yield return SceneManager.UnloadSceneAsync(loaded);}
            if (stationPanel) Destroy(stationPanel);
            IsStationTest = false;
            lobby.rig.avatar.walkableGroundSurfaces = previousGround;
            foreach (var entry in hidden) if (entry.Key) entry.Key.SetActive(entry.Value);
            hidden.Clear();
            if (lobbyScene.isLoaded) SceneManager.SetActiveScene(lobbyScene);
            lobby.ResetSeatPlacement();
            if (ownsStation && stationScene.isLoaded) yield return SceneManager.UnloadSceneAsync(stationScene);
            yield return null; // Let the existing lobby alignment run before revealing.
            if(lobby.rig.releaseUnusedAssetsOnReturn)yield return TransitionMemory.ReleaseAfterReturn();
            revealing = true; if (fade) yield return fade.Reveal(); revealing = false;
            ownsStation = false; Armed = false; busy = false; returnRequested = false; Status = "PLAY WITH BOT";
        }
        IEnumerator AbortLoad(string message)
        {
            foreach (var entry in hidden) if (entry.Key) entry.Key.SetActive(entry.Value);
            hidden.Clear();
            if (lobbyScene.isLoaded) SceneManager.SetActiveScene(lobbyScene);
            lobby.ResetSeatPlacement();
            if (previousGround != null) lobby.rig.avatar.walkableGroundSurfaces = previousGround;
            if (ownsStation && stationScene.isLoaded) yield return SceneManager.UnloadSceneAsync(stationScene);
            yield return null;
            if (StationViewFade.Instance) yield return StationViewFade.Instance.Reveal();
            ownsStation = false; returnRequested = false; Fail(message);
        }
        void Fail(string message) { busy = false; Armed = false; Status = message; Debug.LogWarning("Station bot test: " + message, this); }
        void OnDisable()
        {
            bool cityOwned=StationSessionFlow.Instance&&StationSessionFlow.Instance.SoloCityLoaded;
            if(StationSessionFlow.Instance&&StationSessionFlow.Instance.SoloMissionActive)StationSessionFlow.Instance.EndSoloMission();
            if(OwnsStation&&StationViewFade.Instance)StationViewFade.Instance.HoldManual(false);
            // Lifecycle cleanup only; never shut down a room or change network ownership.
            if (Bot) Destroy(Bot.gameObject);
            if (stationPanel) Destroy(stationPanel);
            foreach (var entry in hidden) if (entry.Key) entry.Key.SetActive(entry.Value);
            hidden.Clear();
            if (lobby)
            {
                if (readyCovered[0]) lobby.left.ready.gameObject.SetActive(readyWasActive[0]);
                if (readyCovered[1]) lobby.right.ready.gameObject.SetActive(readyWasActive[1]);
            }
            foreach (var button in play) if (button) button.gameObject.SetActive(false);
            foreach (var button in ready) if (button) button.gameObject.SetActive(false);
            if (IsStationTest && Application.isPlaying && !quitting && lobby)
            {
                lobby.rig.avatar.walkableGroundSurfaces = previousGround;
                if (lobbyScene.isLoaded) SceneManager.SetActiveScene(lobbyScene);
                lobby.ResetSeatPlacement();
                if (ownsStation && stationScene.isLoaded) SceneManager.UnloadSceneAsync(stationScene);
                if(cityOwned){var city=SceneManager.GetSceneByName(StationSessionFlow.CitySceneName);if(city.isLoaded)SceneManager.UnloadSceneAsync(city);}
                IsStationTest = false; ownsStation = false;
            }
        }
        void OnApplicationQuit() => quitting = true;
    }
}
