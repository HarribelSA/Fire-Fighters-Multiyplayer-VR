using System.Collections;
using FirefighterVR.StationBot;
using UnityEngine;
using UnityEngine.SceneManagement;
using CityNightPresentation.Mission;

namespace FirefighterVR.Station
{
    // Local storage/commands for the SAME mission controller and stage model.
    // No network client, ownership change, NetworkObject or second countdown.
    public sealed partial class StationSessionFlow
    {
        CrewMissionState soloMission;
        StationSoloBotTest soloSession;
        StationBotNavigator soloNavigation;
        FireTruckMission soloCityMission;
        uint soloEpoch;
        bool soloBotPress;
        float nextSoloTablet;
        string soloTask="",soloFailure="";
        Vector3 soloTaskPoint;
        float nextGoalTry;
        public bool SoloMissionActive {get;private set;}
        public StationRole SoloHumanRole {get;private set;}=StationRole.Driver;
        public bool SoloBotDriverActive=>SoloMissionActive&&SoloHumanRole==StationRole.Assistant&&soloSession&&soloSession.Bot&&soloSession.Bot.Role==StationRole.Driver&&StationSoloBotTest.RealPlayerCount<2;
        public string SoloTaskStatus=>soloFailure.Length>0?soloFailure:soloTask;
        public bool SoloCityLoaded {get;private set;}
        public bool BeginSoloMission(StationSoloBotTest session)
        {
            if(SoloMissionActive||!session||!session.IsStationTest||!session.Bot||session.HumanRole==StationRole.None||session.Bot.Role!=StationBotRoles.Opposite(session.HumanRole)||StationSoloBotTest.RealPlayerCount>=2)return false;
            var view=StationMissionView.Instance;var setup=StationSceneSetup.Instance;
            if(!view||!setup||!localRig||!view.tablet||!view.dispatchSeat||!view.garage){Debug.LogError("Solo mission cannot start: station/rig/tablet/chair/garage references missing",this);return false;}
            soloSession=session;SoloHumanRole=session.HumanRole;SoloMissionActive=true;SoloCityLoaded=false;soloFailure="";soloTask="Explore station";soloEpoch++;
            soloNavigation=new StationBotNavigator(setup,localRig.origin.transform,session.Bot.transform);
            soloMission=new CrewMissionState{stage=CrewMissionStage.Exploration,epoch=soloEpoch,deadline=SharedTime+explorationSeconds,garageAt=SharedTime,tabletPosition=view.tablet.position,tabletRotation=view.tablet.rotation};
            Debug.Log("[StationMission] Solo mission started: human "+SoloHumanRole+" / bot "+session.Bot.Role+"; one shared 60-second deadline",this);return true;
        }
        public void EndSoloMission()
        {
            var view=StationMissionView.Instance;if(view&&view.tablet&&view.tabletHome)view.tablet.SetPositionAndRotation(view.tabletHome.position,view.tabletHome.rotation);
            if(soloSession&&soloSession.Bot){soloSession.Bot.HeldObject=null;soloSession.Bot.ClearTask();}
            SoloMissionActive=false;soloSession=null;soloMission=default;soloNavigation=null;soloCityMission=null;soloTask="";soloFailure="";soloBotPress=false;
            if(localRig){localRig.StationSeated=false;localRig.SeatTransition=false;}
        }
        bool Goal(string task,Vector3 target,float minRadius=.65f,bool leftHand=false)
        {
            var bot=soloSession.Bot;
            if(soloTask!=task)
            {
                if(Time.unscaledTime<nextGoalTry)return false;nextGoalTry=Time.unscaledTime+.75f;
                bool found=false;float best=float.PositiveInfinity;Vector3 selected=target;
                for(int ring=0;ring<5;ring++)for(int i=0;i<12;i++)
                {
                    float angle=i*Mathf.PI/6,r=minRadius+ring*.12f;
                    var p=new Vector3(target.x+Mathf.Cos(angle)*r,bot.transform.position.y,target.z+Mathf.Sin(angle)*r);
                    if(target.y-p.y>.6f&&!bot.CanReachFrom(p,target,leftHand))continue;
                    if(!soloNavigation.Ground(p,out var floor)||!soloNavigation.Clear(floor))continue;
                    float cost=ring*1000+(floor-bot.transform.position).sqrMagnitude;
                    if(cost<best&&soloNavigation.Plan(bot.transform.position,floor,true)){best=cost;selected=floor;found=true;}
                }
                if(!found){soloFailure="Bot path blocked: "+task;return false;}
                soloFailure="";soloTask=task;soloTaskPoint=selected;Debug.Log("[StationMission] Bot task: "+task+" goal="+selected,this);
            }
            bot.TaskGoal(soloTaskPoint,.05f);return bot.TaskReached;
        }
        void CarrySoloTablet(StationMissionView view,bool present)
        {
            var bot=soloSession.Bot;var pos=bot.transform.position+Vector3.up*1.15f+bot.transform.forward*.43f;
            var eye=view.dispatchSeat.position+Vector3.up*1.35f;
            if(present)pos=eye+(bot.transform.position-view.dispatchSeat.position).normalized*.55f-Vector3.up*.12f;
            var facing=Quaternion.FromToRotation(Vector3.up,(eye-pos).normalized);
            bot.Reach(pos,facing);var actual=bot.HandPosition+Vector3.up*.035f;
            view.tablet.SetPositionAndRotation(actual,facing);
            if(Time.unscaledTime<nextSoloTablet)return;nextSoloTablet=Time.unscaledTime+.1f;
            var s=soloMission;RecordTablet(ref s,actual,facing,true,view);PublishCrew(s);
        }
        void TickSoloAssistant(float dt=-1)
        {
            if(dt<0)dt=Time.deltaTime;
            if(!SoloMissionActive)return;
            if(StationSoloBotTest.RealPlayerCount>=2){soloSession.ReturnToLobby();EndSoloMission();return;}
            if(!soloSession||!soloSession.Bot){soloFailure="Existing bot missing; mission paused";return;}
            var state=soloMission;var bot=soloSession.Bot;var view=StationMissionView.Instance;
            if(SoloBotDriverActive){TickSoloDriver(view);return;}
            if(state.stage==CrewMissionStage.DrivingMission)
            {
                var mission=soloCityMission;
                if(mission)soloTask=mission.HasArrived?"Arrival confirmed":mission.HasFailed?"Mission time expired":"Assistant navigation active · Driver operates emergency pull handle";return;
            }
            if(!view||state.stage<CrewMissionStage.EmergencyAlarm||state.stage>=CrewMissionStage.Departure)return;
            if(state.stage==CrewMissionStage.EmergencyAlarm||state.stage==CrewMissionStage.DispatchReview)
            {
                if(!state.tabletPicked)
                {
                    if(!Goal("Pick up dispatch tablet",view.tablet.position,.45f))return;
                    bot.Face(view.tablet.position,dt);bot.Reach(view.tablet.position,view.tablet.rotation);
                    if(Vector3.Distance(bot.HandPosition,view.tablet.position)<.1f){bot.HeldObject=view.tablet;RecordTablet(ref state,view.tablet.position,view.tablet.rotation,true,view);PublishCrew(state);soloTask="Tablet collected";}return;
                }
                bool present=false;
                if(state.driverChair){present=Goal("Present tablet to seated Driver",view.dispatchSeat.position,.85f);bot.Face(view.dispatchSeat.position,dt);}
                CarrySoloTablet(view,present);return;
            }
            if(state.stage==CrewMissionStage.MissionAccepted){CarrySoloTablet(view,false);return;}
            if(state.stage==CrewMissionStage.DriverInTruck)
            {
                CarrySoloTablet(view,false);
                var button=view.garage.transform.parent.Find("RollupDoorFrame").GetComponent<StationGarageButton>();
                if(!button||!button.upperArrow){soloFailure="Existing green UP button missing";return;}
                if(!state.garageTask)
                {
                    if(!Goal("Operate existing green UP arrow",button.upperArrow.bounds.center,.25f,true))return;
                    bot.Face(button.upperArrow.bounds.center,dt);bot.ReachLeft(button.upperArrow.bounds.center,Quaternion.LookRotation(-button.transform.right));
                    if(Vector3.Distance(button.upperArrow.ClosestPoint(bot.LeftHandPosition),bot.LeftHandPosition)<.04f)
                    {soloBotPress=true;try{button.PressFromAssistantBot(bot);}finally{soloBotPress=false;}}return;
                }
                bot.StopLeftReach();
                if(!state.CanBoardAssistant(SharedTime,garageTravelSeconds)){soloTask="Wait for actual garage opening completion";return;}
                if(!Goal("Board existing passenger seat",view.passengerApproach.position,.15f))return;
                if(!state.CanBoardAssistant(SharedTime,garageTravelSeconds))return;
                bot.Seat(view.passengerSeat);
                view.tablet.SetPositionAndRotation(bot.HandPosition+Vector3.up*.035f,bot.transform.rotation);RecordTablet(ref state,view.tablet.position,view.tablet.rotation,true,view);
                if(bot.IsMissionSeated&&state.TrySeat(StationSeatKind.AssistantTruck,StationRole.Assistant,SharedTime,garageTravelSeconds,departureSeconds))PublishCrew(state);
            }
        }
        IEnumerator LoadSoloCity()
        {
            var fade=StationViewFade.Instance;if(!fade){soloFailure="Existing local fade missing";yield break;}
            localRig.SeatTransition=true;yield return fade.Cover();
            if(!SoloMissionActive)yield break;
            var existing=SceneManager.GetSceneByName(CitySceneName);
            if(existing.isLoaded){soloFailure="City already loaded outside Solo mission";localRig.SeatTransition=false;yield return fade.Reveal();yield break;}
            var load=SceneManager.LoadSceneAsync(CitySceneName,LoadSceneMode.Additive);
            if(load==null){soloFailure="Existing city load failed";localRig.SeatTransition=false;yield return fade.Reveal();yield break;}
            yield return load;
            if(!SoloMissionActive||!soloSession||!soloSession.Bot){var abandoned=SceneManager.GetSceneByName(CitySceneName);if(abandoned.isLoaded)yield return SceneManager.UnloadSceneAsync(abandoned);localRig.SeatTransition=false;yield return fade.Reveal();yield break;}
            SoloCityLoaded=true;
            var city=SceneManager.GetSceneByName(CitySceneName);FireTruckMission mission=null;
            foreach(var root in city.GetRootGameObjects()){mission=root.GetComponentInChildren<FireTruckMission>(true);if(mission)break;}
            if(!mission||!mission.passengerSeat||!mission.driverSeat){soloFailure="Existing city mission/seat missing";localRig.SeatTransition=false;yield return fade.Reveal();yield break;}
            soloCityMission=mission;
            var bot=soloSession.Bot;bot.transform.SetParent(null,true);SceneManager.MoveGameObjectToScene(bot.gameObject,city);bot.gameObject.SetActive(true);bot.Seat(SoloBotDriverActive?mission.driverSeat:mission.passengerSeat);
            var s=soloMission;s.citySeats|=(byte)(SoloBotDriverActive?1:2);PublishCrew(s);
            if(SoloBotDriverActive){var adapter=mission.GetComponent<FireTruckBotDriver>();if(!adapter)adapter=mission.gameObject.AddComponent<FireTruckBotDriver>();adapter.mission=mission;adapter.emergency=mission.GetComponent<FireTruckEmergency>();}
            double deadline=Time.unscaledTimeAsDouble+125;
            while(SoloMissionActive&&Time.unscaledTimeAsDouble<deadline&&(soloMission.citySeats!=3||!mission.controller||!mission.controller.Initialized||!mission.route||mission.route.waypoints.Length<2))yield return null;
            if(!SoloMissionActive){localRig.SeatTransition=false;yield return fade.Reveal();yield break;}
            if(soloMission.citySeats!=3||!mission.controller||!mission.controller.Initialized||!mission.route){soloFailure="City crew/controller readiness timed out";localRig.SeatTransition=false;yield return fade.Reveal();yield break;}
            s=soloMission;s.stage=CrewMissionStage.DrivingMission;PublishCrew(s);localRig.SeatTransition=false;yield return fade.Reveal();
        }
        void TickSoloDriver(StationMissionView view)
        {
            var s=soloMission;var bot=soloSession.Bot;
            if(s.stage==CrewMissionStage.DrivingMission){var ai=soloCityMission?soloCityMission.GetComponent<FireTruckBotDriver>():null;soloTask=ai?ai.Status:"Bot Driver adapter missing";return;}
            if(!view)return;
            if(s.stage<=CrewMissionStage.DispatchReview&&!bot.IsMissionSeated)bot.Seat(view.dispatchSeat);
            if(s.stage==CrewMissionStage.EmergencyAlarm&&bot.IsMissionSeated&&s.TrySeat(StationSeatKind.DispatchChair,StationRole.Driver,SharedTime,garageTravelSeconds,departureSeconds)){PublishCrew(s);soloTask="Seated Driver waiting for human Assistant tablet";return;}
            if(s.stage==CrewMissionStage.DispatchReview){soloTask="Waiting for human tablet pickup and presentation";if(s.CanAccept&&s.TryAccept(StationRole.Driver)){PublishCrew(s);soloTask="Dispatch accepted after tablet presentation";}return;}
            if(s.stage==CrewMissionStage.MissionAccepted){bot.Seat(view.driverSeat);if(bot.IsMissionSeated&&s.TrySeat(StationSeatKind.DriverTruck,StationRole.Driver,SharedTime,garageTravelSeconds,departureSeconds))PublishCrew(s);return;}
            if(s.stage==CrewMissionStage.DriverInTruck)soloTask="Driver seated · waiting for human garage UP and passenger boarding";
        }
    }
}
