#if UNITY_EDITOR
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
namespace FirefighterVR.Station.Editor
{
    public static class TransitionPerformanceProbe
    {
        public static bool Running{get;private set;}
        public static string Begin(string label)
        {if(!EditorApplication.isPlaying||Running)throw new Exception("Requires idle Play Mode probe");var flow=UnityEngine.Object.FindFirstObjectByType<StationSessionFlow>();if(!flow||flow.SoloMissionActive||flow.IsSpawned)throw new Exception("Probe requires offline lobby; no mission or network session");foreach(var name in new[]{"Sample","demo_city_night"})if(SceneManager.GetSceneByName(name).isLoaded)throw new Exception("Start with lobby only");Running=true;flow.StartCoroutine(Run(label));return "Started bounded offline additive load probe: "+label;}
        static IEnumerator Run(string label)
        {
            var results=new List<object>();var loaded=new List<Scene>();
            try
            {
                foreach(var path in new[]{"Assets/FireStation/Scenes/Sample.unity","Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity"})
                {
                    TransitionDiagnostics.Begin("probe_"+label+"_"+path);long memory=Profiler.GetTotalAllocatedMemoryLong();long managed=GC.GetTotalMemory(false);int collections=GC.CollectionCount(0);double start=Time.realtimeSinceStartupAsDouble,activation=-1,progress90=-1,maxGap=0,last=start;
                    UnityEngine.Events.UnityAction<Scene,LoadSceneMode> callback=(s,m)=>{if(s.path==path)activation=Time.realtimeSinceStartupAsDouble;};SceneManager.sceneLoaded+=callback;
                    var operation=SceneManager.LoadSceneAsync(path,LoadSceneMode.Additive);
                    while(!operation.isDone){double now=Time.realtimeSinceStartupAsDouble;maxGap=Math.Max(maxGap,(now-last)*1000);last=now;if(progress90<0&&operation.progress>=.9f)progress90=now;yield return null;}
                    double complete=Time.realtimeSinceStartupAsDouble;maxGap=Math.Max(maxGap,(complete-last)*1000);SceneManager.sceneLoaded-=callback;loaded.Add(SceneManager.GetSceneByPath(path));
                    // Observe Start readiness rather than substituting a tracked-player/mission acknowledgement.
                    yield return null;double ready=Time.realtimeSinceStartupAsDouble;
                    bool controllerReady=true;if(path.Contains("demo_city")){var m=loaded[loaded.Count-1].GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<CityNightPresentation.Mission.FireTruckMission>(true)).Single();controllerReady=m.controller.Initialized;}
                    var origins=UnityEngine.Object.FindObjectsByType<Unity.XR.CoreUtils.XROrigin>(FindObjectsInactive.Include,FindObjectsSortMode.None);
                    bool cleanupBlocked=!TransitionMemory.ReleaseAfterReturn().MoveNext();
                    results.Add(new{scene=path,loadThroughActivationMs=(complete-start)*1000,sceneLoadedEventMs=activation<0?(double?)null:(activation-start)*1000,payload90ObservedMs=progress90<0?(double?)null:(progress90-start)*1000,activationTailMs=progress90<0||activation<0?(double?)null:(activation-progress90)*1000,startObservationMs=(ready-complete)*1000,maxObservedFrameGapMs=maxGap,allocatedDeltaBytes=Profiler.GetTotalAllocatedMemoryLong()-memory,managedDeltaBytes=GC.GetTotalMemory(false)-managed,gen0Collections=GC.CollectionCount(0)-collections,controllerReady,xrOrigins=origins.Length,activeHmdCameras=origins.Count(o=>o.Camera&&o.Camera.enabled&&o.Camera.gameObject.activeInHierarchy),cleanupBlockedWhileContentLoaded=cleanupBlocked});TransitionDiagnostics.End();
                }
                foreach(var scene in loaded.AsEnumerable().Reverse())if(scene.isLoaded)yield return SceneManager.UnloadSceneAsync(scene);
                var report=new{scope="Warm Unity Editor offline load probe; not a validated XR/mission/network transition. Timings include editor overhead and retained assets. Shader GPU cost not isolated.",results,samples=TransitionDiagnostics.Samples.ToArray(),retainedAllocatedBytes=Profiler.GetTotalAllocatedMemoryLong()};File.WriteAllText("Assets/FirefighterAvatar/Station/Reports/TransitionProbe_"+label+".json",Newtonsoft.Json.JsonConvert.SerializeObject(report,Newtonsoft.Json.Formatting.Indented));
            }
            finally{Running=false;}
        }
        public static string BeginCleanup()
        {if(!EditorApplication.isPlaying||Running||SceneManager.GetSceneByName("Sample").isLoaded||SceneManager.GetSceneByName("demo_city_night").isLoaded)throw new Exception("Requires offline lobby after probe");Running=true;UnityEngine.Object.FindFirstObjectByType<StationSessionFlow>().StartCoroutine(Cleanup());return "Started unused-asset measurement";}
        static IEnumerator Cleanup()
        {long before=Profiler.GetTotalAllocatedMemoryLong();double start=Time.realtimeSinceStartupAsDouble;yield return Resources.UnloadUnusedAssets();var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{scope="Offline lobby after station/city unload; referenced assets protected by Unity",elapsedMs=(Time.realtimeSinceStartupAsDouble-start)*1000,beforeBytes=before,afterBytes=Profiler.GetTotalAllocatedMemoryLong()},Newtonsoft.Json.Formatting.Indented);File.WriteAllText("Assets/FirefighterAvatar/Station/Reports/TransitionUnusedAssetsProbe.json",json);Running=false;}
    }
}
#endif
