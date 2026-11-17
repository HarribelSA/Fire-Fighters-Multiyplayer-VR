using System;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace FireCenter
{
    /// <summary>
    /// Scene-level registry of the role spawn transforms (DriverSpawnPoint / FirefighterSpawnPoint).
    /// Moves the *existing* local XR Origin - it never creates a rig - so headset tracking is preserved.
    /// The networked avatar follows the XR Origin, so other players see this player at their workstation.
    /// One instance per scene (lobby and mission scene each have their own).
    /// </summary>
    public class PlayerSpawnManager : MonoBehaviour
    {
        public static PlayerSpawnManager Instance { get; private set; }

        [SerializeField] Transform m_DriverSpawnPoint;
        [SerializeField] Transform m_FirefighterSpawnPoint;
        [SerializeField, Tooltip("Where the player stands before a role is assigned (main menu position).")]
        Transform m_DefaultSpawnPoint;
        [SerializeField, Tooltip("Move the rig to the default spawn point when the scene starts.")]
        bool m_PlaceOnStart = true;

        public Transform driverSpawnPoint => m_DriverSpawnPoint;
        public Transform firefighterSpawnPoint => m_FirefighterSpawnPoint;

        /// <summary>Raised after the local rig was moved for a role (used to move the menu to the matching monitor).</summary>
        public event Action<PlayerRole> localPlayerPlaced;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake() => Instance = this;

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            if (!m_PlaceOnStart || FirefighterVR.Station.SeatedDispatchLobby.Instance != null) return;
            var role = LobbySessionData.localRole;
            if (role != PlayerRole.Unassigned && LobbyPlayerState.Local != null)
                MoveLocalPlayerToRoleSpawn(role);
            else if (m_DefaultSpawnPoint != null)
                PlaceRig(m_DefaultSpawnPoint);
        }

        public Transform GetSpawnPoint(PlayerRole role)
        {
            switch (role)
            {
                case PlayerRole.Driver: return m_DriverSpawnPoint;
                case PlayerRole.Firefighter: return m_FirefighterSpawnPoint;
                default: return m_DefaultSpawnPoint;
            }
        }

        public void MoveLocalPlayerToRoleSpawn(PlayerRole role)
        {
            var point = GetSpawnPoint(role);
            if (point == null) return;
            PlaceRig(point);
            localPlayerPlaced?.Invoke(role);
        }

        public void ReturnToDefaultSpawn()
        {
            if (m_DefaultSpawnPoint != null) PlaceRig(m_DefaultSpawnPoint);
            localPlayerPlaced?.Invoke(PlayerRole.Unassigned);
        }

        /// <summary>
        /// Rotates the origin so the user's view faces the spawn forward, then translates it so the
        /// user's head (XZ) is over the spawn point and the tracking floor sits at the spawn height.
        /// Real-world head tracking keeps working because only the origin moves.
        /// </summary>
        public static void PlaceRig(Transform point)
        {
            var origin = FindAnyObjectByType<XROrigin>();
            if (origin == null || origin.Camera == null) return;

            var cc = origin.GetComponent<CharacterController>();
            bool ccWasEnabled = cc != null && cc.enabled;
            if (cc != null) cc.enabled = false;

            origin.MatchOriginUpCameraForward(Vector3.up, Vector3.ProjectOnPlane(point.forward, Vector3.up).normalized);

            Vector3 cam = origin.Camera.transform.position;
            Vector3 originPos = origin.Origin.transform.position;
            Vector3 delta = new Vector3(point.position.x - cam.x, point.position.y - originPos.y, point.position.z - cam.z);
            origin.Origin.transform.position += delta;

            if (cc != null) cc.enabled = ccWasEnabled;
        }
    }
}
