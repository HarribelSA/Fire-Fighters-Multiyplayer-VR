#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FirefighterVR.Station.Editor
{
    public static class StationTransitionInstaller
    {
        public const string StationPath="Assets/FireStation/Scenes/Sample.unity";
        public const string Folder="Assets/FirefighterAvatar/Station";
        [MenuItem("Firefighter VR/Station/Install Transition and Teleport Fixes")]
        public static string Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play Mode.");
            var lobby=SceneManager.GetSceneByPath("Assets/S1Test.unity");
            if(!lobby.isLoaded)lobby=EditorSceneManager.OpenScene("Assets/S1Test.unity",OpenSceneMode.Additive);
            if(lobby.isDirty)throw new InvalidOperationException("Lobby has unsaved changes.");
            var rig=lobby.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StationLocalRig>(true)).Single();
            var effect=lobby.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StationViewFade>(true)).SingleOrDefault();
            if(!effect){var root=new GameObject("STATION_LOCAL_TRANSITIONS");SceneManager.MoveGameObjectToScene(root,lobby);effect=root.AddComponent<StationViewFade>();}
            var shader=AssetDatabase.LoadAssetAtPath<Shader>(Folder+"/StationViewFade.shader");
            if(!shader)throw new InvalidOperationException("Local stereo fade shader missing.");
            var material=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/StationTransitionFade.mat");
            if(!material){material=new Material(shader){name="StationTransitionFade"};AssetDatabase.CreateAsset(material,Folder+"/StationTransitionFade.mat");}
            effect.origin=rig.origin;effect.overlay=rig.fadeRenderer;effect.fadeMaterial=material;
            if(!rig.fadeRenderer.GetComponent<MeshFilter>()||rig.fadeRenderer.GetComponent<MeshFilter>().sharedMesh.vertexCount!=4)throw new InvalidOperationException("Existing headset overlay must be a quad.");
            EditorUtility.SetDirty(effect);EditorSceneManager.MarkSceneDirty(lobby);
            var station=SceneManager.GetSceneByPath(StationPath);
            if(!station.isLoaded)station=EditorSceneManager.OpenScene(StationPath,OpenSceneMode.Additive);
            if(station.isDirty)throw new InvalidOperationException("Station has unsaved changes.");
            var setup=station.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StationSceneSetup>(true)).Single();
            setup.architecturalFloors=station.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<MeshCollider>(true)).Where(c=>c.name=="FireStationMainBuilding"||c.name=="FireStationGarage"||c.name=="FireStationGarage (1)").ToArray();
            setup.driverSpawn.name="Station_Player1_Spawn";setup.assistantSpawn.name="Station_Player2_Spawn";
            var rootSurfaces=station.GetRootGameObjects().FirstOrDefault(g=>g.name=="Station_TeleportationSurfaces");
            if(!rootSurfaces){rootSurfaces=new GameObject("Station_TeleportationSurfaces");SceneManager.MoveGameObjectToScene(rootSurfaces,station);}
            var ground=Child(rootSurfaces.transform,"GroundFloor");var upper=Child(rootSurfaces.transform,"UpperFloor");var platforms=Child(rootSurfaces.transform,"Platforms");var stairs=Child(rootSurfaces.transform,"Staircases");
            for(int i=0;i<setup.walkableSurfaces.Length;i++)
            {
                var c=setup.walkableSurfaces[i];c.transform.SetParent(c.name=="StairLandingCoverage"?stairs:i==0?ground:i==1?upper:platforms,true);
                var area=c.GetComponent<StationTeleportArea>();if(!area)throw new InvalidOperationException("Expected the existing StationTeleportArea.");
                area.matchOrientation=MatchOrientation.WorldSpaceUp;
                EditorUtility.SetDirty(area);
            }
            Physics.SyncTransforms();
            AddMissingLandings(setup,stairs,rig.origin);
            if(!setup.ValidDestination(setup.driverSpawn.position,null,false)||!setup.ValidDestination(setup.assistantSpawn.position,null,false))throw new InvalidOperationException("Existing human anchors failed station geometry validation.");
            if(!setup.botSpawn)
            {
                // Select a measured point on the same interior room floor, clear of both anchors.
                var midpoint=(setup.driverSpawn.position+setup.assistantSpawn.position)*.5f;
                Vector3? best=null;float score=float.PositiveInfinity;
                for(float x=midpoint.x-2;x<=midpoint.x+2;x+=.4f)for(float z=midpoint.z-2.4f;z<=midpoint.z+2.4f;z+=.4f)
                {
                    var p=new Vector3(x,midpoint.y,z);
                    if(Vector3.Distance(p,setup.driverSpawn.position)<1.6f||Vector3.Distance(p,setup.assistantSpawn.position)<1.6f||!setup.ValidDestination(p,null,false))continue;
                    float d=(p-midpoint).sqrMagnitude;if(d<score){score=d;best=p;}
                }
                if(!best.HasValue)throw new InvalidOperationException("No clear bot anchor in the existing interior spawn room.");
                var anchor=new GameObject("Station_Bot_Spawn");anchor.transform.SetParent(setup.driverSpawn.parent,false);anchor.transform.SetPositionAndRotation(best.Value,setup.driverSpawn.rotation);setup.botSpawn=anchor.transform;
            }
            var samples=SafeSurfaceSamples(setup,rig.origin);
            var levels=samples.GroupBy(p=>Mathf.Round(p.y*20)/20).OrderBy(g=>g.Key).ToArray();
            foreach(var level in levels.Where(g=>g.Key>.15f))
            {
                var point=level.OrderBy(p=>Vector3.Distance(p,new Vector3(17,p.y,4))).First();
                string name="ValidatedLanding_"+level.Key.ToString("F2",System.Globalization.CultureInfo.InvariantCulture);
                var marker=Child(stairs,name);marker.position=point;
            }
            EditorUtility.SetDirty(setup);EditorSceneManager.MarkSceneDirty(station);
            if(!EditorSceneManager.SaveScene(station)||!EditorSceneManager.SaveScene(lobby))throw new IOException("Scene save failed.");
            AssetDatabase.SaveAssets();
            var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{player1=setup.driverSpawn.position.ToString("F3"),player2=setup.assistantSpawn.position.ToString("F3"),bot=setup.botSpawn.position.ToString("F3"),areas=setup.walkableSurfaces.Length,safeSamples=samples.Count,levels=levels.Select(g=>new{height=g.Key,samples=g.Count()}).ToArray()},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText(Folder+"/Reports/InstalledCoverage.json",json);return json;
        }
        static Transform Child(Transform parent,string name)
        {var t=parent.Find(name);if(t)return t;var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform;}
        static void AddMissingLandings(StationSceneSetup setup,Transform parent,Unity.XR.CoreUtils.XROrigin rig)
        {
            var existing=parent.Find("StairLandingCoverage");
            var architecture=setup.architecturalFloors;
            bool PhysicalSupport(Vector3 point)
            {
                float radius=setup.FootprintRadius(point,rig);
                foreach(var offset in new[]{Vector3.zero,Vector3.right,Vector3.left,Vector3.forward,Vector3.back,new Vector3(.707f,0,.707f),new Vector3(-.707f,0,.707f),new Vector3(.707f,0,-.707f),new Vector3(-.707f,0,-.707f)})
                {
                    bool found=false;
                    foreach(var c in architecture)if(c.Raycast(new Ray(point+offset*radius+Vector3.up*.1f,Vector3.down),out var hit,.2f)&&hit.normal.y>.9f&&Mathf.Abs(hit.point.y-point.y)<.04f){found=true;break;}
                    if(!found)return false;
                }
                return true;
            }
            var vertices=new List<Vector3>();
            var retained=setup.walkableSurfaces;
            setup.walkableSurfaces=retained.Where(c=>c.name!="StairLandingCoverage").ToArray();
            try {
            foreach(var source in architecture)
            {
                var v=source.sharedMesh.vertices;var materials=source.GetComponent<Renderer>().sharedMaterials;
                for(int sub=0;sub<source.sharedMesh.subMeshCount;sub++)
                {
                string material=sub<materials.Length&&materials[sub]?materials[sub].name:"";
                if(!material.Contains("Floor")&&!material.Contains("Concrete")&&!material.Contains("Stair"))continue;
                var tri=source.sharedMesh.GetTriangles(sub);
                for(int i=0;i<tri.Length;i+=3)
                {
                    var a=source.transform.TransformPoint(v[tri[i]]);var b=source.transform.TransformPoint(v[tri[i+1]]);var c=source.transform.TransformPoint(v[tri[i+2]]);
                    var cross=Vector3.Cross(b-a,c-a);if(cross.normalized.y<.99f||cross.magnitude<.08f)continue;
                    var points=new[]{(a+b+c)/3,(a*2+b+c)/4,(a+b*2+c)/4,(a+b+c*2)/4}.Select(p=>p+Vector3.up*.005f).ToArray();
                    var center=points[0];bool stairwell=center.x>15.2f&&center.x<19.6f&&center.z>.65f&&center.z<6.5f;
                    if(center.y<.3f||center.y>11.21f||(!stairwell&&Mathf.Abs(center.y-6.383f)>.04f&&Mathf.Abs(center.y-11.005f)>.04f)||points.Any(p=>setup.HasSupport(p,setup.FootprintRadius(p,rig))))continue;
                    if(!points.Any(p=>PhysicalSupport(p)&&setup.ClearCapsule(p,rig)))continue;
                    vertices.Add(a+Vector3.up*.005f);vertices.Add(b+Vector3.up*.005f);vertices.Add(c+Vector3.up*.005f);
                }
                }
            }
            } finally {setup.walkableSurfaces=retained;}
            if(vertices.Count==0)return;
            string path=Folder+"/Navigation/StationLandingCoverage.asset";
            var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);
            bool create=!mesh;if(create)mesh=new Mesh{name="StationLandingCoverage"};
            mesh.SetVertices(vertices);mesh.SetTriangles(Enumerable.Range(0,vertices.Count).ToArray(),0);mesh.RecalculateBounds();
            if(create)AssetDatabase.CreateAsset(mesh,path);else EditorUtility.SetDirty(mesh);
            var root=existing?existing.gameObject:new GameObject("StairLandingCoverage");root.transform.SetParent(parent,false);
            var collider=root.GetComponent<MeshCollider>();if(!collider)collider=root.AddComponent<MeshCollider>();collider.sharedMesh=null;collider.sharedMesh=mesh;
            var area=root.GetComponent<StationTeleportArea>();if(!area)area=root.AddComponent<StationTeleportArea>();area.colliders.Clear();area.colliders.Add(collider);
            area.interactionLayers=setup.walkableSurfaces[0].GetComponent<StationTeleportArea>().interactionLayers;area.matchOrientation=MatchOrientation.WorldSpaceUp;
            if(!setup.walkableSurfaces.Contains(collider))setup.walkableSurfaces=setup.walkableSurfaces.Concat(new Collider[]{collider}).ToArray();
            Physics.SyncTransforms();
        }
        public static List<Vector3> SafeSurfaceSamples(StationSceneSetup setup,Unity.XR.CoreUtils.XROrigin rig=null)
        {
            var samples=new List<Vector3>();
            foreach(var collider in setup.walkableSurfaces.Cast<MeshCollider>())
            {
                var m=collider.sharedMesh;var v=m.vertices;var tri=m.triangles;
                for(int i=0;i<tri.Length;i+=3)
                {
                    var a=collider.transform.TransformPoint(v[tri[i]]);var b=collider.transform.TransformPoint(v[tri[i+1]]);var c=collider.transform.TransformPoint(v[tri[i+2]]);
                    foreach(var p in new[]{(a+b+c)/3,(a*2+b+c)/4,(a+b*2+c)/4,(a+b+c*2)/4})
                    {if(samples.Any(q=>(q-p).sqrMagnitude<.16f)||!setup.ValidDestination(p,rig,false))continue;samples.Add(p);}
                }
            }
            return samples;
        }
    }
}
#endif
