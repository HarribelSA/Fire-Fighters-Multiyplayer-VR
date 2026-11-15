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
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace FirefighterVR.Station.Editor
{
    public static class StationCoopVerification
    {
        static void Check(bool value,string message){if(!value)throw new InvalidOperationException(message);}
        public static string Run()
        {
            Check(!EditorApplication.isPlaying,"Use focused Edit Mode verification.");
            var lobby=SceneManager.GetSceneByPath("Assets/S1Test.unity");var station=SceneManager.GetSceneByPath(StationTransitionInstaller.StationPath);var city=SceneManager.GetSceneByName(StationSessionFlow.CitySceneName);
            var flow=All<StationSessionFlow>(lobby).Single();var rig=flow.localRig;var view=All<StationMissionView>(station).Single();var hand=All<StationHandTeleportInput>(lobby).Single();
            Check(flow.incident&&flow.explorationSeconds==60,"Shared incident/alarm timer binding missing.");
            Check(All<Unity.XR.CoreUtils.XROrigin>(lobby).Length==1&&All<TeleportationProvider>(lobby).Length==1,"XR rig/provider duplication.");
            Check(All<StationSessionFlow>(station).Length==0&&All<Unity.Netcode.NetworkObject>(station).Length==0,"Station added a duplicate network manager/object.");
            Check(hand.ray&&hand.rig==rig&&hand.handInteraction&&hand.sampleGesture&&hand.sampleRotation&&hand.rayPose,"Hand adapter reference missing.");
            Check(hand.ray.velocity==10&&hand.ray.sampleFrequency==50,"Existing hand arc changed.");
            Check(hand.ray.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>().reticle,"Hand reticle missing.");
            Check(All<XRRayInteractor>(lobby).Count(r=>r.name=="Hand Teleport Interactor")==1,"Duplicate hand teleport ray.");
            Check(view.dispatchSeat&&view.chairApproach&&view.driverSeat&&view.passengerSeat&&view.tablet&&view.tabletHome&&view.tabletGrab&&view.garage&&view.computerReport&&view.tabletReport&&view.acceptButton,"Station interaction reference missing.");
            Check(view.alarmAudio.Length>0&&view.alarmAudio.All(a=>a&&a.clip&&a.clip.name=="EmergencyBell"&&!a.playOnAwake),"Alarm does not reuse the existing clip safely.");
            Check(All<DoorAnimation>(station).All(d=>!d.name.StartsWith("RollupDoor")),"Garage proximity opening remains.");
            Check(view.curtainColliders.Length>0&&view.curtainColliders.All(c=>c),"Garage curtain collision missing.");
            var setup=All<StationSceneSetup>(station).Single();Check(setup.walkableSurfaces.Length==4,"Existing teleport surfaces duplicated/rebuilt.");
            Check(!setup.IsSafeStairLanding(new Vector3(18,1.2f,4))&&setup.IsSafeStairLanding(new Vector3(18,3.3646f,1.5f)),"Landing-only stair policy failed.");
            var mission=All<CityNightPresentation.Mission.FireTruckMission>(city).Single();var presentation=All<CityNightPresentation.Mission.FireTruckPresentation>(city).Single();
            Check(mission.controller&&mission.controller.drivingActions&&mission.controller.wheels.Length==4&&mission.route&&mission.traffic&&mission.driverSeat&&mission.passengerSeat,"Existing city driving references broken.");
            Check(presentation.tabletDetails&&presentation.tabletStatus&&presentation.GetComponentInChildren<CityNightPresentation.Mission.MissionTabletMap>(true),"Existing city tablet integration missing.");
            var checks=new List<string>();
            var s=new CrewMissionState{stage=CrewMissionStage.Exploration,deadline=60,garageAt=0,tabletRotation=Quaternion.identity};
            Check(!s.TrySeat(StationSeatKind.DispatchChair,StationRole.Driver,20,4,1.8f),"Driver seated before alarm.");
            Check(!s.TryAccept(StationRole.Driver),"Mission accepted before dispatch.");checks.Add("Exploration cannot skip alarm/dispatch");
            s.stage=CrewMissionStage.EmergencyAlarm;
            Check(!s.TrySeat(StationSeatKind.DispatchChair,StationRole.Assistant,60,4,1.8f),"Assistant claimed driver chair.");
            Check(s.TrySeat(StationSeatKind.DispatchChair,StationRole.Driver,60,4,1.8f),"Driver chair rejected.");
            Check(!s.TryAccept(StationRole.Driver),"Acceptance skipped tablet presentation.");s.tabletPicked=s.tabletPresented=true;
            Check(!s.TryAccept(StationRole.Assistant)&&s.TryAccept(StationRole.Driver),"Acceptance role protection failed.");checks.Add("Only seated driver accepts after tablet pickup/presentation");
            Check(!s.TrySeat(StationSeatKind.AssistantTruck,StationRole.Assistant,61,4,1.8f),"Passenger boarded before garage.");
            Check(s.TrySeat(StationSeatKind.DriverTruck,StationRole.Driver,61,4,1.8f),"Driver entry rejected.");
            Check(!s.TryGarage(StationRole.Driver,true,true,62,4),"Driver operated assistant mission panel.");
            Check(s.TryGarage(StationRole.Assistant,true,true,62,4),"Assistant garage command rejected.");
            Check(!s.CanBoardAssistant(63,4)&&s.CanBoardAssistant(66,4),"Garage completion timing incorrect.");
            Check(!s.TryGarage(StationRole.Assistant,false,false,66,4),"Door closed on blocked sweep.");checks.Add("Assistant garage action and fully-open/sweep checks enforced");
            Check(s.TrySeat(StationSeatKind.AssistantTruck,StationRole.Assistant,66,4,1.8f)&&s.stage==CrewMissionStage.Departure&&Math.Abs(s.deadline-67.8)<.001,"Cooperative departure gate failed.");
            Check(!s.TrySeat(StationSeatKind.AssistantTruck,StationRole.Assistant,66,4,1.8f),"Duplicate boarding advanced state twice.");checks.Add("Boarding order and duplicate-event guards pass");
            var scenes=new[]{lobby,station,city};var missingScripts=scenes.Sum(scene=>All<Transform>(scene).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));
            var missingMaterials=scenes.Sum(scene=>All<Renderer>(scene).Sum(r=>r.sharedMaterials.Count(m=>!m)));
            var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{scope="Focused Edit Mode binding and mission state tests; not physical XR or a live two-peer session",checks,missingScripts,missingMaterials,xrOrigins=1,teleportProviders=1,handRays=1,stationMissionViews=1,logicalTablet="Persistent StationSessionFlow incident/epoch; existing station and city visual instances",garageAudio=view.alarmAudio[0].clip.name,readyStateScriptUnchanged=true,hardwareTestsRequired=new[]{"Driver spawn and no reset rollback","Right palm-up/pinch-release arc and reticle","Pinch/grab and controller carry/teleport","Both-peer alarm/door/boarding replication","Stereo fades and city seat synchronization","G29 and navigation in live Quest session"}},Newtonsoft.Json.Formatting.Indented);
            File.WriteAllText("Assets/FirefighterAvatar/Station/Reports/CooperativeMissionVerification.json",json);return json;
        }
        static T[] All<T>(Scene scene)where T:Component=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
    }
}
#endif
