using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using CityNightPresentation.Mission;
using TMPro;
using Unity.Netcode;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CityNightPresentation.Editor
{
    public static class FireTruckMissionBuilder
    {
        const string Folder="Assets/CityNightPresentation/Mission/Assets";
        static GameObject Node(string name,Transform parent)
        {var t=parent.Find(name);if(t)return t.gameObject;var g=new GameObject(name);g.transform.SetParent(parent,false);return g;}
        static Material Material(string name,Color color,bool emission=false)
        {
            var path=Folder+"/"+name+".mat";var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!m){m=new Material(Shader.Find("Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(m,path);}
            m.SetColor("_BaseColor",color);m.enableInstancing=true;if(emission){m.EnableKeyword("_EMISSION");m.SetColor("_EmissionColor",color*2);}
            EditorUtility.SetDirty(m);return m;
        }
        static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 size,Material mat)
        {
            var g=Node(name,parent);g.transform.localPosition=position;g.transform.localScale=size;
            var f=g.GetComponent<MeshFilter>();if(!f)f=g.AddComponent<MeshFilter>();f.sharedMesh=Resources.GetBuiltinResource<Mesh>("Cube.fbx");
            var r=g.GetComponent<MeshRenderer>();if(!r)r=g.AddComponent<MeshRenderer>();r.sharedMaterial=mat;r.shadowCastingMode=ShadowCastingMode.Off;return g;
        }
        static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
        {
            var t=parent.Find(name)as RectTransform;if(!t){var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);t=(RectTransform)g.transform;}
            t.gameObject.layer=LayerMask.NameToLayer("UI");
            t.anchorMin=t.anchorMax=t.pivot=new Vector2(.5f,.5f);t.sizeDelta=size;t.anchoredPosition=position;return t;
        }
        static TextMeshProUGUI Text(string name,Transform parent,Vector2 size,Vector2 p,string value,float fontSize)
        {
            var rect=Rect(name,parent,size,p);var t=rect.GetComponent<TextMeshProUGUI>();if(!t)t=rect.gameObject.AddComponent<TextMeshProUGUI>();
            t.font=TMP_Settings.defaultFontAsset;t.text=value;t.fontSize=fontSize;t.color=new Color(.85f,.94f,.94f);t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;return t;
        }
        static void MakeSiren()
        {
            string path=Folder+"/SirenWail.wav";if(File.Exists(path))return;
            const int rate=22050;const int count=rate*4;
            using(var b=new BinaryWriter(File.Create(path)))
            {
                b.Write(System.Text.Encoding.ASCII.GetBytes("RIFF"));b.Write(36+count*2);b.Write(System.Text.Encoding.ASCII.GetBytes("WAVEfmt "));b.Write(16);b.Write((short)1);b.Write((short)1);b.Write(rate);b.Write(rate*2);b.Write((short)2);b.Write((short)16);b.Write(System.Text.Encoding.ASCII.GetBytes("data"));b.Write(count*2);
                double phase=0;for(int i=0;i<count;i++){double t=(double)i/rate;double hz=690+220*Math.Sin(Math.PI*t);phase+=2*Math.PI*hz/rate;double fade=Math.Min(1,Math.Min(t*12,(4-t)*12));b.Write((short)(Math.Sin(phase)*fade*16000));}
            }
            AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);
        }
        static InputActionAsset MakeActions()
        {
            string path=Folder+"/FireTruckDriving.inputactions";var existing=AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);if(existing)return existing;
            var json=Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Assets/Basic Arcade Car Controller/Input/Controls.inputactions"));json["name"]="FireTruckDriving";
            foreach(var map in json["maps"])foreach(var binding in map["bindings"])
                if(((string)binding["path"]).Contains("G29")&&((string)binding["action"]=="Throttle"||(string)binding["action"]=="BrakeReverse"))binding["processors"]="Invert,Normalize(min=-1,max=1,zero=-1)";
            var asset=InputActionAsset.FromJson(json.ToString());var drive=asset.FindActionMap("Drive",true);var reverse=drive.AddAction("ReverseGear",InputActionType.Button);reverse.AddBinding("<Keyboard>/r");reverse.AddBinding("<HID::Logitech G29 Driving Force Racing Wheel>/button6");
            File.WriteAllText(path,asset.ToJson());UnityEngine.Object.DestroyImmediate(asset);AssetDatabase.ImportAsset(path,ImportAssetOptions.ForceSynchronousImport);return AssetDatabase.LoadAssetAtPath<InputActionAsset>(path);
        }
        public static object Build()
        {
            var scene=SceneManager.GetActiveScene();if(scene.name!="demo_city_night")throw new Exception("Wrong scene.");
            var route=GameObject.Find("FIREFIGHTER_DRIVING_ROUTE").GetComponent<CityDrivingRoute>();
            var traffic=GameObject.Find("TRAFFIC_SYSTEM/TrafficManager").GetComponent<CityTrafficManager>();
            var truck=scene.GetRootGameObjects().FirstOrDefault(g=>g.name=="MISSION_FIRE_TRUCK");
            if(truck)throw new Exception("Mission truck already exists; inspect and refine it rather than rebuilding.");
            var preview=EditorSceneManager.OpenPreviewScene("Assets/FireStation/Scenes/Sample.unity");
            try
            {
                var source=preview.GetRootGameObjects().Single(g=>g.name=="firetruck");truck=UnityEngine.Object.Instantiate(source);SceneManager.MoveGameObjectToScene(truck,scene);
            }
            finally{EditorSceneManager.ClosePreviewScene(preview);}
            truck.name="MISSION_FIRE_TRUCK";truck.transform.SetPositionAndRotation(route.spawnPoint.position+Vector3.up*.3f,route.spawnPoint.rotation);
            foreach(var t in truck.GetComponentsInChildren<Transform>(true))GameObjectUtility.SetStaticEditorFlags(t.gameObject,0);
            foreach(var r in truck.GetComponentsInChildren<Renderer>()){r.lightmapIndex=-1;r.realtimeLightmapIndex=-1;r.lightProbeUsage=LightProbeUsage.BlendProbes;r.shadowCastingMode=ShadowCastingMode.Off;}
            var materialCopies=new Dictionary<Material,Material>();
            foreach(var r in truck.GetComponentsInChildren<Renderer>())
            {
                var ms=r.sharedMaterials;for(int i=0;i<ms.Length;i++)
                {
                    var src=ms[i];if(!src)continue;
                    if(!materialCopies.TryGetValue(src,out var copy))
                    {
                        string path=Folder+"/Truck_"+src.name.Replace(" ","_")+".mat";copy=AssetDatabase.LoadAssetAtPath<Material>(path);
                        if(!copy){copy=new Material(src){enableInstancing=true};AssetDatabase.CreateAsset(copy,path);}materialCopies[src]=copy;
                    }
                    ms[i]=copy;
                }r.sharedMaterials=ms;
            }
            foreach(var l in truck.GetComponentsInChildren<Light>()){l.shadows=LightShadows.None;l.range=Mathf.Min(18,l.range);l.intensity=Mathf.Min(2,l.intensity);}
            var rb=truck.AddComponent<Rigidbody>();rb.mass=12000;rb.linearDamping=.04f;rb.angularDamping=2;rb.interpolation=RigidbodyInterpolation.Interpolate;rb.collisionDetectionMode=CollisionDetectionMode.ContinuousDynamic;rb.centerOfMass=new Vector3(0,.85f,-.55f);
            var tuning=truck.AddComponent<BasicCarController>();tuning.enabled=false;tuning.maxSpeed=65;tuning.maxReverseSpeed=12;tuning.motorPower=9000;tuning.brakePower=12000;tuning.maxSteeringAngle=31;tuning.steeringSpeed=45;tuning.accelerationCurve=AnimationCurve.Linear(0,1,1,0);
            var controller=truck.AddComponent<FireTruckWheelController>();controller.importedTuning=tuning;controller.drivingActions=MakeActions();controller.maximumKmh=65;controller.reverseKmh=12;controller.axleTorque=9000;controller.brakeTorque=12000;
            var wheelGroup=Node("DrivingWheelColliders",truck.transform).transform;var colliders=new List<WheelCollider>();var visuals=new List<Transform>();var offsets=new List<Quaternion>();
            foreach(var suffix in new[]{"FL","FR","RL","RR"})
            {
                var visual=truck.transform.Find("firetruck_wheel_"+suffix);if(!visual)throw new Exception("Missing original truck wheel "+suffix);
                var g=Node(suffix,wheelGroup);g.transform.localPosition=visual.localPosition;var wheel=g.AddComponent<WheelCollider>();wheel.radius=.59f;wheel.mass=70;wheel.suspensionDistance=.3f;wheel.sprungMass=3000;
                var spring=wheel.suspensionSpring;spring.spring=180000;spring.damper=18000;spring.targetPosition=.5f;wheel.suspensionSpring=spring;
                var friction=wheel.sidewaysFriction;friction.stiffness=1.15f;wheel.sidewaysFriction=friction;
                colliders.Add(wheel);visuals.Add(visual);offsets.Add(visual.localRotation);
            }
            controller.wheels=colliders.ToArray();controller.wheelVisuals=visuals.ToArray();controller.wheelVisualOffsets=offsets.ToArray();controller.steeringVisual=truck.transform.Find("firetruck_steering_wheel");
            tuning.allWheels=colliders.ToArray();
            var so=new SerializedObject(tuning);so.FindProperty("driveWheels").arraySize=2;so.FindProperty("steeringWheels").arraySize=2;
            for(int i=0;i<2;i++){so.FindProperty("driveWheels").GetArrayElementAtIndex(i).objectReferenceValue=colliders[i+2];so.FindProperty("steeringWheels").GetArrayElementAtIndex(i).objectReferenceValue=colliders[i];}so.ApplyModifiedPropertiesWithoutUndo();
            truck.AddComponent<NetworkObject>();truck.AddComponent<XRMultiplayer.ClientNetworkTransform>();
            var mission=truck.AddComponent<FireTruckMission>();mission.controller=controller;controller.mission=mission;
            var seats=Node("CrewSeats",truck.transform).transform;mission.driverSeat=Node("DriverSeat",seats).transform;mission.driverSeat.localPosition=new Vector3(-.75f,1.15f,2.2f);mission.passengerSeat=Node("PassengerSeat",seats).transform;mission.passengerSeat.localPosition=new Vector3(.75f,1.15f,2.2f);
            var seating=truck.AddComponent<FireTruckSeating>();seating.mission=mission;seating.chassisCollider=truck.GetComponent<BoxCollider>();
            var cabin=Node("CabinMissionEquipment",truck.transform).transform;
            var black=Material("CabinHardware",new Color(.035f,.045f,.055f));var emergency=truck.AddComponent<FireTruckEmergency>();emergency.mission=mission;
            var leverRoot=Node("Ceiling_Siren_Lever",cabin);leverRoot.transform.localPosition=new Vector3(0,2.7f,2.2f);
            Cube("Mount",leverRoot.transform,Vector3.zero,new Vector3(.18f,.05f,.18f),black);
            var handle=Node("Handle",leverRoot.transform).transform;Cube("Grip",handle,new Vector3(0,-.2f,0),new Vector3(.08f,.35f,.08f),Material("Lever_Grip",new Color(.38f,.025f,.025f)));
            var leverCollider=handle.gameObject.AddComponent<BoxCollider>();leverCollider.center=new Vector3(0,-.2f,0);leverCollider.size=new Vector3(.14f,.4f,.14f);
            emergency.lever=leverRoot.AddComponent<UnityEngine.XR.Content.Interaction.XRLever>();emergency.lever.handle=handle;emergency.lever.minAngle=-40;emergency.lever.maxAngle=40;emergency.lever.colliders.Add(leverCollider);
            var bar=Node("Emergency_Lightbar",truck.transform).transform;bar.localPosition=new Vector3(0,3.27f,2.65f);Cube("Housing",bar,Vector3.zero,new Vector3(1.7f,.12f,.25f),black);
            emergency.redOn=Material("Lightbar_Red",new Color(1,.015f,.02f),true);emergency.blueOn=Material("Lightbar_Blue",new Color(.03f,.16f,1),true);emergency.lensOff=Material("Lightbar_Off",new Color(.035f,.04f,.05f));
            emergency.redLens=Cube("Red",bar,new Vector3(-.48f,.09f,0),new Vector3(.65f,.15f,.24f),emergency.lensOff).GetComponent<Renderer>();emergency.blueLens=Cube("Blue",bar,new Vector3(.48f,.09f,0),new Vector3(.65f,.15f,.24f),emergency.lensOff).GetComponent<Renderer>();
            MakeSiren();emergency.siren=bar.gameObject.AddComponent<AudioSource>();emergency.siren.clip=AssetDatabase.LoadAssetAtPath<AudioClip>(Folder+"/SirenWail.wav");emergency.siren.loop=true;emergency.siren.playOnAwake=false;emergency.siren.spatialBlend=.8f;emergency.siren.volume=.45f;emergency.siren.minDistance=4;emergency.siren.maxDistance=150;emergency.siren.dopplerLevel=0;
            var mixer=AssetDatabase.LoadAssetAtPath<UnityEngine.Audio.AudioMixer>("Assets/VRMPAssets/Audio/Main.mixer");if(mixer)emergency.siren.outputAudioMixerGroup=mixer.FindMatchingGroups("SFX").FirstOrDefault();
            var presentation=truck.AddComponent<FireTruckPresentation>();presentation.mission=mission;
            var timerRoot=Rect("Windshield_Timer",cabin,new Vector2(600,100),Vector2.zero);timerRoot.localPosition=new Vector3(-.42f,2.53f,3.45f);timerRoot.localScale=Vector3.one*.001f;
            var timerCanvas=timerRoot.gameObject.AddComponent<Canvas>();timerCanvas.renderMode=RenderMode.WorldSpace;
            presentation.windshieldTimer=Text("Timer",timerRoot,new Vector2(600,100),Vector2.zero,"00:00",55);
            var tabletMount=Node("Passenger_Tablet",cabin).transform;tabletMount.localPosition=new Vector3(.72f,1.78f,2.85f);tabletMount.localRotation=Quaternion.Euler(30,0,0);Cube("Body",tabletMount,Vector3.zero,new Vector3(.46f,.3f,.035f),black);
            var tablet=Rect("Tablet_Canvas",tabletMount,new Vector2(640,400),Vector2.zero);tablet.localPosition=new Vector3(0,0,-.022f);tablet.localScale=Vector3.one*.00065f;var canvas=tablet.gameObject.AddComponent<Canvas>();canvas.renderMode=RenderMode.WorldSpace;tablet.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            presentation.tabletInteraction=tablet.gameObject.AddComponent<CanvasGroup>();
            presentation.tabletStatus=Text("Status",tablet,new Vector2(600,70),new Vector2(0,155),"GARDEN COURT",24);
            var mapRect=Rect("Route_Map",tablet,new Vector2(570,240),new Vector2(0,0));mapRect.gameObject.AddComponent<CanvasRenderer>();var map=mapRect.gameObject.AddComponent<MissionTabletMap>();map.truck=truck.transform;
            var buttonRect=Rect("Follow_Button",tablet,new Vector2(210,45),new Vector2(0,-165));var image=buttonRect.gameObject.AddComponent<UnityEngine.UI.Image>();image.color=new Color(.08f,.2f,.24f);var button=buttonRect.gameObject.AddComponent<UnityEngine.UI.Button>();Text("Label",buttonRect,new Vector2(205,45),Vector2.zero,"FOLLOW / ROUTE",22);UnityEditor.Events.UnityEventTools.AddPersistentListener(button.onClick,map.ToggleFollow);
            // Arrow data are road-conformed at approximately one metre spacing; no permanent arrow objects.
            var arrow=new Mesh{name="MissionArrow"};arrow.vertices=new[]{new Vector3(-.12f,0,-.35f),new Vector3(.12f,0,-.35f),new Vector3(-.12f,0,0),new Vector3(.12f,0,0),new Vector3(-.27f,0,0),new Vector3(.27f,0,0),new Vector3(0,0,.35f)};arrow.triangles=new[]{0,2,1,1,2,3,4,6,5};arrow.RecalculateNormals();arrow.RecalculateBounds();AssetDatabase.CreateAsset(arrow,Folder+"/MissionArrow.asset");presentation.arrowMesh=arrow;
            var arrowMat=new Material(Shader.Find("CityNightPresentation/Nearby Navigation Arrow")){enableInstancing=true};AssetDatabase.CreateAsset(arrowMat,Folder+"/NavigationArrows.mat");presentation.arrowMaterial=arrowMat;
            CityNightBuilder.Validate();var arrowPoints=new List<Vector3>();var arrowRotations=new List<Quaternion>();float carry=0;
            for(int i=0;i<route.waypoints.Length-1;i++)
            {
                var a=route.waypoints[i].position;var b=route.waypoints[i+1].position;float length=Vector3.Distance(a,b);var direction=(b-a).normalized;
                for(float d=carry;d<length;d+=1){var p=Vector3.Lerp(a,b,d/length);if(CityNightBuilder.Height(p.x,p.z,out float height))p.y=height+.075f;arrowPoints.Add(p);arrowRotations.Add(Quaternion.LookRotation(direction,Vector3.up));}
                carry=Mathf.Repeat(carry-length,1);
            }
            presentation.arrowPositions=arrowPoints.ToArray();presentation.arrowRotations=arrowRotations.ToArray();
            var assetPrefab=PrefabUtility.SaveAsPrefabAssetAndConnect(truck,Folder+"/MissionFireTruck.prefab",InteractionMode.AutomatedAction);if(!assetPrefab)throw new Exception("Truck prefab save failed.");
            mission.route=route;mission.traffic=traffic;map.route=route;traffic.emergencyVehicle=truck.transform;EditorUtility.SetDirty(traffic);
            var target=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<Transform>(true)).Where(t=>t.name=="mid_house_1_2 (3)").OrderBy(t=>Vector3.Distance(t.position,new Vector3(151,6,-206))).FirstOrDefault();mission.incidentBuilding=target;
            map.incident=target;PrefabUtility.RecordPrefabInstancePropertyModifications(mission);PrefabUtility.RecordPrefabInstancePropertyModifications(map);
            // Bake a safe lateral pull-aside envelope for civilian traffic; runtime needs no mesh reads.
            foreach(var path in GameObject.Find("TRAFFIC_SYSTEM").GetComponentsInChildren<CityTrafficPath>())
            {
                path.Initialize();path.safeYieldOffsets=new float[path.points.Length];
                for(int i=0;i<path.points.Length;i++)
                {
                    var p=path.points[i];var f=(path.points[(i+1)%path.points.Length]-p).normalized;var right=Vector3.Cross(Vector3.up,f).normalized;
                    foreach(float distance in new[]{3.3f,3f,0f})
                    {
                        bool clear=true;foreach(float end in new[]{-3.1f,3.1f})foreach(float side in new[]{-1.3f,1.3f}){var q=p+right*(distance+side)+f*end;if(!CityNightBuilder.Height(q.x,q.z,out _))clear=false;}
                        if(clear){path.safeYieldOffsets[i]=distance;break;}
                    }
                }
                EditorUtility.SetDirty(path);
            }
            int benches=0,hedges=0;var kept=new List<Vector3>();var propRoot=GameObject.Find("CITY_PROPS");
            foreach(var t in propRoot.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("bench")).ToArray())
            {if(benches<65&&kept.Any(p=>(p-t.position).sqrMagnitude<3.2f*3.2f)){t.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);benches++;}else kept.Add(t.position);}
            foreach(var t in propRoot.GetComponentsInChildren<Transform>().Where(t=>t.name.StartsWith("hedge_with_base")).ToArray())
            {if(traffic.signals.Any(s=>Vector3.ProjectOnPlane(s.transform.position-t.position,Vector3.up).sqrMagnitude<14*14)){t.gameObject.SetActive(false);PrefabUtility.RecordPrefabInstancePropertyModifications(t.gameObject);hedges++;}}
            foreach(var r in truck.GetComponentsInChildren<Renderer>())r.shadowCastingMode=ShadowCastingMode.Off;
            var visibility=GameObject.Find("CITY_OPTIMIZATION").GetComponent<CityVisibilityController>();visibility.items=visibility.items.Where(item=>item.renderer&&item.renderer.gameObject.activeInHierarchy).ToArray();EditorUtility.SetDirty(visibility);
            EditorSceneManager.MarkSceneDirty(scene);AssetDatabase.SaveAssetIfDirty(assetPrefab);
            return new{truck="Existing station firetruck",wheels=controller.wheels.Length,arrows=presentation.arrowPositions.Length,networkObjects=truck.GetComponentsInChildren<NetworkObject>().Length,seats=2,redundantBenchesHidden=benches,sightlineHedgesHidden=hedges,incident=target?target.name:null};
        }
    }
}
