using System.Collections.Generic;
using System.IO;
using System.Linq;
using FirefighterVR.Station;
using FirefighterVR.StationBot;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FireCenter.EditorTools
{
    // Integrates FireCenterLobby as the visible lobby of S1Test without duplicating any system.
    // Idempotent: safe to re-run. Backups were taken before the first run.
    public static class FireCenterLobbyInstaller
    {
        const string LobbyPath = "Assets/FireCenter/Scenes/FireCenterLobby.unity", S1Path = "Assets/S1Test.unity", Gen = "Assets/FireCenter/Generated";

        [MenuItem("Tools/FireCenter/Integrate Lobby With S1Test")]
        public static string Install()
        {
            var log = new System.Text.StringBuilder(); Directory.CreateDirectory(Gen);
            var s1 = SceneManager.GetSceneByPath(S1Path); if (!s1.isLoaded) s1 = EditorSceneManager.OpenScene(S1Path, OpenSceneMode.Additive);
            var lobby = SceneManager.GetSceneByPath(LobbyPath); if (!lobby.isLoaded) lobby = EditorSceneManager.OpenScene(LobbyPath, OpenSceneMode.Additive);
            GameObject Root(Scene s, string n) => s.GetRootGameObjects().FirstOrDefault(g => g.name == n);

            // ---- 1. FireCenterLobby: remove systems S1Test already owns (one NetworkManager / XR Origin / EventSystem).
            foreach (var n in new[] { "Network Manager VR Multiplayer", "XRI Network Game Manager", "Permissions Manager", "XR Origin (XR Rig) - MP Template", "EventSystem" })
            { var g = Root(lobby, n); if (g) { Object.DestroyImmediate(g); log.AppendLine("removed duplicate: " + n); } }
            var anchors = Root(lobby, "Teleport Anchors (designated lobby locations)"); if (anchors) anchors.SetActive(false);
            foreach (var t in Object.FindObjectsByType<TeleportOnlyLocomotion>(FindObjectsInactive.Include, FindObjectsSortMode.None)) if (t.gameObject.scene == lobby) t.enabled = false;

            // Main menu: Settings and Exit overlapped (both at y=-400). Re-stack and add PLAY WITH BOT in the same style.
            var menu = Object.FindObjectsByType<LobbyMenuController>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(m => m.gameObject.scene == lobby);
            if (menu)
            {
                var main = menu.transform.Find("Content/Main Panel");
                var create = main.Find("Create Room Button"); var bot = main.Find("Play With Bot Button");
                if (!bot) { bot = Object.Instantiate(create.gameObject, main).transform; bot.name = "Play With Bot Button"; var b = bot.GetComponent<Button>(); b.onClick = new Button.ButtonClickedEvent(); }
                var label = bot.GetComponentInChildren<TMP_Text>(true); if (label) label.text = "PLAY WITH BOT";
                string[] order = { "Create Room Button", "Join Room Button", "Play With Bot Button", "Settings Button", "Exit Game Button" };
                for (int i = 0; i < order.Length; i++) { var r = main.Find(order[i]) as RectTransform; if (r) { r.anchoredPosition = new Vector2(r.anchoredPosition.x, -150 - i * 112); r.SetSiblingIndex(i + 1); } }
                var so = new SerializedObject(menu); so.FindProperty("m_PlayWithBotButton").objectReferenceValue = bot.GetComponent<Button>(); so.ApplyModifiedPropertiesWithoutUndo();
                log.AppendLine("menu re-stacked + PLAY WITH BOT");
            }

            // Lobby chairs were scaled ~1.33x (1.73 m); restore a natural office-chair size, grounded.
            var driverSpawn = FindIn(lobby, "DriverSpawnPoint"); var fireSpawn = FindIn(lobby, "FirefighterSpawnPoint"); var defSpawn = FindIn(lobby, "DefaultSpawnPoint");
            var driverChair = NormalizeChair(lobby, driverSpawn.position, log); var fireChair = NormalizeChair(lobby, fireSpawn.position, log);
            EditorSceneManager.MarkSceneDirty(lobby);

            // ---- 2. S1Test: host the lobby scene, hide the old room, move seats/rig, loading screen.
            var room = Root(s1, "DispatchControlRoom"); var seated = room.GetComponent<SeatedDispatchLobby>();
            var host = Root(s1, "FIRECENTER_LOBBY_HOST") ?? new GameObject("FIRECENTER_LOBBY_HOST");
            if (host.scene != s1) SceneManager.MoveGameObjectToScene(host, s1);
            var lh = host.GetComponent<FireCenterLobbyHost>() ?? host.AddComponent<FireCenterLobbyHost>();
            var keep = new HashSet<Transform> { seated.leftSeat, seated.rightSeat };
            lh.legacyVisuals = room.transform.Cast<Transform>().Where(c => !keep.Contains(c) && !c.GetComponent<Canvas>() && c.name != "LobbyDefaultSpot").Select(c => c.gameObject).ToArray();
            // The old console canvases keep their logic (SeatedDispatchLobby / bot test) but are no longer shown.
            foreach (Transform c in room.transform) if (c.GetComponent<Canvas>()) c.GetComponent<Canvas>().enabled = false;
            EditorUtility.SetDirty(lh);
            Place(seated.leftSeat, driverChair, driverSpawn); Place(seated.rightSeat, fireChair, fireSpawn);
            var spot = room.transform.Find("LobbyDefaultSpot") ?? new GameObject("LobbyDefaultSpot").transform; spot.SetParent(room.transform, true); spot.SetPositionAndRotation(defSpawn.position, defSpawn.rotation);
            seated.defaultSpot = spot; EditorUtility.SetDirty(seated);
            var rig = Object.FindObjectsByType<StationLocalRig>(FindObjectsSortMode.None).First(r => r.gameObject.scene == s1);
            rig.transform.SetPositionAndRotation(defSpawn.position, defSpawn.rotation); EditorUtility.SetDirty(rig.transform);
            var cal = new System.Text.StringBuilder();
            CityNightPresentation.Fireground.EditorTools.CrewPresentationInstaller.Calibrate(seated.leftSeat, 1, cal);
            CityNightPresentation.Fireground.EditorTools.CrewPresentationInstaller.Calibrate(seated.rightSeat, 2, cal); log.Append(cal);

            var fadeGo = Root(s1, "STATION_LOCAL_TRANSITIONS"); var fade = fadeGo.GetComponent<StationViewFade>();
            var ls = fadeGo.GetComponent<MissionLoadingScreen>() ?? fadeGo.AddComponent<MissionLoadingScreen>();
            BuildLoadingAssets(ls); ls.fade = fade; ls.origin = fade.origin; EditorUtility.SetDirty(ls);

            // ---- 3. Build list: lobby scene must be loadable at runtime.
            var scenes = EditorBuildSettings.scenes.ToList();
            if (!scenes.Any(s => s.path == LobbyPath)) { scenes.Insert(1, new EditorBuildSettingsScene(LobbyPath, true)); EditorBuildSettings.scenes = scenes.ToArray(); log.AppendLine("added FireCenterLobby to build settings"); }

            EditorSceneManager.MarkSceneDirty(s1); EditorSceneManager.SaveScene(lobby); EditorSceneManager.SaveScene(s1); AssetDatabase.SaveAssets();
            log.AppendLine("saved S1Test + FireCenterLobby"); return log.ToString();
        }

        static Transform FindIn(Scene s, string name) { foreach (var r in s.GetRootGameObjects()) foreach (var t in r.GetComponentsInChildren<Transform>(true)) if (t.name == name) return t; return null; }
        static Bounds NormalizeChair(Scene s, Vector3 near, System.Text.StringBuilder log)
        {
            MeshRenderer best = null; float d = float.MaxValue;
            foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            { if (r.gameObject.scene != s || !r.name.StartsWith("OfficeChair_LOD0")) continue; float x = new Vector2(r.bounds.center.x - near.x, r.bounds.center.z - near.z).magnitude; if (x < d) { d = x; best = r; } }
            if (!best) return new Bounds(near, Vector3.one);
            var root = best.transform; while (root.parent && root.parent.name.StartsWith("OfficeChair")) root = root.parent;
            var b = best.bounds;
            if (b.size.y > 1.5f) { root.localScale *= 1.3f / b.size.y; Physics.SyncTransforms(); b = best.bounds; log.AppendLine(root.name + " normalized to 1.30 m"); }
            float floor = near.y; if (Mathf.Abs(b.min.y - floor) > .01f) { root.position += Vector3.up * (floor - b.min.y); b = best.bounds; }
            EditorUtility.SetDirty(root); return b;
        }
        static void Place(Transform seat, Bounds chair, Transform spawn)
        {
            var yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(spawn.forward, Vector3.up).normalized, Vector3.up);
            seat.SetPositionAndRotation(new Vector3(chair.center.x, chair.min.y, chair.center.z), yaw); EditorUtility.SetDirty(seat);
        }

        // ---- Loading screen assets (procedural, lightweight sprites) ----
        static void BuildLoadingAssets(MissionLoadingScreen ls)
        {
            ls.white = Sprite("LoadingWhite", 4, 4, (u, v) => Color.white);
            ls.ring = Sprite("LoadingRing", 256, 256, (u, v) => { float r = Vector2.Distance(new Vector2(u, v), new Vector2(.5f, .5f)) * 2; float a = Mathf.Clamp01((.06f - Mathf.Abs(r - .9f)) * 40) + (Mathf.Abs(r - .9f) < .12f && Mathf.Repeat(Mathf.Atan2(v - .5f, u - .5f) * 6 / Mathf.PI, 1) < .45f ? .6f : 0); return new Color(1, 1, 1, Mathf.Clamp01(a)); });
            ls.barFill = Sprite("LoadingFireBar", 256, 16, (u, v) => { var c = u < .5f ? Color.Lerp(new Color(.85f, .1f, .05f), new Color(1, .5f, .05f), u * 2) : Color.Lerp(new Color(1, .5f, .05f), new Color(1, .9f, .2f), (u - .5f) * 2); float glow = .85f + .15f * Mathf.Sin(u * 60 + v * 6); return new Color(c.r * glow, c.g * glow, c.b * glow, 1); });
            ls.fire = Sprite("LoadingFlame", 96, 128, (u, v) =>
            {
                float x = (u - .5f) * 2, y = v; float w = Mathf.Lerp(.95f, 0, Mathf.Pow(y, .8f)) * (1 - .25f * Mathf.Sin(y * 9));
                float body = Mathf.Clamp01((w - Mathf.Abs(x)) * 6) * Mathf.Clamp01((1 - y) * 8) * Mathf.Clamp01(y * 10 + .3f);
                float core = Mathf.Clamp01((w * .5f - Mathf.Abs(x)) * 6) * Mathf.Clamp01((.7f - y) * 4);
                var c = Color.Lerp(new Color(.95f, .2f, .05f), new Color(1, .75f, .15f), core); return new Color(c.r, c.g, c.b, body);
            });
            ls.firefighter = Sprite("LoadingFirefighter", 128, 128, (u, v) =>
            {
                Vector2 p = new Vector2(u, v);
                bool helmet = (p - new Vector2(.42f, .82f)).sqrMagnitude < .016f && v > .8f || (v > .79f && v < .82f && u > .26f && u < .6f);
                bool head = (p - new Vector2(.42f, .74f)).sqrMagnitude < .0065f;
                bool body = u > .32f && u < .54f && v > .38f && v < .68f;
                bool tank = u > .24f && u < .33f && v > .44f && v < .64f;
                bool legs = (u > .33f && u < .41f && v > .08f && v < .4f) || (u > .45f && u < .53f && v > .08f && v < .4f && v > (u - .45f) * .6f + .08f);
                bool arm = v > .5f && v < .57f && u > .5f && u < .72f;
                bool nozzle = v > .52f && v < .56f && u > .7f && u < .9f;
                return (helmet || head || body || tank || legs || arm || nozzle) ? Color.white : new Color(1, 1, 1, 0);
            });
            var fc = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/FireCenter/Textures/Icon_PCVR_G29.png");
            if (!fc) foreach (var o in AssetDatabase.LoadAllAssetsAtPath("Assets/FireCenter/Textures/Icon_PCVR_G29.png")) if (o is Sprite sp) fc = sp;
            ls.driverMark = fc ? fc : ls.ring;
            var ui = Mat(Gen + "/LoadingUI.mat", Shader.Find("UI/AlwaysOnTop")); ui.renderQueue = 4950; ls.uiMaterial = ui;
            var font = TMP_Settings.defaultFontAsset; ls.font = font;
            var tm = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/LoadingText.mat");
            if (!tm) { tm = new Material(font.material); AssetDatabase.CreateAsset(tm, Gen + "/LoadingText.mat"); }
            tm.CopyPropertiesFromMaterial(font.material); tm.shader = Shader.Find("TextMeshPro/Distance Field Overlay"); tm.renderQueue = 4960; EditorUtility.SetDirty(tm); ls.textMaterial = tm;
        }
        static Material Mat(string path, Shader shader)
        {
            var m = AssetDatabase.LoadAssetAtPath<Material>(path); if (!m) { m = new Material(shader); AssetDatabase.CreateAsset(m, path); } else m.shader = shader;
            EditorUtility.SetDirty(m); return m;
        }
        static Sprite Sprite(string name, int w, int h, System.Func<float, float, Color> f)
        {
            string path = Gen + "/" + name + ".png"; var t = new Texture2D(w, h, TextureFormat.RGBA32, false);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) t.SetPixel(x, y, f((x + .5f) / w, (y + .5f) / h));
            File.WriteAllBytes(path, t.EncodeToPNG()); Object.DestroyImmediate(t); AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path); imp.textureType = TextureImporterType.Sprite; imp.alphaIsTransparency = true; imp.mipmapEnabled = false; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(path);
        }
    }
}
