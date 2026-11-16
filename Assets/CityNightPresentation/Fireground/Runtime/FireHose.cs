using UnityEngine;

namespace CityNightPresentation.Fireground
{
    // Lightweight Verlet hose (no Rigidbody/Joint chain). Simulated locally on every peer from the
    // replicated payout and the two endpoints (reel outlet + nozzle coupling); only those are synced.
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class FireHose : MonoBehaviour
    {
        public FiregroundOperation operation;
        public Transform start, end;
        [Range(16, 64)] public int points = 44;
        [Range(4, 12)] public int sides = 8;
        public float radius = .034f;
        [Range(2, 24)] public int iterations = 12;
        [Range(.9f, 1)] public float damping = .985f;
        public LayerMask groundMask = 1;
        public float textureTiling = 3;

        Vector3[] pos, prev, verts, normals; Vector2[] uvs; float[] ground;
        Mesh mesh; int cursor; float acc, length; bool initialised;
        readonly RaycastHit[] hits = new RaycastHit[6];
        const float Step = 1f / 60f;

        void Awake()
        {
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); transform.localScale = Vector3.one;
            pos = new Vector3[points]; prev = new Vector3[points]; ground = new float[points];
            for (int i = 0; i < points; i++) ground[i] = float.NegativeInfinity;
            int ring = sides + 1; verts = new Vector3[points * ring]; normals = new Vector3[verts.Length]; uvs = new Vector2[verts.Length];
            var tris = new int[(points - 1) * sides * 6]; int t = 0;
            for (int i = 0; i < points - 1; i++) for (int s = 0; s < sides; s++)
            { int a = i * ring + s, b = a + ring; tris[t++] = a; tris[t++] = b; tris[t++] = a + 1; tris[t++] = a + 1; tris[t++] = b; tris[t++] = b + 1; }
            mesh = new Mesh { name = "FireHose_Runtime" }; mesh.MarkDynamic(); mesh.vertices = verts; mesh.normals = normals; mesh.uv = uvs; mesh.triangles = tris;
            GetComponent<MeshFilter>().sharedMesh = mesh;
        }
        void OnDestroy() { if (mesh) Destroy(mesh); }

        void LayOut(float sagLength)
        {
            Vector3 a = start.position, b = end.position;
            for (int i = 0; i < points; i++)
            {
                float u = i / (points - 1f);
                float sag = Mathf.Sin(u * Mathf.PI) * Mathf.Max(0, sagLength - Vector3.Distance(a, b)) * .5f;
                pos[i] = prev[i] = Vector3.Lerp(a, b, u) + Vector3.down * sag;
            }
        }

        void LateUpdate()
        {
            if (!start || !end || !operation) return;
            var s = operation.Current;
            float target = Mathf.Max(.3f, s.payout);
            length = initialised ? Mathf.MoveTowards(length, target, Time.deltaTime * 6) : target;
            // Docked during the drive: rigid short loop that follows the moving truck, no simulation.
            if (s.nozzleDocked && !s.Held && s.phase <= FiregroundPhase.Dismount) { LayOut(length); initialised = true; BuildMesh(); return; }
            if (!initialised) { LayOut(length); initialised = true; }
            acc = Mathf.Min(acc + Time.deltaTime, Step * 3);
            while (acc >= Step) { acc -= Step; Simulate(); }
            BuildMesh();
        }

        void Simulate()
        {
            int n = points; float seg = length / (n - 1);
            // Round-robin ground probes (never all points in one frame).
            for (int k = 0; k < 6; k++) { cursor = (cursor + 1) % n; ground[cursor] = Ground(pos[cursor]); }
            Vector3 g = Physics.gravity * Step * Step;
            for (int i = 1; i < n - 1; i++)
            {
                Vector3 v = (pos[i] - prev[i]) * damping; prev[i] = pos[i]; pos[i] += v + g;
            }
            pos[0] = start.position; pos[n - 1] = end.position; prev[0] = pos[0]; prev[n - 1] = pos[n - 1];
            for (int it = 0; it < iterations; it++)
            {
                for (int i = 0; i < n - 1; i++)
                {
                    Vector3 d = pos[i + 1] - pos[i]; float dist = d.magnitude; if (dist < 1e-6f) continue;
                    Vector3 c = d * ((dist - seg) / dist);
                    bool pinA = i == 0, pinB = i + 1 == n - 1;
                    if (pinA && pinB) continue;
                    if (pinA) pos[i + 1] -= c; else if (pinB) pos[i] += c; else { pos[i] += c * .5f; pos[i + 1] -= c * .5f; }
                }
                // Mild bending stiffness keeps a thick hose from kinking like a string.
                for (int i = 1; i < n - 1; i++) { Vector3 mid = (pos[i - 1] + pos[i + 1]) * .5f; pos[i] += (mid - pos[i]) * .04f; }
                for (int i = 1; i < n - 1; i++)
                {
                    float floor = ground[i] + radius;
                    if (pos[i].y < floor)
                    {
                        pos[i].y = floor;
                        // Ground friction: kill horizontal sliding so the hose settles instead of jittering.
                        prev[i].x = Mathf.Lerp(prev[i].x, pos[i].x, .55f); prev[i].z = Mathf.Lerp(prev[i].z, pos[i].z, .55f); prev[i].y = pos[i].y;
                    }
                }
            }
        }
        float Ground(Vector3 p)
        {
            int count = Physics.RaycastNonAlloc(p + Vector3.up * .6f, Vector3.down, hits, 4, groundMask, QueryTriggerInteraction.Ignore);
            float best = float.NegativeInfinity;
            for (int i = 0; i < count; i++) { var c = hits[i].collider; if (c is CharacterController || c.attachedRigidbody && c.attachedRigidbody.isKinematic && c.GetComponentInParent<HoseNozzle>()) continue; best = Mathf.Max(best, hits[i].point.y); }
            return best;
        }

        void BuildMesh()
        {
            int n = points, ring = sides + 1; Vector3 tangent = (pos[1] - pos[0]).normalized; if (tangent == Vector3.zero) tangent = Vector3.forward;
            Vector3 normal = Vector3.Cross(tangent, Mathf.Abs(tangent.y) < .9f ? Vector3.up : Vector3.right).normalized;
            float v = 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 t = i < n - 1 ? pos[i + 1] - pos[i] : pos[i] - pos[i - 1];
                if (i > 0 && i < n - 1) t = pos[i + 1] - pos[i - 1];
                t = t.sqrMagnitude > 1e-10f ? t.normalized : tangent;
                normal = Quaternion.FromToRotation(tangent, t) * normal; tangent = t;
                normal = Vector3.ProjectOnPlane(normal, t).normalized; Vector3 binormal = Vector3.Cross(t, normal);
                if (i > 0) v += Vector3.Distance(pos[i], pos[i - 1]) / (radius * 2 * Mathf.PI) / textureTiling;
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2; Vector3 dir = normal * Mathf.Cos(a) + binormal * Mathf.Sin(a);
                    // Lay-flat look: a charged hose is round, an empty one slightly flattened.
                    int k = i * ring + s; normals[k] = dir; verts[k] = pos[i] + dir * radius; uvs[k] = new Vector2(s / (float)sides, v);
                }
            }
            mesh.vertices = verts; mesh.normals = normals; mesh.uv = uvs; mesh.RecalculateBounds();
        }
    }
}
