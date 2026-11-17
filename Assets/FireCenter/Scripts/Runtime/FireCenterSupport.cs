using UnityEngine;
using UnityEngine.SceneManagement;

// Reconstructed FireCenter helpers (missing from the imported package), wired to the project's systems.
namespace FireCenter
{
    public static class LocalSettings
    {
        public static int GetInt(string k, int d) => PlayerPrefs.GetInt("FC." + k, d);
        public static void SetInt(string k, int v) => PlayerPrefs.SetInt("FC." + k, v);
        public static float GetFloat(string k, float d) => PlayerPrefs.GetFloat("FC." + k, d);
        public static void SetFloat(string k, float v) => PlayerPrefs.SetFloat("FC." + k, v);
        public static void Save() => PlayerPrefs.Save();
    }

    public static class RoleRules
    {
        public static string RoleLabel(PlayerRole r) => r == PlayerRole.Driver ? "Driver" : r == PlayerRole.Firefighter ? "Firefighter" : "Unassigned";
        public static string ControlModeLabel(ControlMode m) => m == ControlMode.MetaQuestControllers ? "Meta Quest controllers" : "PCVR + G29";
        public static string ControlModeLongLabel(ControlMode m) => m == ControlMode.MetaQuestControllers ? "Meta Quest controllers (Firefighter)" : "PCVR hand tracking + Logitech G29 (Driver)";
        public static ControlMode PreferredModeFor(PlayerRole r) => StationColors.ModeForRole(r);
        public static ControlMode DefaultModeForDevice() => Application.platform == RuntimePlatform.Android ? ControlMode.MetaQuestControllers : ControlMode.PCVR_G29_HandTracking;
    }

    public static class MissionSettings
    {
        // Session settings come from the room host (the local player when offline).
        public static LobbyPlayerState Host { get { foreach (var p in LobbyPlayerState.All) if (p.isSessionHost && p != LobbyPlayerState.Local) return p; return null; } }
    }

    // The project's synchronized loading overlay is FirefighterVR.Station.MissionLoadingScreen.
    public static class LoadingScreenController
    {
        public static bool IsDisplayComplete => true;
    }

    // Applies the lobby G29 calibration to the existing city truck controller (now or when the city loads).
    public static class SteeringCalibrationProcessor
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Hook() { SceneManager.sceneLoaded -= Loaded; SceneManager.sceneLoaded += Loaded; }
        static void Loaded(Scene s, LoadSceneMode m) => ApplyToSteeringAction();
        public static void ApplyToSteeringAction()
        {
            foreach (var w in Object.FindObjectsByType<CityNightPresentation.Mission.FireTruckWheelController>(FindObjectsSortMode.None))
            {
                w.steeringCenter = Mathf.Clamp(FireCenterSettings.steeringCenterOffset, -.5f, .5f);
                w.steeringDeadzone = Mathf.Clamp(FireCenterSettings.steeringDeadzone, 0, .2f);
                w.steeringRange = Mathf.Clamp(1f / Mathf.Max(.25f, FireCenterSettings.g29SteeringSensitivity), .3f, 1f);
            }
        }
    }

    // Who sits at each lobby station (role-based), derived from LobbyPlayerState.
    public static class StationSeats
    {
        public struct Occupant { public bool present, isLocal; public PlayerColor color; public LobbyPlayerState state; }
        public static Occupant Get(PlayerRole seat)
        {
            var p = LobbyPlayerState.ByRole(seat);
            return new Occupant { present = p != null, isLocal = p != null && p == LobbyPlayerState.Local, state = p, color = p != null ? p.color : (seat == PlayerRole.Driver ? PlayerColor.Red : PlayerColor.Blue) };
        }
        public static bool IsConnected() => LobbyPlayerState.Local != null;
    }
}
