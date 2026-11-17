using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace FireCenter
{
    /// <summary>
    /// Activates the Input Action Map that matches the locally selected <see cref="ControlMode"/>.
    /// All bindings live in the FireCenterControls Input Action Asset - this class only enables/disables maps
    /// and persists the choice locally. It never touches the XR Interaction Toolkit action maps, so
    /// XR UI interaction (ray interactors, poke, XR UI Input Module) keeps working in every mode.
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class InputModeManager : MonoBehaviour
    {
        public const string DriverMapName = "DriverControls";
        public const string MetaQuestMapName = "MetaQuestControls";
        public const string UIMapName = "UIControls";
        const string k_SettingsKey = "ControlMode";

        public static InputModeManager Instance { get; private set; }

        [SerializeField, Tooltip("FireCenterControls.inputactions")]
        InputActionAsset m_Actions;

        public InputActionAsset actions => m_Actions;
        public ControlMode currentMode { get; private set; }

        /// <summary>Raised whenever the control mode changes (including the initial load).</summary>
        public event Action<ControlMode> modeChanged;

        InputActionMap m_DriverMap, m_QuestMap, m_UIMap;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(this);
                return;
            }
            Instance = this;

            if (m_Actions == null)
            {
                Debug.LogError("[FireCenter] InputModeManager has no Input Action Asset assigned.", this);
                enabled = false;
                return;
            }

            m_DriverMap = m_Actions.FindActionMap(DriverMapName, false);
            m_QuestMap = m_Actions.FindActionMap(MetaQuestMapName, false);
            m_UIMap = m_Actions.FindActionMap(UIMapName, false);

            int saved = LocalSettings.GetInt(k_SettingsKey, (int)RoleRules.DefaultModeForDevice());
            ApplyMode((ControlMode)Mathf.Clamp(saved, 0, 1), save: false);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                m_DriverMap?.Disable();
                m_QuestMap?.Disable();
                m_UIMap?.Disable();
                Instance = null;
            }
        }

        /// <summary>UI entry point (int to be usable from UnityEvents).</summary>
        public void SetMode(int mode) => SetMode((ControlMode)mode);

        public void SetMode(ControlMode mode)
        {
            if (mode == currentMode && (GetMapForMode(mode)?.enabled ?? false))
                return;
            ApplyMode(mode, save: true);
        }

        void ApplyMode(ControlMode mode, bool save)
        {
            currentMode = mode;

            // UI map is always on - switching control maps must never disable UI interaction.
            m_UIMap?.Enable();

            if (mode == ControlMode.PCVR_G29_HandTracking)
            {
                m_QuestMap?.Disable();
                m_DriverMap?.Enable();
            }
            else
            {
                m_DriverMap?.Disable();
                m_QuestMap?.Enable();
            }

            if (save)
            {
                LocalSettings.SetInt(k_SettingsKey, (int)mode);
                LocalSettings.Save();
            }

            modeChanged?.Invoke(mode);
        }

        public InputActionMap GetMapForMode(ControlMode mode) =>
            mode == ControlMode.PCVR_G29_HandTracking ? m_DriverMap : m_QuestMap;

        /// <summary>Convenience lookup for gameplay scripts that prefer a string path ("DriverControls/Steering").</summary>
        public InputAction FindAction(string path) => m_Actions != null ? m_Actions.FindAction(path, false) : null;
    }
}
