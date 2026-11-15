using System.Collections.Generic;
using FirefighterVR.Station;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FirefighterVR.StationBot
{
    // Small, bounded local planner. No global navigation bake, package, or physics actor.
    public sealed class StationBotNavigator
    {
        const float Cell = .55f, Radius = .24f;
        const int SearchLimit = 700;
        readonly StationSceneSetup station;
        readonly Transform human, bot;
        readonly StationBotFollower follower;
        readonly PhysicsScene physics;
        readonly Collider[] overlaps = new Collider[64];
        readonly List<Vector3> path = new List<Vector3>();
        readonly List<Vector2Int> open = new List<Vector2Int>();
        readonly Dictionary<Vector2Int, Vector3> points = new Dictionary<Vector2Int, Vector3>();
        readonly Dictionary<Vector2Int, float> costs = new Dictionary<Vector2Int, float>();
        readonly Dictionary<Vector2Int, Vector2Int> parents = new Dictionary<Vector2Int, Vector2Int>();
        readonly HashSet<Vector2Int> closed = new HashSet<Vector2Int>();
        readonly HashSet<Vector2Int> rejected = new HashSet<Vector2Int>();
        public StationBotNavigator(StationSceneSetup setup, Transform player, Transform character)
        { station = setup; human = player; bot = character; follower=character.GetComponent<StationBotFollower>();physics = setup.gameObject.scene.GetPhysicsScene(); }
        public bool Ground(Vector3 p, out Vector3 floor)
        {
            floor = p; float best = float.PositiveInfinity;
            foreach (var c in station.walkableSurfaces)
                if (c && c.Raycast(new Ray(p + Vector3.up * .55f, Vector3.down), out var hit, 1.05f) && hit.normal.y > .7f)
                { float d = Mathf.Abs(hit.point.y - p.y); if (d < best) { best = d; floor = hit.point + Vector3.up * .012f; } }
            return best < .38f;
        }
        public bool Clear(Vector3 p,float margin=0)
        {
            float radius=Radius+margin;
            int count = physics.OverlapCapsule(p + Vector3.up * (radius + .06f), p + Vector3.up * (1.75f - radius), radius, overlaps, ~0, QueryTriggerInteraction.Ignore);
            if (count == overlaps.Length) return false;
            for (int i = 0; i < count; ++i)
            {
                var c = overlaps[i];
                if (c.transform.IsChildOf(human) || c.transform.IsChildOf(bot) || System.Array.IndexOf(station.walkableSurfaces, c) >= 0) continue;
                if(follower&&follower.HeldObject&&c.transform.IsChildOf(follower.HeldObject))continue;
                var localPlayer = c.GetComponentInParent<XRMultiplayer.XRINetworkPlayer>();
                if (localPlayer && localPlayer.IsOwner) continue;
                return false;
            }
            return true;
        }
        public bool Segment(Vector3 a, Vector3 b)
        {
            int steps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(a, b) / .12f));
            Vector3 previous = a;
            for (int i = 1; i <= steps; ++i)
            {
                var p = Vector3.Lerp(a, b, i / (float)steps);
                p.y = previous.y;
                if (!Ground(p, out var floor) || !Clear(floor)) return false;
                previous = floor;
            }
            return true;
        }
        Vector2Int Key(Vector3 p) => new Vector2Int(Mathf.RoundToInt(p.x / Cell), Mathf.RoundToInt(p.z / Cell));
        static float Heuristic(Vector2Int a, Vector2Int b) => Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
        public bool Plan(Vector3 from, Vector3 target,bool precise=false)
        {
            path.Clear(); open.Clear(); points.Clear(); costs.Clear(); parents.Clear(); closed.Clear(); rejected.Clear();
            var start = Key(from); var goal = Key(target); open.Add(start); points[start] = from; costs[start] = 0;
            for (int expanded = 0; open.Count > 0 && expanded < SearchLimit; ++expanded)
            {
                int index = 0; float score = float.PositiveInfinity;
                for (int i = 0; i < open.Count; ++i) { float f = costs[open[i]] + Heuristic(open[i], goal); if (f < score) { score = f; index = i; } }
                var current = open[index]; open.RemoveAt(index); closed.Add(current);
                if ((current - goal).sqrMagnitude <= 2 && Mathf.Abs(points[current].y - target.y) < .55f&&(!precise||Segment(points[current],target)))
                {
                    var endpoint=points[current];
                    while (current != start) { path.Add(points[current]); current = parents[current]; }
                    path.Reverse();if(Ground(target,out var exact)&&Segment(endpoint,exact))path.Add(exact); return path.Count > 0;
                }
                for (int d = 0; d < 4; ++d)
                {
                    var next = current + (d == 0 ? Vector2Int.right : d == 1 ? Vector2Int.left : d == 2 ? Vector2Int.up : Vector2Int.down);
                    if (closed.Contains(next) || rejected.Contains(next)) continue;
                    if (!points.TryGetValue(next, out var point))
                    {
                        var sample = new Vector3(next.x * Cell, points[current].y, next.y * Cell);
                        if (!Ground(sample, out point) || !Clear(point,.035f) || !Segment(points[current], point)) { rejected.Add(next); continue; }
                        points[next] = point;
                    }
                    float cost = costs[current] + 1;
                    if (costs.TryGetValue(next, out var previousCost) && cost >= previousCost) continue;
                    costs[next] = cost; parents[next] = current;
                    if (!open.Contains(next)) open.Add(next);
                }
            }
            return false;
        }
        public Vector3 Next(Vector3 position)
        {
            while (path.Count > 0 && Vector3.ProjectOnPlane(path[0] - position, Vector3.up).sqrMagnitude < (path.Count==1?.0025f:.0001f)) path.RemoveAt(0);
            return path.Count > 0 ? path[0] : position;
        }
    }
}
