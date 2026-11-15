using UnityEngine;
namespace FirefighterVR.Station
{
    [CreateAssetMenu(menuName="Firefighter VR/Incident Definition")]
    public sealed class StationIncidentDefinition : ScriptableObject
    {
        public string incidentType="Residential structure fire";
        public string location="Residential incident · route destination";
        public string priority="HIGH";
        [TextArea] public string description="Respond to the residential building identified by the existing mission route. Prepare the crew and proceed safely.";
        public string assignedVehicle="Fire truck";
        public string objective="Complete dispatch, open the garage and board together.";
    }
}
