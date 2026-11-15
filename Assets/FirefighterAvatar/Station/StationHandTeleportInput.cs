using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem.XR;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Inputs;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.XRHandLocomotion;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FirefighterVR.Station
{
    // Station input adapter for the existing right-hand teleport ray, not a second
    // hand rig, gesture stack, interactor, or teleportation provider.
    [DefaultExecutionOrder(-110)]
    public sealed class StationHandTeleportInput : MonoBehaviour
    {
        public StationLocalRig rig;
        public XRRayInteractor ray;
        public NearFarInteractor handInteraction;
        public TeleportHandGesture sampleGesture;
        public JoystickPinchHandGesture sampleRotation;
        public TrackedPoseDriver rayPose;
        public float activationHold=.08f,pinchClose=.022f,pinchRelease=.045f;
        [Min(.1f)] public float destinationGrace=.35f;
        public bool Aiming {get;private set;}
        string gestureStatus="Inactive";
        public string GestureStatus {get=>gestureStatus;private set{if(gestureStatus==value)return;gestureStatus=value;Debug.Log("[StationHandTeleport] "+value+" rayActive="+(ray&&ray.gameObject.activeInHierarchy),this);}}
        [SerializeField,TextArea] string runtimeHandDiagnostics;
        FirefighterVR.StationBot.StationSoloBotTest solo;
        TeleportationProvider provider;
        Vector3 beforeTeleport,afterTeleport;
        float correctionCheck=-1;
        bool lastPointing,lastValid;
        float nextHandDiagnostic;
        bool providerMovePending;
        BaseTeleportationInteractable observedTarget;
        void NativeSelected(SelectEnterEventArgs args)
        {if(observedTarget)observedTarget.teleporting.RemoveListener(NativeRequest);observedTarget=args.interactableObject as BaseTeleportationInteractable;if(observedTarget)observedTarget.teleporting.AddListener(NativeRequest);}
        void NativeRequest(TeleportingEventArgs args){if(args.interactorObject==ray)Debug.Log("[StationHandTeleport] Native teleport request submitted: "+args.teleportRequest.destinationPosition,this);}
        void Awake(){solo=FindFirstObjectByType<FirefighterVR.StationBot.StationSoloBotTest>(FindObjectsInactive.Include);provider=rig.origin.GetComponentInChildren<TeleportationProvider>(true);if(provider){provider.locomotionStateChanged+=ProviderState;provider.locomotionEnded+=ProviderEnded;}}
        void ProviderState(LocomotionProvider p,LocomotionState state){if(state==LocomotionState.Preparing&&Aiming){providerMovePending=true;beforeTeleport=rig.origin.transform.position;correctionCheck=-1;Debug.Log("[StationHandTeleport] Existing provider accepted request; originBefore="+beforeTeleport,this);}}
        void ProviderEnded(LocomotionProvider p){if(!providerMovePending)return;providerMovePending=false;afterTeleport=rig.origin.transform.position;correctionCheck=Time.unscaledTime+.75f;Debug.Log("[StationHandTeleport] Provider movement ended; originAfter="+afterTeleport+" displacement="+Vector3.Distance(beforeTeleport,afterTeleport),this);}
        void OnDestroy(){if(provider){provider.locomotionStateChanged-=ProviderState;provider.locomotionEnded-=ProviderEnded;}}
        readonly List<XRHandSubsystem> systems=new List<XRHandSubsystem>();
        XRHandSubsystem hands;
        XRInputButtonReader.InputSourceMode selectMode;
        bool configured,gestureWasEnabled,rotationWasEnabled,interactionWasEnabled,poseWasEnabled,pinched,confirming;
        float held;
        int releaseFrame;
        int pressFrame;
        float validUntil;
        Pose validAim;
        void Configure()
        {
            configured=true;selectMode=ray.selectInput.inputSourceMode;
            ray.selectEntered.AddListener(NativeSelected);
            gestureWasEnabled=sampleGesture&&sampleGesture.enabled;rotationWasEnabled=sampleRotation&&sampleRotation.enabled;
            interactionWasEnabled=handInteraction&&handInteraction.enabled;poseWasEnabled=rayPose&&rayPose.enabled;
            if(sampleGesture){sampleGesture.StopAllCoroutines();sampleGesture.enabled=false;}
            if(sampleRotation){sampleRotation.StopAllCoroutines();sampleRotation.enabled=false;}
            ray.selectInput.inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue;
            ray.gameObject.SetActive(false);
        }
        bool StationActive=>rig&&StationSceneSetup.Instance&&UnityEngine.SceneManagement.SceneManager.GetActiveScene()==StationSceneSetup.Instance.gameObject.scene;
        void Update()
        {
            if(rig&&rig.permanentlySeatedDriver&&(rig.LocalRole==StationRole.Driver||(solo&&solo.IsStationTest&&solo.HumanRole==StationRole.Driver)))
            {
                if(configured)Restore();
                if(sampleGesture){sampleGesture.StopAllCoroutines();sampleGesture.enabled=false;}if(sampleRotation){sampleRotation.StopAllCoroutines();sampleRotation.enabled=false;}
                if(ray)ray.gameObject.SetActive(false);GestureStatus="Seated Driver profile · hand teleport activation disabled";return;
            }
            // Use this right-hand rig's actual activation, not the global two-hand
            // modality value. An occluded left hand must not disable this adapter.
            if(correctionCheck>0&&Time.unscaledTime>=correctionCheck){correctionCheck=-1;if(Vector3.Distance(afterTeleport,rig.origin.transform.position)>.4f)Debug.LogWarning("[StationHandTeleport] Post-teleport root displacement="+(rig.origin.transform.position-afterTeleport)+". Tracking/network/other locomotion cause not established.",this);}
            bool mode=StationActive&&(rig.LocalRole==StationRole.Driver||(solo&&solo.IsStationTest&&solo.HumanRole==StationRole.Driver));
            if(!mode){if(configured)Restore();GestureStatus=StationActive?"Not the local hand-tracking Driver":"Waiting for station";return;}
            if(!configured)Configure();
            if(provider&&!provider.enabled&&!confirming){Cancel();GestureStatus="Existing teleport provider is locomotion-locked";return;}
            if(rig.StationSeated||rig.SeatTransition||(StationViewFade.Instance&&StationViewFade.Instance.SceneBusy)){Cancel();GestureStatus="Seated / transition locomotion lock";return;}
            if(hands==null||!hands.running){systems.Clear();SubsystemManager.GetSubsystems(systems);hands=systems.Find(s=>s.running);}
            if(hands==null||!hands.rightHand.isTracked){Cancel();GestureStatus="Right hand not tracked";return;}
            var hand=hands.rightHand;
            if(!Pose(hand,XRHandJointID.Wrist,out var wrist)||!Pose(hand,XRHandJointID.IndexProximal,out var index)||!Pose(hand,XRHandJointID.IndexTip,out var indexTip)||!Pose(hand,XRHandJointID.ThumbTip,out var thumbTip)){Cancel();GestureStatus="Tracked hand missing required joint poses";return;}
            var tracking=rig.origin.CameraFloorOffsetObject.transform;
            float distance=Vector3.Distance(indexTip.position,thumbTip.position);
            bool blocked=handInteraction&&(handInteraction.hasSelection||(handInteraction.TryGetCurrentUIRaycastResult(out _)&&handInteraction.uiPressInput.ReadIsPerformed()));
            bool pointing=Extended(wrist.position,index.position,indexTip.position)&&Curled(hand,wrist.position,XRHandJointID.MiddleProximal,XRHandJointID.MiddleTip)&&Curled(hand,wrist.position,XRHandJointID.RingProximal,XRHandJointID.RingTip)&&Curled(hand,wrist.position,XRHandJointID.LittleProximal,XRHandJointID.LittleTip);
            if(lastPointing!=pointing){lastPointing=pointing;Debug.Log("[StationHandTeleport] rightTracked=True indexOnlyPointing="+pointing,this);}
            if(Time.unscaledTime>=nextHandDiagnostic){nextHandDiagnostic=Time.unscaledTime+.5f;runtimeHandDiagnostics="rightTracked="+hand.isTracked+" indexExtended="+Extended(wrist.position,index.position,indexTip.position)+" indexOnly="+pointing+" pinchDistance="+distance.ToString("F3")+" indexTip="+indexTip.position+" blocked="+blocked+" stage="+GestureStatus+" ray="+ray.gameObject.activeInHierarchy+" provider="+(provider&&provider.enabled);}
            if(!Aiming)
            {
                held=pointing&&distance>pinchRelease&&!blocked?held+Time.unscaledDeltaTime:0;
                GestureStatus=blocked?"Grab / active UI press has priority":"Point right index finger; curl other fingers";
                if(held<activationHold)return;
                Aiming=true;pinched=false;confirming=false;validUntil=0;releaseFrame=-1;
                if(handInteraction)handInteraction.enabled=false;if(rayPose)rayPose.enabled=false;
                ray.gameObject.SetActive(true);
            }
            if(ray.TryGetCurrentUIRaycastResult(out _)){Cancel();GestureStatus="UI interaction has priority";return;}
            // Cache only a native valid hover. Keep its ray pose while the index
            // curls into a pinch, so native OnSelectExit sees the same destination.
            if(!pinched&&!confirming&&pointing&&ray.TryGetHitInfo(out _,out _,out _,out bool valid)&&valid)
            {validAim=new Pose(ray.transform.position,ray.transform.rotation);validUntil=Time.unscaledTime+destinationGrace;}
            bool hasValid=Time.unscaledTime<=validUntil&&validUntil>0;if(lastValid!=hasValid){lastValid=hasValid;Debug.Log("[StationHandTeleport] nativeValidDestination="+hasValid,this);}
            if(confirming)
            {
                ray.transform.SetPositionAndRotation(validAim.position,validAim.rotation);
                if(releaseFrame<0)
                {
                    if(Time.frameCount>pressFrame&&(ray.hasSelection||Time.frameCount>pressFrame+3))
                    {if(!ray.hasSelection)Debug.LogWarning("[StationHandTeleport] Native selection rejected or unavailable; providerEnabled="+(provider&&provider.enabled),this);ray.selectInput.QueueManualState(false,0);releaseFrame=Time.frameCount;}
                }
                else if(Time.frameCount>releaseFrame+2&&!ray.hasSelection)Cancel();
                return;
            }
            if(distance<pinchClose&&Time.unscaledTime<=validUntil)
            {
                ray.transform.SetPositionAndRotation(validAim.position,validAim.rotation);
                pinched=true;confirming=true;pressFrame=Time.frameCount;
                ray.selectInput.QueueManualState(true,1);GestureStatus="Pinch confirmed · native teleport pending";return;
            }
            if(!pointing)
            {
                if(Time.unscaledTime>validUntil){Cancel();return;}
                ray.transform.SetPositionAndRotation(validAim.position,validAim.rotation);return;
            }
            var forward=tracking.TransformDirection(indexTip.position-index.position).normalized;
            if(forward.sqrMagnitude>.5f)ray.transform.SetPositionAndRotation(tracking.TransformPoint(index.position),Quaternion.LookRotation(forward,rig.origin.Origin.transform.up));
            GestureStatus=Time.unscaledTime<=validUntil?"Pinch to teleport":"Point at a valid floor or available seat marker";
        }
        public static bool Extended(Vector3 wrist,Vector3 proximal,Vector3 tip)=>Vector3.Distance(wrist,tip)>Vector3.Distance(wrist,proximal)+.025f&&Vector3.Dot((proximal-wrist).normalized,(tip-proximal).normalized)>.65f;
        static bool Curled(XRHand hand,Vector3 wrist,XRHandJointID proximal,XRHandJointID tip)=>Pose(hand,proximal,out var p)&&Pose(hand,tip,out var t)&&!Extended(wrist,p.position,t.position);
        static bool Pose(XRHand hand,XRHandJointID joint,out Pose pose)=>hand.GetJoint(joint).TryGetPose(out pose);
        public void Cancel()
        {
            if(!configured)return;
            if(ray.interactionManager&&ray.hasSelection)ray.interactionManager.CancelInteractorSelection((IXRSelectInteractor)ray);
            ray.selectInput.QueueManualState(false,0);ray.gameObject.SetActive(false);
            if(handInteraction)handInteraction.enabled=interactionWasEnabled;if(rayPose)rayPose.enabled=poseWasEnabled;
            Aiming=false;held=0;pinched=false;confirming=false;
        }
        void Restore()
        {
            Cancel();ray.selectInput.inputSourceMode=selectMode;
            ray.selectEntered.RemoveListener(NativeSelected);if(observedTarget)observedTarget.teleporting.RemoveListener(NativeRequest);observedTarget=null;
            if(sampleGesture)sampleGesture.enabled=gestureWasEnabled;if(sampleRotation)sampleRotation.enabled=rotationWasEnabled;
            configured=false;
        }
        void OnDisable(){if(configured)Restore();}
    }
}
