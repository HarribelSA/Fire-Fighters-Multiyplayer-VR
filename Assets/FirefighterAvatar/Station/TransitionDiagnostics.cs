using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
namespace FirefighterVR.Station
{
    // Bounded local telemetry. No network, scene ownership or readiness changes.
    public static class TransitionDiagnostics
    {
        [Serializable] public struct Sample{public string route,stage;public double elapsedMs,stageMs;public int frame;public long allocatedBytes;}
        static readonly List<Sample> samples=new List<Sample>(128);
        static string route="";static long began,last;static bool active,hooked;static double previousFrame;
        public static double LargestFrameGapMs{get;private set;}
        public static IReadOnlyList<Sample> Samples=>samples;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset(){if(hooked)SceneManager.sceneLoaded-=Loaded;hooked=false;active=false;route="";samples.Clear();LargestFrameGapMs=0;previousFrame=0;}
        static double Ms(long ticks)=>ticks*1000.0/System.Diagnostics.Stopwatch.Frequency;
        public static void Begin(string name){if(active&&route==name)return;if(!hooked){SceneManager.sceneLoaded+=Loaded;hooked=true;}route=name;began=last=System.Diagnostics.Stopwatch.GetTimestamp();active=true;LargestFrameGapMs=0;previousFrame=Time.realtimeSinceStartupAsDouble;Mark("begin");}
        public static void Mark(string stage){if(!active)return;long now=System.Diagnostics.Stopwatch.GetTimestamp();Add(new Sample{route=route,stage=stage,elapsedMs=Ms(now-began),stageMs=Ms(now-last),frame=Time.frameCount,allocatedBytes=Profiler.GetTotalAllocatedMemoryLong()});last=now;}
        static void Add(Sample sample){if(samples.Count==128)samples.RemoveAt(0);samples.Add(sample);}
        public static void End(){if(!active)return;Mark("complete");Debug.Log("[TransitionTiming] "+route+" total="+samples[samples.Count-1].elapsedMs.ToString("F1")+"ms maxFrameGap="+LargestFrameGapMs.ToString("F1")+"ms; see TransitionDiagnostics.Samples for stages");active=false;}
        public static void Tick(){if(!active)return;double now=Time.realtimeSinceStartupAsDouble;LargestFrameGapMs=Math.Max(LargestFrameGapMs,(now-previousFrame)*1000);previousFrame=now;}
        static void Loaded(Scene scene,LoadSceneMode mode){if(mode==LoadSceneMode.Additive)Mark("local_scene_activated:"+scene.name);}
        public static IEnumerator WaitForLoad(AsyncOperation operation){bool payload=false;while(!operation.isDone){if(!payload&&operation.progress>=.9f){payload=true;Mark("payload_90_observed");}yield return null;}Mark("local_load_completed");}
        public readonly struct Scope:IDisposable{readonly string name;readonly long start;public Scope(string name){this.name=name;start=System.Diagnostics.Stopwatch.GetTimestamp();}public void Dispose(){if(active)Add(new Sample{route=route,stage=name,elapsedMs=Ms(System.Diagnostics.Stopwatch.GetTimestamp()-began),stageMs=Ms(System.Diagnostics.Stopwatch.GetTimestamp()-start),frame=Time.frameCount,allocatedBytes=Profiler.GetTotalAllocatedMemoryLong()});}}
        public static Scope Measure(string stage)=>new Scope(stage);
        public static void Cancel(string reason){Mark(reason);active=false;}
    }
}
