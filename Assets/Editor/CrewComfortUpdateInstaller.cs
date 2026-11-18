using System.IO;
using System.Linq;
using CityNightPresentation;
using CityNightPresentation.Mission;
using FirefighterVR.Station;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools/Fire Fighters/Apply Crew Update (idempotent; safe to run again):
//  City  : siren pull handle flush on the cab ceiling, ESP32 Bluetooth light link, traffic engine audio,
//          car colliders fitted to the bodies, invisible cab divider between the two seats.
//  Station: ambience (dispatch radio, room tone, garage hum, outside traffic/wind), cab divider in the station truck.
//  Lobby : 5 s departure grace, floor-relative XR tracking.
//  Player: Android INTERNET permission forced (Quest joins the online session reliably).
public static class CrewComfortUpdateInstaller
{
    const string CityPath = "Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity";
    const string StationPath = "Assets/FireStation/Scenes/Sample.unity";
    const string LobbyPath = "Assets/S1Test.unity";
    const string AudioDir = "Assets/FirefighterAvatar/Station/Audio/Generated";

    [MenuItem("Tools/Fire Fighters/Apply Crew Update")]
    public static string Apply()
    {
        var log = new System.Text.StringBuilder();
        PlayerSettings.Android.forceInternetPermission = true; log.AppendLine("Android INTERNET permission: required");
        var city = Open(CityPath); ApplyCity(log); EditorSceneManager.SaveScene(city);
        var station = Open(StationPath); ApplyStation(log); EditorSceneManager.SaveScene(station);
        var lobby = Open(LobbyPath); ApplyLobby(log); EditorSceneManager.SaveScene(lobby);
        AssetDatabase.SaveAssets();
        Debug.Log("[CrewUpdate]\n" + log);
        return log.ToString();
    }

    static T Get<T>(GameObject g) where T : Component { var c = g.GetComponent<T>(); return c ? c : g.AddComponent<T>(); }
    static UnityEngine.SceneManagement.Scene Open(string path)
    {
        var s = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(path);
        return s.isLoaded ? s : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
    }
    static T Find<T>(string scenePath) where T : Component =>
        Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene.path == scenePath);

    // ---------------- City ----------------
    static void ApplyCity(System.Text.StringBuilder log)
    {
        var mission = Find<FireTruckMission>(CityPath); var truck = mission.transform;
        var e = truck.GetComponent<FireTruckEmergency>();
        if (e && e.pullMount)
        {
            // Flush with the flat cab headliner above-front of the Driver, perfectly level with the truck.
            var local = new Vector3(-.42f, 0, 2.45f);
            float ceiling = CeilingY(truck, local, e.pullMount, out bool hit);
            var mount = e.pullMount.Find("Mount"); float half = mount ? mount.localScale.z * .5f : .0125f;
            local.y = (hit ? ceiling : 2.69f) - half;
            e.pullMount.SetPositionAndRotation(truck.TransformPoint(local), truck.rotation * Quaternion.Euler(-90, 0, 0));
            if (e.pullHandle) e.pullHandle.localPosition = e.closedLocalPosition;
            EditorUtility.SetDirty(e.pullMount); EditorUtility.SetDirty(e);
            log.AppendLine($"siren handle: ceiling y={ceiling:F3} (hit={hit}) mount y={local.y:F3}, level");
        }
        var esp = Get<Esp32SirenLight>(truck.gameObject);
        esp.mission = mission; esp.sirenOnCommand = "OFF"; esp.sirenOffCommand = "ON"; EditorUtility.SetDirty(esp); // this relay lights on "OFF" log.AppendLine("ESP32 light link on " + esp.portName);

        var traffic = mission.traffic;
        var audio = Get<CityTrafficAudio>(traffic.gameObject);
        audio.engineClips = new[] { "engine_S1", "engine_S2", "engine_N" }
            .Select(n => AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/Basic Arcade Car Controller/Audio Placeholder/" + n + ".wav")).Where(c => c).ToArray();
        EditorUtility.SetDirty(audio); log.AppendLine("traffic engine audio clips " + audio.engineClips.Length);
        foreach (var v in traffic.vehicles)
        {
            if (!v.body) continue;
            var box = Get<BoxCollider>(v.body.gameObject);
            var b = LocalBounds(v.body); box.center = b.center; box.size = b.size; box.isTrigger = false;
            var rb = Get<Rigidbody>(v.body.gameObject);
            rb.isKinematic = true; rb.useGravity = false; rb.interpolation = RigidbodyInterpolation.None;
            EditorUtility.SetDirty(box); EditorUtility.SetDirty(rb);
            log.AppendLine($"  car {v.body.name}: collider {box.size:F1}");
        }
        Divider(truck, new Vector3(0, 1.85f, 2.3f), Quaternion.identity, log, "city truck");
        EditorSceneManager.MarkSceneDirty(mission.gameObject.scene);
    }
    static float CeilingY(Transform truck, Vector3 local, Transform exclude, out bool hit)
    {
        var temps = truck.GetComponentsInChildren<MeshFilter>().Where(m => m.sharedMesh && !m.transform.IsChildOf(exclude))
            .Select(m => { var g = new GameObject("tmp"); g.transform.SetParent(m.transform, false); g.AddComponent<MeshCollider>().sharedMesh = m.sharedMesh; return g; }).ToList();
        Physics.SyncTransforms();
        var o = truck.TransformPoint(new Vector3(local.x, 2.0f, local.z));
        var hits = Physics.RaycastAll(o, truck.up, 1.5f).Where(h => temps.Contains(h.collider.gameObject)).OrderBy(h => h.distance).ToArray();
        hit = hits.Length > 0; float y = hit ? truck.InverseTransformPoint(hits[0].point).y : 0;
        foreach (var g in temps) Object.DestroyImmediate(g);
        return y;
    }
    static Bounds LocalBounds(Transform root)
    {
        bool first = true; var b = new Bounds();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = root.InverseTransformPoint(r.transform.TransformPoint(c));
                if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        }
        // Keep a small gap above the road so the box never scrapes the asphalt collider.
        if (!first) { float bottom = Mathf.Max(b.min.y, .12f); var max = b.max; b.SetMinMax(new Vector3(b.min.x, bottom, b.min.z), max); }
        return b;
    }
    // Invisible wall between the Driver and the passenger seat (blocks walking/grabbing through the console).
    static void Divider(Transform truck, Vector3 local, Quaternion localRot, System.Text.StringBuilder log, string label)
    {
        var d = truck.Find("CabCrewDivider"); if (!d) { d = new GameObject("CabCrewDivider").transform; d.SetParent(truck, false); }
        d.localPosition = local; d.localRotation = localRot; d.localScale = Vector3.one; d.gameObject.layer = 0;
        var c = Get<BoxCollider>(d.gameObject); c.size = new Vector3(.06f, 1.0f, 1.2f); c.center = Vector3.zero; c.isTrigger = false;
        EditorUtility.SetDirty(d); EditorUtility.SetDirty(c); log.AppendLine("cab divider: " + label);
    }

    // ---------------- Station ----------------
    static void ApplyStation(System.Text.StringBuilder log)
    {
        var view = Find<StationMissionView>(StationPath); var scene = view.gameObject.scene;
        view.alarmSoundSeconds = 10; EditorUtility.SetDirty(view);
        var root = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "STATION_AMBIENCE");
        if (root) Object.DestroyImmediate(root);
        root = new GameObject("STATION_AMBIENCE"); UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, scene);
        Directory.CreateDirectory(AudioDir);
        var radio = MakeWav("station_dispatch_radio", 24, Radio);
        var traffic = MakeWav("station_distant_traffic", 30, DistantTraffic);
        var hum = MakeWav("station_garage_hum", 6, Hum);
        var roomTone = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/FireCenter/Audio/OfficeRoomTone_Loop.wav");
        var wind = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/FireCenter/Audio/NightWind_Loop.wav");
        Vector3 desk = view.dispatchSeat ? view.dispatchSeat.position : new Vector3(9.8f, 0, 7.4f);
        Vector3 garage = view.garage ? view.garage.transform.position : new Vector3(.8f, 4.3f, -5);
        Source(root.transform, "DispatchRadio", radio, desk + view.dispatchSeat.forward * .6f + Vector3.up * 1.0f, .32f, 1, 12);
        Source(root.transform, "OfficeRoomTone", roomTone, desk + Vector3.up * 2.2f, .22f, 2, 16);
        Source(root.transform, "GarageHum", hum, new Vector3(garage.x + 4, 4.2f, garage.z), .12f, 3, 18);
        Source(root.transform, "OutsideTraffic", traffic, new Vector3(garage.x - 5, 1.5f, garage.z), .5f, 5, 40);
        Source(root.transform, "OutsideWind", wind, new Vector3(garage.x - 7, 2.5f, garage.z - 4), .18f, 4, 30);
        log.AppendLine("station ambience: 5 sources");
        if (view.driverSeat && view.passengerSeat)
        {
            var truck = view.driverSeat.root;
            var mid = (view.driverSeat.position + view.passengerSeat.position) * .5f + Vector3.up * .7f;
            var rot = Quaternion.LookRotation(view.driverSeat.forward, Vector3.up);
            Divider(truck, truck.InverseTransformPoint(mid + view.driverSeat.forward * .1f), Quaternion.Inverse(truck.rotation) * rot, log, "station truck (" + truck.name + ")");
        }
        EditorSceneManager.MarkSceneDirty(scene);
    }
    static void Source(Transform parent, string name, AudioClip clip, Vector3 position, float volume, float min, float max)
    {
        if (!clip) return;
        var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.position = position;
        var a = g.AddComponent<AudioSource>(); a.clip = clip; a.loop = true; a.playOnAwake = true; a.volume = volume;
        a.spatialBlend = 1; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = min; a.maxDistance = max; a.dopplerLevel = 0;
    }

    // ---------------- Lobby ----------------
    static void ApplyLobby(System.Text.StringBuilder log)
    {
        var flow = Find<StationSessionFlow>(LobbyPath);
        if (flow) { flow.departureSeconds = 5; EditorUtility.SetDirty(flow); log.AppendLine("departure grace 5 s"); }
        var origin = Find<Unity.XR.CoreUtils.XROrigin>(LobbyPath);
        if (origin) { origin.RequestedTrackingOriginMode = Unity.XR.CoreUtils.XROrigin.TrackingOriginMode.Floor; EditorUtility.SetDirty(origin); log.AppendLine("XR Origin tracking: Floor (camera y offset " + origin.CameraYOffset.ToString("F2") + " as fallback)"); }
        EditorSceneManager.MarkSceneDirty(flow ? flow.gameObject.scene : origin.gameObject.scene);
    }

    // ---------------- Procedural audio ----------------
    const int Rate = 22050;
    static AudioClip MakeWav(string name, float seconds, System.Func<float, System.Random, float> f)
    {
        string path = AudioDir + "/" + name + ".wav";
        if (!File.Exists(path))
        {
            int n = (int)(seconds * Rate); var rnd = new System.Random(name.GetHashCode());
            using (var w = new BinaryWriter(File.Create(path)))
            {
                w.Write("RIFF".ToCharArray()); w.Write(36 + n * 2); w.Write("WAVE".ToCharArray()); w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
                w.Write("data".ToCharArray()); w.Write(n * 2);
                float fadeLen = .005f * Rate;
                for (int i = 0; i < n; i++)
                {
                    float t = i / (float)Rate, s = f(t, rnd);
                    float edge = Mathf.Min(1, Mathf.Min(i, n - 1 - i) / fadeLen); // click-free loop seam
                    w.Write((short)(Mathf.Clamp(s * edge, -1, 1) * 32000));
                }
            }
            AssetDatabase.ImportAsset(path);
            var imp = (AudioImporter)AssetImporter.GetAtPath(path);
            var settings = imp.defaultSampleSettings; settings.loadType = AudioClipLoadType.CompressedInMemory; settings.compressionFormat = AudioCompressionFormat.Vorbis; settings.quality = .5f;
            imp.defaultSampleSettings = settings; imp.forceToMono = true; imp.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
    static float lp1, lp2, lp3, bp;
    // Dispatch radio: squelch clicks, short band-limited "voice" bursts with syllable rhythm, quiet gaps.
    static float Radio(float t, System.Random r)
    {
        float cycle = t % 8f; float noise = (float)(r.NextDouble() * 2 - 1);
        lp1 += (noise - lp1) * .35f; bp += (lp1 - bp) * .08f; float band = lp1 - bp;
        bool talk = cycle > .4f && cycle < 3.4f || cycle > 5.0f && cycle < 6.2f;
        float syll = .5f + .5f * Mathf.Sin(t * 2 * Mathf.PI * 4.2f + Mathf.Sin(t * 3.1f) * 2); syll *= syll;
        float voice = talk ? band * 2.2f * (.35f + syll) * (1 + .3f * Mathf.Sin(t * 2 * Mathf.PI * 180)) : 0;
        float squelch = (cycle > .3f && cycle < .4f) || (cycle > 3.4f && cycle < 3.47f) || (cycle > 6.2f && cycle < 6.26f) ? noise * .5f : 0;
        float beep = cycle > 7.0f && cycle < 7.12f ? Mathf.Sin(t * 2 * Mathf.PI * 1250) * .25f : 0;
        return (voice + squelch + beep + noise * .015f) * .8f;
    }
    // Distant city: low rumble with slow swells of passing cars and a rare far horn.
    static float DistantTraffic(float t, System.Random r)
    {
        float noise = (float)(r.NextDouble() * 2 - 1);
        lp2 += (noise - lp2) * .02f; lp3 += (lp2 - lp3) * .05f;
        float swell = .45f + .3f * Mathf.Sin(t * .41f) + .25f * Mathf.Max(0, Mathf.Sin(t * .23f + 1));
        float pass = Mathf.Exp(-Mathf.Pow(((t % 11f) - 5.5f) / 1.6f, 2));
        float horn = (t % 30f) > 17.5f && (t % 30f) < 18.1f ? (Mathf.Sin(t * 2 * Mathf.PI * 392) + .5f * Mathf.Sin(t * 2 * Mathf.PI * 494)) * .03f : 0;
        return lp3 * 9f * (swell + pass * .8f) + lp2 * 1.5f * pass + horn;
    }
    // Fluorescent / ventilation hum for the apparatus bay.
    static float Hum(float t, System.Random r)
    {
        float noise = (float)(r.NextDouble() * 2 - 1);
        return .22f * Mathf.Sin(t * 2 * Mathf.PI * 100) + .09f * Mathf.Sin(t * 2 * Mathf.PI * 200) + .05f * Mathf.Sin(t * 2 * Mathf.PI * 300) + noise * .03f;
    }
}
