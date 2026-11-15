#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FirefighterVR.Station;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirefighterVR.StationBot.Editor
{
    public static class StationBotVerification
    {
        static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
        [MenuItem("Firefighter VR/Station Bot/Verify Station Bot in Preview")]
        public static string Run()
        {
            Require(!EditorApplication.isPlayingOrWillChangePlaymode, "Preview verification requires Edit Mode.");
            var results = new List<object>();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(StationBotInstaller.PrefabPath);
            Require(prefab, "Bot prefab missing.");
            Require(prefab.GetComponentsInChildren<Unity.Netcode.NetworkObject>(true).Length == 0, "Bot must not be network-spawned.");
            Require(prefab.GetComponentsInChildren<Unity.XR.CoreUtils.XROrigin>(true).Length == 0, "Bot must not contain an XR Origin.");
            Require(prefab.GetComponentsInChildren<Transform>(true).Sum(t => GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)) == 0, "Bot has missing script.");
            Require(prefab.GetComponentsInChildren<Renderer>(true).All(r => r.sharedMaterials.All(m => m)), "Bot has missing material.");
            var lobbyScene = EditorSceneManager.OpenPreviewScene("Assets/S1Test.unity");
            try
            {
                var solo = lobbyScene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<StationSoloBotTest>(true)).Single();
                var lobby = solo.lobby;
                Require(lobby && solo.botPrefab, "Lobby binding missing.");
                foreach (var humanRole in new[] { StationRole.Driver, StationRole.Assistant })
                {
                    // Selection changes affect only the isolated preview copy.
                    lobby.SelectSeat(humanRole);
                    Require(StationBotRoles.HumanRole(lobby) == humanRole, "Existing lobby selection was not read correctly.");
                    Require(StationBotRoles.Opposite(humanRole) != humanRole, "Bot must take opposite role.");
                }
                typeof(StationSoloBotTest).GetMethod("Start", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(solo, null);
                var buttons = lobbyScene.GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<UnityEngine.UI.Button>(true)).ToArray();
                Require(buttons.Count(b => b.name == "Solo_PlayWithBot") == 2 && buttons.Count(b => b.name == "Solo_Ready") == 2, "Separate solo controls missing.");
                Require(buttons.Where(b => b.name == "Solo_Ready").All(b => b.onClick.GetPersistentEventCount() == 0), "Copied Ready retained protected persistent listeners.");
                results.Add(new { test = "Separate controls and existing selection reader", passed = true, originalReadyUntouched = true });
            }
            finally { EditorSceneManager.ClosePreviewScene(lobbyScene); }
            foreach (var humanRole in new[] { StationRole.Driver, StationRole.Assistant })
            {
                var scene = EditorSceneManager.OpenPreviewScene(StationSoloBotTest.StationPath);
                try
                {
                    var station = StationSoloBotTest.FindSetup(scene);
                    var human = new GameObject("VerificationHuman"); SceneManager.MoveGameObjectToScene(human, scene);
                    var head = new GameObject("VerificationHead"); head.transform.SetParent(human.transform, false); head.transform.localPosition = Vector3.up * 1.7f;
                    human.transform.position = station.Spawn(humanRole).position;
                    var bot = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                    var botAnchor=station.botSpawn?station.botSpawn:station.Spawn(StationBotRoles.Opposite(humanRole));
                    bot.transform.SetPositionAndRotation(botAnchor.position,botAnchor.rotation);
                    var follower = bot.GetComponent<StationBotFollower>();
                    follower.Initialize(station, human.transform, head.transform, humanRole);
                    Require(follower.Role == StationBotRoles.Opposite(humanRole), "Incorrect role assignment.");
                    Require(!follower.DrivingAdapterActive, "Driving must stay inactive.");
                    Require(bot.GetComponentsInChildren<SkinnedMeshRenderer>(true).All(r => r.gameObject.activeInHierarchy && r.enabled), "Exterior avatar renderer inactive.");
                    var nav = new StationBotNavigator(station, human.transform, bot.transform);
                    Physics.SyncTransforms();
                    Require(nav.Ground(bot.transform.position, out var floor) && nav.Clear(floor), "Bot spawn invalid.");
                    Require(nav.Ground(human.transform.position, out var humanFloor) && nav.Clear(humanFloor), "Human spawn invalid.");
                    bot.transform.position = floor;
                    // A supported target beyond the initial stopping distance on the apparatus floor.
                    human.transform.position += Vector3.forward * 3;
                    Require(nav.Ground(human.transform.position, out humanFloor) && nav.Clear(humanFloor), "Walking test destination invalid.");
                    human.transform.position = humanFloor;
                    var obstacle = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    SceneManager.MoveGameObjectToScene(obstacle, scene); obstacle.name = "TemporaryFollowObstacle";
                    obstacle.transform.position = (bot.transform.position + human.transform.position) * .5f + Vector3.up * .85f;
                    obstacle.transform.localScale = new Vector3(.65f, 1.7f, .65f);
                    Physics.SyncTransforms();
                    Require(nav.Plan(bot.transform.position, human.transform.position), "No route to supported station test target.");
                    follower.animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                    follower.animator.Rebind(); follower.animator.Update(0);
                    var initial = bot.transform.position;
                    var states = new HashSet<string>();
                    bool clearance = true;
                    for (int i = 0; i < 900; ++i)
                    {
                        follower.Tick(1f / 60); follower.animator.Update(1f / 60); states.Add(follower.MotionState);
                        clearance &= nav.Clear(bot.transform.position);
                    }
                    float distance = Vector3.ProjectOnPlane(human.transform.position - bot.transform.position, Vector3.up).magnitude;
                    Require(Vector3.Distance(initial, bot.transform.position) > 1, "Bot did not follow.");
                    Require(distance >= follower.stopDistance - .08f && distance < follower.stopDistance + .4f, "Stopping distance invalid.");
                    // A nearby person moving behind the bot should trigger an in-place turn,
                    // followed by idle, without a new walking path.
                    human.transform.position = bot.transform.position - bot.transform.forward;
                    for (int i = 0; i < 240; ++i) { follower.Tick(1f / 60); follower.animator.Update(1f / 60); states.Add(follower.MotionState); }
                    Require(follower.animator.GetCurrentAnimatorStateInfo(0).IsName("Idle"), "Animator did not settle into Idle.");
                    Require(states.Contains("Walk") && states.Contains("Idle") && states.Any(s => s.StartsWith("Turn")), "Idle / Walk / Turn states not all exercised.");
                    Require(clearance, "Bot entered an obstacle.");
                    results.Add(new { test = humanRole == StationRole.Driver ? "Hands/G29 human Driver → bot Assistant" : "Controllers human Assistant → bot Driver", passed = true, botRole = follower.Role.ToString(), travelledMeters = Vector3.Distance(initial, bot.transform.position), stoppingDistance = distance, states = states.ToArray(), clearance, drivingActive = follower.DrivingAdapterActive });
                }
                finally { EditorSceneManager.ClosePreviewScene(scene); }
            }
            Require(StationBotRoles.CanUseBot(1, StationPhase.Waiting), "Solo should be permitted while waiting.");
            Require(!StationBotRoles.CanUseBot(2, StationPhase.Waiting) && !StationBotRoles.CanUseBot(3, StationPhase.Waiting), "Bot allowed with real teammates.");
            Require(!StationBotRoles.CanUseBot(1, StationPhase.Countdown) && !StationBotRoles.CanUseBot(1, StationPhase.Loading), "Bot must not interrupt multiplayer transitions.");
            var realTeam = StationLobbyState.Initial;
            Require(realTeam.Claim(101, StationRole.Driver) && realTeam.Claim(102, StationRole.Assistant), "Existing two-player role claims failed.");
            Require(realTeam.SetReady(101, true, true, 2, 50) && realTeam.SetReady(102, true, true, 2, 50), "Existing two-player Ready failed.");
            Require(realTeam.BothReady && realTeam.Phase == StationPhase.Countdown && realTeam.Deadline == 60, "Existing two-player countdown changed.");
            results.Add(new { test = "Two real players suppress bot; original Ready/countdown state logic preserved", passed = true });
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(new { scope = "Edit Mode preview scenes and isolated station physics; no Play Mode or connected XR/network session", utc = DateTime.UtcNow, results, hardwareValidation = "Not performed: headset/G29 and two-device network session required", windowsPowerAction = "None" }, Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(StationBotInstaller.Folder + "/Reports/StationBotVerification.json", json);
            AssetDatabase.ImportAsset(StationBotInstaller.Folder + "/Reports/StationBotVerification.json");
            return json;
        }
    }
}
#endif
