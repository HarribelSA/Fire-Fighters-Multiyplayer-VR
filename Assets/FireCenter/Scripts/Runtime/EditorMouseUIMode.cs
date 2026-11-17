using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.UI;
using XRMultiplayer;

namespace FireCenter
{
    /// <summary>
    /// EDITOR-ONLY test aid. Press the "UIControls/Editor Mouse UI" action (F8 by default, defined in
    /// FireCenterControls.inputactions) to click world-space UI directly with the mouse.
    /// The XR Interaction Simulator normally disables XRUIInputModule mouse input while a simulated
    /// controller is driven (point-and-click mode) - this toggle turns point-and-click off, enables
    /// mouse input on the existing XRUIInputModule and adds a GraphicRaycaster next to each
    /// TrackedDeviceGraphicRaycaster. Press again to restore XR controller-ray testing.
    /// Does nothing in player builds; XR controller input is never modified.
    /// </summary>
    public class EditorMouseUIMode : MonoBehaviour
    {
#if UNITY_EDITOR
        const string k_ActionPath = "UIControls/Editor Mouse UI";
        InputAction m_Toggle;
        bool m_Active;
        readonly List<GraphicRaycaster> m_Added = new();

        void Start()
        {
            m_Toggle = InputModeManager.Instance != null ? InputModeManager.Instance.FindAction(k_ActionPath) : null;
            if (m_Toggle == null) { Debug.LogWarning($"[FireCenter] '{k_ActionPath}' action not found; editor mouse UI toggle disabled."); return; }
            m_Toggle.performed += OnToggle;
            m_Toggle.Enable();
            Debug.Log("[FireCenter] Editor UI testing: aim the simulated controller (press T / Y, then move the mouse and left-click), or press F8 to click the UI directly with the mouse.");
        }

        void OnDestroy()
        {
            if (m_Toggle != null) m_Toggle.performed -= OnToggle;
            if (m_Active) SetActive(false);
        }

        void OnToggle(InputAction.CallbackContext ctx) => SetActive(!m_Active);

        void SetActive(bool on)
        {
            m_Active = on;
            foreach (var sim in FindObjectsByType<XRInteractionSimulator>(FindObjectsSortMode.None))
                sim.usePointAndClick = !on;

            if (!on)
            {
                foreach (var r in m_Added) if (r != null) Destroy(r);
                m_Added.Clear();
            }
            Enforce();
            string msg = on ? "Mouse UI mode ON (F8 to return to controller rays)" : "Mouse UI mode OFF - use the simulated controller ray";
            Debug.Log("[FireCenter] " + msg);
            if (PlayerHudNotification.Instance != null) PlayerHudNotification.Instance.ShowText(msg, 2.5f);
        }

        void Update()
        {
            if (m_Active) Enforce();
        }

        void Enforce()
        {
            var module = EventSystem.current != null ? EventSystem.current.currentInputModule as XRUIInputModule : null;
            if (module != null && m_Active) module.enableMouseInput = true;
            if (!m_Active) return;

            var cam = Camera.main;
            foreach (var tracked in FindObjectsByType<TrackedDeviceGraphicRaycaster>(FindObjectsSortMode.None))
            {
                var canvas = tracked.GetComponent<Canvas>();
                if (canvas != null && canvas.worldCamera == null) canvas.worldCamera = cam;
                if (tracked.GetComponent<GraphicRaycaster>() == null)
                    m_Added.Add(tracked.gameObject.AddComponent<GraphicRaycaster>());
            }
        }
#endif
    }
}
