using UnityEngine;
using UnityEngine.Rendering;

namespace FirefighterVR.Station
{
    // Role-coloured guidance marker: a 1 m glowing beam with a ground ring and a bobbing arrow.
    // Blue = Player 1 / Driver, red = Player 2 / Assistant. Pure presentation (no text, no physics).
    public sealed class TaskBeacon : MonoBehaviour
    {
        public static readonly Color DriverColor = new Color(.15f, .55f, 1f, 1), AssistantColor = new Color(1f, .16f, .1f, 1);
        static Mesh beamMesh, ringMesh, arrowMesh;
        static Material glow, solid;
        Renderer beam, ring, arrow;
        MaterialPropertyBlock block;
        float seed;

        public static Color ColorOf(StationRole role) => role == StationRole.Assistant ? AssistantColor : DriverColor;

        public static TaskBeacon Create(string name, Transform parent)
        {
            var g = new GameObject(name); g.transform.SetParent(parent, false);
            var b = g.AddComponent<TaskBeacon>(); b.Build(); return b;
        }
        void Build()
        {
            EnsureAssets(); block = new MaterialPropertyBlock(); seed = Random.value * 10;
            beam = Part("Beam", beamMesh, glow); ring = Part("Ring", ringMesh, glow); arrow = Part("Arrow", arrowMesh, solid);
        }
        Renderer Part(string name, Mesh mesh, Material mat)
        {
            var g = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer)); g.transform.SetParent(transform, false);
            g.GetComponent<MeshFilter>().sharedMesh = mesh; var r = g.GetComponent<MeshRenderer>(); r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off; r.receiveShadows = false; r.lightProbeUsage = LightProbeUsage.Off; return r;
        }

        // position = bottom of the beam. beamOn=false gives an arrow-only pointer (for screens/handles).
        public void Show(Vector3 position, StationRole role, float radius = .35f, bool beamOn = true, float height = 1f)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            transform.SetPositionAndRotation(position, Quaternion.identity);
            float t = Time.time + seed, pulse = .75f + .25f * Mathf.Sin(t * 4);
            var c = ColorOf(role);
            beam.enabled = ring.enabled = beamOn;
            if (beamOn)
            {
                beam.transform.localScale = new Vector3(radius, height, radius);
                ring.transform.localScale = new Vector3(radius * (1.1f + .15f * Mathf.Sin(t * 3)), 1, radius * (1.1f + .15f * Mathf.Sin(t * 3)));
                block.SetColor("_BaseColor", c * (pulse * 1.6f)); beam.SetPropertyBlock(block); ring.SetPropertyBlock(block);
            }
            float top = beamOn ? height + .12f : .18f;
            arrow.transform.localPosition = Vector3.up * (top + .06f * Mathf.Sin(t * 3));
            arrow.transform.localRotation = Quaternion.Euler(0, t * 90 % 360, 0);
            arrow.transform.localScale = Vector3.one * (beamOn ? Mathf.Clamp(radius * .55f, .14f, .4f) : .1f);
            block.SetColor("_BaseColor", c); arrow.SetPropertyBlock(block);
        }
        public void Hide() { if (gameObject.activeSelf) gameObject.SetActive(false); }

        // Floating role arrow above a player's head.
        public void ShowHeadArrow(Vector3 head, StationRole role)
        {
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            beam.enabled = ring.enabled = false; float t = Time.time + seed;
            transform.SetPositionAndRotation(head + Vector3.up * (.42f + .05f * Mathf.Sin(t * 2.5f)), Quaternion.identity);
            arrow.transform.localPosition = Vector3.zero; arrow.transform.localRotation = Quaternion.Euler(0, t * 60 % 360, 0); arrow.transform.localScale = Vector3.one * .2f;
            block.SetColor("_BaseColor", ColorOf(role)); arrow.SetPropertyBlock(block);
        }

        static void EnsureAssets()
        {
            if (beamMesh) return;
            glow = Resources.Load<Material>("CrewGuideGlow"); solid = Resources.Load<Material>("CrewGuideSolid");
            if (!glow) glow = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            if (!solid) solid = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
            const int sides = 24;
            // Beam: open cylinder r=1, h=1, alpha fades upward (vertex colour).
            var v = new Vector3[(sides + 1) * 2]; var col = new Color[v.Length]; var tri = new int[sides * 12];
            for (int i = 0; i <= sides; i++)
            {
                float a = i / (float)sides * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                v[i] = d; v[i + sides + 1] = d * .85f + Vector3.up; col[i] = new Color(1, 1, 1, .55f); col[i + sides + 1] = new Color(1, 1, 1, 0);
            }
            for (int i = 0, k = 0; i < sides; i++) { int a = i, b = i + 1, c = i + sides + 1, d = c + 1; tri[k++] = a; tri[k++] = c; tri[k++] = b; tri[k++] = b; tri[k++] = c; tri[k++] = d; tri[k++] = a; tri[k++] = b; tri[k++] = c; tri[k++] = b; tri[k++] = d; tri[k++] = c; }
            beamMesh = new Mesh { name = "CrewBeam", vertices = v, colors = col, triangles = tri };
            // Ring: flat annulus r 0.8..1.0 slightly above the ground.
            var rv = new Vector3[(sides + 1) * 2]; var rc = new Color[rv.Length]; var rt = new int[sides * 12];
            for (int i = 0; i <= sides; i++) { float a = i / (float)sides * Mathf.PI * 2; var d = new Vector3(Mathf.Cos(a), .02f, Mathf.Sin(a)); rv[i] = d * .78f + Vector3.up * .02f; rv[i + sides + 1] = d; rc[i] = new Color(1, 1, 1, .9f); rc[i + sides + 1] = new Color(1, 1, 1, .9f); }
            for (int i = 0, k = 0; i < sides; i++) { int a = i, b = i + 1, c = i + sides + 1, d = c + 1; rt[k++] = a; rt[k++] = c; rt[k++] = b; rt[k++] = b; rt[k++] = c; rt[k++] = d; rt[k++] = a; rt[k++] = b; rt[k++] = c; rt[k++] = b; rt[k++] = d; rt[k++] = c; }
            ringMesh = new Mesh { name = "CrewRing", vertices = rv, colors = rc, triangles = rt };
            // Arrow pointing down: cone head + square shaft, unit height ~1.
            var av = new System.Collections.Generic.List<Vector3>(); var at = new System.Collections.Generic.List<int>();
            const int cs = 12; av.Add(Vector3.zero);
            for (int i = 0; i < cs; i++) { float a = i / (float)cs * Mathf.PI * 2; av.Add(new Vector3(Mathf.Cos(a) * .45f, .5f, Mathf.Sin(a) * .45f)); }
            av.Add(new Vector3(0, .5f, 0));
            for (int i = 0; i < cs; i++) { int a = 1 + i, b = 1 + (i + 1) % cs; at.AddRange(new[] { 0, b, a, cs + 1, a, b }); }
            int s0 = av.Count; float w = .14f;
            foreach (var y in new[] { .5f, 1.1f }) { av.Add(new Vector3(-w, y, -w)); av.Add(new Vector3(w, y, -w)); av.Add(new Vector3(w, y, w)); av.Add(new Vector3(-w, y, w)); }
            for (int i = 0; i < 4; i++) { int a = s0 + i, b = s0 + (i + 1) % 4, c = a + 4, d = b + 4; at.AddRange(new[] { a, c, b, b, c, d }); }
            at.AddRange(new[] { s0 + 4, s0 + 6, s0 + 5, s0 + 4, s0 + 7, s0 + 6 });
            arrowMesh = new Mesh { name = "CrewArrow", vertices = av.ToArray(), triangles = at.ToArray() }; arrowMesh.RecalculateNormals();
            foreach (var m in new[] { beamMesh, ringMesh, arrowMesh }) { m.RecalculateBounds(); m.bounds = new Bounds(m.bounds.center, m.bounds.size + Vector3.one); }
        }
    }
}
