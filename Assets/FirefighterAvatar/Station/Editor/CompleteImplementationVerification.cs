#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using Unity.Netcode;
using Unity.Collections;
using CityNightPresentation;
using CityNightPresentation.Mission;

namespace FirefighterVR.Station.Editor
{
    // Explicit Edit Mode fixtures: no fake clients, Play Mode, or hardware success claims.
    public static class CompleteImplementationVerification
    {
        const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
        static T[] All<T>(Scene s)where T:Component=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static object Field(object o,string n)=>o.GetType().GetField(n,Flags).GetValue(o);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,Flags).SetValue(o,v);
        static void Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,Flags).Invoke(o,args);
        public static string Run()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Edit Mode only");
            var checks=new List<string>();
            void Check(bool pass,string name){if(!pass)throw new InvalidOperationException(name);checks.Add(name);}
            var lobby=SceneManager.GetSceneByPath("Assets/S1Test.unity");
            var station=SceneManager.GetSceneByPath("Assets/FireStation/Scenes/Sample.unity");
            var city=SceneManager.GetSceneByName(StationSessionFlow.CitySceneName);
            var flow=All<StationSessionFlow>(lobby).Single();var rig=flow.localRig;
            var view=All<StationMissionView>(station).Single();var mission=All<FireTruckMission>(city).Single();var cab=mission.GetComponent<FireTruckCabInteraction>();var emergency=mission.GetComponent<FireTruckEmergency>();
            var state=StationLobbyState.Initial;
            Check(SeatedDispatchLobby.AvailableProfile(state,0,StationRole.None,false)==StationRole.None,"No first-player role inference");
            Check(state.Claim(0,StationRole.Assistant)&&state.Claim(7,StationRole.Driver)&&state.RoleOf(0)==StationRole.Assistant&&state.RoleOf(7)==StationRole.Driver,"Host Assistant / client Driver selection");
            Check(!state.Claim(0,StationRole.Driver)&&state.RoleOf(0)==StationRole.Assistant&&SeatedDispatchLobby.AvailableProfile(state,0,StationRole.Driver,true)==StationRole.None,"Explicit conflict preserves reservations");
            state=StationLobbyState.Initial;state.Claim(7,StationRole.Assistant);
            Check(SeatedDispatchLobby.AvailableProfile(state,0,StationRole.None,false)==StationRole.Driver&&state.Claim(0,StationRole.Driver),"Complementary role / Host Driver / client Assistant");
            var providers=rig.origin.GetComponentsInChildren<LocomotionProvider>(true);
            Check(providers.All(p=>!StationLocalRig.MovementAllowed(p,StationRole.Driver,StationLocalRig.AssistantMovement.SmoothAndTeleport,false,true,true)),"Every Driver locomotion provider blocked");
            var move=providers.First(p=>p is ContinuousMoveProvider);var tele=providers.OfType<TeleportationProvider>().Single();
            Check(StationLocalRig.MovementAllowed(move,StationRole.Assistant,StationLocalRig.AssistantMovement.SmoothAndTeleport,false,false,true)&&StationLocalRig.MovementAllowed(tele,StationRole.Assistant,StationLocalRig.AssistantMovement.SmoothAndTeleport,false,false,true),"Assistant Smooth Move and Teleport independently available");
            Check(!StationLocalRig.MovementAllowed(move,StationRole.Assistant,StationLocalRig.AssistantMovement.Teleport,false,true,true)&&!StationLocalRig.MovementAllowed(tele,StationRole.Assistant,StationLocalRig.AssistantMovement.Smooth,false,true,true),"Local Assistant movement preferences filter only locomotion");
            var root=rig.origin.transform;var p0=root.position;var q0=root.rotation;var camera=rig.origin.Camera.transform;var cp=camera.localPosition;var cq=camera.localRotation;var cc=root.GetComponent<CharacterController>();bool cc0=cc&&cc.enabled;
            var avatar=rig.avatar;bool seated0=avatar.Seated;var seat0=avatar.SeatPosition;float yaw0=avatar.SeatYaw;
            try{StationSeatPlacement.Place(rig.origin,view.dispatchSeat,avatar);Check(Vector3.Distance(camera.position,StationSeatPlacement.EyeTarget(view.dispatchSeat,avatar))<.002f&&camera.localPosition==cp&&camera.localRotation==cq&&avatar.Seated,"Seated HMD alignment moves rig root and preserves camera-local tracking pose");}
            finally{root.SetPositionAndRotation(p0,q0);if(cc)cc.enabled=cc0;avatar.Seated=seated0;avatar.SeatPosition=seat0;avatar.SeatYaw=yaw0;}
            var singleton=typeof(StationSessionFlow).GetField("<Instance>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic);var originalInstance=singleton.GetValue(null);var soloProperty=typeof(StationSessionFlow).GetProperty("SoloMissionActive");bool solo0=flow.SoloMissionActive;var mission0=Field(flow,"soloMission");var localSeat0=Field(view,"localSeat");var epoch0=Field(view,"automaticChairEpoch");
            try
            {
                singleton.SetValue(null,flow);soloProperty.SetValue(flow,true);Set(flow,"soloMission",new CrewMissionState{stage=CrewMissionStage.EmergencyAlarm,epoch=9001,tabletRotation=Quaternion.identity});Set(view,"localSeat",view.dispatchSeat);Set(view,"automaticChairEpoch",(uint)0);
                view.ConfirmSeatedDispatch();var s=flow.MissionState;Check(s.stage==CrewMissionStage.DispatchReview&&s.driverChair,"Already-seated Driver advances dispatch without locomotion");view.ConfirmSeatedDispatch();Check(flow.MissionState.revision==s.revision,"Dispatch chair acknowledgement is idempotent");
                Check(!s.TryAccept(StationRole.Driver),"Dispatch acceptance still requires Assistant tablet task");s.tabletPicked=s.tabletPresented=true;Check(s.TryAccept(StationRole.Driver)&&s.TrySeat(StationSeatKind.DriverTruck,StationRole.Driver,10,4,1.8f),"Accepted Driver transfer uses existing mission stages");
                Check(!s.CanBoardAssistant(11,4)&&s.TryGarage(StationRole.Assistant,true,true,11,4)&&!s.CanBoardAssistant(12,4)&&s.CanBoardAssistant(15,4)&&s.TrySeat(StationSeatKind.AssistantTruck,StationRole.Assistant,15,4,1.8f),"Garage and Assistant boarding gates remain enforced");
            }
            finally{soloProperty.SetValue(flow,solo0);Set(flow,"soloMission",mission0);Set(view,"localSeat",localSeat0);Set(view,"automaticChairEpoch",epoch0);singleton.SetValue(null,originalInstance);}
            var mounted=mission.controller.steeringVisual.localRotation;var turn=FireTruckWheelController.SteeringRotation(mounted,Vector3.up,1,900);
            Check(Quaternion.Angle(FireTruckWheelController.SteeringRotation(mounted,Vector3.up,0,900),mounted)<.001f&&Vector3.Distance(turn*Vector3.up,mounted*Vector3.up)<.001f,"Steering uses mounted local Y shaft without neutral drift");
            var zone=cab.wheel.Find("SteeringRimGrabZone");Check(zone&&zone.GetComponentsInChildren<SphereCollider>(true).Length==24&&zone.GetComponentsInChildren<Renderer>(true).Length==0,"Invisible circular rim: 24 trigger segments and no rendered markers");
            var rim=FireTruckCabInteraction.RimPoint(cab.rimCenter,cab.rimCenter,cab.rimRadius);Check(Vector3.Distance(rim,cab.rimCenter)>cab.rimTolerance,"Empty wheel center does not count as rim proximity");
            var left=Field(cab,"left");var right=Field(cab,"right");int l0=(int)Field(left,"attachment"),r0=(int)Field(right,"attachment");
            try{Set(left,"attachment",1);Set(right,"attachment",1);Call(cab,"Release",right);Check((int)Field(left,"attachment")==1&&(int)Field(right,"attachment")==0,"Right-hand release leaves left grip intact");Set(right,"attachment",1);Call(cab,"Release",left);Check((int)Field(right,"attachment")==1&&(int)Field(left,"attachment")==0,"Left-hand release leaves right grip intact");}
            finally{Set(left,"attachment",l0);Set(right,"attachment",r0);}
            Check(FireTruckCabInteraction.ProjectPull(Vector3.zero,Vector3.right,Vector3.back,.18f,0)==0&&FireTruckCabInteraction.ProjectPull(Vector3.zero,Vector3.back,Vector3.back,.18f,0)==1,"Pull rejects off-axis displacement and clamps travel");
            float pull0=emergency.PullValue;var hp=emergency.pullHandle.localPosition;var hq=emergency.pullHandle.localRotation;
            try{emergency.SetPullValue(1);Check(Vector3.Distance(emergency.pullHandle.localPosition,emergency.closedLocalPosition+emergency.pullAxis.normalized*emergency.pullTravel)<.0001f&&emergency.pullHandle.localRotation==hq&&!emergency.lever.enabled,"Existing emergency control slides linearly without rotation");}
            finally{emergency.SetPullValue(pull0);emergency.pullHandle.localPosition=hp;emergency.pullHandle.localRotation=hq;}
            var packet=new FirefighterAvatarState{StandingHeight=1.7f,AvatarScale=1,GripVehicleSpace=true,LeftVisualWeight=255,LeftVisualPosition=new Vector3(.2f,1.1f,.5f),LeftVisualRotation=Quaternion.Euler(10,20,30)};
            using(var writer=new FastBufferWriter(1024,Allocator.Temp)){writer.WriteNetworkSerializable(packet);using(var reader=new FastBufferReader(writer,Allocator.Temp)){reader.ReadNetworkSerializable(out FirefighterAvatarState copy);Check(copy.IsValid&&copy.LeftVisualWeight==255&&copy.RightVisualWeight==0&&Vector3.Distance(copy.LeftVisualPosition,packet.LeftVisualPosition)<.0001f,"Existing avatar packet round-trips independent grip / release state");}}
            var evaluation=new FireTruckEvaluation{revision=2,outcome=MissionOutcome.TimedOut,emergencyActivations=1,trafficWaitingSeconds=12,intersectionDelaySeconds=8,challengeStopRecorded=true};
            using(var writer=new FastBufferWriter(256,Allocator.Temp)){writer.WriteNetworkSerializable(evaluation);using(var reader=new FastBufferReader(writer,Allocator.Temp)){reader.ReadNetworkSerializable(out FireTruckEvaluation copy);Check(copy.outcome==MissionOutcome.TimedOut&&copy.emergencyActivations==1&&copy.intersectionDelaySeconds==8,"Evaluation network serialization preserves outcome and delays");}}
            var signalGO=new GameObject("TEMP_CompleteImplementationSignalTest"){hideFlags=HideFlags.HideAndDontSave};
            try{var signal=signalGO.AddComponent<CitySignalController>();signal.SetClock(0);signal.RequestPriority(1);signal.SetClock(.1f);Check(signal.GetAspect(0)==CitySignalController.Aspect.Yellow&&signal.GetAspect(1)==CitySignalController.Aspect.Red,"Priority first clears conflicting green through yellow");signal.SetClock(3.1f);Check(Enumerable.Range(0,4).All(i=>signal.GetAspect(i)==CitySignalController.Aspect.Red),"Priority all-red clearance");signal.SetClock(5.2f);Check(signal.Allows(1)&&Enumerable.Range(0,4).Count(i=>signal.Allows(i))==1,"Priority grants exactly one approach green");signal.ReleasePriority();signal.SetClock(5.3f);Check(signal.GetAspect(1)==CitySignalController.Aspect.Yellow,"Priority release uses yellow before normal cycle");}
            finally{UnityEngine.Object.DestroyImmediate(signalGO);}
            bool offline0=mission.offlineDriverTesting;var elapsed0=Field(mission,"offlineElapsed");var eval0=Field(mission,"evaluation");var started0=Field(mission,"started");var at0=Field(mission,"networkAt");var sim0=mission.traffic.localSimulation;var auth0=Field(mission,"wasEvaluationAuthority");
            try{mission.offlineDriverTesting=true;Set(mission,"offlineElapsed",mission.missionTimeLimitSeconds+1);Set(mission,"evaluation",default(FireTruckEvaluation));Set(mission,"started",true);Set(mission,"networkAt",float.PositiveInfinity);Call(mission,"Update");Check(mission.RemainingSeconds==0&&mission.HasFailed&&mission.ConsumeDriverInput().z==1,"Expired existing timer fails mission and applies safe brake input");}
            finally{mission.offlineDriverTesting=offline0;Set(mission,"offlineElapsed",elapsed0);Set(mission,"evaluation",eval0);Set(mission,"started",started0);Set(mission,"networkAt",at0);Set(mission,"wasEvaluationAuthority",auth0);mission.traffic.localSimulation=sim0;}
            var activities=All<StationComputerActivities>(station).Single();var scroll=activities.libraryPanel.GetComponentInChildren<UnityEngine.UI.ScrollRect>(true);
            Check(activities.cells.Length==9&&activities.articles.Length==4&&activities.pace&&scroll&&scroll.content&&scroll.viewport&&scroll.content.GetComponent<UnityEngine.UI.ContentSizeFitter>(),"Existing monitor has drill, pace slider and masked scrollable local library");
            var uiActive=new[]{activities.activityRoot,activities.drillPanel,activities.libraryPanel,view.computerReport.gameObject,view.acceptButton.gameObject,view.objective.gameObject}.Distinct().ToDictionary(g=>g,g=>g.activeSelf);
            var texts=activities.GetComponentsInChildren<TMPro.TMP_Text>(true).ToDictionary(t=>t,t=>t.text);var colors=activities.cells.ToDictionary(b=>b.targetGraphic,b=>b.targetGraphic.color);
            var activityFields=new[]{"target","score","misses","targetUntil"}.ToDictionary(n=>n,n=>Field(activities,n));
            try
            {
                singleton.SetValue(null,flow);soloProperty.SetValue(flow,true);Set(flow,"soloMission",new CrewMissionState{stage=CrewMissionStage.Exploration});Call(activities,"Update");Call(activities,"ShowDrill");Call(activities,"Restart");Call(activities,"Hit",Field(activities,"target"));
                Check((int)Field(activities,"score")==1&&activities.activityRoot.activeSelf,"Optional drill scores an actual target hit during Exploration");
                Call(activities,"ShowLibrary");Call(activities,"OpenArticle",2);Check(activities.libraryPanel.activeSelf&&activities.articleText.text.Contains("NO INTERNET ACCESS")&&activities.articleText.text.Contains("EMERGENCY PRIORITY"),"Local library navigation is truthful about offline content");
                Set(flow,"soloMission",new CrewMissionState{stage=CrewMissionStage.EmergencyAlarm});Call(activities,"Update");Check(!activities.activityRoot.activeSelf&&view.computerReport.gameObject.activeSelf&&view.acceptButton.gameObject.activeSelf,"Emergency immediately interrupts optional content and restores existing dispatch UI");
            }
            finally{foreach(var entry in uiActive)entry.Key.SetActive(entry.Value);foreach(var entry in texts)entry.Key.text=entry.Value;foreach(var entry in colors)entry.Key.color=entry.Value;foreach(var entry in activityFields)Set(activities,entry.Key,entry.Value);soloProperty.SetValue(flow,solo0);Set(flow,"soloMission",mission0);singleton.SetValue(null,originalInstance);}
            Check(emergency.siren&&emergency.siren.clip&&emergency.siren.clip.length>0&&!emergency.siren.mute&&emergency.siren.volume>0,"Siren has valid assigned audio and non-muted source (audibility unverified)");
            Check(emergency.redOn.IsKeywordEnabled("_EMISSION")&&emergency.blueOn.IsKeywordEnabled("_EMISSION"),"Existing roof lenses have emission enabled");
            var scenes=new[]{lobby,station,city};int missingScripts=scenes.Sum(s=>All<Transform>(s).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));int missingMaterials=scenes.Sum(s=>All<Renderer>(s).Sum(r=>r.sharedMaterials.Count(m=>!m)));
            Check(missingScripts==0&&missingMaterials==0,"No missing scripts or renderer materials in targeted scenes");
            Check(All<Unity.XR.CoreUtils.XROrigin>(lobby).Length==1&&All<TeleportationProvider>(lobby).Length==1&&All<UnityEngine.EventSystems.EventSystem>(lobby).Length==1&&All<StationSessionFlow>(lobby).Length==1&&All<FireTruckCabInteraction>(city).Length==1,"No duplicate XR Origin, provider, EventSystem, mission flow or cab adapter");
            var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{scope="Focused Edit Mode geometry, policy, serializer and state fixtures; not Play Mode or physical hardware / connected-peer validation",checks,missingScripts,missingMaterials,hardwareRequired=new[]{"Quest stereo and seated alignment","Real finger joints, pinch/drag UI and independent Grab edges","G29 device calibration and wheel motion","Siren audibility and traffic under live driving","Two-peer authority, role selection, mission and grip replication","Full Solo bot sequence with seated human"}},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText("Assets/FirefighterAvatar/Station/Reports/CompleteImplementationVerification.json",json);return json;
        }
    }
}
#endif
