using System.Collections.Generic;
using FirefighterVR.Station;
using Unity.XR.CoreUtils;
using UnityEngine;

namespace CityNightPresentation.Fireground
{
    // Local, per-player feedback at the fireground (added at runtime by FiregroundOperation):
    //  - Heat pain: walking close to a burning fire plays a pain / cough sound and tints the view red;
    //    stepping back fades both out.
    //  - Head guard: the player's head cannot enter houses, trees, walls or the truck — when the real head
    //    walks into one, the rig is pushed back out (horizontal only).
    [DefaultExecutionOrder(250)]
    public sealed class FiregroundCrewFeedback : MonoBehaviour
    {
        public FiregroundOperation op;
        public AudioClip painClip;
        [Tooltip("Pain starts this far (m) outside a fire's burn radius and is full at the burn edge.")]
        public float painReach = 1.6f;
        [Range(0, 1)] public float maxRedAlpha = .45f, painVolume = .8f;
        public float headRadius = .16f;
        public LayerMask headBlockers = 1; // Default

        AudioSource painAudio; Renderer redOverlay; MaterialPropertyBlock block; float pain;
        SphereCollider probe; readonly Collider[] overlaps = new Collider[16];

        public static FiregroundCrewFeedback Attach(FiregroundOperation operation)
        {
            var f = operation.GetComponent<FiregroundCrewFeedback>(); if (!f) f = operation.gameObject.AddComponent<FiregroundCrewFeedback>();
            f.op = operation; if (!f.painClip) f.painClip = Resources.Load<AudioClip>("FirePain"); return f;
        }

        void Update()
        {
            var flow = StationSessionFlow.Instance; var rig = flow ? flow.localRig : null; var origin = rig ? rig.origin : null;
            bool active = op && origin && origin.Camera && op.LocalDismounted && !MissionDebrief.CrewSeated;
            UpdatePain(active ? origin.Camera.transform : null);
            if (active) GuardHead(origin);
        }

        // ---------------- Heat pain ----------------
        void UpdatePain(Transform cam)
        {
            float target = 0;
            if (cam)
            {
                var s = op.Current;
                for (int i = 0; i < op.zones.Length && i < s.zoneCount; i++)
                {
                    var z = op.zones[i]; float intensity = s.Intensity(i); if (!z || intensity <= .02f) continue;
                    var d = cam.position - z.Center; d.y = 0;
                    float t = Mathf.Clamp01(1 - (d.magnitude - z.radius) / Mathf.Max(.1f, painReach));
                    target = Mathf.Max(target, t * Mathf.Clamp01(intensity * 1.3f));
                }
            }
            pain = Mathf.MoveTowards(pain, target, Time.deltaTime * (target > pain ? 2.5f : 1.2f));
            if (pain > .001f && cam) EnsurePresentation(cam);
            if (painAudio)
            {
                painAudio.volume = painVolume * Mathf.SmoothStep(0, 1, pain);
                if (painAudio.volume > .01f && !painAudio.isPlaying) painAudio.Play(); else if (painAudio.volume <= .01f && painAudio.isPlaying) painAudio.Stop();
            }
            if (redOverlay)
            {
                bool show = pain > .01f; if (redOverlay.gameObject.activeSelf != show) redOverlay.gameObject.SetActive(show);
                if (show) { float pulse = .85f + .15f * Mathf.Sin(Time.time * 6); block.SetColor("_BaseColor", new Color(.75f, .02f, 0, maxRedAlpha * pain * pulse)); redOverlay.SetPropertyBlock(block); }
            }
        }
        void EnsurePresentation(Transform cam)
        {
            if (!painAudio && painClip)
            {
                var g = new GameObject("FirePainAudio"); g.transform.SetParent(cam, false);
                painAudio = g.AddComponent<AudioSource>(); painAudio.clip = painClip; painAudio.loop = true; painAudio.playOnAwake = false; painAudio.spatialBlend = 0; painAudio.volume = 0;
            }
            if (!redOverlay)
            {
                var fade = StationViewFade.Instance; var mat = fade ? fade.fadeMaterial : null; if (!mat) return;
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Destroy(q.GetComponent<Collider>()); q.name = "FirePainOverlay";
                q.transform.SetParent(cam, false); var c = cam.GetComponent<Camera>();
                float z = Mathf.Max(.06f, (c ? c.nearClipPlane : .05f) + .02f); q.transform.localPosition = new Vector3(0, 0, z); q.transform.localScale = Vector3.one * z * 6;
                redOverlay = q.GetComponent<Renderer>(); redOverlay.sharedMaterial = mat; redOverlay.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; redOverlay.receiveShadows = false;
                redOverlay.sortingOrder = 30000; block = new MaterialPropertyBlock(); q.SetActive(false);
            }
        }

        // ---------------- Head guard ----------------
        void GuardHead(XROrigin origin)
        {
            if (!probe) { var g = new GameObject("HeadGuardProbe"); g.hideFlags = HideFlags.HideAndDontSave; probe = g.AddComponent<SphereCollider>(); probe.isTrigger = true; probe.radius = headRadius; g.transform.position = new Vector3(0, -9999, 0); }
            var head = origin.Camera.transform.position;
            int n = Physics.OverlapSphereNonAlloc(head, headRadius, overlaps, headBlockers, QueryTriggerInteraction.Ignore);
            Vector3 push = Vector3.zero;
            for (int i = 0; i < n; i++)
            {
                var c = overlaps[i]; if (!c || c == probe || c is CharacterController || c.transform.IsChildOf(origin.transform) || c.GetComponentInParent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRBaseInteractable>()) continue;
                if (op.nozzle && c.transform.IsChildOf(op.nozzle.transform)) continue;
                if (Physics.ComputePenetration(probe, head, Quaternion.identity, c, c.transform.position, c.transform.rotation, out var dir, out float dist))
                { dir.y = 0; push += dir.normalized * dist; }
            }
            if (push.sqrMagnitude < 1e-6f) return;
            var cc = origin.GetComponent<CharacterController>(); bool was = cc && cc.enabled; if (cc) cc.enabled = false;
            origin.Origin.transform.position += Vector3.ClampMagnitude(push, .25f); Physics.SyncTransforms();
            if (cc) cc.enabled = was;
        }
        void OnDisable() { if (painAudio) painAudio.Stop(); if (redOverlay) redOverlay.gameObject.SetActive(false); pain = 0; }
        void OnDestroy() { if (probe) Destroy(probe.gameObject); if (painAudio) Destroy(painAudio.gameObject); if (redOverlay) Destroy(redOverlay.gameObject); }
    }
}
