using System;
using UnityEngine;

namespace CityNightPresentation
{
    public sealed class CityTrafficPath : MonoBehaviour
    {
        [Serializable] public struct Gate
        {
            public CitySignalController signal;
            [Range(0,3)] public int approach;
            public int stopPointIndex;
            [Min(10)] public float clearanceDistance;
        }
        public Vector3[] points = new Vector3[0];
        public Gate[] gates = new Gate[0];
        public float[] safeYieldOffsets = new float[0];
        [Min(1)] public float speedLimit = 8;
        [Min(0)]public int loopStartPoint;
        public float LoopStartDistance=>cumulative==null?0:cumulative[Mathf.Clamp(loopStartPoint,0,points.Length-1)];
        public float NormalizeDistance(float d)=>loopStartPoint==0?Mathf.Repeat(d,Length):d<Length?Mathf.Max(0,d):LoopStartDistance+Mathf.Repeat(d-LoopStartDistance,Mathf.Max(.01f,Length-LoopStartDistance));
        public float ForwardDistance(float from,float to)
        {from=NormalizeDistance(from);to=NormalizeDistance(to);if(to>=from)return to-from;return to>=LoopStartDistance?Length-from+to-LoopStartDistance:float.PositiveInfinity;}
        public float DistanceToGate(int i,float from)=>ForwardDistance(from,GateDistance(i));
        // Local-only lane loop. Absolute distances are baked once; no per-frame path allocation.
        [NonSerialized] public float[] cumulative;
        public float Length { get; private set; }
        public void Initialize()
        {
            if (points == null || points.Length < 2) { Length = 0; return; }
            cumulative = new float[points.Length + 1];
            loopStartPoint=Mathf.Clamp(loopStartPoint,0,points.Length-1);
            for (int i = 0; i < points.Length; i++) cumulative[i+1] = cumulative[i] + Vector3.Distance(points[i], points[i+1<points.Length?i+1:loopStartPoint]);
            Length = cumulative[points.Length];
        }
        public float GateDistance(int i) => cumulative[Mathf.Clamp(gates[i].stopPointIndex, 0, points.Length-1)];
        public float YieldClearance(float d)
        {
            if(cumulative==null)Initialize();
            if(safeYieldOffsets==null||safeYieldOffsets.Length!=points.Length)return 0;
            d=NormalizeDistance(d);int i=0;while(i<points.Length-1&&cumulative[i+1]<d)i++;
            return Mathf.Min(safeYieldOffsets[i],safeYieldOffsets[i+1<points.Length?i+1:loopStartPoint]);
        }
        public void Sample(float distance, out Vector3 position, out Vector3 forward)
        {
            if (cumulative == null) Initialize();
            if (Length < .01f) { position = transform.position; forward = Vector3.forward; return; }
            float d = NormalizeDistance(distance);
            int lo = 0, hi = points.Length-1;
            while (lo < hi) { int mid = (lo+hi+1)/2; if (cumulative[mid] <= d) lo = mid; else hi = mid-1; }
            int next = lo+1<points.Length?lo+1:loopStartPoint;
            float span = cumulative[lo+1]-cumulative[lo];
            position = Vector3.Lerp(points[lo], points[next], span > .001f ? (d-cumulative[lo])/span : 0);
            forward = (points[next]-points[lo]).normalized;
        }
        void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(.4f,1,.4f);
            for (int i=0;i<points.Length;i++) Gizmos.DrawLine(points[i],points[(i+1)%points.Length]);
        }
    }
}
