using System.Reflection;
using FirefighterVR.Station;

namespace FirefighterVR.StationBot
{
    // These assignments are local test data, never network ownership or lobby-slot claims.
    public static class StationBotRoles
    {
        static readonly FieldInfo Preference = typeof(SeatedDispatchLobby).GetField("preference", BindingFlags.Instance | BindingFlags.NonPublic);
        public static StationRole HumanRole(SeatedDispatchLobby lobby)
        {
            // The lobby selection includes explicit console selection and its modality fallback.
            // Read only: do not replace or configure the existing control selection system.
            if (lobby && Preference?.GetValue(lobby) is StationRole role && role != StationRole.None) return role;
            return StationRole.None;
        }
        public static StationRole Opposite(StationRole human) => human == StationRole.Driver ? StationRole.Assistant : human == StationRole.Assistant ? StationRole.Driver : StationRole.None;
        public static bool CanUseBot(int realPlayers, StationPhase phase) => realPlayers < 2 && phase == StationPhase.Waiting;
        public static string Label(StationRole role) => role == StationRole.Assistant ? "Passenger / Assistant" : role.ToString();
    }
}
