using System;
using UnityEngine;

// The FireCenter package arrived without its shared type definitions. These are reconstructed from
// their usages so the FireCenter UI scripts compile; gameplay authority stays with the project's
// existing StationSessionFlow (roles/ready/transitions) — see LobbyPlayerState.
namespace FireCenter
{
    public enum PlayerRole { Unassigned, Driver, Firefighter }
    // Player identity colour (seat-independent). Roles are separate.
    public enum PlayerColor { None, Red, Blue }
    public enum ControlMode { PCVR_G29_HandTracking, MetaQuestControllers }
    public enum GameDifficulty { Easy, Normal, Hard }
    public enum TrafficDensity { Low, Medium, High }
    public enum TurnStyle { Snap, Smooth }
    public enum GameLanguage { English, Arabic }

    public static class StationColors
    {
        public static readonly Color Red = new Color(.92f, .2f, .18f), Blue = new Color(.2f, .55f, 1f);
        public static readonly Color Ok = new Color(.3f, .86f, .42f), Bad = new Color(1f, .35f, .3f);
        public static Color Of(PlayerColor c) => c == PlayerColor.Red ? Red : c == PlayerColor.Blue ? Blue : Color.gray;
        public static string Label(PlayerColor c) => c == PlayerColor.Red ? "RED" : c == PlayerColor.Blue ? "BLUE" : "—";
        public static PlayerRole InitialRole(PlayerColor c) => c == PlayerColor.Blue ? PlayerRole.Firefighter : PlayerRole.Driver;
        public static ControlMode ModeForRole(PlayerRole r) => r == PlayerRole.Firefighter ? ControlMode.MetaQuestControllers : ControlMode.PCVR_G29_HandTracking;
        public static PlayerRole Opposite(PlayerRole r) => r == PlayerRole.Driver ? PlayerRole.Firefighter : r == PlayerRole.Firefighter ? PlayerRole.Driver : PlayerRole.Unassigned;
        public static string RoleLabel(PlayerRole r) => r == PlayerRole.Driver ? "DRIVER" : r == PlayerRole.Firefighter ? "FIREFIGHTER" : "UNASSIGNED";
    }

    public static class LobbySessionData
    {
        public const int MaxPlayers = 2;
        public static string RoomName = "";
        public static bool allowEditorHardwareBypass = true;
        public static bool enforceControllerCompatibility = false;
        public static PlayerColor localColor = PlayerColor.None;
        public static PlayerRole localRole = PlayerRole.Unassigned;
    }

    // Persistent local settings (PlayerPrefs). `changed` passes the setting key.
    public static class FireCenterSettings
    {
        public static event Action<string> changed;
        static int I(string k, int d) => PlayerPrefs.GetInt("FC." + k, d);
        static float F(string k, float d) => PlayerPrefs.GetFloat("FC." + k, d);
        static void SetI(string k, int v) { PlayerPrefs.SetInt("FC." + k, v); changed?.Invoke(k); }
        static void SetF(string k, float v) { PlayerPrefs.SetFloat("FC." + k, v); changed?.Invoke(k); }
        public static GameDifficulty difficulty { get => (GameDifficulty)I("Mission.Difficulty", 1); set => SetI("Mission.Difficulty", (int)value); }
        public static GameDifficulty fireSpread { get => (GameDifficulty)I("Mission.FireSpread", 1); set => SetI("Mission.FireSpread", (int)value); }
        public static TrafficDensity trafficDensity { get => (TrafficDensity)I("Mission.Traffic", 1); set => SetI("Mission.Traffic", (int)value); }
        public static string teamName { get => PlayerPrefs.GetString("FC.Mission.TeamName", "Civil Defence Unit"); set { PlayerPrefs.SetString("FC.Mission.TeamName", value ?? ""); changed?.Invoke("Mission.TeamName"); } }
        public static GameLanguage language { get => (GameLanguage)I("Language", 0); set => SetI("Language", (int)value); }
        public static TurnStyle turnStyle { get => (TurnStyle)I("Comfort.TurnStyle", 0); set => SetI("Comfort.TurnStyle", (int)value); }
        public static float snapTurnAngle { get => F("Comfort.SnapTurnAngle", 45); set => SetF("Comfort.SnapTurnAngle", value); }
        public static float smoothTurnSpeed { get => F("Comfort.SmoothTurnSpeed", 90); set => SetF("Comfort.SmoothTurnSpeed", value); }
        // Default walk speed 2.4 m/s (was 1.5 = too slow). Older saved values at/below the old default are raised once.
        public static float moveSpeed
        {
            get { if (PlayerPrefs.GetInt("FC.Comfort.MoveSpeed.v2", 0) == 0) { PlayerPrefs.SetInt("FC.Comfort.MoveSpeed.v2", 1); if (F("Comfort.MoveSpeed", 2.4f) <= 1.5f) PlayerPrefs.SetFloat("FC.Comfort.MoveSpeed", 2.4f); } return F("Comfort.MoveSpeed", 2.4f); }
            set => SetF("Comfort.MoveSpeed", value);
        }
        public static float heightOffset { get => F("Comfort.HeightOffset", 0); set => SetF("Comfort.HeightOffset", value); }
        public static bool vignetteEnabled { get => I("Comfort.Vignette", 1) != 0; set => SetI("Comfort.Vignette", value ? 1 : 0); }
        public static bool teleportEnabled { get => I("Comfort.Teleport", 1) != 0; set => SetI("Comfort.Teleport", value ? 1 : 0); }
        public static float g29SteeringSensitivity { get => F("Controls.G29Sensitivity", 1); set => SetF("Controls.G29Sensitivity", value); }
        public static float steeringDeadzone { get => F("Controls.SteeringDeadzone", .035f); set => SetF("Controls.SteeringDeadzone", value); }
        public static float steeringCenterOffset { get => F("Controls.SteeringCenter", 0); set => SetF("Controls.SteeringCenter", value); }
    }

    // Waiting-room row (the shipped scene has no rows configured; kept for API compatibility).
    [Serializable]
    public class PlayerRowUI
    {
        public GameObject root;
        public TMPro.TMP_Text nameText, roleText, statusText;
        public UnityEngine.UI.Image colorSwatch;
        public void Show(int index, LobbyPlayerState p, bool isHost, bool isLocal)
        {
            if (root) root.SetActive(true);
            if (nameText) nameText.text = p.playerName + (isLocal ? " (YOU)" : "") + (isHost ? " · HOST" : "");
            if (roleText) roleText.text = StationColors.RoleLabel(p.role);
            if (statusText) statusText.text = p.isReady ? "READY" : "NOT READY";
            if (colorSwatch) colorSwatch.color = StationColors.Of(p.color);
        }
        public void ShowEmpty(int index, PlayerRole role)
        {
            if (root) root.SetActive(true);
            if (nameText) nameText.text = "Waiting for operator…";
            if (roleText) roleText.text = StationColors.RoleLabel(role);
            if (statusText) statusText.text = "";
            if (colorSwatch) colorSwatch.color = Color.gray;
        }
    }
}
