#if UNITY_EDITOR
using System;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CityNightPresentation.Mission;
namespace CityNightPresentation.Editor
{
    public static class ResponseIntersectionUpdate
    {
        public const string ScenePath="Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity";
        public static string Apply()
        {
            if(EditorApplication.isPlaying)throw new Exception("Edit Mode only");var scene=SceneManager.GetSceneByPath(ScenePath);if(!scene.isLoaded)scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            var mission=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<FireTruckMission>(true)).Single();var traffic=mission.traffic;var first=traffic.signals[0];
            first.responseChallenge=true;first.responseApproach=0;first.responseWaitSeconds=60;
            foreach(var signal in traffic.signals)
            {
                foreach(var head in signal.heads)
                {
                    var f=head.stopLine.forward;var right=Vector3.Cross(Vector3.up,f);var basePoint=head.stopLine.position-Vector3.up*.03f+right*4.4f;var parent=head.red.transform.parent;
                    var pole=parent.Find("Pole");pole.position=basePoint+Vector3.up*2.05f;pole.localScale=new Vector3(.16f,4.1f,.16f);
                    var box=parent.Find("SignalHousing");box.position=basePoint+Vector3.up*3.45f;box.localScale=new Vector3(.68f,1.65f,.35f);
                    var lenses=new[]{head.red,head.yellow,head.green};for(int i=0;i<3;i++){lenses[i].transform.position=basePoint+Vector3.up*(3.97f-i*.52f)-f*.19f;lenses[i].enabled=true;lenses[i].gameObject.SetActive(true);}
                }
                foreach(var pair in new[]{(signal.redOn,new Color(1,.025f,.015f)),(signal.yellowOn,new Color(1,.6f,.025f)),(signal.greenOn,new Color(.025f,1,.12f))})
                {if(!AssetDatabase.GetAssetPath(pair.Item1).StartsWith("Assets/CityNightPresentation/"))throw new Exception("Refusing shared source material edit");pair.Item1.shader=Shader.Find("Universal Render Pipeline/Unlit");pair.Item1.SetColor("_BaseColor",pair.Item2);if(pair.Item1.HasProperty("_Cull"))pair.Item1.SetFloat("_Cull",0);EditorUtility.SetDirty(pair.Item1);}
                signal.RefreshVisuals();EditorUtility.SetDirty(signal);
            }
            var old=traffic.vehicles[2].path;old.Initialize();int join=Array.FindIndex(old.points,p=>Vector3.Distance(p,new Vector3(115,4.75f,-78))<1);
            if(join<0)throw new Exception("Existing loop merge point missing");
            var parentPath=old.transform.parent;var existing=parentPath.Find("FirstJunction_ResponseQueue");var go=existing?existing.gameObject:new GameObject("FirstJunction_ResponseQueue");if(!existing)go.transform.SetParent(parentPath,false);var path=go.GetComponent<CityTrafficPath>();if(!path)path=go.AddComponent<CityTrafficPath>();
            var points=new List<Vector3>();var yields=new List<float>();
            // Exact existing drivable route prefix, then merge into the existing southern civilian loop.
            foreach(var x in new[]{170f,190f,207f,217f}){var source=mission.route.waypoints.OrderBy(w=>Mathf.Abs(w.position.x-x)+Mathf.Abs(w.position.z-14)).First();float z=Mathf.Lerp(16,14,Mathf.InverseLerp(170,220,x));points.Add(new Vector3(x,source.position.y-.04f,z));yields.Add(3.3f);}
            int firstGate=3;
            foreach(var waypoint in mission.route.waypoints.Skip(21).Take(32)){points.Add(waypoint.position-Vector3.up*.04f);yields.Add(waypoint.position.x<=220&&waypoint.position.z>10?3.3f:0);}
            path.loopStartPoint=points.Count;
            for(int n=0;n<old.points.Length;n++){int i=(join+n)%old.points.Length;points.Add(old.points[i]);yields.Add(old.safeYieldOffsets[i]);}
            path.points=points.ToArray();path.safeYieldOffsets=yields.ToArray();path.speedLimit=6;
            var gates=new List<CityTrafficPath.Gate>{new CityTrafficPath.Gate{signal=first,approach=0,stopPointIndex=firstGate,clearanceDistance=45}};
            // Prefix also crosses the second junction southbound and third westbound.
            int Nearest(Vector3 p,int start,int end)=>Enumerable.Range(start,end-start).OrderBy(i=>(path.points[i]-p).sqrMagnitude).First();
            gates.Add(new CityTrafficPath.Gate{signal=traffic.signals[1],approach=1,stopPointIndex=Nearest(new Vector3(241,4.7f,-59),4,path.loopStartPoint),clearanceDistance=45});
            foreach(var gate in old.gates){int relative=(gate.stopPointIndex-join+old.points.Length)%old.points.Length;var copy=gate;copy.stopPointIndex=path.loopStartPoint+relative;gates.Add(copy);}
            path.gates=gates.ToArray();path.Initialize();
            for(int i=0;i<2;i++){var v=traffic.vehicles[i];v.path=path;v.distance=path.GateDistance(0)-i*11;v.speed=0;v.yieldOffset=0;v.reserved=null;path.Sample(v.distance,out var p,out var f);v.body.SetPositionAndRotation(p,Quaternion.LookRotation(f));v.body.gameObject.SetActive(true);}
            traffic.activeVehicles=6;EditorUtility.SetDirty(path);EditorUtility.SetDirty(traffic);
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new Exception("Scene save failed");AssetDatabase.SaveAssets();return "Saved visible low-mounted signals, two first-junction queued civilians, existing-loop merge and 60-second response wait.";
        }
        public static string Verify()
        {
            var checks=new List<string>();void Check(bool p,string n){if(!p)throw new Exception(n);checks.Add(n);}
            var go=new GameObject("TEMP_ResponseSignalTest"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var s=go.AddComponent<CitySignalController>();s.responseChallenge=true;s.responseApproach=0;s.responseWaitSeconds=60;s.SetClock(0);Check(s.GetAspect(0)==CitySignalController.Aspect.Red,"Mission approach held red before truck arrival");s.StartResponseWait();
                for(int i=1;i<60;i++)s.SetClock(i);Check(!s.Allows(0)&&s.ResponseWaitRemaining==1,"No normal opening before 60 seconds of waiting");s.SetClock(60);s.SetClock(63.1f);s.SetClock(65.2f);Check(s.Allows(0),"Normal 60-second expiry grants safe green");s.ReleasePriority();Check(s.HasPriority,"Normal timed green is not cancelled by inactive emergency update");s.CompleteResponsePassage();s.SetClock(65.3f);Check(s.GetAspect(0)==CitySignalController.Aspect.Yellow,"Passage releases priority through yellow");
            }
            finally{UnityEngine.Object.DestroyImmediate(go);}
            go=new GameObject("TEMP_ResponseEmergencyTest"){hideFlags=HideFlags.HideAndDontSave};
            try{var s=go.AddComponent<CitySignalController>();s.responseChallenge=true;s.SetClock(18);s.StartResponseWait();s.RequestPriority(0);s.SetClock(18.1f);Check(s.GetAspect(1)==CitySignalController.Aspect.Yellow,"Siren priority first clears cross traffic through yellow");s.SetClock(21.1f);Check(Enumerable.Range(0,4).All(i=>!s.Allows(i)),"Siren includes all-red clearance");s.SetClock(23.2f);Check(s.Allows(0)&&Enumerable.Range(0,4).Count(s.Allows)==1&&s.ResponseWaitRemaining>0,"Siren safely bypasses remaining minute with one green only");}finally{UnityEngine.Object.DestroyImmediate(go);}
            var scene=SceneManager.GetSceneByPath(ScenePath);var m=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<FireTruckMission>(true)).Single();var path=m.traffic.vehicles[0].path;path.Initialize();Check(path==m.traffic.vehicles[1].path&&path.gates[0].signal==m.traffic.signals[0],"Two actual civilians queue at first mission signal");
            Check(float.IsPositiveInfinity(path.DistanceToGate(0,path.Length+10)),"Merged vehicles do not loop or teleport back to approach prefix");path.Sample(path.Length-.001f,out var before,out _);path.Sample(path.Length+.001f,out var after,out _);Check(Vector3.Distance(before,after)<.01f,"Civilian prefix merges continuously into existing road loop");
            Check(path.YieldClearance(m.traffic.vehicles[0].distance)>3,"Queued civilians have lateral emergency clearance");
            VerifyQueue(m,checks);
            Check(m.traffic.signals.SelectMany(s=>s.heads).All(h=>h.red.enabled&&h.red.gameObject.activeInHierarchy&&h.red.transform.position.y-h.stopLine.position.y<4.1f&&h.red.sharedMaterials.All(mat=>mat)),"Signal lenses active and within driver-visible roadside height");
            var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{scope="Edit Mode phase, queue, geometry and path checks; physical Quest validation still required",checks},Newtonsoft.Json.Formatting.Indented);System.IO.File.WriteAllText("Assets/FirefighterAvatar/Station/Reports/ResponseIntersectionVerification.json",json);return json;
        }
        static void VerifyQueue(FireTruckMission mission,List<string> checks)
        {
            var root=new GameObject("TEMP_ResponseQueueTest"){hideFlags=HideFlags.HideAndDontSave};
            try
            {
                var source=mission.traffic.vehicles[0].path;var p=root.AddComponent<CityTrafficPath>();p.points=(Vector3[])source.points.Clone();p.safeYieldOffsets=(float[])source.safeYieldOffsets.Clone();p.loopStartPoint=source.loopStartPoint;p.Initialize();
                var signal=root.AddComponent<CitySignalController>();signal.responseChallenge=true;signal.responseApproach=0;signal.responseWaitSeconds=60;p.gates=new[]{new CityTrafficPath.Gate{signal=signal,approach=0,stopPointIndex=source.gates[0].stopPointIndex,clearanceDistance=45}};
                var traffic=root.AddComponent<CityTrafficManager>();traffic.activeVehicles=2;traffic.signals=new[]{signal};traffic.vehicles=new CityTrafficManager.Vehicle[2];
                for(int i=0;i<2;i++){var body=new GameObject("QueueVehicle_"+i).transform;body.SetParent(root.transform);traffic.vehicles[i]=new CityTrafficManager.Vehicle{body=body,path=p,distance=p.GateDistance(0)-i*11,length=5,cruiseSpeed=6};}
                traffic.Initialize();float d=traffic.vehicles[0].distance;for(int i=0;i<40;i++)traffic.SimulateStep(.05f);if(Mathf.Abs(traffic.vehicles[0].distance-d)>.01f)throw new Exception("Red queue moved through signal");checks.Add("Actual traffic simulation holds lead civilian at red");
                signal.StartResponseWait();for(int i=0;i<1320;i++)traffic.SimulateStep(.05f);if(traffic.vehicles[0].distance<=d+.1f)throw new Exception("Timed queue failed to resume");checks.Add("Normal no-siren traffic resumes after minute and clearance");
                // Reuse clean temporary objects for emergency branch.
                UnityEngine.Object.DestroyImmediate(signal);signal=root.AddComponent<CitySignalController>();signal.responseChallenge=true;signal.responseApproach=0;signal.SetClock(0);p.gates[0]=new CityTrafficPath.Gate{signal=signal,approach=0,stopPointIndex=source.gates[0].stopPointIndex,clearanceDistance=45};traffic.signals=new[]{signal};
                for(int i=0;i<2;i++){traffic.vehicles[i].distance=d-i*11;traffic.vehicles[i].yieldRequested=true;traffic.vehicles[i].yieldOffset=0;}
                traffic.ResetClock();traffic.Initialize();signal.StartResponseWait();signal.RequestPriority(0);for(int i=0;i<160;i++)traffic.SimulateStep(.05f);
                if(!traffic.vehicles.All(v=>v.yieldOffset>=3&&v.reserved==null)||Mathf.Abs(traffic.vehicles[0].distance-d)>.01f||!signal.Allows(0))throw new Exception("Emergency queue failed to yield and hold clear");checks.Add("Siren branch shifts both civilians aside and holds clear despite green");
            }
            finally{UnityEngine.Object.DestroyImmediate(root);}
        }
    }
}
#endif
