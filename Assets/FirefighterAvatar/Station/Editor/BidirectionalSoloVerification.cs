#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FirefighterVR.StationBot;
using CityNightPresentation;
using CityNightPresentation.Mission;
namespace FirefighterVR.Station.Editor
{
    public static class BidirectionalSoloVerification
    {
        const BindingFlags F=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
        static object Get(object o,string n)=>o.GetType().GetField(n,F).GetValue(o);
        static void Set(object o,string n,object v)=>o.GetType().GetField(n,F).SetValue(o,v);
        static void Property(object o,string n,object v)=>o.GetType().GetProperty(n,F).SetValue(o,v);
        static object Call(object o,string n,params object[] args)=>o.GetType().GetMethod(n,F).Invoke(o,args);
        static T[] All<T>(Scene s)where T:Component=>s.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<T>(true)).ToArray();
        static Scene Open(string p){var s=SceneManager.GetSceneByPath(p);return s.isLoaded?s:EditorSceneManager.OpenScene(p,OpenSceneMode.Additive);}
        public static string Run()
        {
            if(EditorApplication.isPlaying)throw new Exception("Focused Edit Mode only");
            var lobby=Open("Assets/S1Test.unity");var station=Open(StationSoloBotTest.StationPath);var city=Open("Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity");
            var flow=All<StationSessionFlow>(lobby).Single();var session=All<StationSoloBotTest>(lobby).Single();var view=All<StationMissionView>(station).Single();var setup=All<StationSceneSetup>(station).Single();var mission=All<FireTruckMission>(city).Single();var rig=flow.localRig;
            var checks=new List<string>();void Check(bool p,string name){if(!p)throw new Exception(name);checks.Add(name);}
            var instances=new[]{typeof(StationSessionFlow),typeof(StationSceneSetup),typeof(StationMissionView)}.Select(t=>t.GetField("<Instance>k__BackingField",BindingFlags.Static|BindingFlags.NonPublic)).ToArray();var oldInstances=instances.Select(f=>f.GetValue(null)).ToArray();
            var flowNames=new[]{"soloSession","soloMission","soloNavigation","soloTask","soloFailure","soloEpoch","lastTabletPacket","soloCityMission"};var flowOld=flowNames.ToDictionary(n=>n,n=>Get(flow,n));
            bool oldActive=flow.SoloMissionActive;var oldRole=flow.SoloHumanRole;var oldBot=session.Bot;var oldHuman=session.HumanRole;var oldTest=session.IsStationTest;
            StationBotFollower bot=null;FireTruckBotDriver adapter=null;var priorMissionTransform=mission.transform.position;var priorRotation=mission.transform.rotation;
            var missionNames=new[]{"evaluation","offlineSiren","input","inputAt","started","offlineArrived"};var missionOld=missionNames.ToDictionary(n=>n,n=>Get(mission,n));var reason=mission.BotFailureReason;float pull=mission.GetComponent<FireTruckEmergency>().PullValue;
            try
            {
                instances[0].SetValue(null,flow);instances[1].SetValue(null,setup);instances[2].SetValue(null,view);
                bot=UnityEngine.Object.Instantiate(session.botPrefab);bot.name="TEMP_BidirectionalSoloFixture";bot.Initialize(setup,rig.origin.transform,rig.origin.Camera.transform,StationRole.Assistant);
                Property(session,"Bot",bot);Property(session,"HumanRole",StationRole.Assistant);Property(session,"IsStationTest",true);
                Check(flow.BeginSoloMission(session)&&flow.SoloHumanRole==StationRole.Assistant&&flow.SoloBotDriverActive&&flow.MissionLocalRole==StationRole.Assistant&&mission.LocalRole==StationRole.Assistant,"Solo Controllers keeps human Assistant and complementary bot Driver");
                Call(flow,"TickSoloDriver",view);Check(bot.IsMissionSeated&&!flow.MissionState.driverChair,"Bot seated during exploration without skipping alarm");
                var hips=bot.animator.GetBoneTransform(HumanBodyBones.Hips);Check(Vector3.Distance(hips.position,view.dispatchSeat.position+view.dispatchSeat.up*.59f)<.01f,"Bot pelvis rests at existing chair height");
                var thigh=bot.animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);var shin=bot.animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);var foot=bot.animator.GetBoneTransform(HumanBodyBones.LeftFoot);Check(Vector3.Angle(shin.position-thigh.position,foot.position-shin.position)>45&&Mathf.Abs(foot.position.y-view.dispatchSeat.position.y-.07f)<.025f,"Bot seated knees bend and feet reach chair-floor targets");
                Set(flow,"soloMission",new CrewMissionState{stage=CrewMissionStage.EmergencyAlarm,epoch=42,tabletRotation=Quaternion.identity});Call(flow,"TickSoloDriver",view);Check(flow.MissionState.stage==CrewMissionStage.DispatchReview&&flow.MissionState.driverChair,"Bot acknowledges existing dispatch chair after alarm");Call(flow,"TickSoloDriver",view);Check(flow.MissionState.stage==CrewMissionStage.DispatchReview&&!flow.MissionState.tabletPicked,"Bot cannot accept or fabricate human tablet tasks");
                var tabletPosition=view.dispatchSeat.position+Vector3.up*1.2f+view.dispatchSeat.forward*.45f;var tabletRotation=Quaternion.FromToRotation(Vector3.up,(view.dispatchSeat.position+Vector3.up*1.35f-tabletPosition).normalized);Set(flow,"lastTabletPacket",double.NegativeInfinity);flow.UpdateDispatchTablet(tabletPosition,tabletRotation,true);
                Check(flow.MissionState.tabletPicked&&flow.MissionState.tabletPresented,"Solo human Assistant tablet uses existing presentation validation");Call(flow,"TickSoloDriver",view);Check(flow.MissionState.stage==CrewMissionStage.MissionAccepted,"Bot accepts only valid presented dispatch");Call(flow,"TickSoloDriver",view);Check(flow.MissionState.stage==CrewMissionStage.DriverInTruck&&bot.transform.parent==view.driverSeat&&!flow.MissionState.garageTask&&!flow.MissionState.assistantTruck,"Bot takes existing Driver seat and leaves human garage/boarding incomplete");
                flow.NotifyStationSeat(StationSeatKind.AssistantTruck);Check(!flow.MissionState.assistantTruck,"Human passenger cannot board before garage gate");flow.CommandGarage(true);Check(flow.MissionState.garageTask&&flow.MissionState.garageOpen,"Human Solo Assistant can operate existing garage command");var s=flow.MissionState;s.garageAt=flow.SharedTime-flow.garageTravelSeconds;Set(flow,"soloMission",s);flow.NotifyStationSeat(StationSeatKind.AssistantTruck);Check(flow.MissionState.stage==CrewMissionStage.Departure&&flow.MissionState.assistantTruck,"Human boards only after actual garage timing gate");
                s=flow.MissionState;s.stage=CrewMissionStage.CityTransition;s.citySeats=1;Set(flow,"soloMission",s);flow.AcknowledgeCitySeat(s.epoch);Check(flow.MissionState.citySeats==3,"Assistant ACK sets passenger bit, complementary bot Driver owns Driver bit");
                s=flow.MissionState;s.stage=CrewMissionStage.DrivingMission;Set(flow,"soloMission",s);Check(mission.BotDriverMayControl&&!mission.LocalDriverMayControl,"Only Solo bot input controls vehicle in human Assistant configuration");
                Check(!mission.GetComponent<FireTruckEmergency>().RequestFromBotDriver(true)&&!mission.SirenActive,"Bot emergency cannot activate immediately at city spawn");
                adapter=mission.gameObject.AddComponent<FireTruckBotDriver>();adapter.mission=mission;adapter.emergency=mission.GetComponent<FireTruckEmergency>();Check((bool)Call(adapter,"Initialize"),"Bot adapter binds all 81 existing road waypoints without a second path system");Call(adapter,"Compute");Check(((Vector4)Get(adapter,"input")).y>0&&!mission.SirenActive,"Route start produces existing acceleration input without automatic siren");
                var signal=mission.traffic.signals[0];var head=signal.heads.First(h=>h.approach==0);var signalState=new[]{"priorityWanted","priorityStage","transitionApproach","transitionDeadline","lastCode"}.ToDictionary(n=>n,n=>Get(signal,n));var clock=signal.Clock;int occupied=signal.occupiedApproach;bool red=signal.externalAllRed;
                try
                {
                    signal.externalAllRed=true;signal.SetClock(0);mission.transform.SetPositionAndRotation(head.stopLine.position-head.stopLine.forward*4.7f,Quaternion.LookRotation(head.stopLine.forward));
                    // Deterministic geometry/state fixture, not a simulated physical drive.
                    Set(adapter,"segment",20);Property(adapter,"Progress",0f);Call(adapter,"Compute");Check(mission.EmergencyConditionsSatisfied&&mission.SirenActive,"Bot activates through existing stopped/designated-red conditions");
                    Check(((Vector4)Get(adapter,"input")).y==0&&((Vector4)Get(adapter,"input")).z==1,"Bot remains stopped while signal clearance is pending");
                }
                finally{signal.externalAllRed=red;signal.occupiedApproach=occupied;foreach(var kv in signalState)Set(signal,kv.Key,kv.Value);signal.SetClock(clock);signal.RefreshVisuals();}
                mission.SubmitBotDriverInput(new Vector4(.2f,.4f,0,0));Check(mission.ConsumeDriverInput().y==.4f,"Bot input feeds existing validated vehicle channel");mission.FailBotDriver("Fixture bounded block");Check(mission.HasFailed&&mission.EvaluationState.outcome==MissionOutcome.BotBlocked&&mission.ConsumeDriverInput().z==1&&!mission.SirenActive,"Bounded bot failure stops safely, resets emergency and preserves evaluation");
                Check(FireTruckBotDriver.Steering(new Vector3(2,0,8),4.8f,31)>0&&FireTruckBotDriver.Steering(new Vector3(-2,0,8),4.8f,31)<0&&FireTruckBotDriver.StoppingSpeed(0)==0,"Deterministic steering signs and stop-speed policy");
                Property(flow,"SoloHumanRole",StationRole.Driver);Check(flow.MissionLocalRole==StationRole.Driver&&!flow.SoloBotDriverActive&&mission.LocalRole==StationRole.Driver,"Solo Human Driver retains Driver role and excludes bot driving");
                var providers=rig.origin.GetComponentsInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.LocomotionProvider>(true);Check(providers.All(p=>!StationLocalRig.MovementAllowed(p,StationRole.Driver,StationLocalRig.AssistantMovement.SmoothAndTeleport,false,true,true)),"Physical head motion cannot unlock Driver locomotion policy");Check(providers.OfType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement.ContinuousMoveProvider>().Any(p=>StationLocalRig.MovementAllowed(p,StationRole.Assistant,StationLocalRig.AssistantMovement.SmoothAndTeleport,false,false,true))&&providers.OfType<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationProvider>().Any(p=>StationLocalRig.MovementAllowed(p,StationRole.Assistant,StationLocalRig.AssistantMovement.SmoothAndTeleport,false,false,true)),"Assistant movement and teleport restore independently of Driver policy");
                Check(!StationBotRoles.CanUseBot(2,StationPhase.Waiting),"Two real players exclude Solo bot");
                VerifySeatedHuman(rig,Check);
            }
            finally
            {
                if(adapter)UnityEngine.Object.DestroyImmediate(adapter);if(bot)UnityEngine.Object.DestroyImmediate(bot.gameObject);mission.transform.SetPositionAndRotation(priorMissionTransform,priorRotation);foreach(var kv in missionOld)Set(mission,kv.Key,kv.Value);Property(mission,"BotFailureReason",reason);mission.GetComponent<FireTruckEmergency>().SetPullValue(pull);
                Property(session,"Bot",oldBot);Property(session,"HumanRole",oldHuman);Property(session,"IsStationTest",oldTest);Property(flow,"SoloMissionActive",oldActive);Property(flow,"SoloHumanRole",oldRole);foreach(var kv in flowOld)Set(flow,kv.Key,kv.Value);for(int i=0;i<instances.Length;i++)instances[i].SetValue(null,oldInstances[i]);
            }
            var scenes=new[]{lobby,station,city};int missing=scenes.Sum(s=>All<Transform>(s).Sum(t=>GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)));Check(missing==0&&scenes.Sum(s=>All<Renderer>(s).Sum(r=>r.sharedMaterials.Count(m=>!m)))==0,"No missing scripts or renderer materials");
            var json=Newtonsoft.Json.JsonConvert.SerializeObject(new{scope="Focused Edit Mode fixtures with synthetic state and positions, not Play Mode, a physical drive or network session",checks,missingScripts=missing,remaining="Quest seated comfort and IK appearance; physical G29; live route tracking/collision/timing; two-peer sessions"},Newtonsoft.Json.Formatting.Indented);File.WriteAllText("Assets/FirefighterAvatar/Station/Reports/BidirectionalSoloVerification.json",json);return json;
        }
        static void VerifySeatedHuman(StationLocalRig rig,Action<bool,string> check)
        {
            var copy=UnityEngine.Object.Instantiate(rig.avatar.placement.gameObject);copy.name="TEMP_SeatedHumanFixture";var space=new GameObject("TEMP_TrackedSources");
            var cameraLocal=rig.origin.Camera.transform.localPosition;
            try
            {
                var avatar=copy.GetComponentInChildren<FirefighterAvatarDriver>(true);avatar.originalLocalVisuals=Array.Empty<Renderer>();avatar.breathingCondensation=null;avatar.localFirstPerson=false;avatar.playerRoot=space.transform;
                Transform Source(string name,Vector3 p){var g=new GameObject(name);g.transform.SetParent(space.transform);g.transform.position=p;return g.transform;}
                avatar.Seated=true;avatar.SeatPosition=new Vector3(-.85f,0,0);avatar.SeatYaw=0;avatar.headSource=Source("Head",avatar.SeatPosition+new Vector3(.02f,StationSeatPlacement.EyeHeight(avatar),.2f));avatar.leftSource=Source("Left",avatar.SeatPosition+new Vector3(-.3f,.95f,.5f));avatar.rightSource=Source("Right",avatar.SeatPosition+new Vector3(.3f,.95f,.5f));
                Call(avatar,"LateUpdate");var anchored=avatar.placement.position;var hips=avatar.animator.GetBoneTransform(HumanBodyBones.Hips);var seatedHip=hips.position;
                avatar.headSource.position+=new Vector3(1.2f,1.2f,0);Call(avatar,"LateUpdate");
                check(Vector3.Distance(avatar.placement.position,anchored)<.002f&&Vector3.Distance(hips.position,seatedHip)<.02f&&avatar.movementSpeed==0,"Synthetic standing/jumping does not move seated body or enter walking");
                check(Mathf.Abs(avatar.floorY-avatar.SeatPosition.y)<.001f&&avatar.legs.All(l=>Mathf.Abs(l.target.position.y-avatar.SeatPosition.y-l.soleOffset)<.1f),"Human seated feet retain chair-floor elevation despite displaced head");
                check(rig.origin.Camera.transform.localPosition==cameraLocal,"Synthetic pose checks never alter real HMD camera-local tracking");
            }
            finally{UnityEngine.Object.DestroyImmediate(copy);UnityEngine.Object.DestroyImmediate(space);}
        }
    }
}
#endif
