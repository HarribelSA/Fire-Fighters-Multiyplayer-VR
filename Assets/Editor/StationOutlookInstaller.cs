using System.IO;
using System.Linq;
using CityNightPresentation.Mission;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Tools/Fire Fighters/Apply Station Outlook Update (idempotent):
//  - station audio: the droning garage hum and the engine-like traffic rumble are removed; the rest is
//    re-balanced (quiet radio, office room tone, soft night wind + crickets outside the open garage)
//  - outside the garage door: a road leading out and a backdrop picture of the night city rendered from
//    the real city scene, so the crew sees a city in front of the station when the door opens
//  - city truck: steering wheel grabs a little earlier and holds a little longer
public static class StationOutlookInstaller
{
    const string StationPath = "Assets/FireStation/Scenes/Sample.unity";
    const string CityPath = "Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity";
    const string Dir = "Assets/FireStation/Backdrop";
    // 12 m: the city starts right behind the garage door (was 45 m: too far, looked like a picture).
    const float BackdropDistance = 12f, EyeHeight = 1.8f, VerticalFov = 62f;
    const int Width = 2400, Height = 1000;
    static Color SkyColor = new Color(.04f, .06f, .12f), GroundColor = new Color(.12f, .12f, .12f);

    [MenuItem("Tools/Fire Fighters/Apply Station Outlook Update")]
    public static string Apply()
    {
        var log = new System.Text.StringBuilder();
        Directory.CreateDirectory(Dir);
        var city = Open(CityPath); var station = Open(StationPath);
        var texture = RenderCityBackdrop(city, log);
        StationAudio(station, log);
        Backdrop(station, texture, log);
        var cab = Object.FindObjectsByType<FireTruckCabInteraction>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == city);
        if (cab) { cab.snapDistance = .18f; cab.releaseDistance = .45f; EditorUtility.SetDirty(cab); log.AppendLine("steering wheel: snap 0.18 m, release 0.45 m"); }
        EditorSceneManager.SaveScene(station); EditorSceneManager.SaveScene(city);
        Debug.Log("[StationOutlook]\n" + log);
        return log.ToString();
    }
    static Scene Open(string path) { var s = SceneManager.GetSceneByPath(path); return s.isLoaded ? s : EditorSceneManager.OpenScene(path, OpenSceneMode.Additive); }

    // ---------------- City picture ----------------
    static Texture2D RenderCityBackdrop(Scene city, System.Text.StringBuilder log)
    {
        string path = Dir + "/CityBackdrop.png";
        var mission = Object.FindObjectsByType<FireTruckMission>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(m => m.gameObject.scene == city);
        var truck = mission.transform;
        // Only the city is visible, lit with its own night settings; the truck itself is hidden.
        var previousActive = SceneManager.GetActiveScene(); SceneManager.SetActiveScene(city);
        var hidden = new System.Collections.Generic.List<GameObject>();
        for (int i = 0; i < SceneManager.sceneCount; i++) { var s = SceneManager.GetSceneAt(i); if (s == city || !s.isLoaded) continue; foreach (var g in s.GetRootGameObjects()) if (g.activeSelf) { g.SetActive(false); hidden.Add(g); } }
        var truckRenderers = truck.GetComponentsInChildren<Renderer>().Where(r => r.enabled).ToList(); foreach (var r in truckRenderers) r.enabled = false;
        var go = new GameObject("TmpBackdropCam"); SceneManager.MoveGameObjectToScene(go, city);
        var cam = go.AddComponent<Camera>(); cam.fieldOfView = VerticalFov; cam.nearClipPlane = .3f; cam.farClipPlane = 1200; cam.allowHDR = false;
        var forward = Vector3.ProjectOnPlane(truck.forward, Vector3.up).normalized;
        go.transform.SetPositionAndRotation(truck.position + forward * 4 + Vector3.up * (EyeHeight + .4f), Quaternion.LookRotation(forward, Vector3.up));
        var rt = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
        cam.targetTexture = rt; cam.Render();
        RenderTexture.active = rt; var tex = new Texture2D(Width, Height, TextureFormat.RGB24, false); tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0); tex.Apply(); RenderTexture.active = null;
        // Average colour of the top rows (night sky) and of the row just under the horizon (street level),
        // used to blend the picture into the surrounding sky and ground.
        Color Avg(int y0, int y1) { Color s = Color.black; int n = 0; for (int y = y0; y < y1; y++) for (int x = 0; x < Width; x += 8) { s += tex.GetPixel(x, y); n++; } return s / Mathf.Max(1, n); }
        SkyColor = Avg(Height - 40, Height); GroundColor = Avg(Height / 2 - 60, Height / 2 - 20);
        File.WriteAllBytes(path, tex.EncodeToPNG()); Object.DestroyImmediate(tex);
        Object.DestroyImmediate(go); rt.Release();
        foreach (var r in truckRenderers) r.enabled = true; foreach (var g in hidden) g.SetActive(true);
        if (previousActive.IsValid() && previousActive.isLoaded) SceneManager.SetActiveScene(previousActive);
        AssetDatabase.ImportAsset(path);
        var imp = (TextureImporter)AssetImporter.GetAtPath(path);
        imp.textureType = TextureImporterType.Default; imp.sRGBTexture = true; imp.wrapMode = TextureWrapMode.Clamp; imp.mipmapEnabled = true; imp.maxTextureSize = 2048;
        var android = imp.GetPlatformTextureSettings("Android"); android.overridden = true; android.maxTextureSize = 2048; android.format = TextureImporterFormat.ASTC_6x6; imp.SetPlatformTextureSettings(android);
        imp.SaveAndReimport();
        log.AppendLine("city backdrop rendered from the city street at the truck start: " + path);
        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    // ---------------- Station audio ----------------
    static void StationAudio(Scene station, System.Text.StringBuilder log)
    {
        var amb = station.GetRootGameObjects().FirstOrDefault(g => g.name == "STATION_AMBIENCE");
        if (!amb) { log.AppendLine("STATION_AMBIENCE not found"); return; }
        void Remove(string n) { var t = amb.transform.Find(n); if (t) { Object.DestroyImmediate(t.gameObject); log.AppendLine("removed " + n); } }
        Remove("GarageHum"); Remove("OutsideTraffic");
        void Tune(string n, float vol, float min, float max) { var t = amb.transform.Find(n); var a = t ? t.GetComponent<AudioSource>() : null; if (!a) return; a.volume = vol; a.minDistance = min; a.maxDistance = max; EditorUtility.SetDirty(a); }
        Tune("DispatchRadio", .14f, 1, 8);
        Tune("OfficeRoomTone", .16f, 2, 14);
        Tune("OutsideWind", .14f, 4, 28);
        var crickets = AssetDatabase.LoadAssetAtPath<AudioClip>("Assets/FireCenter/Audio/NightCrickets_Loop.wav");
        var old = amb.transform.Find("OutsideCrickets"); if (old) Object.DestroyImmediate(old.gameObject);
        if (crickets)
        {
            var g = new GameObject("OutsideCrickets"); g.transform.SetParent(amb.transform, false); g.transform.position = new Vector3(-10, 1, -5);
            var a = g.AddComponent<AudioSource>(); a.clip = crickets; a.loop = true; a.playOnAwake = true; a.volume = .12f; a.spatialBlend = 1; a.rolloffMode = AudioRolloffMode.Linear; a.minDistance = 6; a.maxDistance = 30; a.dopplerLevel = 0;
            log.AppendLine("added soft crickets outside the garage");
        }
        // The two ceiling air conditioners were at full volume: keep them as a faint background only.
        foreach (var a in Object.FindObjectsByType<AudioSource>(FindObjectsInactive.Include, FindObjectsSortMode.None).Where(a => a.gameObject.scene == station && a.clip && a.clip.name == "AirConditioner"))
        { a.volume = .25f; EditorUtility.SetDirty(a); }
        log.AppendLine("air conditioners: volume 0.25");
        EditorSceneManager.MarkSceneDirty(station);
    }

    // ---------------- Road + backdrop outside the garage ----------------
    static void Backdrop(Scene station, Texture2D texture, System.Text.StringBuilder log)
    {
        var view = Object.FindObjectsByType<FirefighterVR.Station.StationMissionView>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(v => v.gameObject.scene == station);
        var doorBounds = view.garage.GetComponentsInChildren<Renderer>().Select(r => r.bounds).Aggregate((a, b) => { a.Encapsulate(b); return a; });
        // Outside = direction the parked truck faces (station truck faces -X, the door is in front of it).
        var outward = Vector3.ProjectOnPlane(view.driverSeat.forward, Vector3.up).normalized;
        var door = new Vector3(doorBounds.center.x, 0, doorBounds.center.z);
        var old = station.GetRootGameObjects().FirstOrDefault(g => g.name == "GARAGE_CITY_OUTLOOK"); if (old) Object.DestroyImmediate(old);
        var root = new GameObject("GARAGE_CITY_OUTLOOK"); SceneManager.MoveGameObjectToScene(root, station);
        root.transform.SetPositionAndRotation(door, Quaternion.LookRotation(outward, Vector3.up));
        var rot = root.transform.rotation;
        float groundY = -.18f; // just above the large ground plane (y -0.2) outside the apron

        // Backdrop: same field of view as the render, so the street lines up with the road from the door.
        float h = 2 * BackdropDistance * Mathf.Tan(VerticalFov * .5f * Mathf.Deg2Rad), w = h * Width / (float)Height;
        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(quad.GetComponent<Collider>());
        quad.name = "CityBackdrop"; quad.transform.SetParent(root.transform, false);
        quad.transform.localPosition = new Vector3(0, EyeHeight + groundY, BackdropDistance); quad.transform.localRotation = Quaternion.identity; quad.transform.localScale = new Vector3(w, h, 1);
        quad.GetComponent<Renderer>().sharedMaterial = Unlit("CityBackdrop", Color.white, texture);
        Shadowless(quad);
        // Night sky behind and around the picture (same colour as its top edge) so there is no hard frame,
        // and dark ground on both sides of the road up to the picture instead of the bright grey plane.
        var sky = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(sky.GetComponent<Collider>());
        sky.name = "NightSky"; sky.transform.SetParent(root.transform, false);
        sky.transform.localPosition = new Vector3(0, 60, BackdropDistance + .5f); sky.transform.localScale = new Vector3(600, 160, 1);
        sky.GetComponent<Renderer>().sharedMaterial = Unlit("CityBackdropSky", SkyColor, null); Shadowless(sky);
        var groundFill = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(groundFill.GetComponent<Collider>());
        groundFill.name = "NightGround"; groundFill.transform.SetParent(root.transform, false);
        groundFill.transform.localPosition = new Vector3(0, groundY - .004f, 3 + (BackdropDistance - 3) * .5f); groundFill.transform.localRotation = Quaternion.Euler(90, 0, 0);
        groundFill.transform.localScale = new Vector3(240, BackdropDistance - 3, 1);
        groundFill.GetComponent<Renderer>().sharedMaterial = Lit("StationOutsideGround", GroundColor * 1.4f, .1f); Shadowless(groundFill);

        // Road: asphalt strip from the apron to the backdrop, dashed centre line, kerbs on both sides.
        float start = 3.5f, length = BackdropDistance - start + 2, width = 9;
        Strip(root.transform, "Road", new Vector3(0, groundY, start + length * .5f), new Vector3(width, length, 1), Lit("StationRoadAsphalt", new Color(.07f, .07f, .075f), .55f));
        var paint = Lit("StationRoadPaint", new Color(.85f, .85f, .8f), .3f);
        for (float z = start + 1; z < start + length - 1; z += 4) Strip(root.transform, "Dash", new Vector3(0, groundY + .005f, z + 1), new Vector3(.15f, 2f, 1), paint);
        foreach (float x in new[] { -width * .5f + .35f, width * .5f - .35f }) Strip(root.transform, "EdgeLine", new Vector3(x, groundY + .005f, start + length * .5f), new Vector3(.12f, length, 1), paint);
        var kerb = Lit("StationRoadKerb", new Color(.42f, .42f, .4f), .2f);
        foreach (float x in new[] { -width * .5f - .15f, width * .5f + .15f })
        {
            var k = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(k.GetComponent<Collider>()); k.name = "Kerb"; k.transform.SetParent(root.transform, false);
            k.transform.localPosition = new Vector3(x, groundY + .07f, start + length * .5f); k.transform.localScale = new Vector3(.3f, .15f, length); k.GetComponent<Renderer>().sharedMaterial = kerb; Shadowless(k);
        }
        foreach (var t in root.GetComponentsInChildren<Transform>()) t.gameObject.isStatic = true;
        log.AppendLine($"garage outlook: road {length:F0} m, backdrop {w:F0} x {h:F0} m at {BackdropDistance} m from the door");
        EditorSceneManager.MarkSceneDirty(station);
    }
    static void Strip(Transform parent, string name, Vector3 local, Vector3 scale, Material m)
    {
        var q = GameObject.CreatePrimitive(PrimitiveType.Quad); Object.DestroyImmediate(q.GetComponent<Collider>());
        q.name = name; q.transform.SetParent(parent, false); q.transform.localPosition = local; q.transform.localRotation = Quaternion.Euler(90, 0, 0); q.transform.localScale = scale;
        q.GetComponent<Renderer>().sharedMaterial = m; Shadowless(q);
    }
    static void Shadowless(GameObject g) { var r = g.GetComponent<Renderer>(); r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; }
    static Material Unlit(string name, Color c, Texture2D tex)
    {
        string path = Dir + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); if (tex) m.SetTexture("_BaseMap", tex); EditorUtility.SetDirty(m); return m;
    }
    static Material Lit(string name, Color c, float smooth)
    {
        string path = Dir + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
        m.SetColor("_BaseColor", c); m.SetFloat("_Smoothness", smooth); m.SetFloat("_Metallic", 0); EditorUtility.SetDirty(m); return m;
    }
}
