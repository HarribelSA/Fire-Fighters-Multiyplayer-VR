using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CityNightPresentation.Editor
{
    public static class CityNightBuilder
    {
        const string Root = "Assets/CityNightPresentation";
        const string ScenePath = "Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity";
        static Vector3[] vertices;
        static int[][] triangles;
        static Material[] roadMaterials;
        static readonly List<string> warnings = new List<string>();
        static readonly Dictionary<string,Material> materials = new Dictionary<string,Material>();
        static Mesh cylinder, disc;
        static Transform roads, signs, markings, traffic, lights, props;
        static CitySignalController[] controllers;

        static GameObject Node(string name,Transform parent=null)
        {
            Transform found=parent ? parent.Find(name) : SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(g=>g.name==name)?.transform;
            if(found)return found.gameObject;
            var g=new GameObject(name);if(parent)g.transform.SetParent(parent,false);return g;
        }
        static void Begin()
        {
            if(SceneManager.GetActiveScene().path!=ScenePath || EditorApplication.isPlaying)throw new Exception("Open demo_city_night in Edit Mode.");
            warnings.Clear();materials.Clear();
            foreach(string sub in new[]{"Materials","Meshes","Reports"})if(!AssetDatabase.IsValidFolder(Root+"/"+sub))AssetDatabase.CreateFolder(Root,sub);
            var t=GameObject.Find("city_part_demo_main1").transform;
            var mesh=t.GetComponent<MeshFilter>().sharedMesh;
            vertices=mesh.vertices.Select(t.TransformPoint).ToArray();
            triangles=Enumerable.Range(0,mesh.subMeshCount).Select(mesh.GetTriangles).ToArray();
            roadMaterials=t.GetComponent<Renderer>().sharedMaterials;
            roads=Node("ROAD_NETWORK").transform;
            markings=Node("RoadMarkings",roads).transform;signs=Node("RoadSigns",roads).transform;
            Node("Intersections",roads);Node("ParkingAreas",roads);
            traffic=Node("TRAFFIC_SYSTEM").transform;
            lights=Node("CITY_LIGHTING").transform;
            props=Node("CITY_PROPS").transform;
            cylinder=RadialMesh("StreetPole_8",8,false);disc=RadialMesh("SignalLens_12",12,true);
        }
        public static bool Height(float x,float z,out float height,bool asphaltOnly=true)
        {
            height=-1000;bool found=false;
            for(int sub=0;sub<triangles.Length;sub++)
            {
                if(asphaltOnly&&!roadMaterials[sub].name.StartsWith("asphalt"))continue;
                var ids=triangles[sub];
                for(int i=0;i<ids.Length;i+=3)
                {
                    var a=vertices[ids[i]];var b=vertices[ids[i+1]];var c=vertices[ids[i+2]];
                    if(x<Mathf.Min(a.x,Mathf.Min(b.x,c.x))-.001f||x>Mathf.Max(a.x,Mathf.Max(b.x,c.x))+.001f||z<Mathf.Min(a.z,Mathf.Min(b.z,c.z))-.001f||z>Mathf.Max(a.z,Mathf.Max(b.z,c.z))+.001f)continue;
                    float d=(b.z-c.z)*(a.x-c.x)+(c.x-b.x)*(a.z-c.z);if(Mathf.Abs(d)<.0001f)continue;
                    float u=((b.z-c.z)*(x-c.x)+(c.x-b.x)*(z-c.z))/d;
                    float v=((c.z-a.z)*(x-c.x)+(a.x-c.x)*(z-c.z))/d;
                    if(u>=-.001f&&v>=-.001f&&u+v<=1.001f){float y=u*a.y+v*b.y+(1-u-v)*c.y;if(y>height){height=y;found=true;}}
                }
            }
            return found;
        }
        static Vector3 Ground(Vector2 p,float lift=.04f,bool road=true)
        {
            if(!Height(p.x,p.y,out float y,road)){warnings.Add("No surface at "+p);y=5;}
            return new Vector3(p.x,y+lift,p.y);
        }
        static Material Mat(string name,Color color,bool emission=false)
        {
            if(materials.TryGetValue(name,out var cached))return cached;
            string p=Root+"/Materials/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(p);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));m.name=name;AssetDatabase.CreateAsset(m,p);}
            m.SetColor("_BaseColor",color);if(m.HasProperty("_Smoothness"))m.SetFloat("_Smoothness",.18f);m.enableInstancing=true;
            if(name=="RoadMarking_White"||name=="RoadMarking_Amber")
            {
                m.shader=Shader.Find("Universal Render Pipeline/Unlit");
                m.SetColor("_BaseColor",name.EndsWith("White")?new Color(.42f,.45f,.44f):new Color(.6f,.39f,.08f));
            }
            if(emission){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*1.35f);}
            EditorUtility.SetDirty(m);materials[name]=m;return m;
        }
        static GameObject Box(string name,Transform parent,Vector3 pos,Vector3 size,Material mat,Quaternion rotation=default)
        {
            var g=Node(name,parent);var mf=g.GetComponent<MeshFilter>();if(!mf)mf=g.AddComponent<MeshFilter>();
            mf.sharedMesh=Resources.GetBuiltinResource<Mesh>("Cube.fbx");var r=g.GetComponent<MeshRenderer>();if(!r)r=g.AddComponent<MeshRenderer>();
            r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            g.transform.SetPositionAndRotation(pos,rotation==default?Quaternion.identity:rotation);g.transform.localScale=size;
            return g;
        }
        static Mesh RadialMesh(string name,int sides,bool flat)
        {
            string path=Root+"/Meshes/"+name+".asset";var existing=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(existing){if(flat&&existing.normals.Length>0&&existing.normals[0].z<0){var ids=existing.triangles;for(int i=0;i<ids.Length;i+=3){int k=ids[i+1];ids[i+1]=ids[i+2];ids[i+2]=k;}existing.triangles=ids;existing.RecalculateNormals();EditorUtility.SetDirty(existing);}return existing;}
            var v=new List<Vector3>();var tr=new List<int>();
            if(flat){v.Add(Vector3.zero);for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;v.Add(new Vector3(Mathf.Sin(a)*.5f,Mathf.Cos(a)*.5f,0));}for(int i=0;i<sides;i++){tr.Add(0);tr.Add((i+1)%sides+1);tr.Add(i+1);}}
            else {for(int i=0;i<sides;i++){float a=i*Mathf.PI*2/sides;v.Add(new Vector3(Mathf.Cos(a)*.5f,-.5f,Mathf.Sin(a)*.5f));v.Add(new Vector3(Mathf.Cos(a)*.5f,.5f,Mathf.Sin(a)*.5f));}for(int i=0;i<sides;i++){int j=(i+1)%sides;tr.AddRange(new[]{i*2,j*2,i*2+1,j*2,j*2+1,i*2+1});}}
            var m=new Mesh{name=name};m.SetVertices(v);m.SetTriangles(tr,0);m.RecalculateNormals();m.RecalculateBounds();AssetDatabase.CreateAsset(m,path);return m;
        }
        static Renderer MeshObject(string name,Transform parent,Mesh mesh,Material material,Vector3 pos,Vector3 scale,Quaternion rot)
        {
            var g=Node(name,parent);var f=g.GetComponent<MeshFilter>();if(!f)f=g.AddComponent<MeshFilter>();f.sharedMesh=mesh;
            var r=g.GetComponent<MeshRenderer>();if(!r)r=g.AddComponent<MeshRenderer>();r.sharedMaterial=material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;r.lightProbeUsage=LightProbeUsage.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Off;
            g.transform.SetPositionAndRotation(pos,rot);g.transform.localScale=scale;return r;
        }
        static void TextSign(string name,Vector2 pos,Vector3 facing,string text,Color color,float width=4)
        {
            var p=Ground(pos,0,false);var rot=Quaternion.LookRotation(facing);
            var group=Node(name,signs).transform;
            var dark=Mat("StreetHardware",new Color(.045f,.055f,.065f));
            MeshObject("Post",group,cylinder,dark,p+Vector3.up*1.6f,new Vector3(.1f,3.2f,.1f),Quaternion.identity);
            string panelName=name.StartsWith("Final_")?"Sign_Stop":name.StartsWith("Incident_")?"Sign_Arrival":"Sign_Direction";
            var panelMat=Mat(panelName,color);panelMat.shader=Shader.Find("Universal Render Pipeline/Unlit");EditorUtility.SetDirty(panelMat);
            Box("Panel",group,p+Vector3.up*3,new Vector3(width,1.05f,.1f),panelMat,rot);
            var label=Node("Label",group);var tm=label.GetComponent<TextMesh>();if(!tm)tm=label.AddComponent<TextMesh>();
            tm.text=text;tm.anchor=TextAnchor.MiddleCenter;tm.alignment=TextAlignment.Center;tm.characterSize=.12f;tm.fontSize=48;tm.color=new Color(.91f,.94f,.92f);tm.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var r=tm.GetComponent<MeshRenderer>();r.sharedMaterial=tm.font.material;r.shadowCastingMode=ShadowCastingMode.Off;r.receiveShadows=false;
            label.transform.SetPositionAndRotation(p+Vector3.up*3+facing*.065f,Quaternion.LookRotation(-facing));
            label.transform.localScale=Vector3.one;
            float fit=Mathf.Min((width-.3f)/Mathf.Max(.01f,r.bounds.size.x),.76f/Mathf.Max(.01f,r.bounds.size.y));
            label.transform.localScale=Vector3.one*Mathf.Min(1,fit);
            tm.color=new Color(.7f,.76f,.74f);
        }
        static void Stripe(string name,Vector2 center,float width,float depth,Vector3 forward,Material mat)
        {
            // Conformed road ribbon; both slopes and z-fighting offset follow the existing mesh.
            var right=Vector3.Cross(Vector3.up,forward.normalized);var c=new Vector3(center.x,0,center.y);var f=forward.normalized;
            var v=new Vector3[4];var uv=new[]{new Vector2(0,0),new Vector2(1,0),new Vector2(0,1),new Vector2(1,1)};
            for(int i=0;i<4;i++){var p=c+right*((i%2==0?-.5f:.5f)*width)+f*((i<2?-.5f:.5f)*depth);v[i]=Ground(new Vector2(p.x,p.z),.025f);}
            string path=Root+"/Meshes/"+name+".asset";var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!m){m=new Mesh{name=name};AssetDatabase.CreateAsset(m,path);}m.vertices=v;m.uv=uv;m.triangles=new[]{0,2,1,1,2,3};m.RecalculateNormals();m.RecalculateBounds();EditorUtility.SetDirty(m);
            Transform target=name.StartsWith("Parking")||name.StartsWith("ArrivalBoundary")||name=="ArrivalCurbBoundary"?roads.Find("ParkingAreas"):name.Contains("_Stop_")?roads.Find("Intersections"):markings;
            MeshObject(name,target,m,mat,Vector3.zero,Vector3.one,Quaternion.identity);
        }
        static void Arrow(string name,Vector2 center,Vector3 forward,Material material)
        {
            Stripe(name+"_Stem",center-new Vector2(forward.x,forward.z),.38f,3,forward,material);
            var right=Vector3.Cross(Vector3.up,forward);var c=new Vector3(center.x,0,center.y);
            var ps=new[]{c-right*.9f,c+forward*1.8f,c+right*.9f};
            string path=Root+"/Meshes/"+name+"_Head.asset";var mesh=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!mesh){mesh=new Mesh{name=name+"_Head"};AssetDatabase.CreateAsset(mesh,path);}
            mesh.vertices=ps.Select(p=>Ground(new Vector2(p.x,p.z),.027f)).ToArray();mesh.triangles=new[]{0,1,2};mesh.RecalculateNormals();mesh.RecalculateBounds();EditorUtility.SetDirty(mesh);
            MeshObject(name+"_Head",markings,mesh,material,Vector3.zero,Vector3.one,Quaternion.identity);
        }
        static List<Vector2> Lane(Vector2 start,params Vector2[] rest)
        {
            var points=new List<Vector2>{start};foreach(var p in rest)Line(points,p,10);return points;
        }
        static void Line(List<Vector2> list,Vector2 end,float spacing=10)
        {
            var start=list[list.Count-1];int n=Mathf.Max(1,Mathf.CeilToInt(Vector2.Distance(start,end)/spacing));for(int i=1;i<=n;i++)list.Add(Vector2.Lerp(start,end,(float)i/n));
        }
        static void Curve(List<Vector2> list,Vector2 control,Vector2 end,int n=8)
        {
            var start=list[list.Count-1];for(int i=1;i<=n;i++){float t=(float)i/n;list.Add((1-t)*(1-t)*start+2*(1-t)*t*control+t*t*end);}
        }
        static CitySignalController Signal(string name,Vector2 center,int offset)
        {
            var g=Node(name,Node("TrafficLights",traffic).transform);g.transform.position=Ground(center,0);
            var s=g.GetComponent<CitySignalController>();if(!s)s=g.AddComponent<CitySignalController>();
            s.greenSeconds=13;s.yellowSeconds=3;s.allRedSeconds=2;s.offsetSeconds=offset;
            s.redOn=Mat("Signal_Red",new Color(1,.035f,.012f),true);s.yellowOn=Mat("Signal_Amber",new Color(1,.55f,.025f),true);s.greenOn=Mat("Signal_Green",new Color(.035f,.75f,.15f),true);s.lensOff=Mat("Signal_Off",new Color(.025f,.03f,.025f));
            var hardware=Mat("StreetHardware",new Color(.045f,.055f,.065f));var white=Mat("RoadMarking_White",new Color(.8f,.83f,.8f));
            var heads=new List<CitySignalController.Head>();var dirs=new[]{Vector3.right,Vector3.back,Vector3.left,Vector3.forward};
            s.phaseOrder=name.Contains("01_")||name.Contains("03_")?new[]{0,2,3}:new[]{0,1,3};
            for(int a=0;a<4;a++)
            {
                if(!s.phaseOrder.Contains(a))
                {
                    var unused=g.transform.Find("Approach_"+a);if(unused)unused.gameObject.SetActive(false);
                    var unusedMark=markings.Find(name+"_Stop_"+a);if(unusedMark)unusedMark.gameObject.SetActive(false);
                    continue;
                }
                var f=dirs[a];var right=Vector3.Cross(Vector3.up,f);var location=new Vector3(center.x,0,center.y)-f*18+right*11.5f;
                var baseP=Ground(new Vector2(location.x,location.z),0,false);var head=Node("Approach_"+a,g.transform).transform;
                MeshObject("Pole",head,cylinder,hardware,baseP+Vector3.up*2.9f,new Vector3(.16f,5.8f,.16f),Quaternion.identity);
                var face=Quaternion.LookRotation(-f);
                Box("SignalHousing",head,baseP+Vector3.up*5.4f,new Vector3(.62f,1.65f,.35f),hardware,face);
                var rr=new Renderer[3];for(int k=0;k<3;k++)rr[k]=MeshObject(new[]{"Red","Yellow","Green"}[k],head,disc,s.lensOff,baseP+Vector3.up*(5.92f-k*.52f)-f*.19f,new Vector3(.4f,.4f,1),face);
                Vector3 stop=new Vector3(center.x,0,center.y)-f*18+right*4.2f;
                var line=Node("StopLine",head).transform;line.position=Ground(new Vector2(stop.x,stop.z),.03f);line.rotation=Quaternion.LookRotation(f);
                Stripe(name+"_Stop_"+a,new Vector2(stop.x,stop.z),7.8f,.35f,f,white);
                heads.Add(new CitySignalController.Head{approach=a,red=rr[0],yellow=rr[1],green=rr[2],stopLine=line});
            }
            s.heads=heads.ToArray();s.SetClock(0);s.RefreshVisuals();EditorUtility.SetDirty(s);return s;
        }
        static CityTrafficPath Path(string name,List<Vector2> points,params (CitySignalController signal,int approach,Vector2 stop)[] gates)
        {
            var g=Node(name,Node("VehiclePaths",traffic).transform);var path=g.GetComponent<CityTrafficPath>();if(!path)path=g.AddComponent<CityTrafficPath>();
            path.points=points.Select(p=>Ground(p,.04f)).ToArray();path.speedLimit=7;
            path.gates=gates.Select(gate=>new CityTrafficPath.Gate{signal=gate.signal,approach=gate.approach,stopPointIndex=Enumerable.Range(0,points.Count).OrderBy(i=>(points[i]-gate.stop).sqrMagnitude).First(),clearanceDistance=45}).ToArray();
            path.Initialize();EditorUtility.SetDirty(path);return path;
        }
        static void VehicleLOD(GameObject vehicle)
        {
            var rs=vehicle.GetComponentsInChildren<Renderer>();foreach(var r in rs){r.shadowCastingMode=ShadowCastingMode.Off;r.reflectionProbeUsage=ReflectionProbeUsage.Simple;}
            var lod=vehicle.GetComponent<LODGroup>();if(!lod)lod=vehicle.AddComponent<LODGroup>();lod.SetLODs(new[]{new LOD(.008f,rs)});lod.fadeMode=LODFadeMode.None;lod.RecalculateBounds();
        }
        static GameObject Car(string name,string prefabName,Transform parent,Vector3 p,Vector3 forward)
        {
            var existing=parent.Find(name);GameObject g;
            if(existing)g=existing.gameObject;
            else {string path="Assets/Awbmecreations/Mobile Optimize-Free Low Poly Cars/Prefabs/"+prefabName+".prefab";var asset=AssetDatabase.LoadAssetAtPath<GameObject>(path);if(!asset)throw new Exception("Missing vehicle "+path);g=(GameObject)PrefabUtility.InstantiatePrefab(asset,parent);g.name=name;}
            g.transform.SetPositionAndRotation(p,Quaternion.LookRotation(forward));g.transform.localScale=Vector3.one;VehicleLOD(g);
            foreach(var r in g.GetComponentsInChildren<Renderer>())
            {
                var ms=r.sharedMaterials;
                for(int i=0;i<ms.Length;i++)
                {
                    var src=ms[i];if(!src||AssetDatabase.GetAssetPath(src).StartsWith(Root))continue;
                    string mp=Root+"/Materials/Civilian_"+src.name.Replace(" ","_")+".mat";
                    var copy=AssetDatabase.LoadAssetAtPath<Material>(mp);if(!copy){copy=new Material(src);AssetDatabase.CreateAsset(copy,mp);}copy.enableInstancing=true;EditorUtility.SetDirty(copy);ms[i]=copy;
                }
                r.sharedMaterials=ms;PrefabUtility.RecordPrefabInstancePropertyModifications(r);
            }
            PrefabUtility.RecordPrefabInstancePropertyModifications(g.transform);return g;
        }
        static Mesh PoolMesh(string name,Vector2 center,float length,float width,Vector2 direction)
        {
            string path=Root+"/Meshes/"+name+".asset";var m=AssetDatabase.LoadAssetAtPath<Mesh>(path);if(!m){m=new Mesh{name=name};AssetDatabase.CreateAsset(m,path);}
            const int n=6;var v=new List<Vector3>();var valid=new List<bool>();var uv=new List<Vector2>();var tr=new List<int>();var right=new Vector2(direction.y,-direction.x);
            for(int z=0;z<=n;z++)for(int x=0;x<=n;x++)
            {
                float u=(float)x/n,w=(float)z/n;Vector2 p=center+right*((u-.5f)*width)+direction*((w-.5f)*length);
                // Missing road samples are placed below the road and won't brighten sidewalks/buildings.
                bool road=Height(p.x,p.y,out float y);valid.Add(road);v.Add(new Vector3(p.x,road?y+.035f:0,p.y));uv.Add(new Vector2(u,w));
            }
            for(int z=0;z<n;z++)for(int x=0;x<n;x++){int i=z*(n+1)+x;if(valid[i]&&valid[i+n+1]&&valid[i+1]&&valid[i+n+2])tr.AddRange(new[]{i,i+n+1,i+1,i+1,i+n+1,i+n+2});}
            m.SetVertices(v);m.SetUVs(0,uv);m.SetTriangles(tr,0);m.RecalculateNormals();m.RecalculateBounds();EditorUtility.SetDirty(m);return m;
        }
        public static object Build()
        {
            Begin();
            // Group existing outer prefab instances without unpacking or changing world transforms.
            var existingProps=SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.parent&&t.parent.name=="PROPS").ToArray();
            int organized=0;
            foreach(var t in existingProps)
            {
                if(PrefabUtility.IsPartOfPrefabInstance(t.gameObject)&&PrefabUtility.GetOutermostPrefabInstanceRoot(t.gameObject)!=t.gameObject)continue;
                string district=t.position.x>100&&t.position.x<180&&t.position.z< -190?"IncidentFrontage":t.position.z> -70?"Commercial":"Residential";
                var group=Node(district,props).transform;string kind=t.name.StartsWith("bench")?"StreetFurniture":t.name.StartsWith("hedge")?"Planters":"Barriers";
                var parent=Node(kind,group).transform;t.SetParent(parent,true);PrefabUtility.RecordPrefabInstancePropertyModifications(t);organized++;
            }
            // Gently regularize redundant seating clusters, keeping their elevation/footprint on the sidewalk.
            // Physical additions below use existing asphalt shoulders, not the emergency travel lane.
            controllers=new[]{Signal("Junction_01_MainCommercial",new Vector2(241,18),0),Signal("Junction_02_CommercialResidential",new Vector2(246,-82),18),Signal("Junction_03_Residential",new Vector2(93,-82),36)};
            var routeRoot=Node("FIREFIGHTER_DRIVING_ROUTE");var route=routeRoot.GetComponent<CityDrivingRoute>();if(!route)route=routeRoot.AddComponent<CityDrivingRoute>();
            var fire=new List<Vector2>{new Vector2(20,22)};Line(fire,new Vector2(220,14));Curve(fire,new Vector2(241,14),new Vector2(241,-7));Line(fire,new Vector2(241,-60));Curve(fire,new Vector2(241,-78),new Vector2(221,-78));Line(fire,new Vector2(111,-78));Curve(fire,new Vector2(88,-78),new Vector2(88,-103));Line(fire,new Vector2(84,-160));Curve(fire,new Vector2(83,-187),new Vector2(110,-187));Line(fire,new Vector2(124,-189));Line(fire,new Vector2(140,-192));
            var waypointGroup=Node("Route_Waypoints",routeRoot.transform).transform;var wps=new List<Transform>();
            for(int i=0;i<fire.Count;i++){var wp=Node("WP_"+(i+1).ToString("000"),waypointGroup).transform;wp.position=Ground(fire[i],.08f);if(i<fire.Count-1)wp.rotation=Quaternion.LookRotation(Ground(fire[i+1]) - Ground(fire[i]));wps.Add(wp);}
            route.waypoints=wps.ToArray();route.spawnPoint=Node("FireTruck_DrivingSpawnPoint",routeRoot.transform).transform;route.spawnPoint.SetPositionAndRotation(Ground(fire[0],0),Quaternion.LookRotation(Vector3.right));
            route.arrivalPoint=Node("Incident_ArrivalPoint",routeRoot.transform).transform;route.arrivalPoint.SetPositionAndRotation(Ground(fire.Last(),0),Quaternion.LookRotation(Vector3.right));
            route.stopZone=Node("FireTruck_StopZone",routeRoot.transform).transform;route.stopZone.SetPositionAndRotation(route.arrivalPoint.position,Quaternion.LookRotation(Vector3.right));
            var approach=Node("Incident_ApproachRoad",routeRoot.transform).transform;approach.SetPositionAndRotation(Ground(new Vector2(110,-187),0),Quaternion.LookRotation(Vector3.right));
            var intersectionMarkers=Node("Traffic_Intersections",routeRoot.transform).transform;for(int i=0;i<controllers.Length;i++)Node("Intersection_"+(i+1),intersectionMarkers).transform.position=controllers[i].transform.position;
            route.intersections=controllers;EditorUtility.SetDirty(route);
            // Two disjoint lane loops around the commercial/residential block.
            var a=new List<Vector2>{new Vector2(84,-155)};Curve(a,new Vector2(83,-187),new Vector2(113,-187));Line(a,new Vector2(222,-191));Curve(a,new Vector2(252,-192),new Vector2(252,-161));Line(a,new Vector2(251,-105));Curve(a,new Vector2(251,-78),new Vector2(223,-78));Line(a,new Vector2(115,-78));Curve(a,new Vector2(88,-78),new Vector2(88,-105));Line(a,new Vector2(84,-155));a.RemoveAt(a.Count-1);
            var b=new List<Vector2>{new Vector2(96,-105)};Curve(b,new Vector2(97,-86),new Vector2(116,-86));Line(b,new Vector2(222,-86));Curve(b,new Vector2(242,-86),new Vector2(242,-107));Line(b,new Vector2(244,-163));Curve(b,new Vector2(244,-183),new Vector2(224,-183));Line(b,new Vector2(112,-179));Curve(b,new Vector2(92,-179),new Vector2(92,-157));Line(b,new Vector2(96,-105));b.RemoveAt(b.Count-1);
            var pathA=Path("LaneLoop_LeftTurns",a,(controllers[1],3,new Vector2(251,-107)),(controllers[2],2,new Vector2(115,-78)));
            var pathB=Path("LaneLoop_RightTurns",b,(controllers[1],0,new Vector2(222,-86)),(controllers[2],3,new Vector2(96,-105)));
            var managerObject=Node("TrafficManager",traffic);var manager=managerObject.GetComponent<CityTrafficManager>();if(!manager)manager=managerObject.AddComponent<CityTrafficManager>();
            var moving=Node("MovingVehicles",traffic).transform;var agents=new List<CityTrafficManager.Vehicle>();
            string[] prefabs={"Hatchback Car_15","N Van_10","Pick Up_11","Classic Car_9","N_Muscle Car_10","Sport Car_39"};
            for(int i=0;i<6;i++)
            {
                var path=i<3?pathA:pathB;float distance=path.Length*((i%3+.35f)/3);path.Sample(distance,out var p,out var f);
                var vehicle=Car("Civilian_"+(i+1).ToString("00")+"_"+prefabs[i],prefabs[i],moving,p,f);
                agents.Add(new CityTrafficManager.Vehicle{body=vehicle.transform,path=path,distance=distance,length=i==2?6.1f:i==1?5.1f:4.6f,cruiseSpeed=6.5f+(i%2)*.5f});
                // Only kinematic civilian colliders: no per-car rigidbodies, wheels or networking.
            }
            manager.vehicles=agents.ToArray();manager.signals=controllers;manager.activeVehicles=6;manager.simulationHz=20;manager.obstacleMask=1;manager.Initialize();EditorUtility.SetDirty(manager);
            var parked=Node("ParkedVehicles",traffic).transform;
            Vector2[] spaces={new Vector2(76,13),new Vector2(92,12.5f),new Vector2(110,12),new Vector2(128,11.5f),new Vector2(175,-176.5f),new Vector2(192,-177.5f)};
            var white=Mat("RoadMarking_White",new Color(.8f,.83f,.8f));var yellow=Mat("RoadMarking_Amber",new Color(.85f,.58f,.12f));
            Arrow("MainRoad_Arrow",new Vector2(205,14.5f),Vector3.right,white);
            Arrow("Commercial_Arrow",new Vector2(241,-45),Vector3.back,white);
            Arrow("Residential_Arrow",new Vector2(130,-78),Vector3.left,white);
            Arrow("Arrival_Arrow",new Vector2(85,-145),Vector3.back,white);
            for(int i=0;i<spaces.Length;i++)
            {
                var f=i<4?Vector3.right:Vector3.left;Car("Parked_"+(i+1).ToString("00"),prefabs[i],parked,Ground(spaces[i],.035f),f);
                Stripe("ParkingEdge_"+i,spaces[i]+new Vector2(0,-1.5f),7,.12f,Vector3.forward,white);
                foreach(int end in new[]{-1,1})Stripe("ParkingEnd_"+i+"_"+end,spaces[i]+new Vector2(end*3.7f,0),3,.12f,Vector3.right,white);
            }
            // Reserve a clearly marked roadside stopping bay outside the moving eastbound lane.
            foreach(int end in new[]{-1,1})Stripe("ArrivalBoundary_"+end,new Vector2(140+end*9,-192),3.5f,.18f,Vector3.right,yellow);
            Stripe("ArrivalCurbBoundary",new Vector2(140,-193.7f),18,.16f,Vector3.forward,yellow);
            TextSign("Commercial_District",new Vector2(65,4),Vector3.left,"CITY CENTRE\nRIVER AVENUE",new Color(.035f,.12f,.1f));
            TextSign("Residential_District",new Vector2(103,-108),Vector3.forward,"RESIDENTIAL\nGARDEN STREET",new Color(.08f,.13f,.095f));
            TextSign("Incident_Frontage",new Vector2(152,-199),Vector3.forward,"GARDEN COURT\nACCESS — KEEP CLEAR",new Color(.1f,.12f,.15f),5.3f);
            TextSign("RouteTurn_01",new Vector2(225,-1),Vector3.left,"GARDEN STREET\nTURN RIGHT",new Color(.035f,.12f,.1f));
            TextSign("RouteTurn_02",new Vector2(230,-68),Vector3.forward,"GARDEN COURT\nTURN RIGHT",new Color(.035f,.12f,.1f));
            TextSign("RouteTurn_03",new Vector2(108,-95),Vector3.right,"GARDEN COURT\nTURN LEFT",new Color(.035f,.12f,.1f));
            TextSign("Final_Junction_Stop",new Vector2(76,-163),Vector3.forward,"STOP\nGARDEN COURT",new Color(.35f,.025f,.02f),2.7f);
            // Low-poly bollards mark the incident frontage without entering the stopping bay.
            var curbProps=Node("Incident_Access_Bollards",props).transform;
            foreach(int x in new[]{127,131,149,153})MeshObject("Bollard_"+x,curbProps,cylinder,Mat("Bollard_Stone",new Color(.48f,.5f,.51f)),Ground(new Vector2(x,-198),.55f,false),new Vector3(.2f,1.1f,.2f),Quaternion.identity);
            // Localized conformed additive pools brighten the actual road without a full-scene post effect.
            var poolMaterial=AssetDatabase.LoadAssetAtPath<Material>(Root+"/Materials/RoadPool_Warm.mat");if(!poolMaterial){poolMaterial=new Material(Shader.Find("CityNightPresentation/Road Light Pool"));AssetDatabase.CreateAsset(poolMaterial,Root+"/Materials/RoadPool_Warm.mat");}
            poolMaterial.SetColor("_Color",new Color(.105f,.075f,.035f));EditorUtility.SetDirty(poolMaterial);
            var poolGroup=Node("Road_Illumination_Pools",lights).transform;int pool=0;
            for(int i=0;i<fire.Count;i+=3)
            {
                var p=fire[i];if(i>0&&Vector2.Distance(p,fire[Mathf.Max(0,i-3)])<12)continue;
                Vector2 dir=i+1<fire.Count?(fire[i+1]-p).normalized:Vector2.right;string name="RoadPool_"+(pool++).ToString("00");
                MeshObject(name,poolGroup,PoolMesh(name,p,22,12,dir),poolMaterial,Vector3.zero,Vector3.one,Quaternion.identity);
            }
            var keyLightGroup=Node("Junction_Visibility_Lights",lights).transform;
            Vector2[] lightPoints={new Vector2(238,17),new Vector2(246,-82),new Vector2(93,-82),new Vector2(88,-181),new Vector2(140,-190)};
            for(int i=0;i<lightPoints.Length;i++)
            {
                var g=Node("Visibility_Light_"+(i+1),keyLightGroup);g.transform.SetPositionAndRotation(Ground(lightPoints[i],8),Quaternion.Euler(90,0,0));
                var l=g.GetComponent<Light>();if(!l)l=g.AddComponent<Light>();l.type=LightType.Spot;l.color=new Color(1,.79f,.52f);l.intensity=3.2f;l.range=21;l.spotAngle=105;l.innerSpotAngle=55;l.shadows=LightShadows.None;l.lightmapBakeType=LightmapBakeType.Realtime;l.renderMode=LightRenderMode.Auto;
            }
            // Scene-local culling, no camera/XR or shared renderer asset edits.
            var opt=Node("CITY_OPTIMIZATION");var visibility=opt.GetComponent<CityVisibilityController>();if(!visibility)visibility=opt.AddComponent<CityVisibilityController>();
            var items=new List<CityVisibilityController.Item>();int shadowsDisabled=0;
            foreach(var r in SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Renderer>(true)))
            {
                // Static city lighting is baked; suppress redundant dynamic shadow passes.
                if(r.lightmapIndex>=0&&r.lightmapIndex<65534){r.shadowCastingMode=ShadowCastingMode.Off;PrefabUtility.RecordPrefabInstancePropertyModifications(r);shadowsDisabled++;}
                string n=r.name;float distance=n.StartsWith("bench")||n.StartsWith("hedge")||n.StartsWith("Bollard")?140:n.StartsWith("tree_")?230:n.StartsWith("building_lamp")?190:0;
                if(distance>0)items.Add(new CityVisibilityController.Item{renderer=r,distance=distance});
            }
            visibility.items=items.ToArray();visibility.updateInterval=.35f;visibility.hysteresis=20;EditorUtility.SetDirty(visibility);
            var sky=RenderSettings.skybox;if(sky&&AssetDatabase.GetAssetPath(sky).StartsWith(Root))
            {
                sky.shader=Shader.Find("CityNightPresentation/Night Sky");
                sky.SetColor("_Zenith",new Color(.008f,.018f,.045f));sky.SetColor("_Horizon",new Color(.035f,.06f,.1f));sky.SetColor("_Ground",new Color(.012f,.02f,.035f));EditorUtility.SetDirty(sky);
            }
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            foreach(string guid in AssetDatabase.FindAssets("t:Material t:Mesh",new[]{Root}))AssetDatabase.SaveAssetIfDirty(AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GUIDToAssetPath(guid)));
            SceneView.RepaintAll();
            return new{organizedExistingProps=organized,routeWaypoints=route.waypoints.Length,routeLength=route.Length,movingVehicles=agents.Count,parkedVehicles=spaces.Length,signalControllers=controllers.Length,signalHeads=controllers.Sum(s=>s.heads.Length),roadLightPools=pool,additionalRealtimeLights=lightPoints.Length,distanceCulledRenderers=items.Count,staticShadowCastersDisabled=shadowsDisabled,warnings=warnings.Distinct().ToArray()};
        }
        public static object Validate()
        {
            Begin();var scene=SceneManager.GetActiveScene();var all=scene.GetRootGameObjects();
            var route=all.SelectMany(g=>g.GetComponentsInChildren<CityDrivingRoute>()).Single();var manager=all.SelectMany(g=>g.GetComponentsInChildren<CityTrafficManager>()).Single();
            var paths=all.SelectMany(g=>g.GetComponentsInChildren<CityTrafficPath>()).ToArray();
            var offroad=new List<string>();
            foreach(var wp in route.waypoints)if(!Height(wp.position.x,wp.position.z,out _))offroad.Add(wp.name);
            foreach(var path in paths)for(int i=0;i<path.points.Length;i++)if(!Height(path.points[i].x,path.points[i].z,out _))offroad.Add(path.name+"/"+i);
            var ts=all.SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            int missingScripts=ts.Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject));
            var missingMaterials=ts.SelectMany(t=>t.GetComponents<Renderer>()).Where(r=>r.sharedMaterials.Any(m=>!m)).Select(r=>r.name).ToArray();
            // Check phase exclusivity over the full cycle without advancing the scene simulation.
            bool exclusive=true;foreach(var signal in manager.signals){for(int t=0;t<144;t++){signal.SetClock(t*.5f);if(Enumerable.Range(0,4).Count(signal.Allows)>1)exclusive=false;}signal.SetClock(0);}
            var models=ts.SelectMany(t=>t.GetComponents<MeshFilter>()).Where(f=>f.sharedMesh).ToArray();
            return new{offroadPoints=offroad.ToArray(),missingScripts,missingMaterials,exclusiveSignalPhases=exclusive,waypointCount=route.waypoints.Length,routeMeters=route.Length,trafficPathLengths=paths.Select(p=>{p.Initialize();return new{p.name,p.Length};}).ToArray(),totalMeshInstanceTriangles=models.Sum(f=>Enumerable.Range(0,f.sharedMesh.subMeshCount).Sum(i=>(long)f.sharedMesh.GetIndexCount(i)/3)),lightmaps=LightmapSettings.lightmaps.Length,baking=Lightmapping.isRunning,compiling=EditorApplication.isCompiling,updating=EditorApplication.isUpdating};
        }
    }
}
