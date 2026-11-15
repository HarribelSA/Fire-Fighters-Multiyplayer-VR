#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.XRHandLocomotion;

namespace FirefighterVR.Station.Editor
{
    public static class StationMissionInstaller
    {
        const string Folder="Assets/FirefighterAvatar/Station";
        public static string Install()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play Mode.");
            var lobby=Open("Assets/S1Test.unity");
            var flow=Components<StationSessionFlow>(lobby).Single();var rig=flow.localRig;
            var host=rig.origin.GetComponentsInChildren<Transform>(true).Single(t=>t.name=="StationHandTeleport");
            host.SetParent(rig.origin.CameraFloorOffsetObject.transform,true);
            var ray=host.GetComponentInChildren<XRRayInteractor>(true);var input=host.GetComponent<StationHandTeleportInput>();if(!input)input=host.gameObject.AddComponent<StationHandTeleportInput>();
            input.rig=rig;input.ray=ray;input.handInteraction=rig.modality.rightHand.GetComponentInChildren<NearFarInteractor>(true);
            input.activationHold=.08f;
            input.sampleGesture=host.GetComponent<TeleportHandGesture>();input.sampleRotation=host.GetComponent<JoystickPinchHandGesture>();input.rayPose=ray.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            // Repair the sample's missing tracking-space origin too; its activation is
            // mutually exclusive with the station-only palm-up adapter at runtime.
            var rotation=new SerializedObject(input.sampleRotation);rotation.FindProperty("m_XROrigin").objectReferenceValue=rig.origin.CameraFloorOffsetObject.transform;rotation.ApplyModifiedPropertiesWithoutUndo();
            if(!ray.GetComponent<XRInteractorLineVisual>().reticle)throw new InvalidOperationException("Existing hand teleport reticle is missing.");
            var incident=AssetDatabase.LoadAssetAtPath<StationIncidentDefinition>(Folder+"/StationIncident.asset");
            if(!incident){incident=ScriptableObject.CreateInstance<StationIncidentDefinition>();AssetDatabase.CreateAsset(incident,Folder+"/StationIncident.asset");}
            flow.incident=incident;flow.explorationSeconds=60;
            var station=Open(StationTransitionInstaller.StationPath);var setup=Components<StationSceneSetup>(station).Single();
            var view=Components<StationMissionView>(station).SingleOrDefault();
            if(!view){var root=new GameObject("STATION_MISSION_PRESENTATION");SceneManager.MoveGameObjectToScene(root,station);view=root.AddComponent<StationMissionView>();}
            var all=station.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).ToArray();
            var monitor=all.Single(t=>t.name=="Monitor (6)");var chair=all.Single(t=>t.name=="OfficeChair (4)");var tablet=all.Single(t=>t.name=="Tablet (1)");var truck=station.GetRootGameObjects().Single(g=>g.name=="firetruck");
            var chairSeat=Node("Dispatch_ChairSeat",view.transform);chairSeat.position=new Vector3(chair.position.x,.005f,chair.position.z);
            chairSeat.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(monitor.position-chair.position,Vector3.up));view.dispatchSeat=chairSeat;
            view.chairApproach=SafeApproach("Dispatch_ChairApproach",view.transform,setup,rig,chairSeat.position,chairSeat.position-chairSeat.forward);
            view.driverSeat=Node("Station_DriverSeat",view.transform);view.driverSeat.SetPositionAndRotation(truck.transform.TransformPoint(new Vector3(-.75f,1.15f,2.2f)),truck.transform.rotation);
            view.passengerSeat=Node("Station_PassengerSeat",view.transform);view.passengerSeat.SetPositionAndRotation(truck.transform.TransformPoint(new Vector3(.75f,1.15f,2.2f)),truck.transform.rotation);
            view.driverApproach=SafeApproach("Driver_EntryApproach",view.transform,setup,rig,new Vector3(view.driverSeat.position.x,.005f,view.driverSeat.position.z),truck.transform.TransformPoint(new Vector3(-2.4f,0,2.2f)));
            view.passengerApproach=SafeApproach("Passenger_EntryApproach",view.transform,setup,rig,new Vector3(view.passengerSeat.position.x,.005f,view.passengerSeat.position.z),truck.transform.TransformPoint(new Vector3(2.4f,0,2.2f)));
            var green=Material("Garage_Green",new Color(.08f,.55f,.18f),new Color(.04f,.8f,.1f));var red=Material("Garage_Red",new Color(.7f,.05f,.025f),new Color(1,.04f,.015f));var dark=Material("Dispatch_Hardware",new Color(.025f,.04f,.055f),Color.black);
            view.seatTargets=new[]{SeatTarget(view,"Dispatch_ChairTeleport",view.chairApproach,StationSeatKind.DispatchChair,green),SeatTarget(view,"Driver_EntryTeleport",view.driverApproach,StationSeatKind.DriverTruck,green),SeatTarget(view,"Passenger_EntryTeleport",view.passengerApproach,StationSeatKind.AssistantTruck,green)};
            var doorScripts=Components<DoorAnimation>(station).Where(d=>d.name.StartsWith("RollupDoor")).ToArray();
            if(!view.garage){var primary=doorScripts.OrderBy(d=>Mathf.Abs(d.animator.transform.position.z+5)).First();view.garage=primary.animator;}
            foreach(var script in doorScripts){foreach(var c in script.GetComponents<Collider>())if(c.isTrigger)c.enabled=false;UnityEngine.Object.DestroyImmediate(script);}
            var bounds=view.garage.GetComponentsInChildren<Renderer>(true).Where(r=>r is SkinnedMeshRenderer||r.GetComponent<MeshFilter>()).Select(r=>r.bounds).ToArray();
            if(bounds.Length==0)throw new InvalidOperationException("Garage visual bounds missing.");var sweep=bounds[0];foreach(var b in bounds.Skip(1))sweep.Encapsulate(b);view.garageSweep=sweep;
            var normal=sweep.size.x<sweep.size.z?Vector3.right:Vector3.forward;
            var curtains=view.garage.transform.parent.GetComponentsInChildren<Collider>(true).Where(c=>!c.isTrigger&&c.Raycast(new Ray(sweep.center-normal*2,normal),out _,4)).ToArray();
            if(curtains.Length==0){var blocker=Node("Garage_CurtainCollision",view.transform);blocker.position=sweep.center;var box=blocker.GetComponent<BoxCollider>();if(!box)box=blocker.gameObject.AddComponent<BoxCollider>();box.size=new Vector3(Mathf.Max(.12f,sweep.size.x),sweep.size.y,Mathf.Max(.12f,sweep.size.z));curtains=new Collider[]{box};}
            view.curtainColliders=curtains;
            IntegrateExistingHardware(view,station);
            var screen=Canvas(Node("Station_DispatchUI",monitor),new Vector2(820,600),.0006f);
            FitMonitorCanvas(screen,monitor,chairSeat.position+Vector3.up*1.35f);
            Background(screen,new Vector2(820,600));view.computerReport=Text("IncidentReport",screen,new Vector2(780,440),new Vector2(0,65),24);
            view.acceptButton=Button("ACCEPT MISSION",screen,new Vector2(440,62),new Vector2(-100,-205));view.exitChairButton=Button("EXIT CHAIR",screen,new Vector2(220,52),new Vector2(285,-205));
            view.objective=Text("CooperativeObjective",screen,new Vector2(780,50),new Vector2(0,-270),20);
            view.tablet=tablet;view.tabletHome=Node("Tablet_DeskHome",view.transform);view.tabletHome.SetPositionAndRotation(tablet.position,tablet.rotation);foreach(var t in tablet.GetComponentsInChildren<Transform>(true))GameObjectUtility.SetStaticEditorFlags(t.gameObject,0);
            foreach(var r in tablet.GetComponentsInChildren<Renderer>(true))r.lightmapIndex=-1;
            var body=tablet.GetComponent<Rigidbody>();if(!body)body=tablet.gameObject.AddComponent<Rigidbody>();body.mass=.5f;body.isKinematic=true;body.interpolation=RigidbodyInterpolation.Interpolate;
            view.tabletGrab=tablet.GetComponent<XRGrabInteractable>();if(!view.tabletGrab)view.tabletGrab=tablet.gameObject.AddComponent<XRGrabInteractable>();
            view.tabletGrab.movementType=XRBaseInteractable.MovementType.Kinematic;view.tabletGrab.throwOnDetach=false;
            var tabletColliders=tablet.GetComponentsInChildren<Collider>(true);foreach(var c in tabletColliders.OfType<MeshCollider>())c.convex=true;
            if(tabletColliders.Length==0){var c=tablet.gameObject.AddComponent<BoxCollider>();c.size=new Vector3(.3f,.025f,.2f);tabletColliders=new Collider[]{c};}
            view.tabletGrab.colliders.Clear();foreach(var c in tabletColliders.Where(c=>c.gameObject.activeSelf))view.tabletGrab.colliders.Add(c);
            var tabCanvas=Canvas(Node("Tablet_DispatchUI",tablet),new Vector2(680,460),.00037f);tabCanvas.localPosition=new Vector3(0,.018f,0);tabCanvas.localRotation=Quaternion.Euler(90,0,0);Background(tabCanvas,new Vector2(680,460));view.tabletReport=Text("SharedIncident",tabCanvas,new Vector2(650,440),Vector2.zero,23);
            EditorUtility.SetDirty(input);EditorUtility.SetDirty(flow);EditorUtility.SetDirty(view);EditorSceneManager.MarkSceneDirty(lobby);EditorSceneManager.MarkSceneDirty(station);
            if(!EditorSceneManager.SaveScene(lobby)||!EditorSceneManager.SaveScene(station))throw new Exception("Station mission save failed.");
            var city=Open("Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity");
            var presentation=Components<CityNightPresentation.Mission.FireTruckPresentation>(city).Single();var canvas=presentation.tabletStatus.GetComponentInParent<Canvas>().transform as RectTransform;
            presentation.tabletStatus.text="MISSION NAVIGATION";presentation.tabletStatus.fontSize=19;presentation.tabletStatus.rectTransform.sizeDelta=new Vector2(620,90);presentation.tabletStatus.rectTransform.anchoredPosition=new Vector2(0,145);
            var map=presentation.GetComponentInChildren<CityNightPresentation.Mission.MissionTabletMap>(true);map.rectTransform.sizeDelta=new Vector2(360,210);map.rectTransform.anchoredPosition=new Vector2(-110,-15);
            presentation.tabletDetails=Text("NavigationDetails",canvas,new Vector2(220,245),new Vector2(205,-20),16);
            EditorUtility.SetDirty(presentation);EditorSceneManager.MarkSceneDirty(city);if(!EditorSceneManager.SaveScene(city))throw new Exception("City UI save failed.");AssetDatabase.SaveAssets();
            return "Reused station computer/chair/tablet/garage/alarm and city tablet; mission and hand adapter references saved.";
        }
        public static string IntegrateExistingHardware()
        {
            var scene=SceneManager.GetSceneByPath(StationTransitionInstaller.StationPath);
            if(!scene.isLoaded)throw new InvalidOperationException("Open the saved station first.");
            var view=Components<StationMissionView>(scene).Single();IntegrateExistingHardware(view,scene);
            EditorUtility.SetDirty(view);EditorSceneManager.MarkSceneDirty(scene);
            if(!EditorSceneManager.SaveScene(scene))throw new Exception("Station integration save failed.");
            AssetDatabase.SaveAssets();return "Original rollup-frame arrow panel connected; existing bell clips, audio and red lights retained.";
        }
        static void IntegrateExistingHardware(StationMissionView view,Scene scene)
        {
            if(!AssetDatabase.IsValidFolder(Folder+"/Materials"))AssetDatabase.CreateFolder(Folder,"Materials");
            var frame=view.garage.transform.parent.Find("RollupDoorFrame");
            if(!frame||!frame.GetComponent<MeshRenderer>())throw new InvalidOperationException("Original garage frame missing.");
            // The original two arrow faces are integrated in this imported mesh.
            // Only add collider hit regions on that same object; no replacement mesh/buttons.
            var action=frame.GetComponent<StationGarageButton>();if(!action)action=frame.gameObject.AddComponent<StationGarageButton>();
            action.view=view;
            var upper=action.upperArrow as BoxCollider;if(!upper)upper=frame.gameObject.AddComponent<BoxCollider>();
            var lower=action.lowerArrow as BoxCollider;if(!lower)lower=frame.gameObject.AddComponent<BoxCollider>();
            upper.center=new Vector3(.063f,-2.3955f,-3.3112f);lower.center=new Vector3(.063f,-2.5859f,-3.3112f);
            upper.size=lower.size=new Vector3(.032f,.09f,.09f);upper.isTrigger=lower.isTrigger=false;
            action.upperArrow=upper;action.lowerArrow=lower;
            var xr=frame.GetComponent<XRSimpleInteractable>();if(!xr)xr=frame.gameObject.AddComponent<XRSimpleInteractable>();
            xr.colliders.Clear();xr.colliders.Add(upper);xr.colliders.Add(lower);
            var renderer=frame.GetComponent<MeshRenderer>();
            string path=Folder+"/Materials/OriginalGarageArrowPanel.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material)
            {
                var original=AssetDatabase.LoadAssetAtPath<Material>("Assets/FireStation/Materials/RollupDoor.mat");
                material=new Material(original){name="OriginalGarageArrowPanel"};
                CreateArrowTextures(original.mainTexture,out var albedo,out var emission);
                material.SetTexture("_BaseMap",albedo);material.SetTexture("_EmissionMap",emission);
                material.SetColor("_EmissionColor",Color.white*1.4f);material.EnableKeyword("_EMISSION");
                AssetDatabase.CreateAsset(material,path);
            }
            // URP 6 validates the emission keyword from GI flags on import/save.
            // Keep emission enabled even though these visuals do not request a bake.
            material.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
            material.EnableKeyword("_EMISSION");EditorUtility.SetDirty(material);
            renderer.sharedMaterial=material;
            var duplicate=view.transform.Find("Garage_ControlPanel");if(duplicate)UnityEngine.Object.DestroyImmediate(duplicate.gameObject);
            view.alarmAudio=Components<AudioSource>(scene).Where(a=>a.clip&&a.clip.name=="EmergencyBell").ToArray();
            foreach(var a in view.alarmAudio){a.playOnAwake=false;a.loop=true;}
            view.alarmAnimators=Components<Animator>(scene).Where(a=>a.runtimeAnimatorController&&a.runtimeAnimatorController.animationClips.Any(c=>c.name.StartsWith("EmergencyBell"))).ToArray();
            var lens=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/Materials/EmergencyLens_Mission.mat");
            foreach(var a in view.alarmAnimators)
            {
                a.enabled=false;
                var light=a.GetComponent<Light>();if(light)light.enabled=false;
                if(a.name!="EmergencyBellLight")continue;
                var r=a.GetComponent<Renderer>();if(!r)continue;
                if(!lens){lens=new Material(r.sharedMaterial){name="EmergencyLens_Mission"};lens.EnableKeyword("_EMISSION");lens.SetColor("_EmissionColor",Color.black);AssetDatabase.CreateAsset(lens,Folder+"/Materials/EmergencyLens_Mission.mat");}
                lens.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;
                lens.EnableKeyword("_EMISSION");EditorUtility.SetDirty(lens);r.sharedMaterial=lens;
            }
            EditorUtility.SetDirty(action);EditorUtility.SetDirty(xr);
        }
        static void CreateArrowTextures(Texture source,out Texture2D albedo,out Texture2D emission)
        {
            const int size=2048;
            var rt=RenderTexture.GetTemporary(size,size,0,RenderTextureFormat.ARGB32,RenderTextureReadWrite.sRGB);
            var previous=RenderTexture.active;var copy=new Texture2D(size,size,TextureFormat.RGBA32,false);
            try
            {
                Graphics.Blit(source,rt);RenderTexture.active=rt;copy.ReadPixels(new Rect(0,0,size,size),0,0);copy.Apply();
                var colors=copy.GetPixels32();var glow=new Color32[colors.Length];
                for(int y=(int)(.9365f*size);y<(int)(.961f*size);y++)for(int x=(int)(.9744f*size);x<(int)(.9857f*size);x++)
                {
                    int i=y*size+x;var c=colors[i];
                    // Preserve yellow arrow texels and the grey housing. Only the
                    // original red backplate texels receive green/red emission.
                    if(c.r<c.g*1.5f||c.r<c.b*1.4f||c.r<30)continue;
                    bool up=(float)y/size<.9485f;
                    if(up)colors[i]=new Color32((byte)(c.r*.12f),(byte)Mathf.Min(255,c.r*1.7f),(byte)(c.r*.2f),c.a);
                    glow[i]=up?new Color32(8,200,20,255):new Color32(220,8,3,255);
                }
                copy.SetPixels32(colors);copy.Apply();
                string basePath=Folder+"/Materials/OriginalGarageArrows";
                System.IO.File.WriteAllBytes(basePath+"_Base.png",copy.EncodeToPNG());
                copy.SetPixels32(glow);copy.Apply();System.IO.File.WriteAllBytes(basePath+"_Emission.png",copy.EncodeToPNG());
                foreach(var suffix in new[]{"_Base.png","_Emission.png"})
                {
                    string asset=basePath+suffix;AssetDatabase.ImportAsset(asset);
                    var importer=(TextureImporter)AssetImporter.GetAtPath(asset);importer.maxTextureSize=size;importer.textureCompression=TextureImporterCompression.CompressedHQ;importer.isReadable=false;importer.SaveAndReimport();
                }
                albedo=AssetDatabase.LoadAssetAtPath<Texture2D>(basePath+"_Base.png");emission=AssetDatabase.LoadAssetAtPath<Texture2D>(basePath+"_Emission.png");
            }
            finally{RenderTexture.active=previous;RenderTexture.ReleaseTemporary(rt);UnityEngine.Object.DestroyImmediate(copy);}
        }
        public static string RefreshDisplayPlacement()
        {
            var scene=SceneManager.GetSceneByPath(StationTransitionInstaller.StationPath);var view=Components<StationMissionView>(scene).Single();
            var screen=(RectTransform)view.computerReport.GetComponentInParent<Canvas>().transform;FitMonitorCanvas(screen,screen.parent,view.dispatchSeat.position+Vector3.up*1.35f);
            var mesh=view.tablet.GetComponentsInChildren<MeshFilter>(true).First(f=>f.name.Contains("LOD0")&&f.sharedMesh);
            var bounds=mesh.sharedMesh.bounds;
            var points=(from x in new[]{-1f,1f} from y in new[]{-1f,1f} from z in new[]{-1f,1f} select view.tablet.InverseTransformPoint(mesh.transform.TransformPoint(bounds.center+Vector3.Scale(bounds.extents,new Vector3(x,y,z))))).ToArray();
            float x0=points.Min(p=>p.x),x1=points.Max(p=>p.x),z0=points.Min(p=>p.z),z1=points.Max(p=>p.z),y1=points.Max(p=>p.y);
            var tablet=(RectTransform)view.tabletReport.GetComponentInParent<Canvas>().transform;bool portrait=z1-z0>x1-x0;tablet.sizeDelta=new Vector2(680,portrait?900:460);
            float scale=Mathf.Min((x1-x0)*.9f/tablet.sizeDelta.x,(z1-z0)*.85f/tablet.sizeDelta.y);tablet.localScale=Vector3.one*scale;tablet.localPosition=new Vector3((x0+x1)*.5f,y1+.002f,(z0+z1)*.5f);tablet.localRotation=Quaternion.Euler(90,0,0);
            view.tabletReport.rectTransform.sizeDelta=tablet.sizeDelta-Vector2.one*30;view.tabletReport.fontSize=portrait?28:23;
            var background=tablet.Find("Background") as RectTransform;if(background)background.sizeDelta=tablet.sizeDelta;
            EditorSceneManager.MarkSceneDirty(scene);if(!EditorSceneManager.SaveScene(scene))throw new InvalidOperationException("Display placement save failed.");
            return "Computer UI fitted to measured front screen plane; existing tablet display fitted to its actual body.";
        }
        static void FitMonitorCanvas(RectTransform canvas,Transform monitor,Vector3 viewer)
        {
            var filter=monitor.GetComponentsInChildren<MeshFilter>(true).First(f=>f.name.Contains("LOD0")&&f.sharedMesh&&f.sharedMesh.isReadable);
            var vertices=filter.sharedMesh.vertices.Select(p=>filter.transform.TransformPoint(p)).ToArray();var indices=filter.sharedMesh.triangles;
            Vector3 toward=Vector3.ProjectOnPlane(viewer-monitor.position,Vector3.up).normalized,normal=Vector3.zero,basePoint=Vector3.zero;float best=0;
            for(int i=0;i<indices.Length;i+=3){var a=vertices[indices[i]];var b=vertices[indices[i+1]];var c=vertices[indices[i+2]];var cross=Vector3.Cross(b-a,c-a);if(Vector3.Dot(cross.normalized,toward)<.75f||(a.y+b.y+c.y)/3<monitor.position.y+.15f||cross.magnitude<=best)continue;best=cross.magnitude;normal=cross.normalized;basePoint=a;}
            if(best==0)throw new InvalidOperationException("Monitor front screen plane was not found.");
            var rotation=Quaternion.LookRotation(-normal,Vector3.up);var right=rotation*Vector3.right;var up=rotation*Vector3.up;
            var face=vertices.Where(p=>Mathf.Abs(Vector3.Dot(p-basePoint,normal))<.001f).ToArray();
            float minX=face.Min(p=>Vector3.Dot(p-basePoint,right)),maxX=face.Max(p=>Vector3.Dot(p-basePoint,right)),minY=face.Min(p=>Vector3.Dot(p-basePoint,up)),maxY=face.Max(p=>Vector3.Dot(p-basePoint,up));
            canvas.position=basePoint+right*(minX+maxX)*.5f+up*(minY+maxY)*.5f+normal*.008f;canvas.rotation=rotation;
            float scale=Mathf.Min((maxX-minX)*.93f/canvas.sizeDelta.x,(maxY-minY)*.93f/canvas.sizeDelta.y);var parent=canvas.parent.lossyScale;canvas.localScale=new Vector3(scale/parent.x,scale/parent.y,scale/parent.z);
        }
        static Scene Open(string path){var s=SceneManager.GetSceneByPath(path);if(!s.isLoaded)s=EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);if(s.isDirty)throw new InvalidOperationException("Unsaved scene: "+path);return s;}
        static T[] Components<T>(Scene s)where T:Component=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static Transform Node(string name,Transform parent){var t=parent.Find(name);if(t)return t;var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform;}
        static Transform SafeApproach(string name,Transform parent,StationSceneSetup setup,StationLocalRig rig,Vector3 center,Vector3 preferred)
        {
            preferred.y=.005f;Vector3? best=null;float score=float.PositiveInfinity;
            for(float x=center.x-2;x<=center.x+2;x+=.2f)for(float z=center.z-2;z<=center.z+2;z+=.2f){var p=new Vector3(x,.005f,z);if(!setup.ValidDestination(p,rig.origin,false))continue;float d=(p-preferred).sqrMagnitude;if(d<score){score=d;best=p;}}
            if(!best.HasValue)throw new InvalidOperationException("No safe approach for "+name);var anchor=Node(name,parent);anchor.position=best.Value;anchor.rotation=Quaternion.LookRotation(Vector3.ProjectOnPlane(center-best.Value,Vector3.up));return anchor;
        }
        static StationMissionSeatAnchor SeatTarget(StationMissionView view,string name,Transform approach,StationSeatKind kind,Material material)
        {
            var root=Node(name,view.transform);root.position=approach.position;var marker=Hardware(root,"EntryIndicator",Vector3.up*.025f,new Vector3(.36f,.045f,.36f),material,true);
            var anchor=root.GetComponent<StationMissionSeatAnchor>();if(!anchor)anchor=root.gameObject.AddComponent<StationMissionSeatAnchor>();anchor.view=view;anchor.kind=kind;anchor.indicator=marker;anchor.teleportAnchorTransform=approach;anchor.matchOrientation=MatchOrientation.WorldSpaceUp;anchor.interactionLayers=1<<31;anchor.colliders.Clear();anchor.colliders.Add(marker.GetComponent<Collider>());return anchor;
        }
        static GameObject Hardware(Transform parent,string name,Vector3 position,Vector3 size,Material material,bool collision)
        {var existing=parent.Find(name);var g=existing?existing.gameObject:GameObject.CreatePrimitive(PrimitiveType.Cube);g.name=name;g.transform.SetParent(parent,false);g.transform.localPosition=position;g.transform.localScale=size;g.GetComponent<Renderer>().sharedMaterial=material;if(!collision&&g.GetComponent<Collider>())UnityEngine.Object.DestroyImmediate(g.GetComponent<Collider>());return g;}
        static void GarageButton(Transform parent,string name,Vector3 p,Material material,bool open,StationMissionView view)
        {var g=Hardware(parent,name,p,new Vector3(.11f,.08f,.035f),material,true);var xr=g.GetComponent<XRSimpleInteractable>();if(!xr)xr=g.AddComponent<XRSimpleInteractable>();var action=g.GetComponent<StationGarageButton>();if(!action)action=g.AddComponent<StationGarageButton>();action.view=view;action.open=open;
            var label=parent.Find(name+"_Label");if(!label){var t=new GameObject(name+"_Label",typeof(TextMeshPro));t.transform.SetParent(parent,false);label=t.transform;}label.localPosition=p+new Vector3(0,open?.063f:-.063f,-.025f);label.localRotation=Quaternion.identity;var text=label.GetComponent<TextMeshPro>();text.text=open?"OPEN":"CLOSE";text.fontSize=.8f;text.alignment=TextAlignmentOptions.Center;text.rectTransform.sizeDelta=new Vector2(.25f,.08f);}
        static Material Material(string name,Color color,Color emission)
        {var m=AssetDatabase.LoadAssetAtPath<Material>(Folder+"/"+name+".mat");if(m)return m;m=new Material(Shader.Find("Universal Render Pipeline/Lit")){name=name};m.SetColor("_BaseColor",color);if(emission.maxColorComponent>0){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",emission);}AssetDatabase.CreateAsset(m,Folder+"/"+name+".mat");return m;}
        static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 p)
        {var t=parent.Find(name);var g=t?t.gameObject:new GameObject(name,typeof(RectTransform));if(!t)g.transform.SetParent(parent,false);var r=(RectTransform)g.transform;r.anchorMin=r.anchorMax=r.pivot=Vector2.one*.5f;r.sizeDelta=size;r.anchoredPosition=p;return r;}
        static RectTransform Canvas(Transform target,Vector2 size,float scale)
        {if(!(target is RectTransform)){var parent=target.parent;string name=target.name;UnityEngine.Object.DestroyImmediate(target.gameObject);target=Rect(name,parent,size,Vector2.zero);}var r=(RectTransform)target;r.sizeDelta=size;r.localScale=Vector3.one*scale;var c=r.GetComponent<Canvas>();if(!c)c=r.gameObject.AddComponent<Canvas>();c.renderMode=RenderMode.WorldSpace;if(!r.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>())r.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();return r;}
        static void Background(RectTransform parent,Vector2 size){var r=Rect("Background",parent,size,Vector2.zero);if(!r.GetComponent<CanvasRenderer>())r.gameObject.AddComponent<CanvasRenderer>();var image=r.GetComponent<UnityEngine.UI.Image>();if(!image)image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.015f,.035f,.055f,.98f);image.raycastTarget=false;r.SetAsFirstSibling();}
        static TMP_Text Text(string name,RectTransform parent,Vector2 size,Vector2 p,float font)
        {var r=Rect(name,parent,size,p);if(!r.GetComponent<CanvasRenderer>())r.gameObject.AddComponent<CanvasRenderer>();var text=r.GetComponent<TextMeshProUGUI>();if(!text)text=r.gameObject.AddComponent<TextMeshProUGUI>();text.font=TMP_Settings.defaultFontAsset;text.fontSize=font;text.color=new Color(.85f,.94f,1);text.alignment=TextAlignmentOptions.Center;text.raycastTarget=false;text.text="STATION OPERATIONS";return text;}
        static UnityEngine.UI.Button Button(string name,RectTransform parent,Vector2 size,Vector2 p)
        {var r=Rect(name,parent,size,p);if(!r.GetComponent<CanvasRenderer>())r.gameObject.AddComponent<CanvasRenderer>();var image=r.GetComponent<UnityEngine.UI.Image>();if(!image)image=r.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.06f,.30f,.24f);var button=r.GetComponent<UnityEngine.UI.Button>();if(!button)button=r.gameObject.AddComponent<UnityEngine.UI.Button>();Text("Label",r,size-Vector2.one*8,Vector2.zero,23).text=name;return button;}
    }
}
#endif
