using System.Collections.Generic;
using FirefighterVR.Station;
using FirefighterVR.StationBot;
using UnityEngine;

namespace CityNightPresentation.Fireground
{
    // Solo partner for the fireground: the EXISTING Solo bot takes the role the human did not choose
    // (human Driver -> bot is nozzle operator; human Assistant/controllers -> bot runs pump and reel).
    // It uses the same validated FiregroundOperation rules as a networked teammate. Local only.
    public sealed class FiregroundBot : MonoBehaviour
    {
        public FiregroundOperation op;
        [Header("Plaza route (nozzle role)")]
        public Transform gateOuter, gateInner, hub;
        [Header("Truck side (pump role)")]
        public Transform panelStand, reelStand, waterOnButton, waterOffButton;
        public float walkSpeed = 1.9f, crankRate = 3.2f;

        StationBotFollower bot;
        StationRole role;
        bool unseated;
        readonly List<Vector3> path = new List<Vector3>(8);
        int pathIndex, zoneIndex = -1, pathMode;
        Quaternion aim = Quaternion.identity; bool aimed;
        float reachTimer, pumpDelay;
        int lastPercent = -1, lastZone = -1;
        readonly Vector3[] samples = new Vector3[30];
        static readonly Vector3 GripLocal = new Vector3(0, -.075f, .035f);

        public StationBotFollower Bot => bot;
        void Update() => Tick(Time.deltaTime);
        public void Tick(float dt)
        {
            var flow = StationSessionFlow.Instance;
            if (!op || !flow || !flow.SoloMissionActive) { Release(); return; }
            if (!bot) { var test = FindFirstObjectByType<StationSoloBotTest>(); bot = test ? test.Bot : null; if (!bot) return; }
            role = StationBotRoles.Opposite(flow.SoloHumanRole); if (role == StationRole.None) return;
            op.BotPartnerActive = true;
            var s = op.Current;
            if (s.phase < FiregroundPhase.Dismount) return;
            if (!unseated)
            {
                // Swap out of the cab only while the human view is covered (or the crew is already out).
                var fade = StationViewFade.Instance;
                if (s.phase < FiregroundPhase.Operation && fade && !fade.BlackRendered) return;
                var spawn = role == StationRole.Assistant ? op.nozzleOperatorSpawn : op.pumpOperatorSpawn;
                bot.Unseat(op.GroundBelow(spawn.position), spawn.rotation); bot.ExternalControl = true; unseated = true;
            }
            if (role == StationRole.Assistant) TickNozzle(s, dt); else TickPump(s, dt);
        }
        void Release()
        {
            if (op) op.BotPartnerActive = false;
            if (bot && unseated) { bot.ExternalControl = false; bot.StopReach(); }
            unseated = false; path.Clear(); zoneIndex = -1;
        }
        void Status(string text) => op.BotStatus = text;

        // ---------------- movement ----------------
        bool WalkTo(Vector3 target, float dt, float stop = .12f, bool leashed = false)
        {
            var t = bot.transform; Vector3 d = Vector3.ProjectOnPlane(target - t.position, Vector3.up); float dist = d.magnitude;
            if (dist <= stop) { bot.SetMotion("Idle", .55f); return true; }
            var look = Quaternion.LookRotation(d / dist);
            t.rotation = Quaternion.RotateTowards(t.rotation, look, 200 * dt);
            float angle = Quaternion.Angle(t.rotation, look);
            if (angle > 45) { bot.SetMotion(Vector3.SignedAngle(t.forward, d, Vector3.up) < 0 ? "TurnLeft" : "TurnRight", .8f); return false; }
            Vector3 next = t.position + d / dist * Mathf.Min(dist, walkSpeed * dt);
            if (leashed && op.hoseOutlet)
            {
                // Never walk beyond the paid-out hose: wait for the pump operator to crank more.
                var s = op.Current; float now = Vector3.Distance(op.hoseOutlet.position, t.position), after = Vector3.Distance(op.hoseOutlet.position, next);
                if (after > now && after + .9f > s.payout) { op.BotPull(after + 1.5f); if (after + .9f > op.Current.payout) { bot.SetMotion("Idle", .55f); Status("Need more hose â€” turn the reel crank"); return false; } }
            }
            next.y = op.GroundBelow(next).y; t.position = next;
            bot.SetMotion("Walk", Mathf.Clamp(walkSpeed / .9f, .4f, 1.5f));
            return false;
        }
        bool FollowPath(float dt, bool leashed)
        {
            while (pathIndex < path.Count)
            {
                bool last = pathIndex == path.Count - 1;
                if (!WalkTo(path[pathIndex], dt, last ? .12f : .35f, leashed)) return false;
                pathIndex++;
            }
            return true;
        }
        void SetPath(int mode, params Vector3[] points) { path.Clear(); path.AddRange(points); pathIndex = 0; pathMode = mode; }
        bool InsidePlaza => bot.transform.position.z < (gateInner ? gateInner.position.z + .3f : -200);

        // ---------------- nozzle operator ----------------
        void CarryNozzle(Quaternion rotation)
        {
            var t = bot.transform;
            bot.Reach(t.position + Vector3.up * 1.12f + t.forward * .38f + t.right * .14f, rotation);
            var noz = op.nozzle.transform; noz.SetPositionAndRotation(bot.HandPosition - rotation * GripLocal, rotation);
        }
        Quaternion WalkingCarry => Quaternion.LookRotation((bot.transform.forward - Vector3.up * .35f).normalized, Vector3.up);
        Vector3 DockStand
        {
            get
            {
                var dock = op.nozzleDock.position; var away = Vector3.ProjectOnPlane(op.nozzleOperatorSpawn.position - dock, Vector3.up).normalized;
                return op.GroundBelow(dock + away * .55f);
            }
        }
        void TickNozzle(FiregroundState s, float dt)
        {
            bool holding = s.nozzleHolder == FiregroundOperation.BotId;
            if (s.phase == FiregroundPhase.Dismount) { bot.SetMotion("Idle", .55f); Status("Dismounting"); return; }
            if (s.phase == FiregroundPhase.Operation)
            {
                if (!holding)
                {
                    if (s.Held) { Status("Nozzle held by another firefighter"); return; }
                    Vector3 nozzle = op.nozzle.transform.position;
                    Vector3 stand = s.nozzleDocked ? DockStand : op.GroundBelow(nozzle + Vector3.ProjectOnPlane(bot.transform.position - nozzle, Vector3.up).normalized * .45f);
                    Status("Collecting nozzle");
                    if (!WalkTo(stand, dt, .15f)) { reachTimer = 0; return; }
                    bot.Face(nozzle, dt); bot.Reach(op.nozzle.transform.TransformPoint(GripLocal), op.nozzle.transform.rotation);
                    reachTimer += dt;
                    if (reachTimer > .6f && op.BotGrab()) { reachTimer = 0; zoneIndex = -1; Status("Nozzle in hand"); }
                    return;
                }
                int z = FirstBurning(s);
                if (z < 0) { op.BotTrigger(false); CarryNozzle(WalkingCarry); return; }
                var zone = op.zones[z];
                if (z != zoneIndex || pathMode != 1)
                {
                    zoneIndex = z; aimed = false; op.BotTrigger(false);
                    var approach = zone.botApproachPoint ? zone.botApproachPoint.position : zone.Center;
                    var standPoint = zone.botStandPoint ? zone.botStandPoint.position : zone.Center;
                    if (InsidePlaza) SetPath(1, hub.position, approach, standPoint);
                    else SetPath(1, gateOuter.position, gateInner.position, hub.position, approach, standPoint);
                }
                if (!FollowPath(dt, true)) { if (s.trigger) op.BotTrigger(false); CarryNozzle(WalkingCarry); if (op.BotStatus.StartsWith("Need")) return; Status("Advancing to " + zone.label); return; }
                bot.Face(zone.Center, dt);
                if (!aimed) { aim = SolveAim(zone); aimed = true; }
                CarryNozzle(aim);
                if (!s.pumpOn) { Status("At " + zone.label + " Â· waiting for WATER ON"); return; }
                if (!s.trigger) op.BotTrigger(true);
                int pct = Mathf.CeilToInt(s.Intensity(z) * 100);
                if (pct != lastPercent || z != lastZone) { lastPercent = pct; lastZone = z; Status("Attacking " + zone.label + " Â· " + pct + "%"); }
                return;
            }
            if (s.phase >= FiregroundPhase.Recovery)
            {
                if (holding)
                {
                    if (s.trigger) op.BotTrigger(false);
                    if (pathMode != 2) SetPath(2, hub.position, gateInner.position, gateOuter.position, DockStand);
                    if (!FollowPath(dt, false)) { CarryNozzle(WalkingCarry); Status("Returning nozzle to the truck"); return; }
                    bot.Face(op.nozzleDock.position, dt);
                    bot.Reach(op.nozzleDock.TransformPoint(GripLocal), op.nozzleDock.rotation);
                    var noz = op.nozzle.transform; noz.SetPositionAndRotation(Vector3.MoveTowards(noz.position, op.nozzleDock.position, dt * .8f), Quaternion.RotateTowards(noz.rotation, op.nozzleDock.rotation, dt * 180));
                    if (Vector3.Distance(noz.position, op.nozzleDock.position) < .03f) { op.BotRelease(op.nozzleDock.position, op.nozzleDock.rotation); bot.StopReach(); }
                    Status("Stowing nozzle in bracket");
                    return;
                }
                bot.StopReach();
                if (WalkTo(DockStand + Vector3.ProjectOnPlane(op.nozzleOperatorSpawn.position - op.nozzleDock.position, Vector3.up).normalized * .6f, dt)) bot.Face(op.nozzleDock.position, dt);
                Status(s.phase == FiregroundPhase.Complete ? "Operation complete" : "Nozzle stowed Â· waiting for hose rewind");
            }
        }
        int FirstBurning(FiregroundState s)
        {
            // Keep attacking the current zone until it is out; then the nearest burning one.
            if (zoneIndex >= 0 && zoneIndex < s.zoneCount && s.Intensity(zoneIndex) > 0) return zoneIndex;
            int best = -1; float bestDist = float.MaxValue;
            for (int i = 0; i < s.zoneCount && i < op.zones.Length; i++)
            {
                if (s.Intensity(i) <= 0 || !op.zones[i]) continue;
                float d = (op.zones[i].Center - bot.transform.position).sqrMagnitude; if (d < bestDist) { bestDist = d; best = i; }
            }
            return best;
        }
        Quaternion SolveAim(FireZone zone)
        {
            var t = bot.transform; Vector3 origin = t.position + Vector3.up * 1.12f + t.forward * .75f;
            Vector3 flat = Vector3.ProjectOnPlane(zone.Center - origin, Vector3.up).normalized; Vector3 right = Vector3.Cross(Vector3.up, flat);
            Vector3 best = flat; float bestDistance = float.MaxValue;
            for (float pitch = -10; pitch <= 45; pitch += 1.5f)
            {
                Vector3 dir = Quaternion.AngleAxis(-pitch, right) * flat;
                int n = FiregroundOperation.Trajectory(origin, dir, op.streamSpeed, samples, op.streamObstacles);
                float d = zone.MinDistance(samples, n); if (d < bestDistance) { bestDistance = d; best = dir; }
            }
            return Quaternion.LookRotation(best, Vector3.up);
        }

        // ---------------- pump operator ----------------
        void TickPump(FiregroundState s, float dt)
        {
            if (s.phase == FiregroundPhase.Dismount) { bot.SetMotion("Idle", .55f); Status("Dismounting"); return; }
            if (s.phase == FiregroundPhase.Complete) { bot.StopReach(); if (WalkTo(reelStand.position, dt)) bot.Face(op.hoseOutlet.position, dt); Status("Operation complete"); return; }
            if (s.phase == FiregroundPhase.Operation)
            {
                pumpDelay = s.Held ? pumpDelay + dt : 0;
                if (!s.pumpOn && pumpDelay > 1.2f) { Press(waterOnButton, true, dt, "Charging the line Â· WATER ON"); return; }
                float desired = Mathf.Clamp(op.NozzleDistance() + 3.5f, op.StowedPayout, op.maxPayout);
                if (!s.nozzleDocked && s.payout < desired - .2f) { Crank(1, dt, "Paying out hose"); return; }
                Idle(dt, s.pumpOn ? "Pump running Â· line charged" : "Ready at the pump Â· grab the nozzle"); return;
            }
            // Recovery: water off, wait for the nozzle in its bracket, then rewind.
            if (s.pumpOn) { Press(waterOffButton, false, dt, "Securing water Â· WATER OFF"); return; }
            if (!s.nozzleDocked || s.Held)
            {
                if (s.payout > op.NozzleDistance() + 2.5f) { Crank(-1, dt, "Taking in slack"); return; }
                Idle(dt, "Return the nozzle to its bracket"); return;
            }
            if (s.payout > op.StowedPayout + .05f) { Crank(-1, dt, "Rewinding hose onto the reel"); return; }
            Idle(dt, "Hose stowed");
        }
        void Press(Transform button, bool on, float dt, string status)
        {
            Status(status);
            if (!WalkTo(panelStand.position, dt)) { reachTimer = 0; bot.StopReach(); return; }
            bot.Face(button.position, dt); bot.Reach(button.position - button.forward * .02f, Quaternion.LookRotation(button.forward, Vector3.up));
            reachTimer += dt; if (reachTimer > .7f) { op.BotPump(on); reachTimer = 0; bot.StopReach(); }
        }
        void Crank(float direction, float dt, string status)
        {
            Status(status);
            if (!WalkTo(reelStand.position, dt)) { bot.StopReach(); return; }
            var knob = op.reel && op.reel.knob ? op.reel.knob : op.hoseOutlet;
            bot.Face(op.reel.transform.position, dt); bot.Reach(knob.position, knob.rotation);
            op.BotCrank(direction * crankRate * dt);
        }
        void Idle(float dt, string status)
        {
            Status(status); bot.StopReach();
            if (WalkTo(reelStand.position, dt)) bot.Face(op.hoseOutlet.position, dt);
        }
    }
}
