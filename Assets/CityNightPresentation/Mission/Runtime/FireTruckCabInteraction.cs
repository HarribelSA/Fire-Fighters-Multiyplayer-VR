using System.Collections.Generic;
using FirefighterVR.Station;
using UnityEngine;
using UnityEngine.XR.Hands;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace CityNightPresentation.Mission
{
    // Hand input/visual attachment only. Vehicle steering stays exclusively on G29.
    [DefaultExecutionOrder(290)]
    public sealed class FireTruckCabInteraction : MonoBehaviour
    {
        public FireTruckMission mission;
        public FireTruckWheelController controller;
        public FireTruckEmergency emergency;
        public Transform wheel;
        public Vector3 rimCenter=new Vector3(0,-.05f,0);
        public float rimRadius=.29f,rimTolerance=.065f,blendSeconds=.16f;
        sealed class HandState
        {
            public int attachment,visualType; // 0 free, 1 wheel rim, 2 pull handle
            public bool wasGrab=true,interactorEnabled;
            public float blend,startValue;
            public Vector3 point,offset,startHand;
            public Quaternion rotation;
            public NearFarInteractor interactor;
            public int requested;
            public Transform shoulder,elbow,handBone;
        }
        readonly HandState left=new HandState(),right=new HandState();
        readonly List<XRHandSubsystem> systems=new List<XRHandSubsystem>();
        XRHandSubsystem hands;
        StationLocalRig rig;
        FirefighterVR.FirefighterAvatarDriver avatar;
        public bool LeftAttached=>left.attachment==1;
        public bool RightAttached=>right.attachment==1;
        public bool RightPulling=>right.attachment==2;
        void Start()
        {
            var flow=StationSessionFlow.Instance;rig=flow?flow.localRig:null;avatar=rig?rig.avatar:null;
            if(avatar)avatar.VisualGripReference=mission.transform;
            if(rig){left.interactor=rig.modality.leftHand.GetComponentInChildren<NearFarInteractor>(true);right.interactor=rig.modality.rightHand.GetComponentInChildren<NearFarInteractor>(true);}
            if(avatar&&avatar.animator&&avatar.animator.isHuman)
            {left.shoulder=avatar.animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);left.elbow=avatar.animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);left.handBone=avatar.animator.GetBoneTransform(HumanBodyBones.LeftHand);right.shoulder=avatar.animator.GetBoneTransform(HumanBodyBones.RightUpperArm);right.elbow=avatar.animator.GetBoneTransform(HumanBodyBones.RightLowerArm);right.handBone=avatar.animator.GetBoneTransform(HumanBodyBones.RightHand);}
        }
        public static Vector3 RimPoint(Vector3 point,Vector3 center,float radius)
        {var delta=point-center;delta.y=0;if(delta.sqrMagnitude<.00001f)delta=Vector3.forward;return center+delta.normalized*radius;}
        public static float ProjectPull(Vector3 start,Vector3 now,Vector3 axis,float travel,float initial)=>Mathf.Clamp01(initial+Vector3.Dot(now-start,axis.normalized)/Mathf.Max(.01f,travel));
        static bool Curled(XRHand hand,XRHandJointID proximal,XRHandJointID tip,Vector3 wrist)
        {return hand.GetJoint(proximal).TryGetPose(out var a)&&hand.GetJoint(tip).TryGetPose(out var b)&&Vector3.Dot((a.position-wrist).normalized,(b.position-a.position).normalized)<.45f;}
        public static bool IsGrab(XRHand hand)
        {
            if(!hand.isTracked||!hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var w))return false;
            return Curled(hand,XRHandJointID.IndexProximal,XRHandJointID.IndexTip,w.position)&&Curled(hand,XRHandJointID.MiddleProximal,XRHandJointID.MiddleTip,w.position)&&Curled(hand,XRHandJointID.RingProximal,XRHandJointID.RingTip,w.position)&&Curled(hand,XRHandJointID.LittleProximal,XRHandJointID.LittleTip,w.position);
        }
        void Update()
        {
            bool allowed=mission&&rig&&avatar&&mission.LocalRole==StationRole.Driver&&mission.CrewReady&&!mission.HasArrived&&!mission.HasFailed;
            if(hands==null||!hands.running){systems.Clear();SubsystemManager.GetSubsystems(systems);hands=systems.Find(s=>s.running);}
            Step(left,true,allowed);Step(right,false,allowed);
        }
        void Step(HandState state,bool isLeft,bool allowed)
        {
            var hand=hands!=null?(isLeft?hands.leftHand:hands.rightHand):default;
            bool tracked=hands!=null&&hand.isTracked&&hand.GetJoint(XRHandJointID.Palm).TryGetPose(out _)&&hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out _);
            if(!allowed||!tracked)
            {Release(state);state.wasGrab=true;Blend(state,isLeft);return;}
            hand.GetJoint(XRHandJointID.Palm).TryGetPose(out var palm);hand.GetJoint(XRHandJointID.Wrist).TryGetPose(out var wrist);
            var space=rig.origin.CameraFloorOffsetObject.transform;var position=space.TransformPoint(palm.position);var wristPosition=space.TransformPoint(wrist.position);var wristRotation=space.rotation*wrist.rotation;
            bool grab=IsGrab(hand),closing=grab&&!state.wasGrab;state.wasGrab=grab;
            if(state.attachment!=0&&!grab)Release(state);
            if(state.attachment==0&&closing&&(!state.interactor||!state.interactor.hasSelection)&&(!state.interactor||!(state.interactor.TryGetCurrentUIRaycastResult(out _)&&state.interactor.uiPressInput.ReadIsPerformed())))
            {
                if(!isLeft&&emergency&&emergency.pullHandle&&Vector3.Distance(position,emergency.pullHandle.position)<.11f)
                {
                    Attach(state,2);emergency.BeingPulled=true;state.startHand=emergency.pullMount.InverseTransformPoint(position);state.startValue=emergency.PullValue;state.requested=mission.SirenActive?1:0;
                    state.offset=emergency.pullMount.InverseTransformVector(wristPosition+wristRotation*avatar.rightGripOffset-position);state.rotation=Quaternion.Inverse(emergency.pullMount.rotation)*(wristRotation*Quaternion.Euler(avatar.rightWristEuler));
                }
                else if(wheel)
                {
                    var point=RimPoint(wheel.InverseTransformPoint(position),rimCenter,rimRadius);var contact=wheel.TransformPoint(point);
                    if(Vector3.Distance(position,contact)<=rimTolerance)
                    {
                        Attach(state,1);state.point=point;
                        var target=wristPosition+wristRotation*(isLeft?avatar.leftGripOffset:avatar.rightGripOffset);
                        state.offset=wheel.InverseTransformVector(target-position);state.rotation=Quaternion.Inverse(wheel.rotation)*(wristRotation*Quaternion.Euler(isLeft?avatar.leftWristEuler:avatar.rightWristEuler));
                    }
                }
            }
            if(state.attachment==2)
            {
                float value=ProjectPull(state.startHand,emergency.pullMount.InverseTransformPoint(position),emergency.pullAxis,emergency.pullTravel,state.startValue);
                emergency.SetPullValue(value);
                if(value>=.8f&&state.requested!=1){state.requested=1;if(!emergency.RequestFromPull(true))emergency.SetPullValue(0);}
                else if(value<=.2f&&state.requested!=0){state.requested=0;emergency.RequestFromPull(false);}
            }
            Blend(state,isLeft);
        }
        void Attach(HandState state,int type)
        {state.attachment=state.visualType=type;if(state.interactor){state.interactorEnabled=state.interactor.enabled;state.interactor.enabled=false;}}
        void Release(HandState state)
        {
            if(state.attachment==2&&emergency){emergency.BeingPulled=false;emergency.SetPullValue(mission&&mission.SirenActive?1:0);}
            if(state.attachment!=0&&state.interactor)state.interactor.enabled=state.interactorEnabled;
            state.attachment=0;
        }
        void Blend(HandState state,bool isLeft)
        {
            state.blend=Mathf.MoveTowards(state.blend,state.attachment!=0?1:0,Time.unscaledDeltaTime/Mathf.Max(.05f,blendSeconds));
            if(!avatar)return;
            if(state.attachment==1&&state.shoulder&&state.elbow&&state.handBone)
            {float length=Vector3.Distance(state.shoulder.position,state.elbow.position)+Vector3.Distance(state.elbow.position,state.handBone.position);var target=wheel.TransformPoint(state.point)+wheel.TransformVector(state.offset);if(Vector3.Distance(state.shoulder.position,target)>length+.08f)Release(state);}
            if(state.visualType==2&&emergency&&emergency.pullMount&&emergency.pullHandle)avatar.SetVisualGrip(isLeft,emergency.pullHandle.position+emergency.pullMount.TransformVector(state.offset),emergency.pullMount.rotation*state.rotation,state.blend);
            else if(wheel)avatar.SetVisualGrip(isLeft,wheel.TransformPoint(state.point)+wheel.TransformVector(state.offset),wheel.rotation*state.rotation,state.blend);
        }
        void OnDisable()
        {Release(left);Release(right);left.blend=right.blend=0;if(avatar){avatar.SetVisualGrip(true,Vector3.zero,Quaternion.identity,0);avatar.SetVisualGrip(false,Vector3.zero,Quaternion.identity,0);if(avatar.VisualGripReference==mission.transform)avatar.VisualGripReference=null;}}
    }
}
