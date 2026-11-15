using System.Collections;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
namespace FirefighterVR.Station
{
    public static class TransitionMemory
    {
        static bool busy;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]static void Reset()=>busy=false;
        public static IEnumerator ReleaseAfterReturn()
        {
            // Only unreferenced assets after both content scenes have left, never retained station/lobby objects.
            var flow=StationSessionFlow.Instance;
            if(busy||SceneManager.GetSceneByName("Sample").isLoaded||SceneManager.GetSceneByName(StationSessionFlow.CitySceneName).isLoaded||flow&&flow.MissionSessionActive)yield break;
            long before=Profiler.GetTotalAllocatedMemoryLong();if(before<1536L*1024*1024)yield break;
            var fade=StationViewFade.Instance;if(!fade||!fade.BlackRendered)yield break;
            busy=true;double start=Time.realtimeSinceStartupAsDouble;
            try{yield return Resources.UnloadUnusedAssets();}
            finally{busy=false;Debug.Log("[TransitionMemory] Safe return cleanup "+((Time.realtimeSinceStartupAsDouble-start)*1000).ToString("F1")+"ms; allocated delta "+(before-Profiler.GetTotalAllocatedMemoryLong())+" bytes");}
        }
    }
}
