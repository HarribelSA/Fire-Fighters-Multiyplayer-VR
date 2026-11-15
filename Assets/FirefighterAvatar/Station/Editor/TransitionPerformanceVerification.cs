#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
namespace FirefighterVR.Station.Editor
{
    public static class TransitionPerformanceVerification
    {
        public static string BenchmarkSoles()
        {
            if(EditorApplication.isPlaying)throw new Exception("Editor clone measurement only");var original=UnityEngine.Object.FindFirstObjectByType<StationLocalRig>(FindObjectsInactive.Include).avatar;
            var clone=UnityEngine.Object.Instantiate(original.placement.gameObject);clone.name="TEMP_SolePerformanceFixture";var scratch=new Mesh();var baselineMesh=new Mesh();var vertices=new List<Vector3>();var results=new List<object>();
            try
            {
                var avatar=clone.GetComponentInChildren<FirefighterAvatarDriver>(true);
                foreach(var shoe in avatar.footwear)
                {
                    float Baseline(){shoe.BakeMesh(baselineMesh);baselineMesh.GetVertices(vertices);float min=float.PositiveInfinity;foreach(var p in vertices)min=Mathf.Min(min,shoe.transform.TransformPoint(p).y);return min;}
                    var cache=new AvatarSoleCache(shoe);Baseline();cache.Measure(scratch);const int count=160;var sw=System.Diagnostics.Stopwatch.StartNew();for(int i=0;i<count;i++)Baseline();sw.Stop();double before=sw.Elapsed.TotalMilliseconds/count;
                    sw.Restart();for(int i=0;i<count;i++)cache.Measure(scratch);sw.Stop();double after=sw.Elapsed.TotalMilliseconds/count;
                    var bone=shoe.bones.First(b=>b&&b.name.Contains("Foot"));var q=bone.localRotation;var basePosition=clone.transform.position;var baseRotation=clone.transform.rotation;double maxError=0;
                    int rigidHits=0;
                    try{for(int i=0;i<36;i++){clone.transform.SetPositionAndRotation(basePosition+new Vector3(i*.17f,i*.005f,0),Quaternion.Euler(0,i*7,0)*baseRotation);bone.localRotation=q*Quaternion.Euler(Mathf.Sin(i*.3f)*25,0,0);float expected=Baseline(),measured=cache.Measure(scratch);maxError=Math.Max(maxError,Math.Abs(expected-measured));}int hits=cache.CacheHits;for(int i=0;i<24;i++){clone.transform.SetPositionAndRotation(basePosition+new Vector3(241+i*.2f,6,0),Quaternion.Euler(0,i*13,0)*baseRotation);float expected=Baseline(),measured=cache.Measure(scratch);maxError=Math.Max(maxError,Math.Abs(expected-measured));}rigidHits=cache.CacheHits-hits;}
                    finally{bone.localRotation=q;clone.transform.SetPositionAndRotation(basePosition,baseRotation);}
                    if(maxError>.001)throw new Exception("Sole cache exceeded 1 mm error: "+maxError);
                    if(rigidHits<20)throw new Exception("Rigid motion did not reuse skin geometry");results.Add(new{shoe.name,samples=count,baselineMeanMs=before,cachedMeanMs=after,maxDynamicErrorMetres=maxError,rigidHits,cache.BakeCount,cache.CacheHits});
                }
                var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{scope="Warm Editor microbenchmark on cloned current avatar, same full-vertex minimum; not Quest frame-time",results},Newtonsoft.Json.Formatting.Indented);File.WriteAllText("Assets/FirefighterAvatar/Station/Reports/SolePerformanceComparison.json",json);return json;
            }
            finally{UnityEngine.Object.DestroyImmediate(scratch);UnityEngine.Object.DestroyImmediate(baselineMesh);UnityEngine.Object.DestroyImmediate(clone);}
        }
    }
}
#endif
