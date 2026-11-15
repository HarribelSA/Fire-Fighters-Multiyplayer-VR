#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FirefighterVR.Station.Editor
{
    public static class StationTransitionVerification
    {
        static void Require(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
        public static string Run()
        {
            Require(!EditorApplication.isPlaying,"Use isolated Edit Mode verification.");
            var scene=EditorSceneManager.OpenPreviewScene(StationTransitionInstaller.StationPath);
            try
            {
                var setup=FirefighterVR.StationBot.StationSoloBotTest.FindSetup(scene);
                var lobbyScene=SceneManager.GetSceneByPath("Assets/S1Test.unity");
                var geometryRig=lobbyScene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StationLocalRig>(true)).Single().origin;
                var samples=StationTransitionInstaller.SafeSurfaceSamples(setup,geometryRig);
                Require(setup.walkableSurfaces.Length==4,"Expected three reused areas and one measured missing-landing patch.");
                Require(new[]{setup.driverSpawn,setup.assistantSpawn,setup.botSpawn}.All(t=>t&&setup.ValidDestination(t.position,null,false)),"Spawn anchor clearance failed.");
                Require(Vector3.Distance(setup.driverSpawn.position,setup.assistantSpawn.position)>1.5f,"Human anchors overlap.");
                Require(setup.botSpawn.name=="Station_Bot_Spawn","Bot anchor missing.");
                var levels=samples.GroupBy(p=>Mathf.Round(p.y*20)/20).OrderBy(g=>g.Key).Select(g=>new{height=g.Key,samples=g.Count()}).ToArray();
                Require(levels.Any(g=>g.height>3&&g.height<4),"Safe intermediate landing absent.");
                Require(levels.Any(g=>g.height>6&&g.height<7)&&levels.Any(g=>g.height>10.8f),"Upper floor/platform coverage absent.");
                // Fixture exists only inside an isolated preview; no saved/runtime XR duplicate.
                var root=new GameObject("SpawnOffsetFixture");SceneManager.MoveGameObjectToScene(root,scene);
                var floor=new GameObject("FloorOffset");floor.transform.SetParent(root.transform,false);
                var head=new GameObject("TrackedCamera");head.transform.SetParent(floor.transform,false);var camera=head.AddComponent<Camera>();camera.enabled=false;
                var rig=root.AddComponent<XROrigin>();rig.Origin=root;rig.Camera=camera;rig.CameraFloorOffsetObject=floor;
                var controller=root.AddComponent<CharacterController>();controller.height=1.8f;controller.radius=.1f;controller.center=Vector3.up*.9f;
                var spawnTests=new List<object>();
                foreach(var anchor in new[]{setup.driverSpawn,setup.assistantSpawn})foreach(var offset in new[]{new Vector3(2.2f,1.72f,-1.6f),new Vector3(-1.8f,1.45f,2.4f),new Vector3(.3f,1.9f,.4f)})
                {
                    root.transform.SetPositionAndRotation(new Vector3(40,4,-20),Quaternion.Euler(0,127,0));floor.transform.localPosition=new Vector3(0,.3f,0);head.transform.localPosition=offset;
                    var before=head.transform.localPosition;var height=rig.CameraInOriginSpaceHeight;
                    StationSpawnPlacement.Place(rig,anchor);
                    Require(StationSpawnPlacement.Aligned(rig,anchor,.001f),"Room-scale ground offset did not align.");
                    Require(head.transform.localPosition==before&&Mathf.Abs(rig.CameraInOriginSpaceHeight-height)<.001f,"Camera tracking pose/height changed.");
                    Require(controller.enabled,"CharacterController enabled state changed.");
                    spawnTests.Add(new{anchor=anchor.name,offset=offset.ToString(),error=Vector3.Distance(StationSpawnPlacement.GroundPosition(rig),anchor.position)});
                }
                UnityEngine.Object.DestroyImmediate(root);
                // Prove a chain of head-clear visible destinations through the stairwell in both directions.
                var stairPoints=samples.Where(p=>p.x>14&&p.x<19.5f&&p.z>.7f&&p.z<9.5f).ToList();
                foreach(float y in new[]{.005f,3.3646f,6.383f,11.005f})
                    for(float x=15.6f;x<19.1f;x+=.4f)for(float z=1.1f;z<9;z+=.4f)
                    {var p=new Vector3(x,y,z);if(setup.ValidDestination(p,geometryRig,false)&&!stairPoints.Any(q=>(q-p).sqrMagnitude<.09f))stairPoints.Add(p);}
                var path=StairPath(setup,stairPoints);
                Require(path.Count>0,"No bidirectional staircase route with the existing 10 m/s arcs.");
                var arcProfiles=new List<object>();
                foreach(float velocity in new[]{7.5f,8.5f,9f,10f}){var candidate=StairPath(setup,stairPoints,velocity);arcProfiles.Add(new{velocity,path=candidate.Select(p=>p.ToString("F3")).ToArray()});}
                var obstacle=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(obstacle,scene);
                var point=setup.driverSpawn.position;obstacle.transform.position=point+Vector3.up*.9f;obstacle.transform.localScale=new Vector3(.7f,1.8f,.7f);Physics.SyncTransforms();
                Require(!setup.ValidDestination(point,null,false),"Occupied furniture destination accepted.");
                UnityEngine.Object.DestroyImmediate(obstacle);Physics.SyncTransforms();
                Require(!setup.ValidDestination(new Vector3(-40,.005f,40),null,false),"Outside building destination accepted.");
                var wall=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(wall,scene);
                wall.transform.position=point+Vector3.right+Vector3.up;wall.transform.localScale=new Vector3(.2f,2,2);Physics.SyncTransforms();
                Require(!setup.ReachableFrom(point+Vector3.up*1.7f,point+Vector3.right*2),"Solid wall/closed-door sight obstruction accepted.");
                UnityEngine.Object.DestroyImmediate(wall);Physics.SyncTransforms();
                var ceiling=GameObject.CreatePrimitive(PrimitiveType.Cube);SceneManager.MoveGameObjectToScene(ceiling,scene);
                ceiling.transform.position=point+Vector3.up*2.4f;ceiling.transform.localScale=new Vector3(5,.2f,5);Physics.SyncTransforms();
                Require(!setup.ReachableFrom(point+Vector3.up*1.7f,point+Vector3.up*3),"Teleport through a solid ceiling accepted.");
                UnityEngine.Object.DestroyImmediate(ceiling);
                var lobby=SceneManager.GetSceneByPath("Assets/S1Test.unity");
                var effect=lobby.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StationViewFade>(true)).Single();
                var origin=effect.origin;
                Require(effect.overlay&&effect.fadeMaterial&&effect.fadeMaterial.shader.name=="FirefighterVR/LocalHeadsetFade","Local stereo fade references missing.");
                Require(origin.GetComponentsInChildren<TeleportationProvider>(true).Length==1,"Existing teleport provider duplicated.");
                var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{scope="Isolated Edit Mode physics/geometry and tracked-pose fixtures; no live XR/network session",safeSamples=samples.Count,levels,stairPath=path.Select(p=>p.ToString("F3")).ToArray(),stairPathFound=path.Count>0,arcProfiles,spawnOffsets=spawnTests,invalidFurnitureRejected=true,wallRejected=true,ceilingRejected=true,outsideRejected=true,existingTeleportProviders=1,areas=setup.walkableSurfaces.Length,missingScripts=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)),shaderErrors=ShaderUtil.GetShaderMessages(effect.fadeMaterial.shader).Where(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error).Select(m=>m.message).ToArray(),hardwareValidation="Not performed: stereo HMD, gestures, reticles and two-device networking require live verification."},Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(StationTransitionInstaller.Folder+"/Reports/TransitionVerification.json",json);return json;
            }
            finally{EditorSceneManager.ClosePreviewScene(scene);}
        }
        static List<Vector3> StairPath(StationSceneSetup setup,List<Vector3> points,float speed=10)
        {
            var parents=new Dictionary<int,int>();var queue=new Queue<int>();
            for(int i=0;i<points.Count;i++)if(points[i].y<.1f){parents[i]=-1;queue.Enqueue(i);}
            while(queue.Count>0)
            {
                int from=queue.Dequeue();
                if(points[from].y>10.8f)
                {
                    var path=new List<Vector3>();while(from>=0){path.Add(points[from]);from=parents[from];}path.Reverse();return path;
                }
                for(int to=0;to<points.Count;to++)
                {
                    var delta=points[to]-points[from];if(parents.ContainsKey(to)||delta.magnitude>9||Mathf.Abs(delta.y)>4.8f)continue;
                    if(!setup.ReachableFrom(points[from]+Vector3.up*1.7f,points[to])||!setup.ReachableFrom(points[to]+Vector3.up*1.7f,points[from]))continue;
                    if(!BallisticClear(setup,points[from],points[to],speed)||!BallisticClear(setup,points[to],points[from],speed))continue;
                    parents[to]=from;queue.Enqueue(to);
                }
            }
            return new List<Vector3>();
        }
        static bool BallisticClear(StationSceneSetup setup,Vector3 from,Vector3 to,float speed)
        {
            const float gravity=9.8f;
            var direction=Vector3.ProjectOnPlane(to-from,Vector3.up).normalized;
            var start=from+Vector3.up*1.3f+direction*.2f;var horizontal=Vector3.ProjectOnPlane(to-start,Vector3.up);float range=horizontal.magnitude;if(range<.1f)return false;
            float rise=to.y-start.y;float discriminant=Mathf.Pow(speed,4)-gravity*(gravity*range*range+2*rise*speed*speed);if(discriminant<0)return false;
            var hits=new RaycastHit[64];
            foreach(float sign in new[]{-1f,1f})
            {
                float angle=Mathf.Atan((speed*speed+sign*Mathf.Sqrt(discriminant))/(gravity*range));float duration=range/(speed*Mathf.Cos(angle));
                if(speed*Mathf.Sin(angle)-gravity*duration>=0)continue; // An upward-facing landing is hit while descending.
                Vector3 previous=start;bool clear=true;
                for(int i=1;i<=64;i++)
                {
                    float t=duration*i/64;var p=start+horizontal.normalized*(speed*Mathf.Cos(angle)*t)+Vector3.up*(speed*Mathf.Sin(angle)*t-.5f*gravity*t*t);var delta=p-previous;
                    int count=setup.gameObject.scene.GetPhysicsScene().Raycast(previous,delta.normalized,hits,delta.magnitude,~0,QueryTriggerInteraction.Ignore);
                    if(count==hits.Length){clear=false;break;}
                    for(int h=0;h<count;h++)if(hits[h].collider.gameObject.scene==setup.gameObject.scene&&Vector3.Distance(hits[h].point,to)>.08f){clear=false;break;}
                    if(!clear)break;previous=p;
                }
                if(clear)return true;
            }
            return false;
        }
    }
}
#endif
