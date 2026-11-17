using System.Collections.Generic;
using System.IO;
using System.Linq;
using CityNightPresentation.Mission;
using FirefighterVR;
using FirefighterVR.Station;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityNightPresentation.Fireground.EditorTools
{
    // Seat calibration (SeatPose), cab hardware (ceiling siren handle, emergency lights) and the
    // crew-guide materials. Measures the real seat meshes; nothing is guessed per seat.
    public static class CrewPresentationInstaller
    {
        [MenuItem("Tools/Fireground/Calibrate Seats In Open Scenes")]
        public static string CalibrateOpenScenes()
        {
            var log = new System.Text.StringBuilder(); EnsureGuideMaterials();
            var lobby = Object.FindFirstObjectByType<SeatedDispatchLobby>();
            if (lobby) { Calibrate(lobby.leftSeat, 1, log); Calibrate(lobby.rightSeat, 2, log); EditorSceneManager.MarkSceneDirty(lobby.gameObject.scene); }
            var view = Object.FindFirstObjectByType<StationMissionView>();
            if (view && view.dispatchSeat) NormalizeChair(view.dispatchSeat, log);
            if (view) { Calibrate(view.dispatchSeat, 3, log); Calibrate(view.driverSeat, 4, log); Calibrate(view.passengerSeat, 5, log); EditorSceneManager.MarkSceneDirty(view.gameObject.scene); }
            var mission = Object.FindFirstObjectByType<FireTruckMission>();
            if (mission) { Calibrate(mission.driverSeat, 6, log); Calibrate(mission.passengerSeat, 7, log); EditorSceneManager.MarkSceneDirty(mission.gameObject.scene); }
            EditorSceneManager.SaveOpenScenes(); return log.ToString();
        }

        public static void ApplyCity(Transform truck, System.Text.StringBuilder log)
        {
            EnsureGuideMaterials();
            var mission = truck.GetComponent<FireTruckMission>();
            Calibrate(mission.driverSeat, 6, log); Calibrate(mission.passengerSeat, 7, log);
            var e = truck.GetComponent<FireTruckEmergency>();
            if (e && e.pullMount)
            {
                // Overhead pull handle: mounted under the cab roof above-front of the Driver, pulled DOWN.
                float ceiling = Probe(truck, truck.TransformPoint(new Vector3(-.42f, 2.2f, 2.45f)), truck.up, 1.2f, out bool hit);
                float y = hit ? 2.2f + ceiling - .03f : 2.62f;
                e.pullMount.position = truck.TransformPoint(new Vector3(-.42f, y, 2.45f));
                e.pullMount.rotation = truck.rotation * Quaternion.Euler(-90, 0, 0);
                e.closedLocalPosition = new Vector3(0, 0, -.1f); e.pullAxis = Vector3.back; e.pullTravel = .15f;
                if (e.pullHandle) e.pullHandle.localPosition = e.closedLocalPosition;
                log.AppendLine("siren handle on ceiling y=" + y.ToString("F2"));
                var bar = truck.Find("Emergency_Lightbar");
                if (bar)
                {
                    foreach (var old in bar.GetComponentsInChildren<Light>(true).Where(l => l.name.StartsWith("Emergency_")).ToArray()) Object.DestroyImmediate(old.gameObject);
                    Light L(string n, Vector3 lp, Color c) { var g = new GameObject(n); g.transform.SetParent(bar, false); g.transform.localPosition = lp; var l = g.AddComponent<Light>(); l.type = LightType.Point; l.color = c; l.range = 16; l.intensity = 9; l.shadows = LightShadows.None; l.enabled = false; return l; }
                    e.redLights = new[] { L("Emergency_RedFront", new Vector3(-.55f, .1f, .35f), new Color(1, .08f, .05f)), L("Emergency_RedRear", new Vector3(-.4f, .1f, -6.5f), new Color(1, .08f, .05f)) };
                    e.blueLights = new[] { L("Emergency_BlueFront", new Vector3(.55f, .1f, .35f), new Color(.1f, .3f, 1)), L("Emergency_BlueRear", new Vector3(.4f, .1f, -6.5f), new Color(.1f, .3f, 1)) };
                }
                EditorUtility.SetDirty(e);
            }
        }

        // The dispatch office chair was scaled ~1.34x (1.71 m tall) which leaves legs dangling; restore a normal chair size.
        static void NormalizeChair(Transform seat, System.Text.StringBuilder log)
        {
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            {
                if (r.gameObject.scene != seat.gameObject.scene || !r.name.StartsWith("OfficeChair")) continue;
                var b = r.bounds; if (seat.position.x < b.min.x || seat.position.x > b.max.x || seat.position.z < b.min.z || seat.position.z > b.max.z) continue;
                var root = r.transform; while (root.parent && root.parent.name.StartsWith("OfficeChair")) root = root.parent;
                if (b.size.y > 1.5f) { root.localScale *= 1.3f / b.size.y; log.AppendLine("dispatch chair normalized to 1.30 m (was " + b.size.y.ToString("F2") + ")"); EditorUtility.SetDirty(root); }
                return;
            }
        }
        static float Probe(Transform root, Vector3 origin, Vector3 dir, float max, out bool hit)
        {
            var cols = Temp(root.GetComponentsInChildren<MeshFilter>()); float best = max; hit = false;
            foreach (var c in cols) if (c.Raycast(new Ray(origin, dir), out var h, max) && h.distance < best) { best = h.distance; hit = true; }
            foreach (var c in cols) Object.DestroyImmediate(c.gameObject); return best;
        }
        static List<MeshCollider> Temp(IEnumerable<MeshFilter> filters)
        {
            var list = new List<MeshCollider>();
            foreach (var mf in filters)
            {
                if (!mf || !mf.sharedMesh || !mf.sharedMesh.isReadable && !Application.isEditor) continue;
                if (mf.GetComponentInParent<Canvas>() || mf.GetComponentInParent<Unity.XR.CoreUtils.XROrigin>() || mf.GetComponentInParent<FirefighterAvatarDriver>() || mf.GetComponentInParent<FirefighterVR.StationBot.StationBotFollower>()) continue;
                var g = new GameObject("tmp") { hideFlags = HideFlags.HideAndDontSave }; g.transform.SetPositionAndRotation(mf.transform.position, mf.transform.rotation); g.transform.localScale = mf.transform.lossyScale;
                var c = g.AddComponent<MeshCollider>(); c.sharedMesh = mf.sharedMesh; list.Add(c);
            }
            return list;
        }

        public static void Calibrate(Transform seat, byte id, System.Text.StringBuilder log)
        {
            if (!seat) { log.AppendLine("seat " + id + " missing"); return; }
            var near = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None).Where(m => m.gameObject.scene.isLoaded && m.GetComponent<Renderer>() && m.GetComponent<Renderer>().enabled && !m.name.EndsWith("_LOD1") && !m.name.EndsWith("_LOD2") && m.GetComponent<Renderer>().bounds.SqrDistance(seat.position) < 1.6f * 1.6f).ToList();
            // Prefer the seat's own mesh (renderers whose footprint contains the anchor) so desks/consoles are ignored.
            var own = near.Where(m => { var b = m.GetComponent<Renderer>().bounds; return seat.position.x > b.min.x - .05f && seat.position.x < b.max.x + .05f && seat.position.z > b.min.z - .05f && seat.position.z < b.max.z + .05f && b.size.y < 3 && b.size.y > .8f && b.min.y < seat.position.y + .12f && (b.size.x < 1.7f && b.size.z < 1.7f || m.name.Contains("interior")); }).ToList();
            if (own.Count > 0) { var floorMeshes = near.Where(m => m.GetComponent<Renderer>().bounds.size.y < .4f && m.GetComponent<Renderer>().bounds.size.x > 1.5f).ToList(); near = own.Concat(floorMeshes).Distinct().ToList(); }
            var cols = Temp(near);
            Vector3 up = Vector3.up, fwd = Vector3.ProjectOnPlane(seat.forward, up).normalized, a = seat.position;
            float Cast(Vector3 o, Vector3 d, float max = 3) { float best = float.PositiveInfinity; foreach (var c in cols) if (c.Raycast(new Ray(o, d), out var h, max) && h.distance < best) best = h.distance; return best; }
            var samples = new List<float>();
            foreach (var d in new[] { .12f, .17f, .22f, .27f }) { float h = Cast(a + fwd * d + up * 1.25f, Vector3.down); if (!float.IsInfinity(h)) samples.Add(a.y + 1.25f - h); }
            float cushion = samples.Count > 0 ? samples.OrderBy(x => x).ElementAt(samples.Count / 2) : a.y + .45f;
            float rel = cushion - a.y;
            if (rel < -.25f || rel > 1.2f) { log.AppendLine($"seat {id} {seat.name}: cushion not found (rel {rel:F2}), kept legacy"); foreach (var c in cols) Object.DestroyImmediate(c.gameObject); return; }
            float back = Cast(a + up * (rel + .3f) + fwd * .65f, -fwd, 1.2f); float backFwd = float.IsInfinity(back) ? -.05f : .65f - back;
            float hipFwd = backFwd + .21f, front = hipFwd + .45f;
            for (float d = hipFwd + .1f; d < 1.1f; d += .03f) { float h = Cast(a + fwd * d + up * (rel + .15f), Vector3.down, 3); if (float.IsInfinity(h) || a.y + rel + .15f - h < cushion - .15f) { front = d; break; } }
            float fh = Cast(a + fwd * (front + .12f) + up * rel, Vector3.down, 3); float floor = float.IsInfinity(fh) ? a.y : a.y + rel - fh;
            // A free-standing chair rests on the floor: its own footprint is the most reliable floor height.
            var chair = own.Where(m => !m.name.Contains("interior")).Select(m => m.GetComponent<Renderer>().bounds.min.y).DefaultIfEmpty(float.NaN).Min();
            if (!float.IsNaN(chair) && Mathf.Abs(chair - floor) > .05f) floor = chair;
            foreach (var c in cols) Object.DestroyImmediate(c.gameObject);
            var pose = seat.GetComponent<SeatPose>() ?? seat.gameObject.AddComponent<SeatPose>();
            pose.id = id;
            pose.hipLocal = seat.InverseTransformPoint(a + fwd * hipFwd + up * (rel + .09f));
            pose.floorLocalY = seat.InverseTransformPoint(new Vector3(a.x, floor, a.z)).y;
            pose.footForward = Mathf.Clamp(front + .12f - hipFwd, .38f, .75f);
            EditorUtility.SetDirty(pose);
            log.AppendLine($"seat {id} {seat.name}: cushion {cushion - floor:F2} above floor, hips {rel + .09f - (floor - a.y):F2}, back {backFwd:F2}, front {front:F2}, feet {pose.footForward:F2}");
        }

        public static void EnsureGuideMaterials()
        {
            const string dir = "Assets/FirefighterAvatar/Resources"; Directory.CreateDirectory(dir);
            var glow = Mat(dir + "/CrewGuideGlow.mat", "Universal Render Pipeline/Particles/Unlit");
            glow.SetFloat("_Surface", 1); glow.SetFloat("_Blend", 2); glow.SetOverrideTag("RenderType", "Transparent");
            glow.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); glow.SetFloat("_DstBlend", (float)BlendMode.One); glow.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); glow.SetFloat("_DstBlendAlpha", (float)BlendMode.One);
            glow.SetFloat("_ZWrite", 0); glow.SetFloat("_Cull", 0); glow.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); glow.EnableKeyword("_BLENDMODE_ADD"); glow.renderQueue = (int)RenderQueue.Transparent + 10; glow.SetColor("_BaseColor", Color.white);
            var solid = Mat(dir + "/CrewGuideSolid.mat", "Universal Render Pipeline/Unlit"); solid.SetFloat("_Cull", 0); solid.SetColor("_BaseColor", Color.white);
            EditorUtility.SetDirty(glow); EditorUtility.SetDirty(solid); AssetDatabase.SaveAssets();
        }
        static Material Mat(string path, string shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (m) { m.shader = Shader.Find(shader); return m; }
            m = new Material(Shader.Find(shader)); AssetDatabase.CreateAsset(m, path); return m;
        }
    }
}
