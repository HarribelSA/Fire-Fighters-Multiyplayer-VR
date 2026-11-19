using UnityEngine;
using UnityEngine.InputSystem;

namespace CityNightPresentation.Mission
{
    // In a PC VR build the desktop window is usually NOT the focused window while the Driver wears the
    // headset. The Input System's default background behaviour then disables "non-background" devices,
    // which includes the Logitech G29, so the truck ignored the wheel and pedals (it worked in the Editor,
    // where the Game view keeps input). Input is kept alive regardless of window focus.
    public static class WheelInputFocusFix
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void Apply()
        {
            var settings = InputSystem.settings;
            if (settings != null && settings.backgroundBehavior != InputSettings.BackgroundBehavior.IgnoreFocus)
            {
                settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                Debug.Log("[Input] Background behaviour set to IgnoreFocus: the G29 keeps working when the desktop window is not focused.");
            }
            // Devices that were already disabled while unfocused are switched back on.
            foreach (var d in InputSystem.devices) if (!d.enabled && (d.description.product ?? "").Contains("G29")) InputSystem.EnableDevice(d);
        }
    }
}
