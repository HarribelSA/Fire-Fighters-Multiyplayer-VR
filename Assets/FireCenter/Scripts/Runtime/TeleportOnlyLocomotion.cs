using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FireCenter
{
    /// <summary>
    /// Puts the existing XR Interaction Toolkit locomotion of the local XR Origin into
    /// "Teleportation Only" mode: continuous (joystick) movement is disabled, teleportation,
    /// head tracking, turning and controller/hand interaction are left untouched.
    /// Runs locally, so every player's locomotion settings stay independent.
    /// </summary>
    public class TeleportOnlyLocomotion : MonoBehaviour
    {
        [SerializeField, Tooltip("Re-apply after every scene load (the XR Origin persists into the mission scene).")]
        bool m_ReapplyOnSceneLoad = true;

        void Start() => Apply();

        void OnEnable() { if (m_ReapplyOnSceneLoad) SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() => SceneManager.sceneLoaded -= OnSceneLoaded;

        void OnSceneLoaded(Scene scene, LoadSceneMode mode) => Apply();

        public void Apply()
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin == null)
            {
                Debug.LogWarning("[FireCenter] No XR Origin found - locomotion settings not applied.", this);
                return;
            }

            // 1. Disable continuous joystick movement (DynamicMoveProvider derives from ContinuousMoveProvider).
            foreach (var move in origin.GetComponentsInChildren<ContinuousMoveProvider>(true))
                move.enabled = false;

            // 2. Tell the Starter Assets ControllerInputActionManager to route the thumbstick to teleport instead
            //    of smooth motion. Done via reflection to avoid a hard dependency on the sample assembly.
            foreach (var behaviour in origin.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (behaviour == null || behaviour.GetType().Name != "ControllerInputActionManager") continue;
                var prop = behaviour.GetType().GetProperty("smoothMotionEnabled", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite) prop.SetValue(behaviour, false);
            }

            // 3. Teleportation follows the player's comfort setting (on by default).
            foreach (var teleport in origin.GetComponentsInChildren<TeleportationProvider>(true))
                teleport.enabled = FireCenterSettings.teleportEnabled;
        }
    }
}
