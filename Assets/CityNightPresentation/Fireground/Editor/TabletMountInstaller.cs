using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace CityNightPresentation.Fireground.EditorTools
{
    // Passenger tablet: a realistic, lightweight device (rounded aluminium shell, glass front,
    // camera, dashboard mount arm) fixed on the dashboard over the WARNING label, facing the
    // passenger's eyes. The existing canvas, map and buttons are kept unchanged.
    public static class TabletMountInstaller
    {
        const string Gen = "Assets/CityNightPresentation/Fireground/Generated";

        [MenuItem("Tools/Fireground/Mount Passenger Tablet On Dashboard")]
        public static string Install()
        {
            var mission = Object.FindFirstObjectByType<Mission.FireTruckMission>(); if (!mission) return "city scene not open";
            var truck = mission.transform; var tab = truck.Find("CabinMissionEquipment/Passenger_Tablet"); var canvas = tab ? tab.Find("Tablet_Canvas") as RectTransform : null;
            if (!tab || !canvas) return "tablet not found";
            var old = tab.Find("RuggedCase"); if (old) Object.DestroyImmediate(old.gameObject);
            old = tab.Find("TabletDevice"); if (old) Object.DestroyImmediate(old.gameObject);
            old = truck.Find("CabinMissionEquipment/TabletDashMount"); if (old) Object.DestroyImmediate(old.gameObject);
            var body = tab.Find("Body"); if (body && body.GetComponent<Renderer>()) body.GetComponent<Renderer>().enabled = false;

            // Face the passenger: canvas centre over the dashboard WARNING label (measured dash surface).
            var eye = FirefighterVR.Station.StationSeatPlacement.EyeTarget(mission.passengerSeat, null);
            var center = truck.TransformPoint(new Vector3(.73f, 2.1f, 3.17f));
            var canvasRot = Quaternion.LookRotation(center - eye, truck.up);
            tab.rotation = canvasRot * Quaternion.Inverse(canvas.localRotation);
            tab.position = center - tab.rotation * Vector3.Scale(canvas.localPosition, tab.lossyScale);

            // Device shell aligned with the canvas (canvas forward points away from the viewer).
            Vector2 screen = Vector2.Scale(canvas.rect.size, (Vector2)canvas.lossyScale);
            float bezel = .016f, depth = .011f, w = screen.x + bezel * 2, h = screen.y + bezel * 2;
            var device = new GameObject("TabletDevice").transform; device.SetParent(tab, true);
            device.SetPositionAndRotation(canvas.position + canvas.forward * (depth * .5f + .0012f), canvas.rotation);
            var shell = new GameObject("Shell", typeof(MeshFilter), typeof(MeshRenderer)); shell.transform.SetParent(device, false);
            shell.GetComponent<MeshFilter>().sharedMesh = ShellMesh(w, h, depth, .02f);
            var r = shell.GetComponent<MeshRenderer>(); r.sharedMaterials = new[] { Lit("TabletAluminium", new Color(.32f, .33f, .35f), .85f, .62f), Lit("TabletGlass", new Color(.015f, .016f, .02f), .1f, .95f) };
            r.shadowCastingMode = ShadowCastingMode.Off;
            var cam = GameObject.CreatePrimitive(PrimitiveType.Cylinder); Object.DestroyImmediate(cam.GetComponent<Collider>()); cam.name = "Camera";
            cam.transform.SetParent(device, false); cam.transform.localPosition = new Vector3(0, h * .5f - bezel * .5f, -depth * .5f - .0005f); cam.transform.localRotation = Quaternion.Euler(90, 0, 0); cam.transform.localScale = new Vector3(.006f, .0005f, .006f);
            cam.GetComponent<Renderer>().sharedMaterial = Lit("TabletCameraLens", new Color(.05f, .07f, .1f), .2f, .98f);

            // Dashboard mount: low-profile base on the dash + arm + ball joint into the tablet back.
            var mount = new GameObject("TabletDashMount").transform; mount.SetParent(tab.parent, true);
            var dashPoint = truck.TransformPoint(new Vector3(.73f, 1.94f, 3.27f)); var dashNormal = truck.TransformDirection(new Vector3(0, .61f, -.79f)).normalized;
            var joint = device.position + device.forward * (depth * .5f + .012f);
            Part(PrimitiveType.Cylinder, "Base", mount, dashPoint + dashNormal * .006f, Quaternion.FromToRotation(Vector3.up, dashNormal), new Vector3(.07f, .006f, .07f), "TabletMountBlack");
            var arm = joint - dashPoint; Part(PrimitiveType.Cylinder, "Arm", mount, dashPoint + arm * .5f, Quaternion.FromToRotation(Vector3.up, arm.normalized), new Vector3(.016f, arm.magnitude * .5f, .016f), "TabletMountBlack");
            Part(PrimitiveType.Sphere, "Ball", mount, joint, Quaternion.identity, Vector3.one * .028f, "TabletMountBlack");
            Part(PrimitiveType.Cube, "BackPlate", mount, device.position + device.forward * (depth * .5f + .003f), device.rotation, new Vector3(.09f, .09f, .006f), "TabletMountBlack");
            EditorSceneManager.MarkSceneDirty(mission.gameObject.scene); EditorSceneManager.SaveScene(mission.gameObject.scene);
            return "tablet mounted at " + truck.InverseTransformPoint(center).ToString("F2") + ", " + Vector3.Distance(eye, center).ToString("F2") + " m from the passenger's eyes";
        }

        static void Part(PrimitiveType type, string name, Transform parent, Vector3 pos, Quaternion rot, Vector3 scale, string mat)
        {
            var g = GameObject.CreatePrimitive(type); Object.DestroyImmediate(g.GetComponent<Collider>()); g.name = name;
            g.transform.SetParent(parent, true); g.transform.SetPositionAndRotation(pos, rot); g.transform.localScale = scale;
            var r = g.GetComponent<Renderer>(); r.sharedMaterial = Lit(mat, new Color(.04f, .04f, .045f), .3f, .45f); r.shadowCastingMode = ShadowCastingMode.Off;
        }
        // Rounded-rectangle slab: submesh 0 = back + rim (aluminium), submesh 1 = front glass (faces -Z, the viewer).
        static Mesh ShellMesh(float w, float h, float d, float radius)
        {
            var outline = new List<Vector2>(); const int seg = 6;
            Vector2[] corners = { new Vector2(w / 2 - radius, h / 2 - radius), new Vector2(-w / 2 + radius, h / 2 - radius), new Vector2(-w / 2 + radius, -h / 2 + radius), new Vector2(w / 2 - radius, -h / 2 + radius) };
            for (int c = 0; c < 4; c++) for (int i = 0; i <= seg; i++) { float a = (c * 90 + i * 90f / seg) * Mathf.Deg2Rad; outline.Add(corners[c] + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius); }
            int n = outline.Count; var v = new List<Vector3>(); var back = new List<int>(); var front = new List<int>();
            v.Add(new Vector3(0, 0, -d / 2)); foreach (var p in outline) v.Add(new Vector3(p.x, p.y, -d / 2));            // front fan
            for (int i = 0; i < n; i++) front.AddRange(new[] { 0, 1 + (i + 1) % n, 1 + i });
            int b0 = v.Count; v.Add(new Vector3(0, 0, d / 2)); foreach (var p in outline) v.Add(new Vector3(p.x, p.y, d / 2)); // back fan
            for (int i = 0; i < n; i++) back.AddRange(new[] { b0, b0 + 1 + i, b0 + 1 + (i + 1) % n });
            int s0 = v.Count; foreach (var p in outline) { v.Add(new Vector3(p.x, p.y, -d / 2)); v.Add(new Vector3(p.x, p.y, d / 2)); } // rim
            for (int i = 0; i < n; i++) { int a = s0 + i * 2, b = s0 + ((i + 1) % n) * 2; back.AddRange(new[] { a, b, a + 1, a + 1, b, b + 1 }); }
            var m = new Mesh { name = "TabletShell" }; m.SetVertices(v); m.subMeshCount = 2; m.SetTriangles(back, 0); m.SetTriangles(front, 1); m.RecalculateNormals(); m.RecalculateBounds();
            string path = Gen + "/TabletShell.asset"; AssetDatabase.DeleteAsset(path); AssetDatabase.CreateAsset(m, path); return m;
        }
        static Material Lit(string name, Color c, float metal, float smooth)
        {
            string path = Gen + "/" + name + ".mat"; var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", c); m.SetFloat("_Metallic", metal); m.SetFloat("_Smoothness", smooth); EditorUtility.SetDirty(m); return m;
        }
    }
}
