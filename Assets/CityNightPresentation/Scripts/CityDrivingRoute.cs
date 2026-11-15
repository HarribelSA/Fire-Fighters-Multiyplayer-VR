using UnityEngine;

namespace CityNightPresentation
{
    public sealed class CityDrivingRoute : MonoBehaviour
    {
        public Transform spawnPoint, arrivalPoint, stopZone;
        public Transform[] waypoints = new Transform[0];
        public CitySignalController[] intersections = new CitySignalController[0];
        public Vector3 preliminaryTruckSize = new Vector3(2.6f, 4f, 10f);
        public Vector3 stopZoneSize = new Vector3(4f, 0.1f, 18f);
        [TextArea] public string clearanceNotes = "Preliminary clearance only. Actual fire truck swept-path validation is still required.";
        public float Length
        {
            get { float d = 0; for (int i = 1; i < waypoints.Length; i++) if (waypoints[i] && waypoints[i-1]) d += Vector3.Distance(waypoints[i-1].position, waypoints[i].position); return d; }
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.cyan;
            for (int i = 0; i < waypoints.Length; i++)
            {
                if (!waypoints[i]) continue;
                Gizmos.DrawWireSphere(waypoints[i].position, .45f);
                if (i > 0 && waypoints[i-1]) Gizmos.DrawLine(waypoints[i-1].position, waypoints[i].position);
            }
            if (stopZone) { Gizmos.color = Color.yellow; Gizmos.matrix = stopZone.localToWorldMatrix; Gizmos.DrawWireCube(Vector3.zero, stopZoneSize); }
        }
    }
}
