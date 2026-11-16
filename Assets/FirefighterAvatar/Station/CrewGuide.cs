using System.Collections.Generic;
using CityNightPresentation.Fireground;
using CityNightPresentation.Mission;
using FirefighterVR.StationBot;
using UnityEngine;

namespace FirefighterVR.Station
{
    // Visual (non-text) crew guidance during the mission:
    //  - blue arrow above Player 1 (Driver / pump), red arrow above Player 2 (Assistant / nozzle);
    //  - role-coloured 1 m glow beams on the current tasks, hidden when not relevant.
    // Purely local presentation derived from the already replicated mission/fireground state.
    public sealed class CrewGuide : MonoBehaviour
    {
        readonly List<TaskBeacon> beacons = new List<TaskBeacon>(), heads = new List<TaskBeacon>();
        int used, headsUsed;
        XRMultiplayer.XRINetworkPlayer[] players = new XRMultiplayer.XRINetworkPlayer[0];
        float nextScan;
        StationGarageButton garageButton; StationMissionView garageView;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot() { if (FindFirstObjectByType<CrewGuide>()) return; var g = new GameObject("CrewGuide"); DontDestroyOnLoad(g); g.AddComponent<CrewGuide>(); }

        void LateUpdate()
        {
            used = headsUsed = 0;
            var flow = StationSessionFlow.Instance;
            if (flow && flow.MissionSessionActive && flow.MissionState.stage != CrewMissionStage.None)
            {
                HeadArrows(flow);
                var s = flow.MissionState;
                if (s.stage <= CrewMissionStage.Departure) StationTasks(flow, s);
                else CityTasks(flow);
            }
            for (int i = used; i < beacons.Count; i++) beacons[i].Hide();
            for (int i = headsUsed; i < heads.Count; i++) heads[i].Hide();
        }

        TaskBeacon Next(List<TaskBeacon> pool, ref int index)
        {
            if (index >= pool.Count) pool.Add(TaskBeacon.Create("Beacon" + pool.Count, transform));
            return pool[index++];
        }
        void Beam(Vector3 p, StationRole role, float radius = .35f, float height = 1) => Next(beacons, ref used).Show(p, role, radius, true, height);
        void Pointer(Vector3 p, StationRole role) => Next(beacons, ref used).Show(p, role, .1f, false);

        void HeadArrows(StationSessionFlow flow)
        {
            if (Time.unscaledTime >= nextScan) { nextScan = Time.unscaledTime + .5f; players = FindObjectsByType<XRMultiplayer.XRINetworkPlayer>(FindObjectsSortMode.None); }
            if (flow.IsSpawned && !flow.SoloMissionActive)
            {
                var lobby = flow.Lobby.Value;
                foreach (var p in players)
                {
                    if (!p || !p.IsSpawned || p.IsOwner || !p.head) continue;
                    var role = lobby.RoleOf(p.OwnerClientId); if (role == StationRole.None) continue;
                    Next(heads, ref headsUsed).ShowHeadArrow(p.head.position, role);
                }
            }
            if (flow.SoloMissionActive)
            {
                var test = FindFirstObjectByType<StationSoloBotTest>(); var bot = test ? test.Bot : null;
                if (bot && bot.isActiveAndEnabled && bot.animator && bot.animator.isHuman)
                {
                    var head = bot.animator.GetBoneTransform(HumanBodyBones.Head);
                    Next(heads, ref headsUsed).ShowHeadArrow(head ? head.position : bot.transform.position + Vector3.up * 1.8f, bot.Role);
                }
            }
        }

        void StationTasks(StationSessionFlow flow, CrewMissionState s)
        {
            var view = StationMissionView.Instance; if (!view || !view.isActiveAndEnabled) return;
            if (s.stage == CrewMissionStage.EmergencyAlarm || s.stage == CrewMissionStage.DispatchReview)
            {
                if (!s.tabletPicked && view.tablet) Beam(view.tablet.position, StationRole.Assistant, .3f);
                else if (!s.tabletPresented && view.dispatchSeat) Beam(view.dispatchSeat.position, StationRole.Assistant, .55f);
                if (s.CanAccept && view.acceptButton) Pointer(view.acceptButton.transform.position, StationRole.Driver);
            }
            else if (s.stage == CrewMissionStage.DriverInTruck)
            {
                if (garageView != view) { garageView = view; var frame = view.garage ? view.garage.transform.parent.Find("RollupDoorFrame") : null; garageButton = frame ? frame.GetComponent<StationGarageButton>() : null; }
                if (!s.garageTask && garageButton && garageButton.upperArrow) Beam(garageButton.upperArrow.bounds.center - Vector3.up * .5f, StationRole.Assistant, .3f);
                else if (s.CanBoardAssistant(flow.SharedTime, flow.garageTravelSeconds) && view.passengerApproach) Beam(view.passengerApproach.position, StationRole.Assistant, .45f);
            }
        }

        void CityTasks(StationSessionFlow flow)
        {
            var op = FiregroundOperation.Instance; var mission = op ? op.mission : FindFirstObjectByType<FireTruckMission>();
            if (!mission) return;
            var fs = op ? op.Current : default;
            if (!mission.HasArrived && (!op || fs.phase == FiregroundPhase.Inactive))
            {
                // Glowing arrival marker at the incident (Driver's destination).
                if (mission.route && mission.route.stopZone) Beam(mission.route.stopZone.position, StationRole.Driver, 2.4f);
                var emergency = mission.GetComponent<FireTruckEmergency>();
                if (emergency && emergency.pullHandle && mission.CanActivateEmergency && !mission.SirenActive) Pointer(emergency.pullHandle.position + Vector3.up * .05f, StationRole.Driver);
                return;
            }
            if (!op || (fs.phase != FiregroundPhase.Operation && fs.phase != FiregroundPhase.Recovery)) return;
            Vector3 Down(Vector3 p) => op.GroundBelow(p);
            // Player 1 (blue): reel and pump panel.
            bool needHose = fs.phase == FiregroundPhase.Operation && !fs.nozzleDocked && fs.payout < op.NozzleDistance() + 1.5f && fs.payout < op.maxPayout - .1f;
            bool rewind = fs.phase == FiregroundPhase.Recovery && fs.nozzleDocked && !fs.pumpOn && fs.payout > op.StowedPayout + .05f;
            if ((needHose || rewind) && op.reel && op.reel.knob) Beam(Down(op.reel.knob.position) + Vector3.up * .02f, StationRole.Driver, .3f, 1.2f);
            var bot = op.GetComponent<FiregroundBot>();
            if (fs.phase == FiregroundPhase.Operation && !fs.pumpOn && (fs.Held || !fs.nozzleDocked) && bot && bot.waterOnButton) Pointer(bot.waterOnButton.position, StationRole.Driver);
            if (fs.phase == FiregroundPhase.Recovery && fs.pumpOn && bot && bot.waterOffButton) Pointer(bot.waterOffButton.position, StationRole.Driver);
            // Player 2 (red): nozzle, fire attack position, bracket.
            if (!fs.Held && (fs.phase == FiregroundPhase.Operation || !fs.nozzleDocked) && op.nozzle) Beam(Down(op.nozzle.transform.position), StationRole.Assistant, .3f);
            if (fs.Held && fs.phase == FiregroundPhase.Operation)
            {
                int best = -1; float bestD = float.MaxValue; Vector3 from = op.nozzle.transform.position;
                for (int i = 0; i < fs.zoneCount && i < op.zones.Length; i++) if (fs.Intensity(i) > 0 && op.zones[i]) { float d = (op.zones[i].Center - from).sqrMagnitude; if (d < bestD) { bestD = d; best = i; } }
                if (best >= 0) { var z = op.zones[best]; Beam(z.botStandPoint ? z.botStandPoint.position : Down(z.Center), StationRole.Assistant, .6f); }
            }
            if (fs.Held && fs.phase == FiregroundPhase.Recovery && op.nozzleDock) Beam(Down(op.nozzleDock.position), StationRole.Assistant, .35f, 1.7f);
        }
    }
}
