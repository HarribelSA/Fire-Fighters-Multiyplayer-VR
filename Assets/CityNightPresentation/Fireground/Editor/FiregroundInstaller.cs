using System.Collections.Generic;
using System.IO;
using System.Linq;
using CityNightPresentation.Mission;
using TMPro;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Filtering;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Transformers;

namespace CityNightPresentation.Fireground.EditorTools
{
    // Idempotent builder for the fireground in demo_city_night. Re-running replaces only the
    // objects it owns (FIREGROUND root, FIREGROUND_EQUIPMENT on the truck, Generated assets).
    // All visual/audio assets are generated procedurally: no third-party downloads.
    public static class FiregroundInstaller
    {
        const string Root = "Assets/CityNightPresentation/Fireground", Gen = Root + "/Generated";
        static readonly Vector3 StopPoint = new Vector3(196, 0, -192);
        static readonly Vector3 PlazaCenter = new Vector3(195, 6.5f, -209);
        static Dictionary<string, Material> mats;
        static AudioMixerGroup sfx;

        [MenuItem("Tools/Fireground/Install In City Scene")]
        public static string Install()
        {
            var log = new System.Text.StringBuilder();
            var mission = Object.FindFirstObjectByType<FireTruckMission>();
            if (!mission) return "FireTruckMission not found: open demo_city_night additively first.";
            var scene = mission.gameObject.scene; var truck = mission.transform;
            Directory.CreateDirectory(Gen);
            sfx = AssetDatabase.LoadAssetAtPath<AudioMixer>("Assets/VRMPAssets/Audio/Main.mixer")?.FindMatchingGroups("SFX").FirstOrDefault();
            BuildAssets(); log.AppendLine("assets ok");

            var old = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "FIREGROUND"); if (old) Object.DestroyImmediate(old);
            var oldEq = truck.Find("FIREGROUND_EQUIPMENT"); if (oldEq) Object.DestroyImmediate(oldEq.gameObject);
            var root = new GameObject("FIREGROUND"); SceneManager.MoveGameObjectToScene(root, scene);
            var eq = new GameObject("FIREGROUND_EQUIPMENT").transform; eq.SetParent(truck, false);

            // ---- Route / arrival ----
            float stopY = Ground(StopPoint + Vector3.up * 20) ;
            var route = mission.route;
            var wps = route.waypoints.ToList();
            if (wps[wps.Count - 1].position.x < 190)
            {
                var parent = wps[0].parent; var last = wps[wps.Count - 1].position;
                for (float x = last.x + 8; x <= StopPoint.x + .01f; x += 8)
                {
                    var p = new Vector3(x, 0, Mathf.Lerp(last.z, StopPoint.z, 1)); p.y = Ground(p + Vector3.up * 20) + .08f;
                    var w = new GameObject("WP_Fireground_" + wps.Count).transform; w.SetParent(parent, true); w.position = p; wps.Add(w);
                }
                var so = new SerializedObject(route); var arr = so.FindProperty("waypoints"); arr.arraySize = wps.Count;
                for (int i = 0; i < wps.Count; i++) arr.GetArrayElementAtIndex(i).objectReferenceValue = wps[i];
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            var stopPos = new Vector3(StopPoint.x, stopY, StopPoint.z);
            foreach (var t in new[] { route.stopZone, route.arrivalPoint }) if (t) t.position = stopPos + Vector3.up * .08f;
            var zone = new GameObject("FireTruck_ArrivalZone"); zone.transform.SetParent(root.transform); zone.layer = 2;
            zone.transform.SetPositionAndRotation(stopPos + Vector3.up * 1.5f, Quaternion.Euler(0, 90, 0));
            var box = zone.AddComponent<BoxCollider>(); box.isTrigger = true; box.size = new Vector3(3.6f, 3, 10);
            var incidentBuilding = FindRenderer("small_house_2 (2)");
            var missionSo = new SerializedObject(mission);
            missionSo.FindProperty("arrivalZone").objectReferenceValue = box;
            if (incidentBuilding) missionSo.FindProperty("incidentBuilding").objectReferenceValue = incidentBuilding.transform;
            missionSo.ApplyModifiedPropertiesWithoutUndo();
            log.AppendLine("route wps=" + wps.Count + " stop=" + stopPos);

            // ---- Walkable ground + colliders ----
            var colliders = new GameObject("Colliders").transform; colliders.SetParent(root.transform);
            var walk = BuildWalkable(root.transform); log.AppendLine("walkable verts=" + walk.sharedMesh.vertexCount);
            foreach (var n in new[] { "mid_house_3 (5)", "small_house_1 (3)", "small_house_2 (2)", "small_house_3 (2)", "mid_house_2 (2)", "hedge_with_base (126)", "hedge_with_base (127)", "hedge_with_base (31)", "hedge_with_base (32)" })
            { var r = FindRenderer(n); if (r) { var c = new GameObject("Col_" + n).AddComponent<BoxCollider>(); c.transform.SetParent(colliders); c.transform.position = r.bounds.center; c.size = r.bounds.size; } else log.AppendLine("missing " + n); }
            // Low plaza wall (visual mesh has no collider) with a walk-through opening between the hedges.
            WallBox(colliders, 176, 191.5f); WallBox(colliders, 198.5f, 212);

            // ---- Fire zones ----
            var fires = new GameObject("FireZones").transform; fires.SetParent(root.transform);
            var zones = new[]
            {
                MakeFire(fires, "Fire_MainEntrance", new Vector3(194.8f, 0, -218.15f), 0, 1.8f),
                MakeFire(fires, "Fire_LeftWing", new Vector3(185.85f, 0, -209.5f), 90, 1.9f),
                MakeFire(fires, "Fire_RightWing", new Vector3(204.95f, 0, -211f), 90, 1.9f),
            };

            // ---- Truck equipment (local to the truck, curb side = local +X) ----
            var op = truck.GetComponent<FiregroundOperation>() ?? truck.gameObject.AddComponent<FiregroundOperation>();
            var reel = BuildReel(eq, out var outlet, out var drum, out var coil, out var knob, out var ratchet);
            var dock = new GameObject("NozzleDock").transform; dock.SetParent(eq, false); dock.localPosition = new Vector3(1.45f, 1.70f, -2.75f); dock.localRotation = Quaternion.Euler(0, 180, 0);
            Prim(PrimitiveType.Cube, "DockHookFront", eq, new Vector3(1.45f, 1.645f, -2.62f), Quaternion.identity, new Vector3(.11f, .02f, .03f), "Chrome");
            Prim(PrimitiveType.Cube, "DockHookRear", eq, new Vector3(1.45f, 1.645f, -2.86f), Quaternion.identity, new Vector3(.11f, .02f, .03f), "Chrome");
            Prim(PrimitiveType.Cube, "DockArm", eq, new Vector3(1.39f, 1.645f, -2.74f), Quaternion.identity, new Vector3(.02f, .02f, .30f), "Chrome");
            var panel = BuildPanel(eq, op, out var lamp, out var needle, out var status, out var pumpAudio);
            var nozzle = BuildNozzle(root.transform, op, dock);
            var hoseGo = new GameObject("FireHose", typeof(MeshFilter), typeof(MeshRenderer)); hoseGo.transform.SetParent(root.transform, false);
            var hr = hoseGo.GetComponent<MeshRenderer>(); hr.sharedMaterial = mats["HoseJacket"]; hr.shadowCastingMode = ShadowCastingMode.Off;
            var hose = hoseGo.AddComponent<FireHose>(); hose.operation = op; hose.start = outlet; hose.end = nozzle.hoseAttach;
            var pumpSpawn = Anchor(eq, "PumpOperator_Spawn", new Vector3(2.2f, 0, -1.25f), -90);
            var nozzleSpawn = Anchor(eq, "NozzleOperator_Spawn", new Vector3(2.35f, 0, -3.0f), -90);
            var door = new GameObject("RearDoorAudio").AddComponent<AudioSource>(); door.transform.SetParent(eq, false); door.transform.localPosition = new Vector3(0, 1.6f, -4.8f); Spatial(door, false, 1, 2, 25);

            var cityCollider = GameObject.Find("city_part_collider")?.GetComponent<Collider>();
            var so2 = new SerializedObject(op);
            void Set(string n, Object v) => so2.FindProperty(n).objectReferenceValue = v;
            Set("mission", mission); Set("seating", Object.FindFirstObjectByType<FireTruckSeating>()); Set("truckBody", truck.GetComponent<Rigidbody>());
            Set("pumpOperatorSpawn", pumpSpawn); Set("nozzleOperatorSpawn", nozzleSpawn); Set("doorAudio", door); Set("doorClose", Clip("door_close"));
            Set("reel", reel); Set("nozzle", nozzle); Set("hoseOutlet", outlet); Set("nozzleDock", dock);
            Set("pumpLamp", lamp); Set("lampOn", mats["LampOn"]); Set("lampOff", mats["LampOff"]); Set("gaugeNeedle", needle); Set("panelText", status); Set("pumpAudio", pumpAudio);
            var walkProp = so2.FindProperty("walkableSurfaces"); var surfaces = new List<Collider> { walk }; if (cityCollider) surfaces.Add(cityCollider);
            walkProp.arraySize = surfaces.Count; for (int i = 0; i < surfaces.Count; i++) walkProp.GetArrayElementAtIndex(i).objectReferenceValue = surfaces[i];
            var zp = so2.FindProperty("zones"); zp.arraySize = zones.Length; for (int i = 0; i < zones.Length; i++) zp.GetArrayElementAtIndex(i).objectReferenceValue = zones[i];
            so2.ApplyModifiedPropertiesWithoutUndo();
            var rso = new SerializedObject(reel); rso.FindProperty("operation").objectReferenceValue = op; rso.FindProperty("drum").objectReferenceValue = drum; rso.FindProperty("coil").objectReferenceValue = coil; rso.FindProperty("knob").objectReferenceValue = knob; rso.FindProperty("ratchet").objectReferenceValue = ratchet;
            var cl = rso.FindProperty("m_Colliders"); cl.arraySize = 1; cl.GetArrayElementAtIndex(0).objectReferenceValue = knob.GetComponent<Collider>(); rso.ApplyModifiedPropertiesWithoutUndo();

            // ---- Solo bot partner: plaza route (clear of hedges/benches/lamp) + truck-side stands ----
            var route2 = new GameObject("BotRoute").transform; route2.SetParent(root.transform);
            Transform P(string n, float x, float z) { var t = new GameObject(n).transform; t.SetParent(route2); t.position = new Vector3(x, Ground(new Vector3(x, 9.5f, z)), z); return t; }
            var standData = new[] { (194.3f, -205f, 194.3f, -212.3f), (188.9f, -204f, 188.9f, -209.5f), (201.6f, -204f, 201.6f, -211f) };
            for (int i = 0; i < zones.Length; i++)
            { var (ax, az, sx, sz) = standData[i]; zones[i].botApproachPoint = P("Approach_" + zones[i].name, ax, az); zones[i].botStandPoint = P("Stand_" + zones[i].name, sx, sz); EditorUtility.SetDirty(zones[i]); }
            var bot = truck.GetComponent<FiregroundBot>() ?? truck.gameObject.AddComponent<FiregroundBot>();
            bot.op = op; bot.gateOuter = P("GateOuter", 195, -196.3f); bot.gateInner = P("GateInner", 195, -200.5f); bot.hub = P("PlazaHub", 195, -204);
            bot.panelStand = Anchor(eq, "Bot_PanelStand", new Vector3(1.6f, 0, -.95f), -90); bot.reelStand = Anchor(eq, "Bot_ReelStand", new Vector3(2.35f, 0, -1.6f), -90);
            bot.waterOnButton = panel.transform.Find("WaterOnButton"); bot.waterOffButton = panel.transform.Find("WaterOffButton"); EditorUtility.SetDirty(bot);

            // ---- Driver switches to Meta controllers at the fireground: glowing controller silhouettes ----
            var prompt = truck.GetComponent<FiregroundControllerPrompt>() ?? truck.gameObject.AddComponent<FiregroundControllerPrompt>(); prompt.op = op; prompt.ghostRoot = BuildControllerGhosts(eq); EditorUtility.SetDirty(prompt);
            CrewPresentationInstaller.ApplyCity(truck, log);

            // ---- Cab: wider steering-rim grab range; tablet presentation ----
            var cab = Object.FindFirstObjectByType<FireTruckCabInteraction>();
            if (cab) { var cs = new SerializedObject(cab); cs.FindProperty("rimTolerance").floatValue = .1f; cs.ApplyModifiedPropertiesWithoutUndo(); }
            var map = Object.FindFirstObjectByType<MissionTabletMap>(); if (map) { var ms = new SerializedObject(map); ms.FindProperty("incident").objectReferenceValue = zones[0].transform; ms.ApplyModifiedPropertiesWithoutUndo(); }
            UpgradeTablet(truck, log);

            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            log.AppendLine("saved " + scene.path);
            return log.ToString();
        }

        // ================= Geometry helpers =================
        static float Ground(Vector3 from)
        { return Physics.Raycast(from, Vector3.down, out var h, 60, 1, QueryTriggerInteraction.Ignore) ? h.point.y : 6.4f; }
        static Renderer FindRenderer(string name)
        {
            Renderer best = null; float d = float.MaxValue;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                if (r.name == name) { float dist = (r.bounds.center - PlazaCenter).sqrMagnitude; if (dist < d) { d = dist; best = r; } }
            return best && d < 50 * 50 ? best : null;
        }
        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 lp, Quaternion lr, Vector3 ls, string mat, bool keepCollider = false)
        {
            var g = GameObject.CreatePrimitive(type); g.name = name; if (!keepCollider) Object.DestroyImmediate(g.GetComponent<Collider>());
            g.transform.SetParent(parent, false); g.transform.localPosition = lp; g.transform.localRotation = lr; g.transform.localScale = ls;
            var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = mats[mat]; r.shadowCastingMode = ShadowCastingMode.Off; return g;
        }
        static Transform Anchor(Transform parent, string name, Vector3 lp, float yaw)
        { var t = new GameObject(name).transform; t.SetParent(parent, false); t.localPosition = lp; t.localRotation = Quaternion.Euler(0, yaw, 0); return t; }
        static void WallBox(Transform parent, float x0, float x1)
        { var c = new GameObject("Col_PlazaWall").AddComponent<BoxCollider>(); c.transform.SetParent(parent); c.transform.position = new Vector3((x0 + x1) * .5f, 7.05f, -198.1f); c.size = new Vector3(x1 - x0, .95f, 1.0f); }
        static void Spatial(AudioSource a, bool loop, float volume, float min, float max)
        { a.loop = loop; a.playOnAwake = false; a.volume = volume; a.spatialBlend = 1; a.minDistance = min; a.maxDistance = max; a.rolloffMode = AudioRolloffMode.Logarithmic; a.dopplerLevel = 0; a.outputAudioMixerGroup = sfx; }

        static MeshCollider BuildWalkable(Transform root)
        {
            var src = GameObject.Find("city_part_demo_main1");
            var tmp = new GameObject("tmp"); tmp.hideFlags = HideFlags.HideAndDontSave; tmp.transform.SetPositionAndRotation(src.transform.position, src.transform.rotation); tmp.transform.localScale = src.transform.lossyScale;
            var mc = tmp.AddComponent<MeshCollider>(); mc.sharedMesh = src.GetComponent<MeshFilter>().sharedMesh;
            int nx = 41, nz = 34; float x0 = 174, z0 = -193.2f; var v = new Vector3[nx * nz]; var ok = new bool[v.Length];
            for (int j = 0; j < nz; j++) for (int i = 0; i < nx; i++)
            {
                var p = new Vector3(x0 + i, 7.6f, z0 - j); int k = j * nx + i;
                if (mc.Raycast(new Ray(p, Vector3.down), out var h, 3)) { v[k] = h.point + Vector3.up * .01f; ok[k] = true; } else v[k] = new Vector3(p.x, 6.4f, p.z);
            }
            Object.DestroyImmediate(tmp);
            var tris = new List<int>();
            for (int j = 0; j < nz - 1; j++) for (int i = 0; i < nx - 1; i++)
            { int a = j * nx + i, b = a + 1, c = a + nx, d = c + 1; if (ok[a] && ok[b] && ok[c] && ok[d]) tris.AddRange(new[] { a, c, b, b, c, d }); }
            var m = new Mesh { name = "FiregroundWalkable", vertices = v, triangles = tris.ToArray() }; m.RecalculateNormals(); m.RecalculateBounds();
            if (Vector3.Dot(m.normals[0], Vector3.up) < 0) { tris.Reverse(); m.triangles = tris.ToArray(); m.RecalculateNormals(); }
            AssetDatabase.CreateAsset(m, Gen + "/FiregroundWalkable.asset");
            var g = new GameObject("WalkableGround"); g.transform.SetParent(root, false);
            var col = g.AddComponent<MeshCollider>(); col.sharedMesh = m;
            var area = g.AddComponent<TeleportationArea>(); area.interactionLayers = 1 << 31;
            return col;
        }

        // ================= Fire =================
        static FireZone MakeFire(Transform parent, string name, Vector3 xz, float yaw, float radius)
        {
            float y = Ground(new Vector3(xz.x, 9.5f, xz.z)); if (y < 5.5f) y = 6.45f;
            var g = new GameObject(name); g.transform.SetParent(parent); g.transform.SetPositionAndRotation(new Vector3(xz.x, y + 1.6f, xz.z), Quaternion.Euler(0, yaw, 0));
            var z = g.AddComponent<FireZone>(); z.label = name; z.radius = radius;
            z.flames = Ps(g.transform, "Flames", Vector3.down * .6f, "Flame", p =>
            {
                var m = p.main; m.startLifetime = new ParticleSystem.MinMaxCurve(.6f, 1.1f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f); m.startSize = new ParticleSystem.MinMaxCurve(1.1f, 2.1f);
                m.startColor = new ParticleSystem.MinMaxGradient(new Color(1, .85f, .55f), new Color(1, .6f, .25f)); m.maxParticles = 140; m.gravityModifier = -.15f; m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
                var e = p.emission; e.rateOverTime = 90; var s = p.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(2.6f, 1.0f, .4f);
                Col(p, (0, new Color(1, .9f, .6f, 0)), (.12f, new Color(1, .75f, .3f, 1)), (.55f, new Color(1, .35f, .08f, .75f)), (1, new Color(.4f, .08f, .03f, 0)));
                Size(p, .6f, 1, .25f); Noise(p, .5f, .9f);
            });
            z.smoke = Ps(g.transform, "Smoke", Vector3.up * 2.4f, "Smoke", p =>
            {
                var m = p.main; m.startLifetime = new ParticleSystem.MinMaxCurve(5, 8); m.startSpeed = new ParticleSystem.MinMaxCurve(1, 2); m.startSize = new ParticleSystem.MinMaxCurve(2.4f, 4f);
                m.startColor = new Color(.05f, .045f, .04f, 1f); m.maxParticles = 80; m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
                var e = p.emission; e.rateOverTime = 12; var s = p.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 12; s.radius = .9f; s.rotation = new Vector3(-90, 0, 0);
                Col(p, (0, new Color(.2f, .18f, .16f, 0)), (.15f, new Color(.08f, .07f, .06f, .9f)), (1, new Color(.05f, .05f, .05f, 0)));
                Size(p, .5f, 1.4f, 2.4f); Noise(p, .3f, .3f);
                var v = p.velocityOverLifetime; v.enabled = true; v.space = ParticleSystemSimulationSpace.World; v.x = new ParticleSystem.MinMaxCurve(.3f); v.y = new ParticleSystem.MinMaxCurve(.7f); v.z = new ParticleSystem.MinMaxCurve(.1f);
            });
            z.embers = Ps(g.transform, "Embers", Vector3.down * .6f, "Ember", p =>
            {
                var m = p.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1.4f, 2.6f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.2f); m.startSize = new ParticleSystem.MinMaxCurve(.03f, .07f);
                m.startColor = new Color(1, .55f, .15f); m.maxParticles = 60; m.gravityModifier = -.25f;
                var e = p.emission; e.rateOverTime = 14; var s = p.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(2.2f, .3f, .5f);
                Col(p, (0, new Color(1, .7f, .3f, 1)), (1, new Color(1, .2f, .05f, 0))); Noise(p, 1.2f, 1.5f);
            });
            z.steam = Ps(g.transform, "Steam", Vector3.zero, "Steam", p =>
            {
                var m = p.main; m.startLifetime = new ParticleSystem.MinMaxCurve(1, 2.2f); m.startSpeed = new ParticleSystem.MinMaxCurve(.8f, 1.8f); m.startSize = new ParticleSystem.MinMaxCurve(.7f, 1.5f);
                m.startColor = new Color(.92f, .93f, .95f, .5f); m.maxParticles = 90; m.playOnAwake = false; m.startRotation = new ParticleSystem.MinMaxCurve(0, 6.28f);
                var e = p.emission; e.rateOverTime = 40; var s = p.shape; s.shapeType = ParticleSystemShapeType.Box; s.scale = new Vector3(2.2f, .6f, .8f);
                Col(p, (0, new Color(1, 1, 1, 0)), (.2f, new Color(1, 1, 1, .55f)), (1, new Color(1, 1, 1, 0))); Size(p, .5f, 1.4f, 2.6f); Noise(p, .4f, .6f);
            });
            var lg = new GameObject("FireLight"); lg.transform.SetParent(g.transform, false); lg.transform.localPosition = new Vector3(0, .2f, 1.2f);
            var l = lg.AddComponent<Light>(); l.type = LightType.Point; l.color = new Color(1, .5f, .18f); l.intensity = 6; l.range = 10; l.shadows = LightShadows.None; z.glow = l;
            var cr = g.AddComponent<AudioSource>(); cr.clip = Clip("fire_crackle"); Spatial(cr, true, .9f, 3, 45); cr.playOnAwake = true; z.crackle = cr;
            var st = z.steam.gameObject.AddComponent<AudioSource>(); st.clip = Clip("steam_hiss"); Spatial(st, true, 0, 2, 25); z.steamAudio = st;
            EditorUtility.SetDirty(z); return z;
        }
        static ParticleSystem Ps(Transform parent, string name, Vector3 lp, string mat, System.Action<ParticleSystem> setup)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.localPosition = lp;
            var p = g.AddComponent<ParticleSystem>(); var m = p.main; m.simulationSpace = ParticleSystemSimulationSpace.World; m.scalingMode = ParticleSystemScalingMode.Hierarchy; m.playOnAwake = true; m.loop = true;
            var s = p.shape; s.enabled = true;
            var r = g.GetComponent<ParticleSystemRenderer>(); r.sharedMaterial = mats[mat]; r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.maxParticleSize = 3;
            setup(p); return p;
        }
        static void Col(ParticleSystem p, params (float t, Color c)[] keys)
        {
            var c = p.colorOverLifetime; c.enabled = true; var g = new Gradient();
            g.SetKeys(keys.Select(k => new GradientColorKey(k.c, k.t)).ToArray(), keys.Select(k => new GradientAlphaKey(k.c.a, k.t)).ToArray()); c.color = g;
        }
        static void Size(ParticleSystem p, float a, float b, float c)
        { var s = p.sizeOverLifetime; s.enabled = true; s.size = new ParticleSystem.MinMaxCurve(1, new AnimationCurve(new Keyframe(0, a), new Keyframe(.4f, b), new Keyframe(1, c))); }
        static void Noise(ParticleSystem p, float strength, float freq)
        { var n = p.noise; n.enabled = true; n.strength = strength; n.frequency = freq; n.scrollSpeed = .6f; n.quality = ParticleSystemNoiseQuality.Low; n.octaveCount = 1; }

        // ================= Reel / panel / nozzle =================
        static HoseReel BuildReel(Transform eq, out Transform outlet, out Transform drum, out Transform coil, out Transform knob, out AudioSource ratchet)
        {
            var g = new GameObject("HoseReel"); g.transform.SetParent(eq, false); g.transform.localPosition = new Vector3(1.66f, 1.25f, -2.05f);
            var t = g.transform; var x90 = Quaternion.Euler(90, 0, 0);
            Prim(PrimitiveType.Cube, "MountPlate", t, new Vector3(-.30f, 0, 0), Quaternion.identity, new Vector3(.02f, .56f, .5f), "PanelDark");
            Prim(PrimitiveType.Cube, "BracketA", t, new Vector3(-.15f, 0, .215f), Quaternion.identity, new Vector3(.30f, .06f, .025f), "PanelDark");
            Prim(PrimitiveType.Cube, "BracketB", t, new Vector3(-.15f, 0, -.215f), Quaternion.identity, new Vector3(.30f, .06f, .025f), "PanelDark");
            Prim(PrimitiveType.Cylinder, "Axle", t, Vector3.zero, x90, new Vector3(.05f, .25f, .05f), "Chrome");
            drum = new GameObject("Drum").transform; drum.SetParent(t, false);
            Prim(PrimitiveType.Cylinder, "Core", drum, Vector3.zero, x90, new Vector3(.2f, .17f, .2f), "ReelRed");
            Prim(PrimitiveType.Cylinder, "FlangeFront", drum, new Vector3(0, 0, .175f), x90, new Vector3(.52f, .006f, .52f), "ReelRed");
            Prim(PrimitiveType.Cylinder, "FlangeRear", drum, new Vector3(0, 0, -.175f), x90, new Vector3(.52f, .006f, .52f), "ReelRed");
            for (int i = 0; i < 6; i++) { float a = i * 60; Prim(PrimitiveType.Cube, "Spoke" + i, drum, Quaternion.Euler(0, 0, a) * new Vector3(.12f, 0, .182f), Quaternion.Euler(0, 0, a), new Vector3(.2f, .025f, .01f), "PanelDark"); }
            coil = Prim(PrimitiveType.Cylinder, "HoseCoil", drum, Vector3.zero, x90, new Vector3(.46f, .165f, .46f), "HoseJacket").transform;
            Prim(PrimitiveType.Cube, "CrankArm", drum, new Vector3(.085f, 0, .2f), Quaternion.identity, new Vector3(.19f, .035f, .02f), "Chrome");
            var k = Prim(PrimitiveType.Cylinder, "CrankKnob", drum, new Vector3(.17f, 0, .26f), x90, new Vector3(.045f, .055f, .045f), "Rubber", true);
            Object.DestroyImmediate(k.GetComponent<Collider>()); var kc = k.AddComponent<CapsuleCollider>(); kc.radius = 1.4f; kc.height = 3.2f; kc.direction = 1;
            knob = k.transform;
            outlet = new GameObject("HoseOutlet").transform; outlet.SetParent(t, false); outlet.localPosition = new Vector3(.06f, -.25f, 0);
            ratchet = g.AddComponent<AudioSource>(); ratchet.clip = Clip("reel_ratchet"); Spatial(ratchet, true, 0, 1, 12);
            var reel = g.AddComponent<HoseReel>(); reel.interactionLayers = 1; var hp = g.AddComponent<InteractionHaptics>(); hp.releaseAmplitude = 0; return reel;
        }
        static GameObject BuildPanel(Transform eq, FiregroundOperation op, out Renderer lamp, out Transform needle, out TMP_Text status, out AudioSource pump)
        {
            var g = new GameObject("PumpControlPanel"); g.transform.SetParent(eq, false); g.transform.localPosition = new Vector3(1.02f, 1.5f, -.7f); g.transform.localRotation = Quaternion.Euler(0, -90, 0);
            var t = g.transform; var face = Quaternion.Euler(90, 0, 0);
            Prim(PrimitiveType.Cube, "Backplate", t, Vector3.zero, Quaternion.identity, new Vector3(.66f, .44f, .02f), "PanelDark");
            Prim(PrimitiveType.Cube, "TopStripe", t, new Vector3(0, .205f, -.011f), Quaternion.identity, new Vector3(.66f, .03f, .002f), "OrangeTip");
            Label(t, "PUMP CONTROL", new Vector3(0, .165f, -.012f), .028f, TextAlignmentOptions.Center, .6f);
            Button(t, op, true, new Vector3(-.25f, .06f, 0)); Button(t, op, false, new Vector3(-.25f, -.11f, 0));
            lamp = Prim(PrimitiveType.Sphere, "PumpLamp", t, new Vector3(-.02f, .10f, -.015f), Quaternion.identity, Vector3.one * .04f, "LampOff").GetComponent<Renderer>();
            Label(t, "LINE", new Vector3(-.02f, .065f, -.012f), .016f, TextAlignmentOptions.Center, .1f);
            Prim(PrimitiveType.Cylinder, "GaugeFace", t, new Vector3(.0f, -.08f, -.012f), face, new Vector3(.14f, .004f, .14f), "GaugeFace");
            Prim(PrimitiveType.Cylinder, "GaugeBezel", t, new Vector3(.0f, -.08f, -.009f), face, new Vector3(.155f, .003f, .155f), "Chrome");
            needle = new GameObject("GaugeNeedle").transform; needle.SetParent(t, false); needle.localPosition = new Vector3(.0f, -.08f, -.018f); needle.localRotation = Quaternion.Euler(0, 0, 120);
            Prim(PrimitiveType.Cube, "Needle", needle, new Vector3(0, .028f, 0), Quaternion.identity, new Vector3(.005f, .058f, .002f), "ButtonRed");
            Label(t, "PSI", new Vector3(.0f, -.12f, -.016f), .012f, TextAlignmentOptions.Center, .08f).color = Color.black;
            status = Label(t, "PUMP CONTROL\nSTANDBY", new Vector3(.19f, -.02f, -.012f), .016f, TextAlignmentOptions.TopLeft, .25f, .3f);
            pump = g.AddComponent<AudioSource>(); pump.clip = Clip("pump_engine"); Spatial(pump, true, 0, 2, 30);
            return g;
        }
        static void Button(Transform panel, FiregroundOperation op, bool on, Vector3 lp)
        {
            var b = new GameObject(on ? "WaterOnButton" : "WaterOffButton"); b.transform.SetParent(panel, false); b.transform.localPosition = lp;
            Prim(PrimitiveType.Cylinder, "Cap", b.transform, new Vector3(0, 0, -.022f), Quaternion.Euler(90, 0, 0), new Vector3(.075f, .018f, .075f), on ? "ButtonGreen" : "ButtonRed");
            Prim(PrimitiveType.Cylinder, "Ring", b.transform, new Vector3(0, 0, -.012f), Quaternion.Euler(90, 0, 0), new Vector3(.095f, .008f, .095f), "Chrome");
            var c = b.AddComponent<BoxCollider>(); c.center = new Vector3(0, 0, -.03f); c.size = new Vector3(.11f, .11f, .06f);
            var si = b.AddComponent<XRSimpleInteractable>(); si.interactionLayers = 1; b.AddComponent<InteractionHaptics>();
            var poke = b.AddComponent<XRPokeFilter>();
            UnityEventTools.AddVoidPersistentListener(si.selectEntered, on ? op.PumpButtonOn : op.PumpButtonOff);
            Label(panel, on ? "WATER ON" : "WATER OFF", lp + new Vector3(.065f, 0, -.012f), .016f, TextAlignmentOptions.Left, .14f);
        }
        static TMP_Text Label(Transform parent, string text, Vector3 lp, float capHeight, TextAlignmentOptions align, float width, float height = .05f)
        {
            var g = new GameObject("Label_" + text.Split('\n')[0]); g.transform.SetParent(parent, false); g.transform.localPosition = lp;
            var tmp = g.AddComponent<TextMeshPro>(); tmp.text = text; tmp.alignment = align; tmp.color = Color.white; tmp.textWrappingMode = TextWrappingModes.Normal; tmp.richText = true;
            tmp.fontSize = 10; float measured = Mathf.Max(.0001f, tmp.GetPreferredValues("H").y); tmp.fontSize = 10 * capHeight * 1.35f / measured;
            tmp.rectTransform.sizeDelta = new Vector2(width, height); if (align == TextAlignmentOptions.TopLeft || align == TextAlignmentOptions.Left) tmp.rectTransform.pivot = new Vector2(0, .5f);
            return tmp;
        }
        static HoseNozzle BuildNozzle(Transform root, FiregroundOperation op, Transform dock)
        {
            var g = new GameObject("FireHoseNozzle"); g.transform.SetParent(root); g.transform.SetPositionAndRotation(dock.position, dock.rotation);
            var t = g.transform;
            Part(t, "Coupling", Lathe("NozzleCoupling", new[] { new Vector2(.040f, -.075f), new Vector2(.046f, -.07f), new Vector2(.046f, -.045f), new Vector2(.040f, -.04f), new Vector2(.044f, -.035f), new Vector2(.044f, -.005f), new Vector2(.036f, 0) }), "Brass");
            Part(t, "ValveBody", Lathe("NozzleBody", new[] { new Vector2(.036f, 0), new Vector2(.042f, .02f), new Vector2(.042f, .09f), new Vector2(.034f, .11f) }), "Chrome");
            Part(t, "Barrel", Lathe("NozzleBarrel", new[] { new Vector2(.034f, .11f), new Vector2(.030f, .25f), new Vector2(.028f, .30f) }), "Chrome");
            Part(t, "TipBumper", Lathe("NozzleTip", new[] { new Vector2(.028f, .295f), new Vector2(.046f, .305f), new Vector2(.046f, .345f), new Vector2(.036f, .365f), new Vector2(.016f, .37f) }), "OrangeTip");
            Prim(PrimitiveType.Cube, "PistolGrip", t, new Vector3(0, -.075f, .035f), Quaternion.Euler(-15, 0, 0), new Vector3(.034f, .11f, .042f), "Rubber");
            Prim(PrimitiveType.Cube, "BaleTop", t, new Vector3(0, .065f, .07f), Quaternion.identity, new Vector3(.012f, .012f, .1f), "Chrome");
            Prim(PrimitiveType.Cube, "BaleArmL", t, new Vector3(-.045f, .03f, .045f), Quaternion.Euler(-35, 0, 0), new Vector3(.01f, .085f, .01f), "Chrome");
            Prim(PrimitiveType.Cube, "BaleArmR", t, new Vector3(.045f, .03f, .045f), Quaternion.Euler(-35, 0, 0), new Vector3(.01f, .085f, .01f), "Chrome");
            var muzzle = new GameObject("Muzzle").transform; muzzle.SetParent(t, false); muzzle.localPosition = new Vector3(0, 0, .375f);
            var attach = new GameObject("HoseAttach").transform; attach.SetParent(t, false); attach.localPosition = new Vector3(0, 0, -.075f);
            var cap = g.AddComponent<CapsuleCollider>(); cap.direction = 2; cap.radius = .05f; cap.height = .46f; cap.center = new Vector3(0, 0, .15f);
            var gripCol = g.AddComponent<BoxCollider>(); gripCol.center = new Vector3(0, -.07f, .035f); gripCol.size = new Vector3(.05f, .12f, .06f);
            var rb = g.AddComponent<Rigidbody>(); rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            var grab = g.AddComponent<XRGrabInteractable>();
            var gripAttach = new GameObject("GripAttach").transform; gripAttach.SetParent(t, false); gripAttach.localPosition = new Vector3(0, -.075f, .035f);
            grab.movementType = XRBaseInteractable.MovementType.Instantaneous; grab.throwOnDetach = false; grab.useDynamicAttach = false; grab.attachTransform = gripAttach;
            g.AddComponent<InteractionHaptics>(); grab.trackScale = false; grab.interactionLayers = 1;
            var general = g.AddComponent<XRGeneralGrabTransformer>();
            var leash = g.AddComponent<NozzleLeash>(); leash.operation = op;
            grab.addDefaultGrabTransformers = false; grab.startingSingleGrabTransformers = new List<XRBaseGrabTransformer> { general, leash };
            var n = g.AddComponent<HoseNozzle>(); n.operation = op; n.leash = leash; n.muzzle = muzzle; n.hoseAttach = attach; leash.nozzle = n;
            var splash = Ps(muzzle, "Splash", Vector3.zero, "Water", p =>
            {
                var m = p.main; m.loop = false; m.playOnAwake = false; m.startLifetime = new ParticleSystem.MinMaxCurve(.3f, .7f); m.startSpeed = new ParticleSystem.MinMaxCurve(1.5f, 3.5f); m.startSize = new ParticleSystem.MinMaxCurve(.08f, .2f); m.gravityModifier = 1; m.maxParticles = 160; m.startColor = new Color(.85f, .92f, 1, .7f);
                var e = p.emission; e.rateOverTime = 0; e.SetBursts(new[] { new ParticleSystem.Burst(0, 2) }); var s = p.shape; s.shapeType = ParticleSystemShapeType.Hemisphere; s.radius = .05f;
            });
            n.stream = Ps(muzzle, "WaterStream", Vector3.zero, "Water", p =>
            {
                var m = p.main; m.startLifetime = 1.5f; m.startSpeed = op ? op.streamSpeed : 21; m.startSize = .3f; m.gravityModifier = 1; m.maxParticles = 300; m.startColor = new Color(.85f, .93f, 1, .85f); m.playOnAwake = false;
                var e = p.emission; e.rateOverTime = 150; e.enabled = false; var s = p.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 1.2f; s.radius = .012f;
                var lv = p.limitVelocityOverLifetime; lv.enabled = true; lv.drag = .16f; lv.multiplyDragByParticleSize = false; lv.multiplyDragByParticleVelocity = false; lv.limit = 100;
                Size(p, .12f, .6f, 1.3f); Col(p, (0, new Color(1, 1, 1, .9f)), (.7f, new Color(1, 1, 1, .6f)), (1, new Color(1, 1, 1, 0)));
                var c = p.collision; c.enabled = true; c.type = ParticleSystemCollisionType.World; c.mode = ParticleSystemCollisionMode.Collision3D; c.quality = ParticleSystemCollisionQuality.Low; c.collidesWith = 1; c.dampen = .7f; c.bounce = .05f; c.lifetimeLoss = .8f; c.radiusScale = .3f; c.maxCollisionShapes = 64;
                var sub = p.subEmitters; sub.enabled = true; sub.AddSubEmitter(splash, ParticleSystemSubEmitterType.Collision, ParticleSystemSubEmitterProperties.InheritNothing, .35f);
                var r = p.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = .025f; r.lengthScale = 1.2f;
            });
            n.mist = Ps(muzzle, "WaterMist", Vector3.zero, "Steam", p =>
            {
                var m = p.main; m.startLifetime = 1.2f; m.startSpeed = new ParticleSystem.MinMaxCurve(13, 18); m.startSize = .25f; m.gravityModifier = .7f; m.maxParticles = 90; m.startColor = new Color(.9f, .95f, 1, .3f); m.playOnAwake = false;
                var e = p.emission; e.rateOverTime = 50; e.enabled = false; var s = p.shape; s.shapeType = ParticleSystemShapeType.Cone; s.angle = 6; s.radius = .02f;
                var lv = p.limitVelocityOverLifetime; lv.enabled = true; lv.drag = .9f; lv.limit = 100; Size(p, .2f, 1.5f, 3); Col(p, (0, new Color(1, 1, 1, .4f)), (1, new Color(1, 1, 1, 0)));
            });
            var spray = g.AddComponent<AudioSource>(); spray.clip = Clip("water_spray"); Spatial(spray, true, 0, 2, 30); n.sprayAudio = spray;
            return n;
        }
        static GameObject BuildControllerGhosts(Transform eq)
        {
            var root = new GameObject("ControllerGhosts"); root.transform.SetParent(eq, false);
            var ghost = Lit("ControllerGhost", new Color(.15f, .55f, 1f), 0, .9f, null, new Color(.2f, .9f, 2.4f));
            foreach (var (path, x) in new[] { ("Assets/VRMPAssets/Prefabs/XRControllerLeftModel.prefab", -.15f), ("Assets/VRMPAssets/Prefabs/XRControllerRightModel.prefab", .15f) })
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path); if (!prefab) continue;
                var g = Object.Instantiate(prefab, root.transform); g.name = Path.GetFileNameWithoutExtension(path) + "_Ghost"; g.transform.localPosition = new Vector3(x, 0, 0); g.transform.localRotation = Quaternion.Euler(-30, 0, 0);
                foreach (var tr in g.GetComponentsInChildren<Transform>(true)) GameObjectUtility.RemoveMonoBehavioursWithMissingScript(tr.gameObject);
                foreach (var c in g.GetComponentsInChildren<Component>(true)) if (c && !(c is Transform) && !(c is MeshFilter) && !(c is Renderer)) Object.DestroyImmediate(c);
                foreach (var r in g.GetComponentsInChildren<Renderer>(true)) { r.sharedMaterials = Enumerable.Repeat(ghost, r.sharedMaterials.Length).ToArray(); r.shadowCastingMode = ShadowCastingMode.Off; }
            }
            root.SetActive(false); return root;
        }
        static void Part(Transform parent, string name, Mesh mesh, string mat)
        { var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); g.transform.SetParent(parent, false); g.GetComponent<MeshFilter>().sharedMesh = mesh; var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = mats[mat]; r.shadowCastingMode = ShadowCastingMode.Off; }
        static Mesh Lathe(string name, Vector2[] profile, int sides = 20)
        {
            var v = new List<Vector3>(); var tri = new List<int>(); int ring = sides + 1;
            foreach (var p in profile) for (int s = 0; s <= sides; s++) { float a = s / (float)sides * Mathf.PI * 2; v.Add(new Vector3(Mathf.Cos(a) * p.x, Mathf.Sin(a) * p.x, p.y)); }
            for (int i = 0; i < profile.Length - 1; i++) for (int s = 0; s < sides; s++) { int a = i * ring + s, b = a + ring; tri.AddRange(new[] { a, a + 1, b, a + 1, b + 1, b }); }
            // End caps.
            foreach (var (row, flip) in new[] { (0, true), (profile.Length - 1, false) })
            { int c = v.Count; v.Add(new Vector3(0, 0, profile[row].y)); for (int s = 0; s < sides; s++) { int a = row * ring + s; if (flip) tri.AddRange(new[] { c, a + 1, a }); else tri.AddRange(new[] { c, a, a + 1 }); } }
            var m = new Mesh { name = name, vertices = v.ToArray(), triangles = tri.ToArray() }; m.RecalculateNormals(); m.RecalculateBounds();
            string path = Gen + "/" + name + ".asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path); return m;
        }

        static void UpgradeTablet(Transform truck, System.Text.StringBuilder log)
        {
            var tab = truck.Find("CabinMissionEquipment/Passenger_Tablet"); if (!tab) { log.AppendLine("tablet missing"); return; }
            tab.localPosition = new Vector3(.72f, 1.72f, 2.78f); tab.localRotation = Quaternion.Euler(35, 0, 0);
            var old = tab.Find("RuggedCase"); if (old) Object.DestroyImmediate(old.gameObject);
            var body = tab.Find("Body"); var mf = body ? body.GetComponent<MeshFilter>() : null; if (!mf) return;
            var b = mf.sharedMesh.bounds; var size = Vector3.Scale(b.size, body.localScale); var c = body.localPosition + Vector3.Scale(b.center, body.localScale);
            var caseRoot = new GameObject("RuggedCase").transform; caseRoot.SetParent(tab, false); caseRoot.localPosition = c; caseRoot.localRotation = body.localRotation;
            // Thinnest axis = depth. Rubber edge bars + orange corner bumpers (civil-defence rugged tablet).
            int depthAxis = size.x < size.y ? (size.x < size.z ? 0 : 2) : (size.y < size.z ? 1 : 2);
            int a1 = (depthAxis + 1) % 3, a2 = (depthAxis + 2) % 3; float e = .016f, d = size[depthAxis] + .012f;
            Vector3 V(int axis, float val) { var r = Vector3.zero; r[axis] = val; return r; }
            foreach (var sgn in new[] { -1, 1 })
            {
                Prim(PrimitiveType.Cube, "EdgeA" + sgn, caseRoot, V(a1, sgn * (size[a1] * .5f + e * .5f)), Quaternion.identity, V(a1, e) + V(a2, size[a2] + e * 2) + V(depthAxis, d), "Rubber");
                Prim(PrimitiveType.Cube, "EdgeB" + sgn, caseRoot, V(a2, sgn * (size[a2] * .5f + e * .5f)), Quaternion.identity, V(a2, e) + V(a1, size[a1]) + V(depthAxis, d), "Rubber");
            }
            foreach (var s1 in new[] { -1, 1 }) foreach (var s2 in new[] { -1, 1 })
                Prim(PrimitiveType.Cube, "Corner", caseRoot, V(a1, s1 * (size[a1] * .5f + e * .5f)) + V(a2, s2 * (size[a2] * .5f + e * .5f)), Quaternion.identity, Vector3.one * (e * 2.2f) + V(depthAxis, d - e * 2.2f + .004f), "OrangeTip");
            var canvas = tab.Find("Tablet_Canvas");
            if (canvas)
            {
                void Font(string n, float s) { var t = canvas.Find(n)?.GetComponent<TMP_Text>(); if (t) { t.fontSize = s; t.richText = true; t.enableAutoSizing = true; t.fontSizeMax = s; t.fontSizeMin = s * .7f; } }
                Font("Status", 22); Font("NavigationDetails", 19); Font("Follow_Button/Label", 24);
                var btn = canvas.Find("Follow_Button") as RectTransform; if (btn) btn.sizeDelta = new Vector2(240, 56);
            }
            log.AppendLine("tablet upgraded depthAxis=" + depthAxis + " size=" + size);
        }

        // ================= Procedural assets =================
        static void BuildAssets()
        {
            Tex("soft", 64, 64, (u, v) => { float r = Vector2.Distance(new Vector2(u, v), new Vector2(.5f, .5f)) * 2; float a = Mathf.Clamp01(1 - r); return new Color(1, 1, 1, a * a); });
            Tex("flame", 64, 128, (u, v) => { float x = (u - .5f) * 2, y = v; float w = Mathf.Lerp(1, .15f, y); float body = Mathf.Clamp01(1 - Mathf.Abs(x) / Mathf.Max(.05f, w)) * Mathf.Clamp01(y * 6) * Mathf.Clamp01((1 - y) * 1.6f); float n = .6f + .4f * Mathf.PerlinNoise(u * 6, v * 5); return new Color(1, 1, 1, Mathf.Clamp01(body * n * 1.3f)); });
            Tex("smoke", 128, 128, (u, v) => { float r = Vector2.Distance(new Vector2(u, v), new Vector2(.5f, .5f)) * 2; float n = Fbm(u * 4, v * 4); float a = Mathf.Clamp01((1 - r) * 1.4f) * Mathf.Clamp01(n * 1.5f - .15f); return new Color(1, 1, 1, a); });
            Tex("weave", 64, 64, (u, v) => { float wx = Mathf.Abs(Mathf.Sin(u * Mathf.PI * 16)), wy = Mathf.Abs(Mathf.Sin(v * Mathf.PI * 16)); float k = ((int)(u * 16) + (int)(v * 16)) % 2 == 0 ? wx : wy; float l = .78f + .22f * k; return new Color(l, l, l, 1); }, true);
            mats = new Dictionary<string, Material>
            {
                ["HoseJacket"] = Lit("HoseJacket", new Color(.62f, .07f, .05f), 0, .25f, Load("weave")),
                ["Brass"] = Lit("Brass", new Color(.78f, .58f, .26f), .9f, .55f),
                ["Chrome"] = Lit("Chrome", new Color(.78f, .79f, .8f), 1, .78f),
                ["Rubber"] = Lit("Rubber", new Color(.05f, .05f, .05f), 0, .25f),
                ["ReelRed"] = Lit("ReelRed", new Color(.62f, .05f, .04f), .3f, .5f),
                ["PanelDark"] = Lit("PanelDark", new Color(.13f, .14f, .15f), .7f, .45f),
                ["OrangeTip"] = Lit("OrangeTip", new Color(.95f, .42f, .08f), 0, .4f),
                ["GaugeFace"] = Lit("GaugeFace", new Color(.95f, .95f, .92f), 0, .6f),
                ["ButtonGreen"] = Lit("ButtonGreen", new Color(.1f, .65f, .2f), 0, .6f, null, new Color(0, .25f, .05f)),
                ["ButtonRed"] = Lit("ButtonRed", new Color(.75f, .08f, .06f), 0, .6f, null, new Color(.25f, 0, 0)),
                ["LampOn"] = Lit("LampOn", new Color(.3f, 1, .4f), 0, .8f, null, new Color(.4f, 2.2f, .6f)),
                ["LampOff"] = Lit("LampOff", new Color(.08f, .15f, .09f), 0, .8f),
                ["Flame"] = Particle("FlameParticle", Load("flame"), new Color(1, .7f, .35f, 1), true),
                ["Ember"] = Particle("EmberParticle", Load("soft"), new Color(1, .6f, .2f, 1), true),
                ["Smoke"] = Particle("SmokeParticle", Load("smoke"), new Color(.35f, .33f, .31f, 1), false),
                ["Steam"] = Particle("SteamParticle", Load("smoke"), new Color(1, 1, 1, 1), false),
                ["Water"] = Particle("WaterParticle", Load("soft"), new Color(.8f, .9f, 1, 1), false),
            };
            Wav("fire_crackle", 4, (t, r) => { float rumble = Brown(r) * .35f; float crack = 0; if (r.NextDouble() < .0009) crackleEnv = 1; crackleEnv *= .985f; crack = crackleEnv * (float)(r.NextDouble() * 2 - 1) * .8f; return rumble + crack; });
            Wav("water_spray", 3, (t, r) => { float n = (float)(r.NextDouble() * 2 - 1); hp = .82f * (hp + n - lastN); lastN = n; return hp * .45f * (.9f + .1f * Mathf.Sin(t * 13)); });
            Wav("steam_hiss", 2, (t, r) => { float n = (float)(r.NextDouble() * 2 - 1); hp = .6f * (hp + n - lastN); lastN = n; return hp * .35f; });
            Wav("pump_engine", 2, (t, r) => .28f * Mathf.Sin(2 * Mathf.PI * 50 * t) + .18f * Mathf.Sin(2 * Mathf.PI * 100 * t) + .1f * Mathf.Sin(2 * Mathf.PI * 150 * t + 1) + Brown(r) * .2f);
            Wav("reel_ratchet", .5f, (t, r) => { float ph = (t * 16) % 1; return ph < .06f ? (float)(r.NextDouble() * 2 - 1) * (1 - ph / .06f) * .7f + Mathf.Sin(2 * Mathf.PI * 2600 * t) * (1 - ph / .06f) * .3f : 0; });
            Wav("door_close", 1.2f, (t, r) => { float thump = Mathf.Sin(2 * Mathf.PI * 62 * t) * Mathf.Exp(-t * 9) * .9f; float slam = (float)(r.NextDouble() * 2 - 1) * Mathf.Exp(-t * 28) * .6f; float latch = t > .09f && t < .14f ? Mathf.Sin(2 * Mathf.PI * 1800 * t) * Mathf.Exp(-(t - .09f) * 80) * .5f : 0; return thump + slam + latch; }, false);
            AssetDatabase.Refresh();
        }
        static float crackleEnv, hp, lastN, brown;
        static float Brown(System.Random r) { brown = Mathf.Clamp(brown + (float)(r.NextDouble() * 2 - 1) * .06f, -1, 1) * .995f; return brown; }
        static float Fbm(float x, float y) { float s = 0, a = .5f; for (int i = 0; i < 4; i++) { s += a * Mathf.PerlinNoise(x + 17 * i, y + 31 * i); x *= 2; y *= 2; a *= .5f; } return s; }
        static void Tex(string name, int w, int h, System.Func<float, float, Color> f, bool repeat = false)
        {
            var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, f((x + .5f) / w, (y + .5f) / h));
            string path = Gen + "/" + name + ".png"; File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t);
            AssetDatabase.ImportAsset(path); var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true; imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.SaveAndReimport();
        }
        static Texture2D Load(string name) => AssetDatabase.LoadAssetAtPath<Texture2D>(Gen + "/" + name + ".png");
        static Material Save(Material m, string name)
        {
            string path = Gen + "/" + name + ".mat"; var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing) { existing.shader = m.shader; existing.CopyPropertiesFromMaterial(m); existing.shaderKeywords = m.shaderKeywords; existing.renderQueue = m.renderQueue; Object.DestroyImmediate(m); EditorUtility.SetDirty(existing); return existing; }
            AssetDatabase.CreateAsset(m, path); return m;
        }
        static Material Lit(string name, Color c, float metal, float smooth, Texture2D tex = null, Color? emission = null)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")); m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth);
            if (tex) { m.SetTexture("_BaseMap", tex); m.SetTextureScale("_BaseMap", Vector2.one); }
            if (emission.HasValue) { m.EnableKeyword("_EMISSION"); m.SetColor("_EmissionColor", emission.Value); m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None; }
            return Save(m, name);
        }
        static Material Particle(string name, Texture2D tex, Color c, bool additive)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Particles/Unlit")); m.SetTexture("_BaseMap", tex); m.SetColor("_BaseColor", c);
            m.SetFloat("_Surface", 1); m.SetFloat("_Blend", additive ? 2 : 0); m.SetOverrideTag("RenderType", "Transparent");
            m.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha); m.SetFloat("_DstBlend", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_SrcBlendAlpha", (float)BlendMode.One); m.SetFloat("_DstBlendAlpha", (float)(additive ? BlendMode.One : BlendMode.OneMinusSrcAlpha));
            m.SetFloat("_ZWrite", 0); m.EnableKeyword("_SURFACE_TYPE_TRANSPARENT"); if (additive) m.EnableKeyword("_BLENDMODE_ADD"); m.renderQueue = (int)RenderQueue.Transparent;
            return Save(m, name);
        }
        static void Wav(string name, float seconds, System.Func<float, System.Random, float> f, bool loop = true)
        {
            const int rate = 22050; int n = (int)(seconds * rate); var s = new float[n]; var r = new System.Random(name.GetHashCode());
            hp = lastN = brown = crackleEnv = 0;
            for (int i = 0; i < n; i++) s[i] = f(i / (float)rate, r);
            if (loop) { int fade = rate / 10; for (int i = 0; i < fade; i++) { float k = i / (float)fade; s[i] = s[i] * k + s[n - fade + i] * (1 - k); } System.Array.Resize(ref s, n - fade); }
            float peak = s.Max(x => Mathf.Abs(x)); if (peak > 0) for (int i = 0; i < s.Length; i++) s[i] = s[i] / peak * .9f;
            string path = Gen + "/" + name + ".wav";
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write("RIFF".ToCharArray()); w.Write(36 + s.Length * 2); w.Write("WAVE".ToCharArray()); w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(s.Length * 2); foreach (var x in s) w.Write((short)(Mathf.Clamp(x, -1, 1) * 32767));
            }
            AssetDatabase.ImportAsset(path);
        }
        static AudioClip Clip(string name) => AssetDatabase.LoadAssetAtPath<AudioClip>(Gen + "/" + name + ".wav");
    }
}
