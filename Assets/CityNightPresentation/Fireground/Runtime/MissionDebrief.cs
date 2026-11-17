using System.Collections;
using System.Collections.Generic;
using CityNightPresentation.Mission;
using FirefighterVR.Station;
using FirefighterVR.Stats;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using XRMultiplayer;

namespace CityNightPresentation.Fireground
{
    // End of the operation, after the hose is stowed (FiregroundPhase.Complete):
    //   "close your eyes" fade -> both crew re-seated in the truck -> cutscene drive along the road ->
    //   debrief dashboard in front of the crew (names, total time, penalty reasons, previous 4 crews).
    // Runs locally on every peer from replicated state; only the session authority uploads the record.
    // Added at runtime by FiregroundOperation, so no scene wiring is required.
    [DefaultExecutionOrder(200)]
    public sealed class MissionDebrief : MonoBehaviour
    {
        // Theme (matches the in-game lobby / dispatch UI).
        public static readonly Color Panel = Hex("#121820F0"), Card = Hex("#1E2530"), Line = Hex("#2E3542");
        public static readonly Color Accent = Hex("#DB3D29"), Amber = Hex("#FFB347"), Good = Hex("#78E7B9"), Muted = Hex("#A0A8B4"), Text = Hex("#F2F4F7");

        public static bool Active { get; private set; }        // debrief sequence started (hide driving HUD)
        public static bool CrewSeated { get; private set; }    // FireTruckSeating keeps the crew in the cab
        public static bool HideTraffic { get; private set; }   // traffic paused/hidden during the cutscene

        [Tooltip("Seconds to show OPERATION COMPLETE before the eye-close fade.")] public float completeHold = 3f;
        public float eyesCloseSeconds = 1.8f, eyesClosedHold = 1.5f, eyesOpenSeconds = 1.6f;
        [Range(3, 14)] public float cruiseSpeed = 8f;
        public float dashboardDistance = .72f, dashboardDrop = .16f, dashboardWidth = 1.05f;

        FiregroundOperation op;
        FireTruckMission mission;
        MissionRunRecord record;
        List<MissionRunRecord> previous;
        bool previousOnline, started, uploadDone;
        string uploadStatus = "";
        Canvas dashboard;
        TMP_Text previousText, footerText;
        TMP_FontAsset font;

        // Cutscene path
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<float> pathDistance = new List<float>();
        float travelled, speed, heightOffset;
        bool driving, moveLocally;
        Rigidbody body;
        Behaviour wheelController;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Active = CrewSeated = HideTraffic = false; }

        public static MissionDebrief Attach(FiregroundOperation operation)
        {
            var d = operation.GetComponent<MissionDebrief>();
            if (!d) d = operation.gameObject.AddComponent<MissionDebrief>();
            d.op = operation;
            return d;
        }

        void OnDisable() { Active = CrewSeated = HideTraffic = false; }

        void Update()
        {
            if (!op) op = FiregroundOperation.Instance;
            if (!op) return;
            mission = op.mission;
            if (!started && op.Current.phase == FiregroundPhase.Complete && mission)
            {
                started = true;
                StartCoroutine(Sequence());
            }
            if (driving) Drive(Time.deltaTime);
        }

        IEnumerator Sequence()
        {
            Active = true;
            record = BuildRecord();
            MissionStatsStore.SaveLocal(record);
            if (mission.Authority) StartCoroutine(MissionStatsStore.Upload(record, (ok, msg) => { uploadDone = true; uploadStatus = msg; RefreshFooter(); }));
            else { uploadDone = true; uploadStatus = MissionStatsStore.RemoteConfigured ? "Saved online by the crew host" : "Saved on this headset"; }
            StartCoroutine(MissionStatsStore.FetchRecent(4, record.id, (runs, online) => { previous = runs; previousOnline = online; RefreshPrevious(); }));

            yield return new WaitForSecondsRealtime(completeHold);
            FiregroundOperation.Notify("CLOSE YOUR EYES… take a breath. The crew heads back.");
            yield return new WaitForSecondsRealtime(1.2f);

            var fade = StationViewFade.Instance; float oldOut = 0, oldIn = 0;
            if (fade) { oldOut = fade.sceneFadeOut; oldIn = fade.sceneFadeIn; fade.sceneFadeOut = eyesCloseSeconds; fade.sceneFadeIn = eyesOpenSeconds; yield return fade.Cover(); }

            // Eyes closed: put the truck at the start of the return drive and seat the crew.
            HideTraffic = true;
            PrepareDrive();
            CrewSeated = true;
            BuildDashboard();
            yield return new WaitForSecondsRealtime(eyesClosedHold);

            driving = true;
            if (fade) { yield return fade.Reveal(); fade.sceneFadeOut = oldOut; fade.sceneFadeIn = oldIn; }
        }

        // ---------------- Record ----------------
        MissionRunRecord BuildRecord()
        {
            var flow = StationSessionFlow.Instance;
            var fg = op.Current;
            var crew = flow && flow.MissionSessionActive ? flow.MissionState : default;
            float missionSeconds = crew.acceptedAt > 0 && fg.completedAt > crew.acceptedAt ? (float)(fg.completedAt - crew.acceptedAt) : mission.DrivingSeconds;
            float penalty = mission.PenaltySeconds;
            var now = System.DateTimeOffset.UtcNow;
            var r = new MissionRunRecord
            {
                id = MissionRunRecord.NewId(),
                ts = now.ToUnixTimeMilliseconds(),
                utc = now.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                missionSeconds = Mathf.Round(missionSeconds * 10) / 10,
                penaltySeconds = penalty,
                totalSeconds = Mathf.Round((missionSeconds + penalty) * 10) / 10,
                redLights = mission.EvaluationState.redLightViolations,
                drivingSeconds = Mathf.Round(mission.DrivingSeconds * 10) / 10,
                sirenSeconds = Mathf.Round(mission.EvaluationState.sirenSeconds),
                incident = flow && flow.incident ? flow.incident.incidentType : "Structure fire",
                location = flow && flow.incident ? flow.incident.location : ""
            };
            string localName = MissionStatsStore.CleanName(XRINetworkGameManager.LocalPlayerName.Value, "Firefighter");
            if (flow && flow.SoloMissionActive)
            {
                r.mode = "solo";
                bool humanDrives = flow.SoloHumanRole == StationRole.Driver;
                r.driver = humanDrives ? localName : "Bot";
                r.assistant = humanDrives ? "Bot" : localName;
            }
            else if (flow && flow.IsSpawned)
            {
                var lobby = flow.Lobby.Value;
                r.driver = MissionStatsStore.CleanName(NameOf(lobby.Driver), "Driver");
                r.assistant = MissionStatsStore.CleanName(NameOf(lobby.Assistant), "Assistant");
            }
            else { r.mode = "solo"; r.driver = localName; r.assistant = "—"; }
            return r;
        }
        static string NameOf(ulong id)
        {
            foreach (var p in FindObjectsByType<XRINetworkPlayer>(FindObjectsSortMode.None))
                if (p && p.IsSpawned && p.OwnerClientId == id) return p.playerName;
            return "";
        }

        // ---------------- Cutscene drive ----------------
        void PrepareDrive()
        {
            var truck = mission.transform;
            body = mission.GetComponent<Rigidbody>();
            wheelController = mission.controller;
            // With a NetworkTransform the authority's pose replicates; otherwise every peer animates the same path.
            var netTransform = mission.GetComponent<Unity.Netcode.Components.NetworkTransform>();
            moveLocally = !netTransform || mission.Authority;
            if (wheelController) wheelController.enabled = false;
            if (body) { if (!body.isKinematic) { body.linearVelocity = Vector3.zero; body.angularVelocity = Vector3.zero; } body.isKinematic = true; }

            path.Clear(); pathDistance.Clear();
            var route = mission.route;
            if (route && route.waypoints.Length > 1)
            {
                // Return trip: the route driven backwards, from the incident toward the station side.
                var raw = new List<Vector3>();
                for (int i = route.waypoints.Length - 1; i >= 0; i--) if (route.waypoints[i]) raw.Add(route.waypoints[i].position);
                Smooth(raw, path);
            }
            if (path.Count < 2) { path.Add(truck.position); path.Add(truck.position + truck.forward * 60); }
            float total = 0; pathDistance.Add(0);
            for (int i = 1; i < path.Count; i++) { total += Vector3.ProjectOnPlane(path[i] - path[i - 1], Vector3.up).magnitude; pathDistance.Add(total); }
            heightOffset = route && route.waypoints.Length > 0 && route.waypoints[route.waypoints.Length - 1] ? truck.position.y - route.waypoints[route.waypoints.Length - 1].position.y : 0;
            travelled = 4; speed = 0;
            if (moveLocally) Pose(travelled, 1);
        }
        static void Smooth(List<Vector3> raw, List<Vector3> output)
        {
            if (raw.Count < 3) { output.AddRange(raw); return; }
            for (int i = 0; i < raw.Count - 1; i++)
            {
                Vector3 p0 = raw[Mathf.Max(0, i - 1)], p1 = raw[i], p2 = raw[i + 1], p3 = raw[Mathf.Min(raw.Count - 1, i + 2)];
                int steps = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(p1, p2) / 1.5f), 1, 40);
                for (int s = 0; s < steps; s++)
                {
                    float t = s / (float)steps, t2 = t * t, t3 = t2 * t;
                    output.Add(.5f * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t2 + (-p0 + 3 * p1 - 3 * p2 + p3) * t3));
                }
            }
            output.Add(raw[raw.Count - 1]);
        }
        Vector3 Sample(float d)
        {
            d = Mathf.Clamp(d, 0, pathDistance[pathDistance.Count - 1]);
            int i = 1; while (i < pathDistance.Count - 1 && pathDistance[i] < d) i++;
            return Vector3.Lerp(path[i - 1], path[i], Mathf.InverseLerp(pathDistance[i - 1], pathDistance[i], d));
        }
        void Drive(float dt)
        {
            if (!moveLocally || path.Count < 2) return;
            float end = pathDistance[pathDistance.Count - 1] - 6;
            float remaining = end - travelled;
            float target = remaining < 25 ? Mathf.Lerp(0, cruiseSpeed, remaining / 25) : cruiseSpeed;
            speed = Mathf.MoveTowards(speed, Mathf.Max(0, target), (target > speed ? 1.6f : 3f) * dt);
            travelled = Mathf.Min(end, travelled + speed * dt);
            Pose(travelled, 1 - Mathf.Exp(-4 * dt));
            if (remaining <= .05f && speed < .05f) driving = false;
        }
        void Pose(float d, float turnBlend)
        {
            var truck = mission.transform;
            Vector3 p = Sample(d); p.y += heightOffset;
            Vector3 ahead = Vector3.ProjectOnPlane(Sample(d + 6) - Sample(d - 2), Vector3.up);
            Quaternion rot = ahead.sqrMagnitude > .01f ? Quaternion.Slerp(truck.rotation, Quaternion.LookRotation(ahead.normalized, Vector3.up), turnBlend) : truck.rotation;
            if (body) { body.position = p; body.rotation = rot; }
            truck.SetPositionAndRotation(p, rot);
        }

        // ---------------- Dashboard ----------------
        void BuildDashboard()
        {
            if (dashboard) return;
            var truck = mission.transform;
            var presentation = FindFirstObjectByType<FireTruckPresentation>();
            font = presentation && presentation.tabletStatus ? presentation.tabletStatus.font : TMP_Settings.defaultFontAsset;

            var go = new GameObject("MissionDebriefDashboard", typeof(RectTransform));
            go.layer = presentation ? presentation.gameObject.layer : 0;
            dashboard = go.AddComponent<Canvas>(); dashboard.renderMode = RenderMode.WorldSpace; dashboard.sortingOrder = 50;
            go.AddComponent<CanvasScaler>().dynamicPixelsPerUnit = 3;
            var rt = (RectTransform)go.transform; rt.sizeDelta = new Vector2(1000, 560);
            go.transform.SetParent(truck, false);
            // Centered between both seats, in front of the crew's eyes.
            Vector3 eyes = truck.position + truck.up * 1.9f;
            if (mission.driverSeat && mission.passengerSeat) eyes = (StationSeatPlacement.EyeTarget(mission.driverSeat, null) + StationSeatPlacement.EyeTarget(mission.passengerSeat, null)) * .5f;
            go.transform.position = eyes + truck.forward * dashboardDistance - truck.up * dashboardDrop;
            go.transform.rotation = Quaternion.LookRotation(truck.forward, truck.up);
            float scale = dashboardWidth / 1000f; go.transform.localScale = Vector3.one * scale / Mathf.Max(.0001f, truck.lossyScale.x);

            Box(rt, "Panel", 0, 0, 1000, 560, Panel);
            Box(rt, "TopStripe", 0, 0, 1000, 8, Accent);
            Label(rt, "MISSION DEBRIEF", 32, 22, 600, 44, 34, Text, FontStyles.Bold);
            Label(rt, $"{record.incident.ToUpperInvariant()}{(record.location.Length > 0 ? " · " + record.location : "")}", 32, 64, 600, 30, 20, Muted);
            Label(rt, "CIVIL DEFENCE · OPERATIONS", 600, 26, 368, 30, 18, Amber, FontStyles.Bold, TextAlignmentOptions.TopRight);
            Label(rt, System.DateTime.Now.ToString("dd MMM yyyy · HH:mm"), 600, 58, 368, 30, 18, Muted, FontStyles.Normal, TextAlignmentOptions.TopRight);

            // Crew cards
            CrewCard(rt, 32, 110, "DRIVER · PUMP", record.driver);
            CrewCard(rt, 262, 110, "ASSISTANT · NOZZLE", record.assistant);

            // Total time
            Box(rt, "TimeCard", 32, 214, 450, 220, Card);
            Box(rt, "TimeAccent", 32, 214, 6, 220, Accent);
            Label(rt, "TEAM TOTAL TIME", 56, 230, 400, 28, 18, Muted, FontStyles.Bold);
            Label(rt, MissionRunRecord.Clock(record.totalSeconds), 56, 256, 420, 96, 84, Text, FontStyles.Bold);
            string reason = record.redLights > 0
                ? $"<color=#A0A8B4>Mission {MissionRunRecord.Clock(record.missionSeconds)}</color>   <color=#FFB347>+{record.penaltySeconds:0} s</color>\n<color=#FFB347>{record.redLights} red light{(record.redLights > 1 ? "s" : "")} without siren</color>"
                : $"<color=#A0A8B4>Mission {MissionRunRecord.Clock(record.missionSeconds)}</color>\n<color=#78E7B9>No penalties</color>";
            Label(rt, reason, 56, 356, 420, 70, 22, Text);

            // Previous crews
            Box(rt, "PrevCard", 506, 110, 462, 324, Card);
            Label(rt, "PREVIOUS CREWS", 526, 124, 420, 28, 18, Muted, FontStyles.Bold);
            previousText = Label(rt, "Loading…", 526, 156, 426, 270, 21, Text);
            previousText.lineSpacing = 18;

            Box(rt, "FooterLine", 32, 456, 936, 2, Line);
            footerText = Label(rt, "", 32, 470, 936, 60, 18, Muted);
            RefreshPrevious(); RefreshFooter();
        }
        void CrewCard(RectTransform parent, float x, float y, string role, string name)
        {
            Box(parent, role, x, y, 214, 88, Card);
            Label(parent, role, x + 16, y + 12, 190, 24, 15, Amber, FontStyles.Bold);
            var n = Label(parent, name, x + 16, y + 38, 190, 44, 28, Text, FontStyles.Bold);
            n.enableAutoSizing = true; n.fontSizeMin = 16; n.fontSizeMax = 28;
        }
        void RefreshPrevious()
        {
            if (!previousText) return;
            if (previous == null) { previousText.text = "<color=#A0A8B4>Loading…</color>"; return; }
            if (previous.Count == 0) { previousText.text = "<color=#A0A8B4>You are the first crew on record.</color>"; return; }
            var sb = new System.Text.StringBuilder();
            foreach (var run in previous)
            {
                string when = System.DateTimeOffset.FromUnixTimeMilliseconds(run.ts).ToLocalTime().ToString("dd MMM");
                sb.Append($"<b>{Short(run.driver)} & {Short(run.assistant)}</b>  <color=#A0A8B4>{when}</color>\n");
                sb.Append($"<color=#78E7B9>{MissionRunRecord.Clock(run.totalSeconds)}</color>{(run.redLights > 0 ? $"  <color=#FFB347>+{run.penaltySeconds:0}s</color>" : "")}\n");
            }
            previousText.text = sb.ToString();
            RefreshFooter();
        }
        void RefreshFooter()
        {
            if (!footerText) return;
            string save = !uploadDone ? "Saving results…" : uploadStatus;
            string source = previous == null ? "" : previousOnline ? " · Previous crews: online records" : " · Previous crews: this headset";
            footerText.text = save + source + "\n<color=#78E7B9>Hose stowed · pump secured · well done, crew.</color>";
        }
        static string Short(string s) { s ??= ""; return s.Length > 12 ? s.Substring(0, 11) + "…" : s; }

        static Image Box(RectTransform parent, string name, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image));
            go.layer = parent.gameObject.layer;
            var r = (RectTransform)go.transform; r.SetParent(parent, false); Place(r, x, y, w, h);
            var img = go.GetComponent<Image>(); img.color = color; img.raycastTarget = false;
            return img;
        }
        TMP_Text Label(RectTransform parent, string text, float x, float y, float w, float h, float size, Color color, FontStyles style = FontStyles.Normal, TextAlignmentOptions align = TextAlignmentOptions.TopLeft)
        {
            var go = new GameObject("Text", typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var r = (RectTransform)go.transform; r.SetParent(parent, false); Place(r, x, y, w, h);
            var t = go.AddComponent<TextMeshProUGUI>();
            if (font) t.font = font;
            t.text = text; t.fontSize = size; t.color = color; t.fontStyle = style; t.alignment = align; t.raycastTarget = false;
            t.textWrappingMode = TextWrappingModes.Normal; t.overflowMode = TextOverflowModes.Ellipsis; t.richText = true;
            return t;
        }
        // Top-left based layout inside a 1000 x 560 canvas.
        static void Place(RectTransform r, float x, float y, float w, float h)
        {
            r.anchorMin = r.anchorMax = new Vector2(0, 1); r.pivot = new Vector2(0, 1);
            r.anchoredPosition = new Vector2(x, -y); r.sizeDelta = new Vector2(w, h);
        }
        static Color Hex(string hex) { ColorUtility.TryParseHtmlString(hex, out var c); return c; }
    }
}
