using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FireCenter
{
    /// <summary>
    /// Restricts teleportation in the lobby to the designated <see cref="TeleportationAnchor"/>s
    /// (workstations, map wall, entrance). Any free-floor <see cref="TeleportationArea"/> in this
    /// scene is disabled. Only affects the scene this component lives in.
    /// </summary>
    public class LobbyTeleportRestriction : MonoBehaviour
    {
        [SerializeField] TeleportationAnchor[] m_AllowedAnchors;

        public TeleportationAnchor[] allowedAnchors => m_AllowedAnchors;

        void Start()
        {
            var scene = gameObject.scene;
            foreach (var area in FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (area.gameObject.scene != scene) continue;
                area.enabled = false;
            }

            foreach (var anchor in m_AllowedAnchors)
                if (anchor != null) anchor.enabled = true;
        }
    }
}
