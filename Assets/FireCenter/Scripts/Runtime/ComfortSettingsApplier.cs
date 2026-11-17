using System.Reflection;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Comfort;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace FireCenter
{
    /// <summary>
    /// Applies the local VR comfort settings (<see cref="FireCenterSettings"/>) to the real XR Interaction Toolkit
    /// components on the local XR Origin: snap/continuous turn, snap angle, turn speed, move speed,
    /// teleportation, tunneling vignette and a height offset. Local only - never networked.
    /// Continuous movement stays disabled in the lobby (teleport-only lobby, see <see cref="TeleportOnlyLocomotion"/>);
    /// the chosen move speed is applied to the move provider and takes effect where movement is enabled.
    /// </summary>
    [DefaultExecutionOrder(100)] // after TeleportOnlyLocomotion
    public class ComfortSettingsApplier : MonoBehaviour
    {
        public static ComfortSettingsApplier Instance { get; private set; }

        XROrigin m_Origin;
        float m_BaseCameraOffsetY = float.NaN;

        /// <summary>True if the rig has a tunneling vignette the comfort toggle can control.</summary>
        public bool hasVignette { get; private set; }
        public bool hasSmoothTurn { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake() => Instance = this;
        void OnEnable() { FireCenterSettings.changed += OnSettingChanged; SceneManager.sceneLoaded += OnSceneLoaded; }
        void OnDisable() { FireCenterSettings.changed -= OnSettingChanged; SceneManager.sceneLoaded -= OnSceneLoaded; }
        void Start() => Apply();

        void OnSceneLoaded(Scene s, LoadSceneMode m) => Apply();
        void OnSettingChanged(string key) { if (key.StartsWith("Comfort.")) Apply(); }

        // Provider enable state belongs to StationLocalRig (lobby/seated lock, roles); only record the preference.
        void SetEnabled(UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider p, bool on)
        {
            var rig = m_Origin != null ? m_Origin.GetComponent<FirefighterVR.Station.StationLocalRig>() : null;
            if (rig != null) rig.SetPreferredEnabled(p, on); else p.enabled = on;
        }
        public void Apply()
        {
            if (m_Origin == null) m_Origin = FindAnyObjectByType<XROrigin>();
            if (m_Origin == null) return;

            var style = FireCenterSettings.turnStyle;

            // Turning: the Starter Assets ControllerInputActionManager routes the thumbstick to snap or smooth turn.
            foreach (var mb in m_Origin.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (mb == null || mb.GetType().Name != "ControllerInputActionManager") continue;
                var prop = mb.GetType().GetProperty("smoothTurnEnabled", BindingFlags.Public | BindingFlags.Instance);
                if (prop != null && prop.CanWrite) prop.SetValue(mb, style == TurnStyle.Smooth);
            }
            foreach (var snap in m_Origin.GetComponentsInChildren<SnapTurnProvider>(true))
            {
                snap.turnAmount = FireCenterSettings.snapTurnAngle;
                SetEnabled(snap, style == TurnStyle.Snap);
            }
            hasSmoothTurn = false;
            foreach (var smooth in m_Origin.GetComponentsInChildren<ContinuousTurnProvider>(true))
            {
                hasSmoothTurn = true;
                smooth.turnSpeed = FireCenterSettings.smoothTurnSpeed;
                SetEnabled(smooth, style == TurnStyle.Smooth);
            }

            // Movement speed (the provider itself stays disabled in the teleport-only lobby).
            foreach (var move in m_Origin.GetComponentsInChildren<ContinuousMoveProvider>(true))
                move.moveSpeed = FireCenterSettings.moveSpeed;

            // Teleportation on/off.
            // Teleport availability is a preference of the existing StationLocalRig movement policy.
            var rig = m_Origin.GetComponent<FirefighterVR.Station.StationLocalRig>();
            if (rig != null) rig.SetAssistantMovement(FireCenterSettings.teleportEnabled ? 0 : 1);
            else foreach (var tp in m_Origin.GetComponentsInChildren<TeleportationProvider>(true)) tp.enabled = FireCenterSettings.teleportEnabled;

            // Comfort vignette.
            var vignettes = m_Origin.GetComponentsInChildren<TunnelingVignetteController>(true);
            hasVignette = vignettes.Length > 0;
            foreach (var v in vignettes) v.gameObject.SetActive(FireCenterSettings.vignetteEnabled);

            // Height calibration: offset the Camera Offset object on top of whatever XROrigin set.
            var offsetObj = m_Origin.CameraFloorOffsetObject;
            if (offsetObj != null)
            {
                if (float.IsNaN(m_BaseCameraOffsetY)) m_BaseCameraOffsetY = offsetObj.transform.localPosition.y;
                var lp = offsetObj.transform.localPosition;
                lp.y = m_BaseCameraOffsetY + FireCenterSettings.heightOffset;
                offsetObj.transform.localPosition = lp;
            }
        }
    }
}
