using System.Collections.Generic;
using System.Linq;
using CityNightPresentation.Mission;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Tools/Fire Fighters/Apply Fire Site Update (idempotent):
//  - siren pull handle on the cab ceiling straight in front of the Driver's eyes
//  - walk-through opening in the low wall + rail at the fire-site entrance (mesh cut, end caps, colliders)
//  - colliders on the fire-site environment so players cannot walk into buildings / props
public static class FireSiteUpdateInstaller
{
    const string CityPath = "Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity";
    const string Gen = "Assets/CityNightPresentation/Fireground/Generated";
    // Truck-local: Driver eye is at (-0.75, 2.35, 2.41); the handle hangs ~40 cm in front of it.
    public static readonly Vector3 SirenHandleLocal = new Vector3(-.75f, 0, 2.8f);
    // Entrance opening (world X) between two rail posts, on the bot's gate path (x = 195).
    const float GateMinX = 191.9f, GateMaxX = 196.5f, GateMinZ = -199.5f, GateMaxZ = -196.5f, GateTopY = 8.5f;
    static readonly Bounds Site = new Bounds(new Vector3(197, 15, -211), new Vector3(60, 30, 50));

    [MenuItem("Tools/Fire Fighters/Apply Fire Site Update")]
    public static string Apply()
    {
        var log = new System.Text.StringBuilder();
        var city = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(CityPath);
        if (!city.isLoaded) city = EditorSceneManager.OpenScene(CityPath, OpenSceneMode.Additive);
        var mission = Object.FindObjectsByType<FireTruckMission>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(m => m.gameObject.scene == city);
        SirenHandle(mission.transform, log);
        Gate(city, log);
        EnvironmentColliders(city, mission.transform, log);
        EditorSceneManager.MarkSceneDirty(city); EditorSceneManager.SaveScene(city);
        Debug.Log("[FireSiteUpdate]\n" + log);
        return log.ToString();
    }

    // ---------------- Siren handle ----------------
    static void SirenHandle(Transform truck, System.Text.StringBuilder log)
    {
        var e = truck.GetComponent<FireTruckEmergency>(); if (!e || !e.pullMount) return;
        var temps = truck.GetComponentsInChildren<MeshFilter>().Where(m => m.sharedMesh && !m.transform.IsChildOf(e.pullMount))
            .Select(m => { var g = new GameObject("tmp"); g.transform.SetParent(m.transform, false); g.AddComponent<MeshCollider>().sharedMesh = m.sharedMesh; return g; }).ToList();
        Physics.SyncTransforms();
        var o = truck.TransformPoint(new Vector3(SirenHandleLocal.x, 2.0f, SirenHandleLocal.z));
        var hit = Physics.RaycastAll(o, truck.up, 1.5f).Where(h => temps.Contains(h.collider.gameObject)).OrderBy(h => h.distance).FirstOrDefault();
        foreach (var g in temps) Object.DestroyImmediate(g);
        float ceiling = hit.collider ? truck.InverseTransformPoint(hit.point).y : 2.69f;
        var mount = e.pullMount.Find("Mount"); float half = mount ? mount.localScale.z * .5f : .0125f;
        var local = new Vector3(SirenHandleLocal.x, ceiling - half, SirenHandleLocal.z);
        Undo.RecordObject(e.pullMount, "Siren handle");
        e.pullMount.SetPositionAndRotation(truck.TransformPoint(local), truck.rotation * Quaternion.Euler(-90, 0, 0));
        if (e.pullHandle) e.pullHandle.localPosition = e.closedLocalPosition;
        EditorUtility.SetDirty(e.pullMount); EditorUtility.SetDirty(e);
        log.AppendLine($"siren handle: truck-local {local:F3} (ceiling hit={(bool)hit.collider})");
    }

    // ---------------- Entrance opening ----------------
    struct V { public Vector3 p, n; public Vector4 t; public Vector2 uv, uv2; public Color c; }
    static V Lerp(V a, V b, float k) => new V { p = Vector3.Lerp(a.p, b.p, k), n = Vector3.Lerp(a.n, b.n, k).normalized, t = Vector4.Lerp(a.t, b.t, k), uv = Vector2.Lerp(a.uv, b.uv, k), uv2 = Vector2.Lerp(a.uv2, b.uv2, k), c = Color.Lerp(a.c, b.c, k) };
    static List<V> Clip(List<V> poly, float x, bool keepBelow)
    {
        var o = new List<V>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            bool ina = keepBelow ? a.p.x <= x : a.p.x >= x, inb = keepBelow ? b.p.x <= x : b.p.x >= x;
            if (ina) o.Add(a);
            if (ina != inb) o.Add(Lerp(a, b, (x - a.p.x) / (b.p.x - a.p.x)));
        }
        return o;
    }
    static void Gate(UnityEngine.SceneManagement.Scene city, System.Text.StringBuilder log)
    {
        var mf = Object.FindObjectsByType<MeshFilter>(FindObjectsInactive.Include, FindObjectsSortMode.None).First(m => m.gameObject.scene == city && m.name == "fence_part58");
        var tr = mf.transform; string path = Gen + "/fence_part58_gate.asset";
        var src = mf.sharedMesh;
        // Always cut from the original model mesh (re-runs do not cut twice).
        var original = FindOriginal(src); if (!original) { log.AppendLine("gate: original fence mesh not found"); return; }
        float xa = tr.InverseTransformPoint(new Vector3(GateMinX, 0, 0)).x, xb = tr.InverseTransformPoint(new Vector3(GateMaxX, 0, 0)).x;
        var verts = original.vertices; var norms = original.normals; var tans = original.tangents; var uvs = original.uv; var uv2s = original.uv2; var cols = original.colors;
        bool hasN = norms.Length == verts.Length, hasT = tans.Length == verts.Length, hasUv = uvs.Length == verts.Length, hasUv2 = uv2s.Length == verts.Length, hasC = cols.Length == verts.Length;
        V Get(int i) => new V { p = verts[i], n = hasN ? norms[i] : Vector3.up, t = hasT ? tans[i] : Vector4.zero, uv = hasUv ? uvs[i] : Vector2.zero, uv2 = hasUv2 ? uv2s[i] : Vector2.zero, c = hasC ? cols[i] : Color.white };
        var outV = new List<V>(); var subs = new List<List<int>>(); int removed = 0;
        var capPoints = new List<Vector3>();
        for (int s = 0; s < original.subMeshCount; s++)
        {
            var tri = original.GetTriangles(s); var list = new List<int>(); subs.Add(list);
            for (int i = 0; i < tri.Length; i += 3)
            {
                var a = Get(tri[i]); var b = Get(tri[i + 1]); var c = Get(tri[i + 2]);
                var wc = tr.TransformPoint((a.p + b.p + c.p) / 3);
                float minX = Mathf.Min(a.p.x, b.p.x, c.p.x), maxX = Mathf.Max(a.p.x, b.p.x, c.p.x);
                bool nearGate = wc.z >= GateMinZ && wc.z <= GateMaxZ && wc.y <= GateTopY;
                if (!nearGate || maxX <= xa || minX >= xb) { Emit(new List<V> { a, b, c }, outV, list); continue; }
                removed++;
                var poly = new List<V> { a, b, c };
                var left = Clip(poly, xa, true); if (left.Count >= 3) { Emit(left, outV, list); capPoints.AddRange(left.Where(v => Mathf.Abs(v.p.x - xa) < .001f).Select(v => tr.TransformPoint(v.p))); }
                var right = Clip(poly, xb, false); if (right.Count >= 3) { Emit(right, outV, list); capPoints.AddRange(right.Where(v => Mathf.Abs(v.p.x - xb) < .001f).Select(v => tr.TransformPoint(v.p))); }
            }
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
        mesh.Clear(); mesh.name = "fence_part58_gate"; mesh.indexFormat = outV.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        mesh.SetVertices(outV.Select(v => v.p).ToList()); if (hasN) mesh.SetNormals(outV.Select(v => v.n).ToList()); if (hasT) mesh.SetTangents(outV.Select(v => v.t).ToList());
        if (hasUv) mesh.SetUVs(0, outV.Select(v => v.uv).ToList()); if (hasUv2) mesh.SetUVs(1, outV.Select(v => v.uv2).ToList()); if (hasC) mesh.SetColors(outV.Select(v => v.c).ToList());
        mesh.subMeshCount = subs.Count; for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
        mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssets();
        Undo.RecordObject(mf, "Gate"); mf.sharedMesh = mesh; EditorUtility.SetDirty(mf);
        log.AppendLine($"gate: cut x {GateMinX}..{GateMaxX} ({removed} triangles clipped), mesh {original.vertexCount} -> {mesh.vertexCount} verts");

        // End caps on the cut wall (concrete part only) + colliders matching the new opening.
        var root = GameObject.Find("FIREGROUND"); var parent = root ? root.transform : null;
        var old = parent ? parent.Find("EntranceGate") : null; if (old) Object.DestroyImmediate(old.gameObject);
        var gate = new GameObject("EntranceGate").transform; if (parent) gate.SetParent(parent, false);
        var wallMat = mf.GetComponent<Renderer>().sharedMaterials[0];
        float floor = capPoints.Count > 0 ? capPoints.Min(p => p.y) : 6.55f;
        foreach (var x in new[] { GateMinX, GateMaxX })
        {
            var pts = capPoints.Where(p => Mathf.Abs(p.x - x) < .01f && p.y < floor + .7f).ToList(); if (pts.Count < 2) continue;
            float y0 = pts.Min(p => p.y), y1 = pts.Max(p => p.y), z0 = pts.Min(p => p.z), z1 = pts.Max(p => p.z);
            var cap = GameObject.CreatePrimitive(PrimitiveType.Cube); Object.DestroyImmediate(cap.GetComponent<Collider>());
            cap.name = "WallCap_" + x.ToString("F1"); cap.transform.SetParent(gate, false);
            cap.transform.position = new Vector3(x, (y0 + y1) * .5f, (z0 + z1) * .5f); cap.transform.localScale = new Vector3(.02f, y1 - y0, z1 - z0);
            cap.GetComponent<Renderer>().sharedMaterial = wallMat; cap.isStatic = true;
            log.AppendLine($"  cap at x={x}: y {y0:F2}..{y1:F2} z {z0:F2}..{z1:F2}");
        }
        // Solid wall colliders right up to the opening (the existing PlazaWall boxes stop at 191.5 / 198.5).
        Box(gate, "Col_GateWallLeft", new Vector3(GateMinX - 1.0f, 7.1f, -198.1f), new Vector3(2.0f, 1.2f, 1.1f));
        Box(gate, "Col_GateWallRight", new Vector3(GateMaxX + 1.0f, 7.1f, -198.1f), new Vector3(2.0f, 1.2f, 1.1f));
    }
    static Mesh FindOriginal(Mesh current)
    {
        if (current.name != "fence_part58_gate") return current;
        foreach (var guid in AssetDatabase.FindAssets("fence_part58"))
            foreach (var m in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<Mesh>())
                if (m.name == "fence_part58") return m;
        return null;
    }
    static void Emit(List<V> poly, List<V> outV, List<int> tris)
    {
        int b = outV.Count; outV.AddRange(poly);
        for (int i = 1; i + 1 < poly.Count; i++) { tris.Add(b); tris.Add(b + i); tris.Add(b + i + 1); }
    }
    static void Box(Transform parent, string name, Vector3 world, Vector3 size)
    {
        var g = new GameObject(name); g.transform.SetParent(parent, false); g.transform.position = world;
        var c = g.AddComponent<BoxCollider>(); c.size = size; g.isStatic = true;
    }

    // ---------------- Invisible city collider at the entrance ----------------
    // The city model ships one big MeshCollider (city_part_collider) that still contains the old wall
    // across the entrance. Cut the same opening in it: wall faces and the wall top are clipped away
    // between GateMinX and GateMaxX; the ground faces stay, so nobody falls through.
    [MenuItem("Tools/Fire Fighters/Open Fire-Site Entrance Collider")]
    public static string OpenEntranceCollider()
    {
        var city = UnityEngine.SceneManagement.SceneManager.GetSceneByPath(CityPath);
        if (!city.isLoaded) city = EditorSceneManager.OpenScene(CityPath, OpenSceneMode.Additive);
        var mc = Object.FindObjectsByType<MeshCollider>(FindObjectsInactive.Include, FindObjectsSortMode.None).FirstOrDefault(c => c.gameObject.scene == city && c.name == "city_part_collider");
        if (!mc || !mc.sharedMesh) return "city_part_collider not found";
        var tr = mc.transform; string path = Gen + "/city_part_collider_gate.asset";
        Mesh original = mc.sharedMesh;
        if (original.name == "city_part_collider_gate")
            foreach (var guid in AssetDatabase.FindAssets("city_part_collider")) foreach (var m in AssetDatabase.LoadAllAssetsAtPath(AssetDatabase.GUIDToAssetPath(guid)).OfType<Mesh>()) if (m.name == "city_part_collider") original = m;
        if (original.name == "city_part_collider_gate") return "original collider mesh not found";
        var world = original.vertices.Select(v => tr.TransformPoint(v)).ToList();
        var verts = new List<Vector3>(world); var removed = 0;
        var subs = new List<int[]>();
        for (int s = 0; s < original.subMeshCount; s++)
        {
            var tri = original.GetTriangles(s); var keep = new List<int>(tri.Length);
            for (int i = 0; i < tri.Length; i += 3)
            {
                Vector3 a = world[tri[i]], b = world[tri[i + 1]], c = world[tri[i + 2]];
                var centre = (a + b + c) / 3; var n = Vector3.Cross(b - a, c - a).normalized;
                float minX = Mathf.Min(a.x, b.x, c.x), maxX = Mathf.Max(a.x, b.x, c.x);
                bool wallPart = Mathf.Abs(n.y) < .5f || centre.y > 6.9f;
                // No height limit: the city's boundary walls are invisible slabs ~55 m tall.
                bool inGate = centre.z >= GateMinZ && centre.z <= GateMaxZ && wallPart && maxX > GateMinX && minX < GateMaxX;
                if (!inGate) { keep.Add(tri[i]); keep.Add(tri[i + 1]); keep.Add(tri[i + 2]); continue; }
                removed++;
                foreach (var piece in new[] { ClipWorld(new List<Vector3> { a, b, c }, GateMinX, true), ClipWorld(new List<Vector3> { a, b, c }, GateMaxX, false) })
                {
                    if (piece.Count < 3) continue; int b0 = verts.Count; verts.AddRange(piece);
                    for (int k = 1; k + 1 < piece.Count; k++) { keep.Add(b0); keep.Add(b0 + k); keep.Add(b0 + k + 1); }
                }
            }
            subs.Add(keep.ToArray());
        }
        var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path); if (!mesh) { mesh = new Mesh(); AssetDatabase.CreateAsset(mesh, path); }
        mesh.Clear(); mesh.name = "city_part_collider_gate"; mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        mesh.SetVertices(verts.Select(v => tr.InverseTransformPoint(v)).ToList()); mesh.subMeshCount = subs.Count;
        for (int s = 0; s < subs.Count; s++) mesh.SetTriangles(subs[s], s);
        mesh.RecalculateBounds(); mesh.RecalculateNormals(); EditorUtility.SetDirty(mesh); AssetDatabase.SaveAssets();
        Undo.RecordObject(mc, "Open entrance collider"); mc.sharedMesh = mesh; EditorUtility.SetDirty(mc);
        EditorSceneManager.MarkSceneDirty(city); EditorSceneManager.SaveScene(city);
        return $"city collider: {removed} wall triangles clipped at the entrance (x {GateMinX}..{GateMaxX})";
    }
    static List<Vector3> ClipWorld(List<Vector3> poly, float x, bool keepBelow)
    {
        var o = new List<Vector3>();
        for (int i = 0; i < poly.Count; i++)
        {
            var a = poly[i]; var b = poly[(i + 1) % poly.Count];
            bool ina = keepBelow ? a.x <= x : a.x >= x, inb = keepBelow ? b.x <= x : b.x >= x;
            if (ina) o.Add(a);
            if (ina != inb) o.Add(Vector3.Lerp(a, b, (x - a.x) / (b.x - a.x)));
        }
        return o;
    }

    // ---------------- Environment colliders ----------------
    static void EnvironmentColliders(UnityEngine.SceneManagement.Scene city, Transform truck, System.Text.StringBuilder log)
    {
        int added = 0; var names = new SortedSet<string>();
        foreach (var r in Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (r.gameObject.scene != city || !r.enabled) continue;
            var b = r.bounds; if (!Site.Intersects(b)) continue;
            if (b.size.x > 45 && b.size.z > 45) continue;          // whole city blocks / ground plates
            if (b.size.y < .35f) continue;                         // flat floors, kerbs, markings (keep teleport surfaces clear)
            if (r.transform.IsChildOf(truck)) continue;
            var t = r.transform; bool skip = false;
            while (t) { if (t.name == "FIREGROUND" || t.name == "MISSION_FIRE_TRUCK" || t.name.StartsWith("Civilian_")) { skip = true; break; } t = t.parent; }
            if (skip || r.GetComponent<Collider>()) continue;
            var mf = r.GetComponent<MeshFilter>(); if (!mf || !mf.sharedMesh) continue;
            if (r.GetComponentInParent<ParticleSystem>()) continue;
            var mc = Undo.AddComponent<MeshCollider>(r.gameObject); mc.sharedMesh = mf.sharedMesh; mc.convex = false;
            added++; names.Add(System.Text.RegularExpressions.Regex.Replace(r.name, @"\s*\(\d+\)$", ""));
        }
        log.AppendLine($"environment colliders added: {added} ({string.Join(", ", names)})");
    }
}
