using System.Collections;
using CityNightPresentation.Mission;
using FirefighterVR.Station;
using TMPro;
using Unity.Netcode;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace CityNightPresentation.Fireground
{
    // Extends the EXISTING city mission NetworkObject (MISSION_FIRE_TRUCK). No new NetworkObject,
    // NetworkManager or player spawner. The session owner (DA authority) owns the fireground state;
    // peers send validated requests. Solo/offline runs the same rules locally.
    [DefaultExecutionOrder(-90)]
    public sealed class FiregroundOperation : NetworkBehaviour
    {
        public static FiregroundOperation Instance { get; private set; }

        [Header("Existing mission")]
        public FireTruckMission mission;
        public FireTruckSeating seating;
        public Rigidbody truckBody;

        [Header("Arrival / dismount")]
        [Min(1)] public float arrivalHoldSeconds = 5;
        public Transform pumpOperatorSpawn, nozzleOperatorSpawn;
        public Collider[] walkableSurfaces = new Collider[0];
        public AudioSource doorAudio;
        public AudioClip doorClose;
        [Min(.2f)] public float fadeOutSeconds = 1.2f, fadeInSeconds = 1f;

        [Header("Hose")]
        public HoseReel reel;
        public HoseNozzle nozzle;
        public Transform hoseOutlet, nozzleDock;
        [Min(5)] public float maxPayout = 30;
        [Min(.5f)] public float payoutPerTurn = 2.5f;
        [Min(.1f)] public float dockRadius = .45f;

        [Header("Pump panel")]
        public Renderer pumpLamp;
        public Material lampOn, lampOff;
        public Transform gaugeNeedle;
        public TMP_Text panelText;
        public AudioSource pumpAudio;

        [Header("Fire")]
        public FireZone[] zones = new FireZone[0];
        [Min(5)] public float streamSpeed = 21;
        [Min(1)] public float extinguishSeconds = 8;
        public float regrowDelay = 4, regrowPerSecond = .025f;
        public LayerMask streamObstacles = 1;

        public readonly NetworkVariable<FiregroundState> State = new NetworkVariable<FiregroundState>(default, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

        FiregroundState local;
        float[] intensity = new float[FiregroundState.MaxZones];
        double[] lastHit = new double[FiregroundState.MaxZones];
        bool[] zoneHit = new bool[FiregroundState.MaxZones];
        readonly Vector3[] stream = new Vector3[30];
        readonly RaycastHit[] groundHits = new RaycastHit[8];
        int streamCount;
        bool wasAuthority, dirty, dismounting;
        double nextPublish;
        float fireAccumulator, nextPanelText, needleAngle;
        FiregroundPhase announcedPhase;

        public float StowedPayout { get; private set; } = 1.2f;
        public bool LocalDismounted { get; private set; }

        StationSessionFlow Flow => StationSessionFlow.Instance;
        bool Solo => Flow && Flow.SoloMissionActive;
        bool UseNetwork => IsSpawned && !Solo;
        public bool Authority => !mission || mission.Authority;
        public FiregroundState Current => UseNetwork && !Authority ? State.Value : local;
        public double Now => Flow && Flow.MissionSessionActive ? Flow.SharedTime : Time.unscaledTimeAsDouble;
        public ulong LocalId => UseNetwork ? NetworkManager.LocalClientId : 0UL;
        public StationRole LocalRole => mission ? mission.LocalRole : StationRole.None;
        bool HandsOn(FiregroundState s) => s.phase == FiregroundPhase.Operation || s.phase == FiregroundPhase.Recovery;
        public bool HandsOnPhase => HandsOn(Current);
        // Driver = pump/reel operator, Assistant = nozzle. Offline without a bot partner the single
        // human may do both; with the Solo bot partner each keeps its own role.
        public const ulong BotId = ulong.MaxValue - 1;
        public bool BotPartnerActive { get; set; }
        public string BotStatus { get; set; } = "";
        bool RoleRestricted => UseNetwork || BotPartnerActive;
        public bool LocalMayOperatePump => !RoleRestricted || LocalRole == StationRole.Driver;
        public bool LocalMayHoldNozzle => !RoleRestricted || LocalRole == StationRole.Assistant;
        // Solo bot partner: same validated rules as a networked peer, applied locally.
        bool BotAllowed => Solo && Authority;
        public void BotCrank(float radians) { if (BotAllowed && float.IsFinite(radians)) ApplyCrank(Mathf.Clamp(radians, -Mathf.PI, Mathf.PI)); }
        public void BotPump(bool on) { if (BotAllowed) ApplyPump(on); }
        public bool BotGrab() { if (BotAllowed) ApplyGrab(BotId); return local.nozzleHolder == BotId; }
        public void BotRelease(Vector3 p, Quaternion q) { if (BotAllowed && Finite(p) && Finite(q)) ApplyRelease(BotId, p, q); }
        public void BotTrigger(bool on) { if (BotAllowed) ApplyTrigger(BotId, on); }
        public bool CanLocalGrabNozzle { get { var s = Current; return LocalMayHoldNozzle && HandsOn(s) && (!s.Held || s.nozzleHolder == LocalId); } }
        public bool LocalHoldsNozzle { get { var s = Current; return s.Held && s.nozzleHolder == LocalId; } }
        public bool IsZoneHit(int i) => i >= 0 && i < zoneHit.Length && zoneHit[i];
        public float SecondsRemaining => Mathf.Max(0, (float)(Current.deadline - Now));

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)] static void ResetStatic() => Instance = null;

        void Awake()
        {
            Instance = this;
            MissionDebrief.Attach(this);
            if (hoseOutlet && nozzle && nozzleDock) StowedPayout = Vector3.Distance(hoseOutlet.position, nozzleDock.TransformPoint(nozzle.HoseAttachLocal)) + .35f;
            local = InitialState();
            for (int i = 0; i < intensity.Length; i++) intensity[i] = i < local.zoneCount ? 1 : 0;
        }
        FiregroundState InitialState()
        {
            var s = new FiregroundState { phase = FiregroundPhase.Inactive, payout = StowedPayout, nozzleDocked = true, nozzleHolder = FiregroundState.NoHolder, zoneCount = (byte)Mathf.Min(zones.Length, FiregroundState.MaxZones), nozzleRotation = Quaternion.identity };
            for (int i = 0; i < s.zoneCount; i++) s.SetIntensity(i, 1);
            if (nozzleDock) { s.nozzlePosition = nozzleDock.position; s.nozzleRotation = nozzleDock.rotation; }
            return s;
        }
        public override void OnNetworkSpawn()
        {
            if (Authority && UseNetwork) { local.revision = State.Value.revision + 1; State.Value = local; }
            wasAuthority = Authority && UseNetwork;
        }
        public override void OnDestroy() { if (Instance == this) Instance = null; base.OnDestroy(); }

        void Update()
        {
            bool networkAuthority = Authority && UseNetwork;
            if (networkAuthority && !wasAuthority)
            {
                // Session-owner migration: continue from the replicated snapshot.
                local = State.Value; if (local.zoneCount == 0) local = InitialState();
                for (int i = 0; i < local.zoneCount; i++) intensity[i] = local.Intensity(i);
            }
            wasAuthority = networkAuthority;
            if (Authority) AuthorityTick();
            if (dirty && Time.unscaledTimeAsDouble >= nextPublish) Publish(true);
            var s = Current;
            if (!LocalHoldsNozzle) localPullTarget = 0;
            if (!Authority) { fireAccumulator += Time.deltaTime; if (fireAccumulator >= .1f) { fireAccumulator = 0; EvaluateStream(s); } }
            UpdateCrew(s);
            UpdatePresentation(s);
        }

        // ---------------- Authority ----------------
        void AuthorityTick()
        {
            var now = Now; var s = local; bool changed = false;
            switch (s.phase)
            {
                case FiregroundPhase.Inactive:
                    if (mission && mission.HasArrived && !mission.HasFailed) { s.phase = FiregroundPhase.ArrivalHold; s.deadline = now + arrivalHoldSeconds; changed = true; }
                    break;
                case FiregroundPhase.ArrivalHold:
                    if (now >= s.deadline) { s.phase = FiregroundPhase.Dismount; s.deadline = now + 15; s.dismountAcks = 0; changed = true; }
                    break;
                case FiregroundPhase.Dismount:
                    byte required = (byte)(UseNetwork ? 3 : 1);
                    if ((s.dismountAcks & required) == required || now >= s.deadline) { s.phase = FiregroundPhase.Operation; changed = true; }
                    break;
                case FiregroundPhase.Recovery:
                    if (!s.pumpOn && s.nozzleDocked && !s.Held && s.payout <= StowedPayout + .08f) { s.phase = FiregroundPhase.Complete; s.completedAt = now; changed = true; }
                    break;
            }
            if (s.phase >= FiregroundPhase.Dismount && truckBody && !truckBody.isKinematic)
            { truckBody.linearVelocity = Vector3.zero; truckBody.angularVelocity = Vector3.zero; truckBody.isKinematic = true; }
            local = s;
            fireAccumulator += Time.deltaTime;
            if (fireAccumulator >= .1f) { float dt = fireAccumulator; fireAccumulator = 0; FireTick(dt); }
            if (changed) Publish(true);
        }
        bool FireTick(float dt)
        {
            var s = local; EvaluateStream(s);
            if (s.phase != FiregroundPhase.Operation) return false;
            bool changed = false; var now = Now;
            for (int i = 0; i < s.zoneCount; i++)
            {
                float value = intensity[i]; if (value <= 0) continue;
                float weight = zoneHitWeight[i];
                if (weight > 0) { value = Mathf.Max(0, value - dt / extinguishSeconds * weight); lastHit[i] = now; }
                else if (now - lastHit[i] > regrowDelay && value < 1) value = Mathf.Min(1, value + regrowPerSecond * dt);
                intensity[i] = value;
                byte before = (byte)Mathf.RoundToInt(s.Intensity(i) * 255); s.SetIntensity(i, value);
                if ((byte)Mathf.RoundToInt(s.Intensity(i) * 255) != before) changed = true;
            }
            if (s.AllExtinguished) { s.phase = FiregroundPhase.Recovery; local = s; Publish(true); return true; }
            local = s; if (changed) Publish(false);
            return changed;
        }
        readonly float[] zoneHitWeight = new float[FiregroundState.MaxZones];
        void EvaluateStream(FiregroundState s)
        {
            streamCount = 0;
            bool spraying = nozzle && nozzle.muzzle && (s.Spraying || (nozzle.LocallyHeld && s.pumpOn && nozzle.LocalTrigger));
            if (spraying) streamCount = Trajectory(nozzle.muzzle.position, nozzle.muzzle.forward, streamSpeed, stream, streamObstacles);
            for (int i = 0; i < zoneHitWeight.Length; i++)
            {
                zoneHitWeight[i] = i < zones.Length && zones[i] && streamCount > 1 ? zones[i].HitWeight(stream, streamCount) : 0;
                zoneHit[i] = zoneHitWeight[i] > 0;
            }
        }
        // Deterministic ballistic water path shared by authority hits and local visuals.
        public static int Trajectory(Vector3 origin, Vector3 direction, float speed, Vector3[] points, LayerMask obstacles, float step = .06f)
        {
            Vector3 p = origin, v = direction.normalized * speed; points[0] = p; int n = 1;
            for (; n < points.Length; n++)
            {
                v += Physics.gravity * step; v *= 1 - .16f * step;
                Vector3 next = p + v * step;
                if (n > 2 && Physics.Linecast(p, next, out var hit, obstacles, QueryTriggerInteraction.Ignore) && !(hit.collider is CharacterController))
                { points[n++] = hit.point; break; }
                points[n] = p = next;
            }
            return n;
        }
        void Publish(bool immediate)
        {
            if (!UseNetwork) { local.revision++; dirty = false; return; }
            if (!Authority) return;
            if (!immediate && Time.unscaledTimeAsDouble < nextPublish) { dirty = true; return; }
            local.revision++; State.Value = local; dirty = false; nextPublish = Time.unscaledTimeAsDouble + .1;
        }
        bool Permit(ulong sender, StationRole role) => Flow && Flow.IsSpawned && Flow.Lobby.Value.RoleOf(sender) == role && NetworkManager.ConnectedClients.ContainsKey(sender);
        static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
        static bool Finite(Quaternion q) => float.IsFinite(q.x) && float.IsFinite(q.y) && float.IsFinite(q.z) && float.IsFinite(q.w) && q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w > .5f;

        // ---------------- Requests (local -> authority) ----------------
        public void PumpButtonOn() => RequestPump(true);
        public void PumpButtonOff() => RequestPump(false);
        public void RequestPump(bool on)
        {
            if (!LocalMayOperatePump) { Notify("The pump panel is operated by the Driver / Pump Operator."); return; }
            if (!HandsOnPhase) return;
            if (!UseNetwork) ApplyPump(on); else PumpRpc(on);
        }
        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void PumpRpc(bool on, RpcParams p = default) { if (Authority && Permit(p.Receive.SenderClientId, StationRole.Driver)) ApplyPump(on); }
        void ApplyPump(bool on) { var s = local; if (!HandsOn(s) || s.pumpOn == on) return; s.pumpOn = on; local = s; Publish(true); }

        public void RequestCrank(float radians)
        {
            if (!LocalMayOperatePump || !HandsOnPhase || !float.IsFinite(radians)) return;
            radians = Mathf.Clamp(radians, -Mathf.PI, Mathf.PI);
            if (!UseNetwork) ApplyCrank(radians); else CrankRpc(radians);
        }
        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void CrankRpc(float radians, RpcParams p = default)
        { if (Authority && float.IsFinite(radians) && Permit(p.Receive.SenderClientId, StationRole.Driver)) ApplyCrank(Mathf.Clamp(radians, -Mathf.PI, Mathf.PI)); }
        // The reel only turns the way the job needs: while deploying (Operation) it pays hose out, during
        // recovery it only rewinds. The wrong way is felt as resistance on the controller (HoseReel).
        public static int CrankDirectionFor(FiregroundPhase phase) => phase == FiregroundPhase.Operation ? 1 : phase == FiregroundPhase.Recovery ? -1 : 0;
        public int AllowedCrankDirection => CrankDirectionFor(Current.phase);
        // True when the reel cannot move any further in this direction (empty drum / fully rewound / taut line).
        public bool CrankBlocked(int direction)
        {
            var s = Current; int allowed = CrankDirectionFor(s.phase);
            if (direction == 0 || allowed == 0 || direction != allowed) return true;
            if (direction > 0) return s.payout >= maxPayout - .01f;
            float minimum = s.nozzleDocked && !s.Held ? StowedPayout : NozzleDistance() + (s.Held ? .8f : .3f);
            return s.payout <= minimum + .01f;
        }
        void ApplyCrank(float radians)
        {
            var s = local; if (!HandsOn(s)) return;
            float delta = radians / (2 * Mathf.PI) * payoutPerTurn, target = s.payout;
            int allowed = CrankDirectionFor(s.phase); if (allowed != 0 && delta * allowed < 0) return;
            if (delta > 0) target = Mathf.Min(maxPayout, s.payout + delta);
            else
            {
                // Never reel against the held nozzle or past the taut line; full stowage needs the docked nozzle.
                float minimum = s.nozzleDocked && !s.Held ? StowedPayout : NozzleDistance() + (s.Held ? .8f : .3f);
                target = Mathf.Min(s.payout, Mathf.Max(s.payout + delta, minimum));
            }
            if (Mathf.Abs(target - s.payout) < .0005f) return;
            s.payout = target; local = s; Publish(false);
        }
        public float NozzleDistance() => hoseOutlet && nozzle && nozzle.hoseAttach ? Vector3.Distance(hoseOutlet.position, nozzle.hoseAttach.position) : 0;

        // Pull-out: the nozzle operator walking away unspools the reel (validated against the authority's
        // view of the nozzle), so the hose holder can always advance; cranking still pays out and rewinds.
        float localPullTarget; float nextPull;
        public float PredictedPayout => Mathf.Max(Current.payout, LocalHoldsNozzle ? localPullTarget : 0);
        public void RequestPullOut(float target)
        {
            if (!float.IsFinite(target)) return; target = Mathf.Min(target, maxPayout);
            if (target <= Current.payout + .02f) return; localPullTarget = Mathf.Max(localPullTarget, target);
            if (Time.unscaledTime < nextPull) return; nextPull = Time.unscaledTime + .1f;
            if (!UseNetwork) ApplyPull(LocalId, localPullTarget); else PullRpc(localPullTarget);
        }
        public void BotPull(float target) { if (BotAllowed && float.IsFinite(target)) ApplyPull(BotId, target); }
        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void PullRpc(float target, RpcParams p = default) { if (Authority && float.IsFinite(target) && Permit(p.Receive.SenderClientId, StationRole.Assistant)) ApplyPull(p.Receive.SenderClientId, target); }
        void ApplyPull(ulong sender, float target)
        {
            var s = local; if (!HandsOn(s) || s.nozzleHolder != sender) return;
            target = Mathf.Min(target, maxPayout, Mathf.Max(NozzleDistance() + 1.2f, s.payout));
            if (target <= s.payout + .005f) return; s.payout = target; local = s; Publish(false);
        }
        public void NozzleGrabbed()
        { if (!UseNetwork) ApplyGrab(LocalId); else GrabRpc(); }
        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void GrabRpc(RpcParams p = default) { if (Authority && Permit(p.Receive.SenderClientId, StationRole.Assistant)) ApplyGrab(p.Receive.SenderClientId); }
        void ApplyGrab(ulong sender)
        {
            var s = local; if (!HandsOn(s) || (s.Held && s.nozzleHolder != sender)) return;
            s.nozzleHolder = sender; s.nozzleDocked = false; s.trigger = false; local = s; Publish(true);
        }
        public void NozzleReleased(Vector3 position, Quaternion rotation)
        { if (!Finite(position) || !Finite(rotation)) return; if (!UseNetwork) ApplyRelease(LocalId, position, rotation); else ReleaseRpc(position, rotation); }
        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void ReleaseRpc(Vector3 position, Quaternion rotation, RpcParams p = default)
        { if (Authority && Finite(position) && Finite(rotation)) ApplyRelease(p.Receive.SenderClientId, position, rotation); }
        void ApplyRelease(ulong sender, Vector3 position, Quaternion rotation)
        {
            var s = local; if (s.nozzleHolder != sender) return;
            s.nozzleHolder = FiregroundState.NoHolder; s.trigger = false;
            if (CanDock(position)) { s.nozzleDocked = true; s.nozzlePosition = nozzleDock.position; s.nozzleRotation = nozzleDock.rotation; }
            else
            {
                if (hoseOutlet) { var d = position - hoseOutlet.position; float limit = s.payout + .3f; if (d.magnitude > limit) position = hoseOutlet.position + d.normalized * limit; }
                s.nozzleDocked = false; s.nozzlePosition = position; s.nozzleRotation = rotation.normalized;
            }
            local = s; Publish(true);
        }
        public bool CanDock(Vector3 position) => nozzleDock && Vector3.Distance(position, nozzleDock.position) <= dockRadius;

        public void NozzleTrigger(bool pressed)
        { if (!UseNetwork) ApplyTrigger(LocalId, pressed); else TriggerRpc(pressed); }
        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void TriggerRpc(bool pressed, RpcParams p = default) { if (Authority) ApplyTrigger(p.Receive.SenderClientId, pressed); }
        void ApplyTrigger(ulong sender, bool pressed) { var s = local; if (s.nozzleHolder != sender || s.trigger == pressed) return; s.trigger = pressed; local = s; Publish(true); }

        // Held nozzle pose: unreliable, ~20 Hz, holder -> other peers. Never per rope point.
        public void SendNozzlePose(Vector3 position, Quaternion rotation) { if (UseNetwork) NozzlePoseRpc(position, rotation); }
        [Rpc(SendTo.NotMe, InvokePermission = RpcInvokePermission.Everyone, Delivery = RpcDelivery.Unreliable)]
        void NozzlePoseRpc(Vector3 position, Quaternion rotation, RpcParams p = default)
        { if (nozzle && Finite(position) && Finite(rotation) && Current.nozzleHolder == p.Receive.SenderClientId) nozzle.ReceiveRemotePose(position, rotation); }

        void AcknowledgeDismount()
        {
            if (!UseNetwork) { if (Authority) { local.dismountAcks |= 1; Publish(true); } }
            else DismountRpc(local.revision);
        }
        [Rpc(SendTo.Authority, InvokePermission = RpcInvokePermission.Everyone)]
        void DismountRpc(uint unused, RpcParams p = default)
        {
            if (!Authority || local.phase != FiregroundPhase.Dismount || !Flow || !Flow.IsSpawned) return;
            var role = Flow.Lobby.Value.RoleOf(p.Receive.SenderClientId);
            byte bit = (byte)(role == StationRole.Driver ? 1 : role == StationRole.Assistant ? 2 : 0); if (bit == 0) return;
            local.dismountAcks |= bit; Publish(true);
        }

        // ---------------- Local crew transition ----------------
        void UpdateCrew(FiregroundState s)
        {
            bool participant = LocalRole != StationRole.None || !UseNetwork;
            if (participant && s.phase >= FiregroundPhase.Dismount && !LocalDismounted && !dismounting) StartCoroutine(Dismount());
        }
        IEnumerator Dismount()
        {
            dismounting = true;
            var fade = StationViewFade.Instance; float oldOut = 0, oldIn = 0;
            if (fade) { oldOut = fade.sceneFadeOut; oldIn = fade.sceneFadeIn; fade.sceneFadeOut = fadeOutSeconds; fade.sceneFadeIn = fadeInSeconds; yield return fade.Cover(); }
            if (doorAudio && doorClose) doorAudio.PlayOneShot(doorClose);
            var rig = Flow ? Flow.localRig : null;
            var origin = rig ? rig.origin : FindFirstObjectByType<XROrigin>();
            var avatar = rig ? rig.avatar : FindFirstObjectByType<FirefighterVR.FirefighterAvatarDriver>();
            LocalDismounted = true; if (seating) seating.ReleaseForFireground();
            // Clear every seated/transition lock left from the station truck and the drive, so the movement
            // policy applies again: Firefighter (nozzle) moves freely, Driver (pump/reel) stays in place.
            if (rig) { rig.StationSeated = false; rig.SeatTransition = false; rig.VehicleSeated = false; }
            yield return null;
            var spawn = LocalRole == StationRole.Assistant ? nozzleOperatorSpawn : pumpOperatorSpawn;
            if (origin && spawn)
            {
                Vector3 ground = GroundBelow(spawn.position);
                float timeout = Time.unscaledTime + 8; int stable = 0; bool placed = false;
                while (stable < 3 && Time.unscaledTime < timeout)
                {
                    if (StationSpawnPlacement.TrackingReady(origin))
                    {
                        if (!placed || Vector3.Distance(StationSpawnPlacement.GroundPosition(origin), ground) > .06f) { StationSpawnPlacement.Place(origin, ground, spawn.rotation); placed = true; stable = 0; }
                        else stable++;
                    }
                    yield return null;
                }
                if (!placed) StationSpawnPlacement.Place(origin, ground, spawn.rotation);
            }
            if (avatar) { avatar.Seated = false; if (walkableSurfaces.Length > 0) avatar.walkableGroundSurfaces = walkableSurfaces; }
            AcknowledgeDismount();
            float wait = Time.unscaledTime + 16;
            while (Current.phase < FiregroundPhase.Operation && Time.unscaledTime < wait) yield return null;
            if (fade) { yield return fade.Reveal(); fade.sceneFadeOut = oldOut; fade.sceneFadeIn = oldIn; }
            dismounting = false;
        }
        public Vector3 GroundBelow(Vector3 point)
        {
            int count = Physics.RaycastNonAlloc(point + Vector3.up * 2, Vector3.down, groundHits, 6, ~0, QueryTriggerInteraction.Ignore);
            float best = float.PositiveInfinity; Vector3 result = point;
            for (int i = 0; i < count; i++)
            {
                var c = groundHits[i].collider; if (!c || c is CharacterController || (mission && c.transform.IsChildOf(mission.transform)) || c.GetComponentInParent<HoseNozzle>() || c.GetComponentInParent<FirefighterVR.StationBot.StationBotFollower>()) continue;
                if (groundHits[i].distance < best) { best = groundHits[i].distance; result = groundHits[i].point; }
            }
            return result;
        }

        // ---------------- Presentation ----------------
        void UpdatePresentation(FiregroundState s)
        {
            float dt = Time.deltaTime;
            for (int i = 0; i < zones.Length; i++) if (zones[i]) zones[i].Apply(i < s.zoneCount ? s.Intensity(i) : 0, IsZoneHit(i), dt);
            if (reel) reel.ShowPayout(s.payout, maxPayout);
            bool pressurized = s.pumpOn;
            if (pumpLamp && lampOn && lampOff) { var m = pressurized ? lampOn : lampOff; if (pumpLamp.sharedMaterial != m) pumpLamp.sharedMaterial = m; }
            float target = pressurized ? (s.Spraying ? 105 : 150) : 0;
            needleAngle = Mathf.MoveTowards(needleAngle, target, dt * 160);
            if (gaugeNeedle) gaugeNeedle.localRotation = Quaternion.Euler(0, 0, 120 - needleAngle * 240f / 200f);
            if (pumpAudio)
            {
                float v = pressurized ? (s.Spraying ? .55f : .4f) : 0;
                pumpAudio.volume = Mathf.MoveTowards(pumpAudio.volume, v, dt);
                pumpAudio.pitch = Mathf.MoveTowards(pumpAudio.pitch, s.Spraying ? .9f : 1, dt);
                if (pumpAudio.volume > .001f && !pumpAudio.isPlaying) pumpAudio.Play(); else if (pumpAudio.volume <= .001f && pumpAudio.isPlaying) pumpAudio.Stop();
            }
            if (s.phase != announcedPhase) { announcedPhase = s.phase; Announce(s); }
            if (panelText && Time.unscaledTime >= nextPanelText) { nextPanelText = Time.unscaledTime + .2f; panelText.text = PanelStatus(s); }
        }
        void Announce(FiregroundState s)
        {
            switch (s.phase)
            {
                case FiregroundPhase.ArrivalHold: Notify("ARRIVED ON SCENE · Truck secured · Crew dismount in " + Mathf.CeilToInt(arrivalHoldSeconds) + " s"); break;
                case FiregroundPhase.Operation: Notify(LocalRole == StationRole.Assistant ? "NOZZLE OPERATOR: grab the nozzle beside the reel, advance to the fire, squeeze Trigger to flow water." : "PUMP OPERATOR: turn the reel crank to pay out hose, then press WATER ON."); break;
                case FiregroundPhase.Recovery: Notify("ALL FIRES OUT · Pump Operator: WATER OFF · Firefighter: return the nozzle to its bracket · then rewind the hose."); break;
                case FiregroundPhase.Complete: Notify("OPERATION COMPLETE · Hose stowed · Pump secured"); break;
            }
        }
        public string PanelStatus(FiregroundState s) => PhaseStatus(s) + (BotPartnerActive && BotStatus.Length > 0 ? "\n<color=#7FD3FF>BOT: " + BotStatus + "</color>" : "");
        string PhaseStatus(FiregroundState s)
        {
            int burning = 0; for (int i = 0; i < s.zoneCount; i++) if (s.Intensity(i) > 0) burning++;
            string pump = s.pumpOn ? "<color=#4CFF6A>PUMP ON · LINE CHARGED</color>" : "<color=#FF5A4C>PUMP OFF</color>";
            string hose = $"HOSE {s.payout:0.0} / {maxPayout:0} m";
            switch (s.phase)
            {
                case FiregroundPhase.Inactive: return "PUMP CONTROL\nSTANDBY · EN ROUTE";
                case FiregroundPhase.ArrivalHold: return $"ARRIVED ON SCENE\nDISMOUNT IN {Mathf.CeilToInt(SecondsRemaining)}";
                case FiregroundPhase.Dismount: return "CREW DISMOUNTING";
                case FiregroundPhase.Operation: return $"{pump}\n{hose}\nFIRES BURNING {burning}/{s.zoneCount}\n{(s.Spraying ? "WATER FLOWING" : s.Held ? "NOZZLE IN HAND" : s.nozzleDocked ? "NOZZLE IN BRACKET" : "NOZZLE ON GROUND")}";
                case FiregroundPhase.Recovery:
                    return $"ALL FIRES OUT\n{Check(!s.pumpOn)} WATER OFF\n{Check(s.nozzleDocked && !s.Held)} NOZZLE IN BRACKET\n{Check(s.payout <= StowedPayout + .08f)} HOSE REWOUND ({s.payout:0.0} m)";
                default: return "<color=#4CFF6A>OPERATION COMPLETE</color>\nHOSE STOWED · PUMP SECURED";
            }
        }
        static string Check(bool done) => done ? "<color=#4CFF6A>[x]</color>" : "[ ]";
        public static void Notify(string message) { var hud = XRMultiplayer.PlayerHudNotification.Instance; if (hud) hud.ShowText(message, 6); }
    }
}
