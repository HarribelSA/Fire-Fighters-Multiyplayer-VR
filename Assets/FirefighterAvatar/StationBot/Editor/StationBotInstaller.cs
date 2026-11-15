#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using FirefighterVR.Station;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirefighterVR.StationBot.Editor
{
    public static class StationBotInstaller
    {
        public const string Folder = "Assets/FirefighterAvatar/StationBot";
        public const string PrefabPath = Folder + "/StationFirefighterBot.prefab";
        public const string ControllerPath = Folder + "/StationBotLocomotion.controller";
        [MenuItem("Firefighter VR/Station Bot/Install Separate Solo Test")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Install outside Play Mode.");
            var scene = SceneManager.GetSceneByPath("Assets/S1Test.unity");
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene("Assets/S1Test.unity", OpenSceneMode.Additive);
            if (scene.isDirty) throw new InvalidOperationException("Save existing lobby edits before installing.");
            EnsureBotPrefab();
            var all = scene.GetRootGameObjects();
            var lobby = all.SelectMany(g => g.GetComponentsInChildren<SeatedDispatchLobby>(true)).Single();
            var solo = all.SelectMany(g => g.GetComponentsInChildren<StationSoloBotTest>(true)).SingleOrDefault();
            if (!solo)
            {
                var root = new GameObject("STATION_SOLO_BOT_TEST"); SceneManager.MoveGameObjectToScene(root, scene);
                solo = root.AddComponent<StationSoloBotTest>();
            }
            solo.lobby = lobby; solo.botPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<StationBotFollower>();
            EditorUtility.SetDirty(solo); EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene)) throw new IOException("Lobby save failed.");
            AssetDatabase.SaveAssets();
        }
        static void EnsureBotPrefab()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath))
            {
                PolishBotAnimations();
                var existing = PrefabUtility.LoadPrefabContents(PrefabPath);
                try
                {
                    // The network visual is stored inactive for its original coordinator.
                    foreach (Transform child in existing.transform) child.gameObject.SetActive(true);
                    PrefabUtility.SaveAsPrefabAsset(existing, PrefabPath);
                }
                finally { PrefabUtility.UnloadPrefabContents(existing); }
                return;
            }
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!controller)
            {
                controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
                controller.AddParameter("PlaybackSpeed", AnimatorControllerParameterType.Float);
                var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/FirefighterAvatar/Animations/FirefighterStanding.anim");
                var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/FirefighterAvatar/Animations/FirefighterGroundedWalk.anim");
                if (!idle || !walk) throw new InvalidOperationException("Existing firefighter locomotion clips are missing.");
                var machine = controller.layers[0].stateMachine;
                foreach (var name in new[] { "Idle", "Walk", "TurnLeft", "TurnRight" })
                {
                    var state = machine.AddState(name); state.motion = name == "Idle" ? idle : walk;
                    if (name != "Idle") { state.speedParameter = "PlaybackSpeed"; state.speedParameterActive = true; }
                    if (name == "TurnRight") state.mirror = true;
                    if (name == "Idle") machine.defaultState = state;
                }
            }
            PolishBotAnimations();
            var source = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/FirefighterAvatar/Prefabs/FirefighterNetworkVisual.prefab");
            if (!source) throw new InvalidOperationException("Existing firefighter visual is missing.");
            var preview = EditorSceneManager.NewPreviewScene();
            try
            {
                var root = new GameObject("StationFirefighterBot"); SceneManager.MoveGameObjectToScene(root, preview);
                var visual = (GameObject)PrefabUtility.InstantiatePrefab(source, preview);
                PrefabUtility.UnpackPrefabInstance(visual, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
                visual.transform.SetParent(root.transform, false);
                visual.SetActive(true);
                var driver = visual.GetComponentInChildren<FirefighterAvatarDriver>(true);
                if (!driver) throw new InvalidOperationException("Existing firefighter visual driver missing.");
                var animator = driver.animator;
                foreach (var renderer in visual.GetComponentsInChildren<Renderer>(true)) { renderer.forceRenderingOff = false; renderer.enabled = true; }
                foreach (var renderer in driver.interiorVisuals) if (renderer) renderer.gameObject.SetActive(false);
                if (driver.originalMask && driver.maskExteriorMaterials.Length > 0) driver.originalMask.sharedMaterials = driver.maskExteriorMaterials;
                visual.transform.localPosition = Vector3.down * driver.standingSole;
                foreach (var audio in visual.GetComponentsInChildren<AudioSource>(true)) UnityEngine.Object.DestroyImmediate(audio);
                foreach (var particles in visual.GetComponentsInChildren<ParticleSystem>(true)) particles.gameObject.SetActive(false);
                // Only the new bot asset loses tracking/IK scripts; its source prefab is untouched.
                foreach (var behaviour in visual.GetComponentsInChildren<MonoBehaviour>(true).Reverse()) if (behaviour) UnityEngine.Object.DestroyImmediate(behaviour);
                foreach (var collider in visual.GetComponentsInChildren<Collider>(true)) UnityEngine.Object.DestroyImmediate(collider);
                animator.runtimeAnimatorController = controller; animator.applyRootMotion = false; animator.enabled = true;
                animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
                var follower = root.AddComponent<StationBotFollower>(); follower.animator = animator;
                if (root.GetComponentInChildren<Unity.Netcode.NetworkObject>(true) || root.GetComponentInChildren<Unity.XR.CoreUtils.XROrigin>(true))
                    throw new InvalidOperationException("Bot must not contain network objects or XR rigs.");
                PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally { EditorSceneManager.ClosePreviewScene(preview); }
        }
        static void PolishBotAnimations()
        {
            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (!controller) return;
            const string idlePath = Folder + "/StationBotRelaxedIdle.anim";
            var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(idlePath);
            if (!idle)
            {
                idle = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/FirefighterAvatar/Animations/FirefighterStanding.anim"));
                idle.name = "StationBotRelaxedIdle";
                var walk = AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/FirefighterAvatar/Animations/FirefighterGroundedWalk.anim");
                // The human idle is authored for tracked hands held forward. A separate bot
                // copy uses the relaxed average arm pose of the existing walking animation.
                foreach (var binding in AnimationUtility.GetCurveBindings(walk))
                {
                    string property = binding.propertyName;
                    bool arm = property.Contains("Shoulder") || property.Contains(" Arm ") || property.Contains("Forearm") || property.Contains(" Hand ");
                    if (!arm && !property.Contains("Hand.")) continue;
                    var curve = AnimationUtility.GetEditorCurve(walk, binding);
                    float value = Enumerable.Range(0, 24).Average(i => curve.Evaluate(walk.length * i / 24f));
                    if (property.Contains("Hand.")) value = property.Contains("Spread") ? 0 : .2f;
                    AnimationUtility.SetEditorCurve(idle, binding, AnimationCurve.Constant(0, idle.length, value));
                }
                AssetDatabase.CreateAsset(idle, idlePath);
            }
            foreach (var child in controller.layers[0].stateMachine.states)
            { child.state.iKOnFeet = child.state.name != "Idle"; if (child.state.name == "Idle") child.state.motion = idle; }
            EditorUtility.SetDirty(controller);
        }
    }
}
#endif
