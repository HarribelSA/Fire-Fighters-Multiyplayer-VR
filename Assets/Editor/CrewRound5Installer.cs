using System.IO;
using System.Linq;
using CityNightPresentation.Mission;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools/Fire Fighters/Apply Crew Update 5 (idempotent):
//  lobby  : 15 s wait before the dispatch alert, faster walking, waiting-room drone muted, quieter alarm bells
//  station: quieter alarm bells, hand blocker on the station truck, city picture right behind the garage door
//  city   : truck throttle / brake / speed sounds, quieter siren, hand blocker on the truck, faster bots
//  audio  : procedural brake squeal, air-brake release, road noise, engine rev and the fire pain voice
public static class CrewRound5Installer
{
    const string LobbyPath = "Assets/S1Test.unity", StationPath = "Assets/FireStation/Scenes/Sample.unity";
    const string CityPath = "Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity";
    const string AudioDir = "Assets/CityNightPresentation/Mission/Assets/Generated", PainDir = "Assets/CityNightPresentation/Fireground/Resources";

    [MenuItem("Tools/Fire Fighters/Apply Crew Update 5")]
    public static string Apply()
    {
        if (EditorApplication.isPlaying) return "Stop Play mode first.";
        var log = new System.Text.StringBuilder();
        Directory.CreateDirectory(AudioDir); Directory.CreateDirectory(PainDir);
        var lobby = Open(LobbyPath); var station = Open(StationPath); var city = Open(CityPath);

        // ---- Lobby ----
        var flow = Find<FirefighterVR.Station.StationSessionFlow>(lobby); if (flow) { flow.explorationSeconds = 15; Dirty(flow); log.AppendLine("dispatch alert after 15 s"); }
        foreach (var m in All<UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider>(lobby)) { m.moveSpeed = 2.0f; Dirty(m); log.AppendLine("walk speed " + m.name + " = 2.0 m/s"); }
        foreach (var a in All<AudioSource>(lobby))
        {
            if (!a.clip) continue; string n = a.clip.name;
            if (n.StartsWith("LobbyAmbientPad") || n.StartsWith("245773")) { a.mute = true; a.playOnAwake = false; Dirty(a); log.AppendLine("muted waiting-room drone: " + a.name); }
            else if (n == "OfficeRoomTone_Loop") { a.volume = .1f; Dirty(a); }
            else if (n == "EmergencyBell") { a.volume = .3f; Dirty(a); }
        }

        // ---- Station ----
        foreach (var a in All<AudioSource>(station)) if (a.clip && a.clip.name == "EmergencyBell") { a.volume = .35f; Dirty(a); }
        log.AppendLine("station alarm bells: 0.35");
        var stationTruck = station.GetRootGameObjects().FirstOrDefault(g => g.name == "firetruck");
        if (stationTruck)
        {
            if (!stationTruck.GetComponentsInChildren<Collider>(true).Any(c => !c.isTrigger))
            {
                var b = LocalBounds(stationTruck.transform); var box = stationTruck.AddComponent<BoxCollider>(); box.center = b.center; box.size = b.size; log.AppendLine("station truck body collider " + b.size.ToString("F1"));
            }
            Get<FirefighterVR.AvatarHandBlocker>(stationTruck); log.AppendLine("hand blocker: station truck");
        }

        // ---- City ----
        var mission = Find<FireTruckMission>(city); var truck = mission.gameObject;
        var e = truck.GetComponent<FireTruckEmergency>(); if (e && e.siren) { e.siren.volume = .2f; Dirty(e.siren); log.AppendLine("siren volume 0.2"); }
        Get<FirefighterVR.AvatarHandBlocker>(truck); log.AppendLine("hand blocker: city truck");
        var audio = Get<TruckEngineAudio>(truck); audio.mission = mission;
        audio.idleClip = Clip("Assets/Basic Arcade Car Controller/Audio Placeholder/engine_N.wav");
        audio.driveClip = Clip("Assets/Basic Arcade Car Controller/Audio Placeholder/engine_S2.wav");
        audio.revClip = Wav(AudioDir, "truck_rev", 1.1f, Rev, false);
        audio.roadClip = Wav(AudioDir, "truck_road_noise", 4f, Road, true);
        audio.brakeSqueal = Wav(AudioDir, "truck_brake_squeal", 3f, Squeal, true);
        audio.airRelease = Wav(AudioDir, "truck_air_release", 1.1f, Air, false);
        Dirty(audio); log.AppendLine("truck sounds: engine idle/drive, rev, road, brake squeal, air release");
        foreach (var bot in All<FireTruckBotDriver>(city)) { bot.cruiseMetresPerSecond = 11; Dirty(bot); log.AppendLine("bot driver cruise 11 m/s"); }
        foreach (var bot in All<CityNightPresentation.Fireground.FiregroundBot>(city)) { bot.walkSpeed = 1.9f; Dirty(bot); log.AppendLine("fireground bot walk 1.9 m/s"); }
        Wav(PainDir, "FirePain", 7f, Pain, true); log.AppendLine("fire pain voice: Resources/FirePain.wav");

        // Bot followers live in prefabs/scenes: speed them up wherever they are serialized.
        foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
        {
            string p = AssetDatabase.GUIDToAssetPath(guid); if (!p.StartsWith("Assets/FirefighterAvatar")) continue;
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(p); var f = go ? go.GetComponentInChildren<FirefighterVR.StationBot.StationBotFollower>(true) : null;
            if (f && f.speed < 1.8f) { f.speed = 1.8f; EditorUtility.SetDirty(go); log.AppendLine("bot walk 1.8 m/s in " + p); }
        }
        foreach (var s in new[] { lobby, station, city }) foreach (var f in All<FirefighterVR.StationBot.StationBotFollower>(s)) { if (f.speed < 1.8f) { f.speed = 1.8f; Dirty(f); log.AppendLine("bot walk 1.8 m/s in " + s.name); } }

        log.AppendLine(StationOutlookInstaller.Apply());
        foreach (var s in new[] { lobby, station, city }) { EditorSceneManager.MarkSceneDirty(s); EditorSceneManager.SaveScene(s); }
        AssetDatabase.SaveAssets();
        Debug.Log("[CrewUpdate5]\n" + log);
        return log.ToString();
    }

    static Scene Open(string path) { var s = SceneManager.GetSceneByPath(path); return s.isLoaded ? s : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); }
    static T[] All<T>(Scene s) where T : Component => Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(c => c.gameObject.scene == s).ToArray();
    static T Find<T>(Scene s) where T : Component => All<T>(s).FirstOrDefault();
    static T Get<T>(GameObject g) where T : Component { var c = g.GetComponent<T>(); return c ? c : g.AddComponent<T>(); }
    static void Dirty(Object o) => EditorUtility.SetDirty(o);
    static AudioClip Clip(string p) => AssetDatabase.LoadAssetAtPath<AudioClip>(p);
    static Bounds LocalBounds(Transform root)
    {
        bool first = true; var b = new Bounds();
        foreach (var r in root.GetComponentsInChildren<MeshRenderer>())
        {
            var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue; var mb = mf.sharedMesh.bounds;
            for (int i = 0; i < 8; i++)
            {
                var c = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                var p = root.InverseTransformPoint(r.transform.TransformPoint(c)); if (first) { b = new Bounds(p, Vector3.zero); first = false; } else b.Encapsulate(p);
            }
        }
        return b;
    }

    // ---------------- Procedural audio ----------------
    const int Rate = 22050;
    static AudioClip Wav(string dir, string name, float seconds, System.Func<float, System.Random, float> f, bool loop)
    {
        string path = dir + "/" + name + ".wav"; int n = (int)(seconds * Rate); var rnd = new System.Random(name.GetHashCode());
        ResetFilters();
        using (var w = new BinaryWriter(File.Create(path)))
        {
            w.Write("RIFF".ToCharArray()); w.Write(36 + n * 2); w.Write("WAVE".ToCharArray()); w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)1); w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(n * 2);
            float edge = (loop ? .01f : .004f) * Rate;
            for (int i = 0; i < n; i++) { float s = f(i / (float)Rate, rnd) * Mathf.Min(1, Mathf.Min(i, n - 1 - i) / edge); w.Write((short)(Mathf.Clamp(s, -1, 1) * 30000)); }
        }
        AssetDatabase.ImportAsset(path);
        var imp = (AudioImporter)AssetImporter.GetAtPath(path); var st = imp.defaultSampleSettings;
        st.loadType = AudioClipLoadType.CompressedInMemory; st.compressionFormat = AudioCompressionFormat.Vorbis; st.quality = .6f; imp.defaultSampleSettings = st; imp.forceToMono = true; imp.SaveAndReimport();
        return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
    }
    static float a1, a2, b1, b2, lp, lp2, phase;
    static void ResetFilters() { a1 = a2 = b1 = b2 = lp = lp2 = phase = 0; }
    static float Noise(System.Random r) => (float)(r.NextDouble() * 2 - 1);
    // Diesel rev: pulsing low harmonics rising then settling.
    static float Rev(float t, System.Random r)
    {
        float f = Mathf.Lerp(38, 95, Mathf.SmoothStep(0, 1, Mathf.Clamp01(t / .45f))) - Mathf.Clamp01((t - .5f) / .6f) * 25;
        phase += f / Rate; float p = phase % 1;
        float saw = (p * 2 - 1) * .5f + Mathf.Sin(phase * 2 * Mathf.PI) * .5f + Mathf.Sin(phase * 4 * Mathf.PI) * .25f;
        lp += (Noise(r) - lp) * .15f;
        float env = Mathf.Clamp01(t / .06f) * Mathf.Clamp01((1.1f - t) / .45f);
        return (saw * .6f + lp * .35f) * env * .8f;
    }
    // Tyre / road rumble that loops.
    static float Road(float t, System.Random r) { lp += (Noise(r) - lp) * .06f; lp2 += (lp - lp2) * .2f; return lp2 * 3.5f * (.85f + .15f * Mathf.Sin(t * 2 * Mathf.PI * .5f)); }
    // Brake squeal: wobbling high tone over gritty noise.
    static float Squeal(float t, System.Random r)
    {
        float f = 2600 + 140 * Mathf.Sin(t * 2 * Mathf.PI * 1.5f) + 60 * Mathf.Sin(t * 2 * Mathf.PI * 7);
        phase += f / Rate; lp += (Noise(r) - lp) * .5f;
        return (Mathf.Sin(phase * 2 * Mathf.PI) * .35f + Mathf.Sin(phase * 4.02f * Mathf.PI) * .08f + lp * .12f) * (.8f + .2f * Mathf.Sin(t * 2 * Mathf.PI * .66f));
    }
    // Air-brake release: sharp hiss decaying.
    static float Air(float t, System.Random r) { float n = Noise(r); lp += (n - lp) * .9f; float hi = n - lp * .6f; return hi * .55f * Mathf.Exp(-t * 3.2f) * Mathf.Clamp01(t / .015f); }

    // Pain voice: grunts, coughs and a groan, with breathing gaps (formant-filtered glottal pulses).
    static float Formant(float x, float freq, float bw, ref float y1, ref float y2)
    {
        float rr = Mathf.Exp(-Mathf.PI * bw / Rate), c = 2 * rr * Mathf.Cos(2 * Mathf.PI * freq / Rate);
        float y = (1 - rr) * x + c * y1 - rr * rr * y2; y2 = y1; y1 = y; return y;
    }
    static float Voice(float t, System.Random r, float f0, float f1, float f2, float amp)
    {
        phase += f0 * (1 + .02f * Noise(r)) / Rate; float pulse = (phase % 1) < .12f ? 1 : -.12f;
        float x = pulse + Noise(r) * .25f;
        return (Formant(x, f1, 90, ref a1, ref a2) + .6f * Formant(x, f2, 120, ref b1, ref b2)) * amp;
    }
    static float Pain(float t, System.Random r)
    {
        float s = 0;
        // 0.2-0.75 s: "ugh!" (f0 falls 170 -> 120)
        if (t > .2f && t < .75f) { float k = (t - .2f) / .55f; s += Voice(t, r, Mathf.Lerp(170, 120, k), 560, 1050, Mathf.Sin(k * Mathf.PI) * 1.2f); }
        // 1.4-2.6 s: three coughs (noisy bursts through an "ah" formant)
        for (int i = 0; i < 3; i++) { float c0 = 1.4f + i * .38f; if (t > c0 && t < c0 + .22f) { float k = (t - c0) / .22f; s += Formant(Noise(r), 700, 300, ref a1, ref a2) * 2.2f * Mathf.Exp(-k * 4) * Mathf.Clamp01(k * 20); } }
        // 3.3-4.6 s: long groan "aaah" (f0 140 -> 105, slight tremolo)
        if (t > 3.3f && t < 4.6f) { float k = (t - 3.3f) / 1.3f; s += Voice(t, r, Mathf.Lerp(140, 105, k) * (1 + .03f * Mathf.Sin(t * 30)), 720, 1150, Mathf.Sin(k * Mathf.PI) * 1.0f); }
        // breaths between
        float breath = (t > .9f && t < 1.3f) || (t > 2.7f && t < 3.2f) || (t > 4.8f && t < 6.6f) ? Formant(Noise(r), 1800, 900, ref b1, ref b2) * .35f * Mathf.Sin(((t * 1.7f) % 1) * Mathf.PI) : 0;
        return Mathf.Clamp(s * .45f + breath, -1, 1);
    }
}
