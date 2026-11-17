using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Hands;

namespace FireCenter
{
    /// <summary>
    /// Reports whether a Logitech G29 is recognized by the Input System, whether the DriverControls
    /// bindings actually resolve to its controls, and whether XR hand tracking is running.
    /// It does not assume hand tracking exists on Windows PC VR - it checks the XRHandSubsystem.
    /// </summary>
    public class InputDeviceProbe : MonoBehaviour
    {
        public static InputDeviceProbe Instance { get; private set; }

        [SerializeField] InputModeManager m_InputModeManager;
        [SerializeField, Tooltip("Seconds between hand-tracking subsystem checks.")]
        float m_HandCheckInterval = 2f;

        public bool g29Detected { get; private set; }
        public string g29DeviceName { get; private set; } = "";
        public bool steeringBound { get; private set; }
        public bool accelerateBound { get; private set; }
        public bool brakeBound { get; private set; }
        public bool handTrackingRunning { get; private set; }

        public event Action statusChanged;

        static readonly List<XRHandSubsystem> s_HandSubsystems = new();
        float m_NextHandCheck;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() => Instance = null;

        void Awake()
        {
            Instance = this;
            if (m_InputModeManager == null) m_InputModeManager = GetComponent<InputModeManager>();
        }

        void OnEnable()
        {
            InputSystem.onDeviceChange += OnDeviceChange;
            Refresh();
        }

        void OnDisable() => InputSystem.onDeviceChange -= OnDeviceChange;

        void OnDeviceChange(InputDevice device, InputDeviceChange change)
        {
            if (change == InputDeviceChange.Added || change == InputDeviceChange.Removed ||
                change == InputDeviceChange.Reconnected || change == InputDeviceChange.Disconnected)
                Refresh();
        }

        void Update()
        {
            if (Time.unscaledTime < m_NextHandCheck) return;
            m_NextHandCheck = Time.unscaledTime + m_HandCheckInterval;
            bool running = IsHandTrackingRunning();
            if (running != handTrackingRunning)
            {
                handTrackingRunning = running;
                statusChanged?.Invoke();
            }
        }

        public static bool IsG29(InputDevice device)
        {
            var d = device.description;
            string product = d.product ?? "";
            string manufacturer = d.manufacturer ?? "";
            if (product.IndexOf("G29", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            // Logitech VID 0x046D, G29 PIDs: 0xC24F (PS3 mode) / 0xC260 (PS4 mode).
            if (d.interfaceName == "HID" && !string.IsNullOrEmpty(d.capabilities))
            {
                string caps = d.capabilities;
                bool logitech = caps.Contains("\"vendorId\":1133") || manufacturer.IndexOf("Logitech", StringComparison.OrdinalIgnoreCase) >= 0;
                bool g29Pid = caps.Contains("\"productId\":49743") || caps.Contains("\"productId\":49760");
                return logitech && g29Pid;
            }
            return false;
        }

        /// <summary>The connected G29 (null when none).</summary>
        public InputDevice g29Device { get; private set; }

        /// <summary>True when tracked VR controllers (or the XR Device Simulator's simulated controllers) are present.</summary>
        public bool vrControllersPresent { get; private set; }

        /// <summary>Raw (un-processed) G29 wheel axis in -1..1, used for centre calibration. NaN without a wheel.</summary>
        public float ReadRawSteering()
        {
            if (g29Device == null) return float.NaN;
            var axis = g29Device.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("stick/x")
                       ?? g29Device.TryGetChildControl<UnityEngine.InputSystem.Controls.AxisControl>("x");
            return axis != null ? axis.ReadUnprocessedValue() : float.NaN;
        }

        public void Refresh()
        {
            g29Detected = false;
            g29DeviceName = "";
            g29Device = null;
            vrControllersPresent = false;
            foreach (var device in InputSystem.devices)
            {
                if (device is UnityEngine.InputSystem.XR.XRController) vrControllersPresent = true;
                if (g29Detected || !IsG29(device)) continue;
                g29Detected = true;
                g29Device = device;
                g29DeviceName = $"{device.displayName} (layout: {device.layout})";
            }

            InputActionAsset asset = m_InputModeManager != null ? m_InputModeManager.actions : null;
            steeringBound = HasG29Control(asset, "DriverControls/Steering");
            accelerateBound = HasG29Control(asset, "DriverControls/Accelerate");
            brakeBound = HasG29Control(asset, "DriverControls/Brake");
            handTrackingRunning = IsHandTrackingRunning();

            if (g29Detected)
                Debug.Log($"[FireCenter] G29 recognized: {g29DeviceName}. Steering bound: {steeringBound}, Accelerate bound: {accelerateBound}, Brake bound: {brakeBound}");

            statusChanged?.Invoke();
        }

        /// <summary>True when the action resolves to at least one control that belongs to the G29.</summary>
        static bool HasG29Control(InputActionAsset asset, string actionPath)
        {
            if (asset == null) return false;
            var action = asset.FindAction(actionPath, false);
            if (action == null) return false;
            // Controls only resolve for enabled maps, so query the bindings directly.
            foreach (var device in InputSystem.devices)
            {
                if (!IsG29(device)) continue;
                foreach (var binding in action.bindings)
                {
                    if (binding.isComposite) continue;
                    string path = binding.effectivePath;
                    if (string.IsNullOrEmpty(path)) continue;
                    if (InputControlPath.TryFindControl(device, path) != null)
                        return true;
                }
            }
            return false;
        }

        static bool IsHandTrackingRunning()
        {
            s_HandSubsystems.Clear();
            SubsystemManager.GetSubsystems(s_HandSubsystems);
            foreach (var s in s_HandSubsystems)
                if (s.running) return true;
            return false;
        }

        public string BuildStatusText()
        {
            var sb = new StringBuilder();
            if (g29Detected)
            {
                sb.Append("<color=#7CFC8A>G29 detected</color>: ").Append(g29DeviceName).Append('\n');
                sb.Append("Bindings - Steering: ").Append(Mark(steeringBound))
                  .Append("  Accelerate: ").Append(Mark(accelerateBound))
                  .Append("  Brake: ").Append(Mark(brakeBound));
            }
            else
            {
                sb.Append(Application.platform == RuntimePlatform.Android
                    ? "<color=#FFB347>G29 not supported on Quest standalone</color>"
                    : "<color=#FFB347>G29 not detected</color> - connect the wheel (Logitech G HUB) and it will be picked up automatically.");
            }
            sb.Append('\n').Append(handTrackingRunning
                ? "Hand tracking: <color=#7CFC8A>running</color>"
                : "Hand tracking: <color=#FFB347>not available on this runtime</color>");
            return sb.ToString();
        }

        static string Mark(bool ok) => ok ? "<color=#7CFC8A>OK</color>" : "<color=#FF6B6B>unresolved</color>";
    }
}
