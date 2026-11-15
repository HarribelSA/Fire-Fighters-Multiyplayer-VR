#if UNITY_EDITOR
using System;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using CityNightPresentation.Mission;

namespace FirefighterVR.Station.Editor
{
    public static class CompleteImplementationInstaller
    {
        const string Lobby="Assets/S1Test.unity",Station="Assets/FireStation/Scenes/Sample.unity",City="Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity";
        static Scene Open(string path){var s=SceneManager.GetSceneByPath(path);return s.isLoaded?s:EditorSceneManager.OpenScene(path,OpenSceneMode.Additive);}
        static T[] All<T>(Scene s)where T:Component=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static T Get<T>(GameObject g)where T:Component{var c=g.GetComponent<T>();return c?c:g.AddComponent<T>();}
        static Transform Node(string name,Transform parent){var t=parent.Find(name);if(t)return t;var g=new GameObject(name);g.transform.SetParent(parent,false);return g.transform;}
        static RectTransform Rect(string name,Transform parent,Vector2 size,Vector2 position)
        {var t=parent.Find(name) as RectTransform;if(!t){var g=new GameObject(name,typeof(RectTransform));g.transform.SetParent(parent,false);t=(RectTransform)g.transform;}t.gameObject.layer=5;t.anchorMin=t.anchorMax=t.pivot=Vector2.one*.5f;t.sizeDelta=size;t.anchoredPosition=position;return t;}
        static UnityEngine.UI.Image Image(RectTransform r,Color color){var im=Get<UnityEngine.UI.Image>(r.gameObject);im.color=color;return im;}
        static TMP_Text Text(string name,Transform parent,Vector2 size,Vector2 position,string value,float font=23)
        {var t=Get<TextMeshProUGUI>(Rect(name,parent,size,position).gameObject);t.font=TMP_Settings.defaultFontAsset;t.fontSize=font;t.text=value;t.color=new Color(.87f,.96f,1);t.alignment=TextAlignmentOptions.Center;t.raycastTarget=false;return t;}
        static UnityEngine.UI.Button Button(string name,Transform parent,Vector2 size,Vector2 position,string label)
        {var r=Rect(name,parent,size,position);var image=Image(r,new Color(.055f,.23f,.22f));var b=Get<UnityEngine.UI.Button>(r.gameObject);b.targetGraphic=image;Text("Label",r,size-Vector2.one*8,Vector2.zero,label,20);return b;}
        public static string Apply()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Install outside Play Mode");
            var lobby=Open(Lobby);var station=Open(Station);var city=Open(City);
            var flow=All<StationSessionFlow>(lobby).Single();var rig=flow.localRig;var menu=All<SeatedDispatchLobby>(lobby).Single();
            rig.permanentlySeatedDriver=true;
            var hand=All<StationHandTeleportInput>(lobby).Single();hand.transform.SetParent(rig.origin.CameraFloorOffsetObject.transform,true);
            hand.sampleGesture.enabled=false;hand.sampleRotation.enabled=false;hand.ray.gameObject.SetActive(false);hand.handInteraction.enabled=true;
            InstallSettings(menu,rig);
            foreach(var button in new[]{menu.left.seat,menu.right.seat}){var group=Get<CanvasGroup>(button.gameObject);group.ignoreParentGroups=true;group.interactable=group.blocksRaycasts=true;}
            foreach(var label in menu.GetComponentsInChildren<TMP_Text>(true))label.text=label.text.Replace("PLAYER 1","DRIVER PROFILE").Replace("PLAYER 2","ASSISTANT PROFILE");
            var leftLabel=menu.left.seat.GetComponentInChildren<TMP_Text>(true);if(leftLabel){leftLabel.text="HAND TRACKING + G29 · DRIVER";leftLabel.fontSize=20;}
            var rightLabel=menu.right.seat.GetComponentInChildren<TMP_Text>(true);if(rightLabel){rightLabel.text="QUEST CONTROLLERS · ASSISTANT";rightLabel.fontSize=20;}
            var view=All<StationMissionView>(station).Single();InstallComputer(view);
            var mission=All<FireTruckMission>(city).Single();InstallCab(mission);
            foreach(var s in new[]{lobby,station,city}){EditorSceneManager.MarkSceneDirty(s);if(!EditorSceneManager.SaveScene(s))throw new Exception("Save failed: "+s.path);}
            AssetDatabase.SaveAssets();return "Saved control profiles, permanent Driver seating, Assistant movement settings, existing-monitor activities, actual-axis wheel/rim grip, linear pull handle, roof emission and mission evaluation bindings.";
        }
        static void InstallSettings(SeatedDispatchLobby menu,StationLocalRig rig)
        {
            var settings=(RectTransform)menu.right.settings.transform;settings.sizeDelta=new Vector2(630,460);
            var back=settings.Find("Back") as RectTransform;if(back)back.anchoredPosition=new Vector2(0,-215);
            Text("AssistantMovementTitle",settings,new Vector2(610,30),new Vector2(0,-110),"ASSISTANT MOVEMENT · LOCAL PLAYER ONLY",20);
            string[] labels={"MOVE + TELEPORT","SMOOTH MOVE","TELEPORT"};
            for(int i=0;i<3;i++)
            {var b=Button("AssistantMovement_"+i,settings,new Vector2(190,42),new Vector2((i-1)*200,-158),labels[i]);b.onClick=new UnityEngine.UI.Button.ButtonClickedEvent();UnityEditor.Events.UnityEventTools.AddIntPersistentListener(b.onClick,rig.SetAssistantMovement,i);}
            Text("DriverSeatedNotice",menu.left.settings.transform,new Vector2(600,42),new Vector2(0,-185),"DRIVER REMAINS SEATED · HAND UI / GRAB / G29",18);
        }
        static void InstallComputer(StationMissionView view)
        {
            var canvas=view.computerReport.GetComponentInParent<Canvas>();
            Get<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>(canvas.gameObject);
            var activities=Get<StationComputerActivities>(canvas.gameObject);activities.view=view;
            var root=Rect("StandbyActivities",canvas.transform,new Vector2(820,600),Vector2.zero);activities.activityRoot=root.gameObject;Image(root,new Color(.015f,.04f,.055f));root.SetAsLastSibling();
            Text("Title",root,new Vector2(780,40),new Vector2(0,265),"STATION STANDBY · OPTIONAL LOCAL ACTIVITIES",24);
            activities.drillTab=Button("DrillTab",root,new Vector2(340,52),new Vector2(-185,205),"RESPONSE DRILL");
            activities.libraryTab=Button("LibraryTab",root,new Vector2(340,52),new Vector2(185,205),"LOCAL LIBRARY · NO INTERNET");
            var drill=Rect("Drill",root,new Vector2(780,450),new Vector2(0,-40));activities.drillPanel=drill.gameObject;
            activities.scoreText=Text("Score",drill,new Vector2(740,70),new Vector2(0,160),"RESPONSE DRILL · SCORE 0 · MISSES 0\nClick the lit square. Drag PACE to adjust difficulty.",23);
            activities.cells=new UnityEngine.UI.Button[9];
            for(int i=0;i<9;i++)activities.cells[i]=Button("DrillCell_"+i,drill,new Vector2(110,65),new Vector2((i%3-1)*125,70-i/3*80),i==0?"RESPOND":"·");
            Text("PaceTitle",drill,new Vector2(170,32),new Vector2(-260,-160),"DRILL PACE",20);
            var sliderRect=Rect("Pace",drill,new Vector2(390,36),new Vector2(40,-160));Image(sliderRect,new Color(.08f,.15f,.19f));activities.pace=Get<UnityEngine.UI.Slider>(sliderRect.gameObject);
            var fill=Rect("Fill",sliderRect,new Vector2(390,12),Vector2.zero);fill.anchorMin=new Vector2(0,.3f);fill.anchorMax=new Vector2(1,.7f);fill.offsetMin=fill.offsetMax=Vector2.zero;Image(fill,new Color(.1f,.7f,.42f)).raycastTarget=false;
            var handle=Rect("Handle",sliderRect,new Vector2(24,40),Vector2.zero);Image(handle,new Color(.8f,.95f,.91f));activities.pace.fillRect=fill;activities.pace.handleRect=handle;activities.pace.targetGraphic=handle.GetComponent<UnityEngine.UI.Image>();activities.pace.direction=UnityEngine.UI.Slider.Direction.LeftToRight;activities.pace.minValue=0;activities.pace.maxValue=1;activities.pace.SetValueWithoutNotify(.4f);
            activities.restart=Button("RestartDrill",drill,new Vector2(180,40),new Vector2(0,-215),"RESTART DRILL");
            var library=Rect("LocalLibrary",root,new Vector2(780,450),new Vector2(0,-40));activities.libraryPanel=library.gameObject;
            string[] titles={"CHECKLIST","CONTROLS","EMERGENCY","HAND UI"};activities.articles=new UnityEngine.UI.Button[4];
            for(int i=0;i<4;i++)activities.articles[i]=Button("Article_"+i,library,new Vector2(180,58),new Vector2(-285,130-i*78),titles[i]);
            var scroll=Rect("ScrollView",library,new Vector2(535,410),new Vector2(110,0));var sr=Get<UnityEngine.UI.ScrollRect>(scroll.gameObject);sr.horizontal=false;sr.vertical=true;sr.movementType=UnityEngine.UI.ScrollRect.MovementType.Clamped;sr.scrollSensitivity=25;
            var viewport=Rect("Viewport",scroll,new Vector2(535,410),Vector2.zero);Image(viewport,new Color(.025f,.07f,.09f));var mask=Get<UnityEngine.UI.Mask>(viewport.gameObject);mask.showMaskGraphic=false;sr.viewport=viewport;
            var content=Rect("Content",viewport,new Vector2(515,1050),Vector2.zero);content.anchorMin=new Vector2(0,1);content.anchorMax=new Vector2(1,1);content.pivot=new Vector2(.5f,1);content.anchoredPosition=Vector2.zero;content.sizeDelta=new Vector2(0,1050);
            var layout=Get<UnityEngine.UI.VerticalLayoutGroup>(content.gameObject);layout.padding=new RectOffset(12,12,12,12);layout.childControlWidth=layout.childControlHeight=true;layout.childForceExpandHeight=false;
            var fitter=Get<UnityEngine.UI.ContentSizeFitter>(content.gameObject);fitter.horizontalFit=UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;fitter.verticalFit=UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
            activities.articleText=Text("LocalArticle",content,new Vector2(490,1000),Vector2.zero,"LOCAL CONTENT — NO INTERNET ACCESS",24);activities.articleText.alignment=TextAlignmentOptions.TopLeft;Get<UnityEngine.UI.LayoutElement>(activities.articleText.gameObject).preferredHeight=1000;sr.content=content;
            library.gameObject.SetActive(false);root.gameObject.SetActive(false);EditorUtility.SetDirty(activities);
        }
        static void InstallCab(FireTruckMission mission)
        {
            var controller=mission.controller;var wheel=controller.steeringVisual;var mesh=wheel.GetComponent<MeshFilter>().sharedMesh;var bounds=mesh.bounds;
            controller.steeringShaftAxis=Vector3.up;controller.physicalSteeringDegrees=900;
            var cab=Get<FireTruckCabInteraction>(mission.gameObject);cab.mission=mission;cab.controller=controller;cab.wheel=wheel;cab.rimRadius=(bounds.extents.x+bounds.extents.z)*.45f;cab.rimCenter=bounds.center;
            using(var data=MeshUtility.AcquireReadOnlyMeshData(mesh))
            {var vertices=new Unity.Collections.NativeArray<Vector3>(data[0].vertexCount,Unity.Collections.Allocator.Temp);try{data[0].GetVertices(vertices);float sum=0;int n=0;for(int i=0;i<vertices.Length;i++){var p=vertices[i]-bounds.center;if(p.x*p.x+p.z*p.z>cab.rimRadius*cab.rimRadius*.8f){sum+=vertices[i].y;n++;}}if(n>0)cab.rimCenter=new Vector3(bounds.center.x,sum/n,bounds.center.z);}finally{vertices.Dispose();}}
            var zone=Node("SteeringRimGrabZone",wheel);zone.gameObject.layer=2;
            for(int i=0;i<24;i++){var t=Node("Rim_"+i,zone);t.gameObject.layer=2;float a=i*Mathf.PI*2/24;t.localPosition=cab.rimCenter+new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*cab.rimRadius;var c=Get<SphereCollider>(t.gameObject);c.radius=.045f;c.isTrigger=true;}
            var emergency=mission.GetComponent<FireTruckEmergency>();cab.emergency=emergency;
            var mount=emergency.lever.transform;mount.SetPositionAndRotation(mission.driverSeat.position+mission.driverSeat.right*.35f+mission.driverSeat.up*.78f+mission.driverSeat.forward*.35f,mission.driverSeat.rotation);
            emergency.lever.enabled=false;emergency.lever.minAngle=emergency.lever.maxAngle=0;
            emergency.pullMount=mount;emergency.pullHandle=emergency.lever.handle;emergency.closedLocalPosition=new Vector3(0,0,-.08f);emergency.pullAxis=Vector3.back;emergency.pullTravel=.18f;emergency.pullHandle.localRotation=Quaternion.identity;emergency.SetPullValue(0);
            var plate=mount.Find("Mount");plate.localPosition=Vector3.zero;plate.localScale=new Vector3(.16f,.20f,.025f);
            var grip=emergency.pullHandle.Find("Grip");grip.localPosition=Vector3.zero;grip.localScale=new Vector3(.22f,.04f,.045f);
            var collider=emergency.pullHandle.GetComponent<BoxCollider>();if(collider){collider.center=Vector3.zero;collider.size=new Vector3(.25f,.07f,.09f);collider.isTrigger=true;}
            var stem=Node("PullStem",emergency.pullHandle);stem.localPosition=new Vector3(0,0,.045f);stem.localScale=new Vector3(.025f,.025f,.09f);Get<MeshFilter>(stem.gameObject).sharedMesh=Resources.GetBuiltinResource<Mesh>("Cube.fbx");Get<MeshRenderer>(stem.gameObject).sharedMaterial=plate.GetComponent<Renderer>().sharedMaterial;
            var label=Node("EmergencyPullLabel",mount);label.localPosition=new Vector3(0,.065f,-.015f);label.localRotation=Quaternion.identity;var text=Get<TextMeshPro>(label.gameObject);text.font=TMP_Settings.defaultFontAsset;text.text="EMERGENCY\nPULL / PUSH";text.fontSize=.12f;text.alignment=TextAlignmentOptions.Center;text.rectTransform.sizeDelta=new Vector2(.155f,.065f);text.color=Color.white;
            foreach(var mat in new[]{emergency.redOn,emergency.blueOn}){mat.globalIlluminationFlags=MaterialGlobalIlluminationFlags.BakedEmissive;mat.EnableKeyword("_EMISSION");EditorUtility.SetDirty(mat);}
            if(emergency.siren){emergency.siren.loop=true;emergency.siren.playOnAwake=false;emergency.siren.dopplerLevel=0;}
            mission.missionTimeLimitSeconds=300;mission.emergencyChallengeSignalIndex=0;
            if(mission.traffic.signals.Length>0){var s=mission.traffic.signals[0];s.offsetSeconds=s.greenSeconds+s.yellowSeconds+s.allRedSeconds;EditorUtility.SetDirty(s);}
            EditorUtility.SetDirty(controller);EditorUtility.SetDirty(cab);EditorUtility.SetDirty(emergency);EditorUtility.SetDirty(mission);
        }
    }
}
#endif
