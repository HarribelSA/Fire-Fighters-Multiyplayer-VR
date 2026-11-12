using System;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Animations.Rigging;
using UnityEngine.Experimental.Animations;
using UnityEngine.InputSystem;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Movement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace FirefighterVR
{
    [DefaultExecutionOrder(300)]
    public sealed class FirefighterAvatarDriver : MonoBehaviour
    {
        public Animator animator;
        public RigBuilder rigBuilder;
        public Transform placement, playerRoot;
        public Collider groundSurface;
        public Transform headSource, leftSource, rightSource;
        public Transform headTarget, neckTarget, chestTarget, hipsTarget;
        public Transform leftTarget, rightTarget, leftHint, rightHint;
        public Renderer[] gloveRenderers, bodyRenderers, originalLocalVisuals;
        public SkinnedMeshRenderer[] footwear;
        public Vector3 leftWristEuler, rightWristEuler, leftGripOffset, rightGripOffset;
        public Vector3 standingEye, standingHips;
        public Quaternion headAxes, neckAxes, chestAxes;
        public float standingSole;
        public Transform[] standingBones;
        public Quaternion[] standingRotations;
        public Vector3 headBindPosition, neckBindPosition;
        public Vector3 standingNeck, eyeOffset = new Vector3(0,.09f,.08f);
        public Renderer[] localHeadVisuals = Array.Empty<Renderer>();
        public Renderer[] interiorVisuals = Array.Empty<Renderer>();
        public Renderer originalMask;
        public Material[] maskExteriorMaterials, maskInteriorMaterials;
        public ParticleSystem breathingCondensation;
        [Range(0,.03f)] public float FogIntensity=.006f;
        [Min(.05f)] public float FadeInDuration=.2f, FadeOutDuration=.65f;
        [Min(1f)] public float BreathInterval=2.5f;
        public bool EnableBreathingEffect=true;
        float configuredFadeIn=-1, configuredFadeOut=-1;
        float nextBreath;
        public Renderer leftGlove, rightGlove;
        public bool localFirstPerson = true;
        public InputActionReference leftGrip, leftTrigger, rightGrip, rightTrigger, leftMove, rightMove;
        public ContinuousMoveProvider moveProvider;
        public TeleportationProvider teleport;
        public AnimationClip walkClip;
        [Serializable] public class Finger
        {
            public Transform bone;
            public Quaternion openRotation;
            public Quaternion closedRotation;
            public Quaternion gunRotation;
            public bool left, index, thumb;
            public int joint;
        }
        [Serializable] public class Leg
        {
            public TwoBoneIKConstraint constraint;
            public Transform target, hint, toe;
            public SkinnedMeshRenderer shoe;
            public Vector3 restFoot;
            public Quaternion footAxes, toeRotation;
            public float soleOffset;
            [NonSerialized] public Vector3 planted;
            [NonSerialized] public bool swinging, ready;
            [NonSerialized] public float lift;
        }
        public Finger[] fingers = Array.Empty<Finger>();
        public Leg[] legs = Array.Empty<Leg>();
        [Serializable] public struct WalkPose
        {
            public Vector3 leftSole, rightSole;
            public Quaternion leftRotation, rightRotation;
        }
        public WalkPose[] walkPoses = Array.Empty<WalkPose>();
        public float walkCycleDistance = .9f;
        [Range(.1f,1.5f)] public float WalkAnimationSpeed=.65f;
        [Min(.8f)] public float StandingHMDHeight=1.65f;
        [UnityEngine.Serialization.FormerlySerializedAs("CrouchThreshold")]
        [Range(.05f,.3f)] public float CrouchStartThreshold=.15f;
        [Range(.3f,.9f)] public float FullCrouchHeightDrop=.65f;
        [Range(0,40)] public float MaxSpineBend=28;
        [Min(.01f)] public float SpineBendSmoothing=.15f;
        [Min(.1f)] public float CrouchBlendSpeed=8;
        public MultiRotationConstraint spinePosture, chestPosture;
        public Transform spinePostureTarget, chestPostureTarget;
        public Quaternion spinePostureAxes, chestPostureAxes;
        public Transform firstPersonMask;
        public Vector3 firstPersonMaskBaseLocalPosition;
        [Range(-.02f,.08f)] public float FirstPersonMaskDistance=.015f;
        public float crouchAmount { get; private set; }
        public float spineBend { get; private set; }
        Vector3 leanOffset;
        bool crouchActive;
        public bool remoteTracking;
        FirefighterAvatarState remoteState;
        bool remoteStateReady;
        Vector3 postureEyeGap;
        bool postureEyeGapReady;
        public float floorY { get; private set; }
        public float soleHeight { get; private set; }
        public bool poseEvaluated { get; private set; }
        public float movementSpeed { get; private set; }
        public string locomotionState => movementSpeed > .08f ? "Walk" : "Idle";
        public Vector3 limitedHeadAngles { get; private set; }
        public float leftGripValue { get; private set; }
        public float leftTriggerValue { get; private set; }
        public float rightGripValue { get; private set; }
        public float rightTriggerValue { get; private set; }
        Mesh soleMesh;
        readonly System.Collections.Generic.List<Vector3> soleVertices=new System.Collections.Generic.List<Vector3>();
        bool initialized;
        float scale=1, yaw, calibrationUntil, phase, suppressUntil, sampleTime, travelled;
        Vector3 previousRoot, sampleDisplacement, velocity;
        float travelThisFrame;
        bool wasWalking;
        bool calibrationHasTrackedHeight;
        bool standingHeightCalibrated;
        float walkBlend;
        bool[] previousVisualStates;
        AnimatorControllerPlayable locomotionController;
        public string evaluatedLocomotionState => locomotionController.IsValid() && locomotionController.GetCurrentAnimatorStateInfo(0).IsName("Walk") ? "Walk" : "Idle";

        void OnEnable()
        {
            initialized=false;poseEvaluated=false;movementSpeed=0;walkBlend=0;phase=0;wasWalking=false;
            standingHeightCalibrated=false;
            crouchAmount=0;spineBend=0;crouchActive=false;postureEyeGapReady=false;
            previousRoot=playerRoot?playerRoot.position:Vector3.zero;
            sampleTime=Time.unscaledTime;travelled=0;sampleDisplacement=Vector3.zero;
            previousVisualStates=new bool[originalLocalVisuals.Length];
            for(int i=0;i<originalLocalVisuals.Length;i++)if(originalLocalVisuals[i])previousVisualStates[i]=originalLocalVisuals[i].enabled;
            foreach(var leg in legs)leg.ready=false;
            foreach(var r in localHeadVisuals)if(r)r.forceRenderingOff=false;
            if(breathingCondensation){ConfigureBreathing();breathingCondensation.Play();}
            RenderPipelineManager.beginCameraRendering+=BeginAvatarCamera;
            RenderPipelineManager.endCameraRendering+=EndAvatarCamera;
            if(teleport)teleport.locomotionStarted+=OnTeleport;
        }
        void OnTeleport(LocomotionProvider provider) { ResetMovement(); }
        void ResetMovement()
        {
            suppressUntil=Time.unscaledTime+.4f;sampleTime=Time.unscaledTime;
            travelled=0;sampleDisplacement=Vector3.zero;movementSpeed=0;velocity=Vector3.zero;
            previousRoot=playerRoot.position;
            foreach(var leg in legs)leg.ready=false;
        }
        static float Read(InputActionReference action) => action && action.action.enabled ? Mathf.Clamp01(action.action.ReadValue<float>()) : 0;
        public bool trackingReady => poseEvaluated&&calibrationHasTrackedHeight&&Time.unscaledTime>calibrationUntil;
        public float trackingOriginY => remoteTracking?remoteState.OriginY:
            (headSource&&headSource.parent?headSource.parent.position.y:playerRoot.position.y);
        float TrackingHeight() => remoteTracking?headSource.position.y-remoteState.OriginY:
            (headSource.parent?headSource.parent.InverseTransformPoint(headSource.position).y:
            playerRoot.InverseTransformPoint(headSource.position).y);
        public void SetRemoteState(FirefighterAvatarState state)
        {remoteState=state;remoteStateReady=true;StandingHMDHeight=state.StandingHeight;}
        [ContextMenu("Calibrate standing HMD height")]
        public void CalibrateStandingHeight()
        {
            if(remoteTracking||!headSource||!playerRoot)return;
            float height=TrackingHeight();
            if(height<.8f||height>2.4f)return;
            StandingHMDHeight=height;standingHeightCalibrated=true;crouchActive=false;crouchAmount=0;spineBend=0;
        }
        void SampleMovement(float dt)
        {
            if(remoteTracking)
            {movementSpeed=Mathf.Lerp(movementSpeed,remoteState.Speed,1-Mathf.Exp(-dt/.1f));return;}
            var delta=Vector3.ProjectOnPlane(playerRoot.position-previousRoot,Vector3.up);
            travelThisFrame=0;
            previousRoot=playerRoot.position;
            if(delta.magnitude>.5f) { ResetMovement();return; }
            bool input=(leftMove&&leftMove.action.enabled&&leftMove.action.ReadValue<Vector2>().sqrMagnitude>.01f)||
                       (rightMove&&rightMove.action.enabled&&rightMove.action.ReadValue<Vector2>().sqrMagnitude>.01f);
            if(!input||!moveProvider||!moveProvider.isActiveAndEnabled||Time.unscaledTime<suppressUntil)
            {
                sampleTime=Time.unscaledTime;travelled=0;sampleDisplacement=Vector3.zero;
                movementSpeed=Mathf.Lerp(movementSpeed,0,1-Mathf.Exp(-dt/ .10f));return;
            }
            travelled+=delta.magnitude;sampleDisplacement+=delta;
            travelThisFrame=delta.magnitude;
            float elapsed=Time.unscaledTime-sampleTime;
            if(elapsed>=.08f)
            {
                velocity=sampleDisplacement/elapsed;
                movementSpeed=Mathf.Lerp(movementSpeed,Mathf.Clamp(travelled/elapsed,0,3),.65f);
                sampleTime=Time.unscaledTime;travelled=0;sampleDisplacement=Vector3.zero;
            }
        }
        void LateUpdate()
        {
            if(!headSource||!leftSource||!rightSource||!playerRoot||(!groundSurface&&!remoteTracking)||(remoteTracking&&!remoteStateReady))return;
            foreach(var r in bodyRenderers)if(r)r.enabled=false;
            foreach(var r in gloveRenderers)if(r)r.enabled=true;
            foreach(var r in originalLocalVisuals)if(r)r.enabled=false;
            float dt=Mathf.Min(Time.unscaledDeltaTime,.05f);
            floorY=remoteTracking?remoteState.FloorY:groundSurface.bounds.max.y;
            if(!remoteTracking&&groundSurface.Raycast(new Ray(headSource.position+Vector3.up,Vector3.down),out var hit,20))floorY=hit.point.y;
            if(!initialized)
            {
                yaw=headSource.eulerAngles.y;calibrationUntil=Time.unscaledTime+(remoteTracking?0:2);
                calibrationHasTrackedHeight=remoteTracking||headSource.localPosition.y>.2f;
                initialized=true;ResetMovement();
            }
            // The simulator may deliver its first positional event after the initial window.
            // Recalibrate once when that first valid eye-height sample arrives, not on crouches.
            if(!remoteTracking&&!calibrationHasTrackedHeight&&headSource.localPosition.y>.2f)
            {calibrationHasTrackedHeight=true;calibrationUntil=Mathf.Max(calibrationUntil,Time.unscaledTime+1);}
            if(remoteTracking)scale=remoteState.AvatarScale;
            else if(Time.unscaledTime<=calibrationUntil)
                scale=Mathf.Clamp((headSource.position.y-playerRoot.position.y)/(standingEye.y-standingSole),.75f,1.25f);
            // Calibrate once after tracking settles. A fixed 1.65 m default otherwise treats
            // shorter standing users as permanently crouched. Later height drops never recalibrate.
            if(!remoteTracking&&!standingHeightCalibrated&&calibrationHasTrackedHeight&&Time.unscaledTime>calibrationUntil)
                CalibrateStandingHeight();
            SampleMovement(dt);
            placement.localScale=Vector3.one*scale;
            float headYaw=headSource.eulerAngles.y;
            float headTurn=Mathf.DeltaAngle(yaw,headYaw);
            float desiredYaw=yaw;
            if(remoteTracking)desiredYaw=remoteState.BodyYaw;
            else if(Mathf.Abs(headTurn)>55)desiredYaw=headYaw-Mathf.Sign(headTurn)*35;
            else if(movementSpeed>.08f&&Mathf.Abs(Mathf.DeltaAngle(playerRoot.eulerAngles.y,headYaw))<45)desiredYaw=playerRoot.eulerAngles.y;
            yaw=Mathf.MoveTowardsAngle(yaw,desiredYaw,(remoteTracking?360:120)*dt);
            var rotation=Quaternion.Euler(0,yaw,0);
            var angles=HeadAngles(rotation);
            UpdatePosture(dt,rotation);
            var neckRotation=rotation*Quaternion.Euler(angles.x*.35f,angles.y*.4f,angles.z*.25f)*neckAxes;
            var anatomicalEyeOffset=rotation*standingNeck*scale+neckRotation*headBindPosition*scale+headSource.rotation*eyeOffset*scale;
            var position=new Vector3(headSource.position.x-anatomicalEyeOffset.x,floorY-standingSole*scale,headSource.position.z-anatomicalEyeOffset.z);
            placement.SetPositionAndRotation(position,rotation);
            headTarget.SetPositionAndRotation(headSource.position,headSource.rotation);
            chestTarget.rotation=rotation*Quaternion.Euler(spineBend,Mathf.Clamp(angles.y*.15f,-10,10),0)*chestAxes;
            hipsTarget.position=transform.TransformPoint(standingHips);
            // Counter the spine arc at the pelvis; the tracked camera and rig root never move.
            float bend=spineBend*Mathf.Deg2Rad;
            float arcForward=(Mathf.Sin(bend*.35f)*.15f+Mathf.Sin(bend*.7f)*.16f+Mathf.Sin(bend)*.26f)*scale;
            float arcDrop=((1-Mathf.Cos(bend*.35f))*.15f+(1-Mathf.Cos(bend*.7f))*.16f+(1-Mathf.Cos(bend))*.26f)*scale;
            hipsTarget.position+=Vector3.up*(-crouchAmount+arcDrop)-rotation*Vector3.forward*arcForward;
            leftTarget.SetPositionAndRotation(leftSource.TransformPoint(leftGripOffset),leftSource.rotation*Quaternion.Euler(leftWristEuler));
            rightTarget.SetPositionAndRotation(rightSource.TransformPoint(rightGripOffset),rightSource.rotation*Quaternion.Euler(rightWristEuler));
            leftHint.position=transform.TransformPoint(new Vector3(-.4f,1,-.28f));
            rightHint.position=transform.TransformPoint(new Vector3(.4f,1,-.28f));
            if(!rigBuilder.graph.IsValid()&&!rigBuilder.Build())return;
            rigBuilder.graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            if(animator.runtimeAnimatorController&&!locomotionController.IsValid())
            {
                locomotionController=AnimatorControllerPlayable.Create(rigBuilder.graph,animator.runtimeAnimatorController);
                var output=AnimationPlayableOutput.Create(rigBuilder.graph,"Firefighter Idle Walk",animator);
                output.SetSourcePlayable(locomotionController);output.SetAnimationStreamSource(AnimationStreamSource.DefaultValues);output.SetSortingOrder(0);
            }
            bool walking=movementSpeed>.08f;
            walkBlend=Mathf.MoveTowards(walkBlend,walking?1:0,dt/.2f);
            float playback=Mathf.Max(.05f,movementSpeed/(walkCycleDistance*scale))*WalkAnimationSpeed;
            if(locomotionController.IsValid())
            {
                locomotionController.SetFloat("Speed",movementSpeed);
                locomotionController.SetFloat("PlaybackSpeed",playback);
                animator.SetFloat("Speed",movementSpeed);animator.SetFloat("PlaybackSpeed",playback);
                var state=locomotionController.IsInTransition(0)?locomotionController.GetNextAnimatorStateInfo(0):locomotionController.GetCurrentAnimatorStateInfo(0);
                phase=Mathf.Repeat(state.normalizedTime,1);
            }
            wasWalking=walking;
            SetFeet(dt);
            if(walkBlend<.01f)FitPelvisToFeet();
            EvaluatePose(dt);
            for(int iteration=0;iteration<2;iteration++)
            {
                bool adjusted=false;
                foreach(var leg in legs)
                {
                    float measured=MeasureSole(leg.shoe);
                    // Keep native horizontal stepping; ground the stance foot without replacing the swing.
                    if(leg.constraint.weight<.05f)
                    {
                        if(leg.swinging&&measured>=floorY-.003f)continue;
                        leg.target.SetPositionAndRotation(leg.constraint.data.tip.position+Vector3.up*(floorY-measured+.001f),leg.constraint.data.tip.rotation);
                        leg.constraint.weight=1;
                        adjusted=true;
                        continue;
                    }
                    float correction=floorY+leg.lift-measured;
                    if(Mathf.Abs(correction)>.001f)
                    { leg.target.position+=Vector3.up*Mathf.Clamp(correction,-.08f,.08f);adjusted=true; }
                }
                if(!adjusted)break;
                if(walkBlend<.01f)FitPelvisToFeet();
                EvaluatePose();
            }
            soleHeight=MeasureSoles();
            ApplyHead();
            var anatomicalEye=animator.GetBoneTransform(HumanBodyBones.Head).position+headSource.rotation*eyeOffset*scale;
            if(!postureEyeGapReady&&Time.unscaledTime>calibrationUntil&&crouchAmount<.01f&&Mathf.Abs(angles.x)<10)
            {postureEyeGap=Quaternion.Inverse(rotation)*(anatomicalEye-headSource.position)/scale;postureEyeGapReady=true;}
            if(postureEyeGapReady&&crouchAmount>.02f)
            {
                float correctionWeight=Mathf.SmoothStep(0,1,Mathf.Clamp01(crouchAmount/.12f));
                hipsTarget.position+=Vector3.ClampMagnitude(headSource.position+rotation*postureEyeGap*scale-anatomicalEye,.1f*scale)*correctionWeight;
                EvaluatePose();ApplyHead();
            }
            ApplyFingers(dt);
            if(breathingCondensation&&!EnableBreathingEffect)breathingCondensation.Clear();
            if(breathingCondensation&&EnableBreathingEffect&&breathingCondensation.gameObject.activeInHierarchy&&Time.time>=nextBreath)
            {
                ConfigureBreathing();
                nextBreath=Time.time+Mathf.Max(1,BreathInterval);
                var puff=new ParticleSystem.EmitParams{startLifetime=Mathf.Max(.1f,FadeInDuration+FadeOutDuration),startColor=new Color(1,1,1,FogIntensity)};
                breathingCondensation.Emit(puff,1);
            }
            if(leftGlove)leftGlove.forceRenderingOff=localFirstPerson&&Vector3.Distance(leftTarget.position,headSource.position)<.12f;
            if(rightGlove)rightGlove.forceRenderingOff=localFirstPerson&&Vector3.Distance(rightTarget.position,headSource.position)<.12f;
            poseEvaluated=true;
        }
        WalkPose SampleWalk()
        {
            if(walkPoses.Length<2)return default;
            float sample=phase*(walkPoses.Length-1);int first=Mathf.Min(walkPoses.Length-2,Mathf.FloorToInt(sample));
            var a=walkPoses[first];var b=walkPoses[first+1];float t=sample-first;
            return new WalkPose{leftSole=Vector3.Lerp(a.leftSole,b.leftSole,t),rightSole=Vector3.Lerp(a.rightSole,b.rightSole,t),leftRotation=Quaternion.Slerp(a.leftRotation,b.leftRotation,t),rightRotation=Quaternion.Slerp(a.rightRotation,b.rightRotation,t)};
        }
        void UpdatePosture(float dt,Quaternion rotation)
        {
            if(firstPersonMask)firstPersonMask.localPosition=firstPersonMaskBaseLocalPosition+Vector3.forward*FirstPersonMaskDistance;
            float height=TrackingHeight();
            float deficit=Mathf.Max(0,StandingHMDHeight-height);
            if(crouchActive){if(deficit<CrouchStartThreshold-.025f)crouchActive=false;}
            else if(deficit>CrouchStartThreshold)crouchActive=true;
            float onset=Mathf.SmoothStep(0,1,Mathf.InverseLerp(CrouchStartThreshold,CrouchStartThreshold+.1f,deficit));
            float drop=crouchActive?Mathf.Min(FullCrouchHeightDrop,deficit)*onset:0;
            float extent=Mathf.SmoothStep(0,1,Mathf.InverseLerp(CrouchStartThreshold,FullCrouchHeightDrop,deficit));
            float targetBend=crouchActive?MaxSpineBend*extent*.6f:0;
            if(Time.unscaledTime<calibrationUntil){drop=0;targetBend=0;}
            crouchAmount=Mathf.Lerp(crouchAmount,drop,1-Mathf.Exp(-dt*CrouchBlendSpeed));
            spineBend=Mathf.Lerp(spineBend,targetBend,1-Mathf.Exp(-dt/Mathf.Max(.01f,SpineBendSmoothing)));
            if(!crouchActive&&crouchAmount<.0001f&&spineBend<.001f){crouchAmount=0;spineBend=0;}
            leanOffset=Vector3.zero;
            // Keep the upright reference active at zero crouch; weight zero exposes the
            // standing clip's baked forward spine pose instead of restoring an upright torso.
            const float weight=1;
            if(spinePosture&&spinePostureTarget)
            {spinePosture.weight=weight;spinePostureTarget.rotation=rotation*Quaternion.Euler(spineBend*.35f,0,0)*spinePostureAxes;}
            if(chestPosture&&chestPostureTarget)
            {chestPosture.weight=weight;chestPostureTarget.rotation=rotation*Quaternion.Euler(spineBend*.7f,0,0)*chestPostureAxes;}
        }
        void SetFeet(float dt)
        {
            var sample=SampleWalk();
            for(int i=0;i<legs.Length;i++)
            {
                var leg=legs[i];var nominal=transform.TransformPoint(leg.restFoot)-leanOffset;nominal.y=floorY;
                if(!leg.ready){leg.planted=nominal;leg.swinging=false;leg.ready=true;}
                Vector3 sole=leg.planted;leg.lift=0;
                if(walkBlend<.01f||walkPoses.Length<2)
                {
                    leg.planted=Vector3.Lerp(leg.planted,nominal,1-Mathf.Exp(-dt/.12f));sole=leg.planted;leg.swinging=false;
                }
                else
                {
                    var authored=i==0?sample.leftSole:sample.rightSole;
                    var restrained=Vector3.Lerp(leg.restFoot,authored,.55f);
                    var authoredWorld=transform.TransformPoint(restrained);authoredWorld.y=floorY;
                    bool swing=authored.y>.012f;
                    if(swing)
                    {
                        sole=authoredWorld;leg.lift=Mathf.Min(.055f,Mathf.Max(0,authored.y-.006f)*1.2f)*scale;leg.swinging=true;
                    }
                    else if(leg.swinging){leg.planted=authoredWorld;leg.swinging=false;sole=leg.planted;}
                    sole=Vector3.Lerp(nominal,sole,walkBlend);leg.lift*=walkBlend;
                }
                sole.y=floorY+leg.lift;
                var footRotation=walkPoses.Length>1?(i==0?sample.leftRotation:sample.rightRotation):leg.footAxes;
                leg.target.SetPositionAndRotation(sole+Vector3.up*leg.soleOffset*scale,placement.rotation*Quaternion.Slerp(leg.footAxes,footRotation,walkBlend*.25f));
                leg.constraint.weight=Mathf.Max(1-walkBlend,Mathf.Clamp01(crouchAmount/.15f));
                var side=i==0?-.10f:.10f;
                leg.hint.position=transform.TransformPoint(new Vector3(side,.45f,.5f));
            }
        }
        void EvaluatePose(float deltaTime=0)
        {
            rigBuilder.Evaluate(deltaTime);
            foreach(var leg in legs)if(leg.toe)leg.toe.localRotation=Quaternion.Slerp(leg.toeRotation,leg.toe.localRotation,walkBlend);
        }
        void FitPelvisToFeet()
        {
            var hips=animator.GetBoneTransform(HumanBodyBones.Hips);
            float height=hipsTarget.position.y;
            foreach(var leg in legs)
            {
                var data=leg.constraint.data;
                var offset=data.root.position-hips.position;
                var hip=hipsTarget.position+offset;
                float length=(Vector3.Distance(data.root.position,data.mid.position)+Vector3.Distance(data.mid.position,data.tip.position))*.985f;
                float horizontal=Vector3.ProjectOnPlane(leg.target.position-hip,Vector3.up).sqrMagnitude;
                float vertical=Mathf.Sqrt(Mathf.Max(.01f,length*length-horizontal));
                height=Mathf.Min(height,leg.target.position.y+vertical-offset.y);
            }
            float minimumHeight=Mathf.Lerp(.5f,.36f,Mathf.Clamp01(crouchAmount/(.45f*scale)))*scale;
            var position=hipsTarget.position;position.y=Mathf.Max(floorY+minimumHeight,height);hipsTarget.position=position;
        }
        static float Signed(float angle)=>Mathf.DeltaAngle(0,angle);
        Vector3 HeadAngles(Quaternion body)
        {
            var relative=(Quaternion.Inverse(body)*headSource.rotation).eulerAngles;
            return new Vector3(Mathf.Clamp(Signed(relative.x),-55,65),Mathf.Clamp(Signed(relative.y),-70,70),Mathf.Clamp(Signed(relative.z),-25,25));
        }
        void ApplyHead()
        {
            var angles=HeadAngles(placement.rotation);var pitch=angles.x;var turn=angles.y;var roll=angles.z;
            limitedHeadAngles=angles;
            var neck=animator.GetBoneTransform(HumanBodyBones.Neck);
            var head=animator.GetBoneTransform(HumanBodyBones.Head);
            neck.localPosition=neckBindPosition;head.localPosition=headBindPosition;
            var neckWorld=placement.rotation*Quaternion.Euler(pitch*.35f,turn*.4f,roll*.25f)*neckAxes;
            neck.localRotation=Quaternion.Inverse(neck.parent.rotation)*neckWorld;
            var headWorld=placement.rotation*Quaternion.Euler(pitch,turn,roll)*headAxes;
            head.localRotation=Quaternion.Inverse(head.parent.rotation)*headWorld;
            neckTarget.SetPositionAndRotation(neck.position,neck.rotation);
            headTarget.SetPositionAndRotation(headSource.position,headSource.rotation);
        }
        void ApplyFingers(float dt)
        {
            float blend=1-Mathf.Exp(-dt/.07f);
            leftGripValue=Mathf.Lerp(leftGripValue,remoteTracking?remoteState.LeftGrip/255f:Read(leftGrip),blend);leftTriggerValue=Mathf.Lerp(leftTriggerValue,remoteTracking?remoteState.LeftTrigger/255f:Read(leftTrigger),blend);
            rightGripValue=Mathf.Lerp(rightGripValue,remoteTracking?remoteState.RightGrip/255f:Read(rightGrip),blend);rightTriggerValue=Mathf.Lerp(rightTriggerValue,remoteTracking?remoteState.RightTrigger/255f:Read(rightTrigger),blend);
            foreach(var finger in fingers)
            {
                float grip=finger.left?leftGripValue:rightGripValue,trigger=finger.left?leftTriggerValue:rightTriggerValue;
                if(finger.thumb)
                {
                    var openOrGun=Quaternion.Slerp(finger.openRotation,finger.gunRotation,trigger*(1-grip));
                    finger.bone.localRotation=Quaternion.Slerp(openOrGun,finger.closedRotation,grip);
                }
                else
                {
                    float curl=finger.index?grip:Mathf.Max(grip,trigger);
                    finger.bone.localRotation=Quaternion.Slerp(finger.openRotation,finger.closedRotation,curl);
                }
            }
        }
        void ConfigureBreathing()
        {
            if(!breathingCondensation||configuredFadeIn==FadeInDuration&&configuredFadeOut==FadeOutDuration)return;
            configuredFadeIn=FadeInDuration;configuredFadeOut=FadeOutDuration;
            var gradient=new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white,0),new GradientColorKey(Color.white,1)},
                new[]{new GradientAlphaKey(0,0),new GradientAlphaKey(1,Mathf.Clamp01(FadeInDuration/Mathf.Max(.1f,FadeInDuration+FadeOutDuration))),new GradientAlphaKey(0,1)});
            var color=breathingCondensation.colorOverLifetime;color.enabled=true;color.color=gradient;
        }
        void BeginAvatarCamera(ScriptableRenderContext context,Camera camera)
        {
            bool firstPerson=localFirstPerson&&headSource&&camera.transform==headSource;
            
            foreach(var r in localHeadVisuals)if(r)r.forceRenderingOff=firstPerson;
            foreach(var r in interiorVisuals)if(r){r.forceRenderingOff=false;r.enabled=firstPerson;}
        }
        void EndAvatarCamera(ScriptableRenderContext context,Camera camera)
        {
            foreach(var r in localHeadVisuals)if(r)r.forceRenderingOff=false;
            foreach(var r in interiorVisuals)if(r){r.enabled=false;r.forceRenderingOff=false;}
        }
        float MeasureSole(SkinnedMeshRenderer shoe)
        {
            if(!soleMesh)soleMesh=new Mesh{name="FirefighterSoleMeasurement"};
            shoe.BakeMesh(soleMesh);float min=float.PositiveInfinity;
            soleMesh.GetVertices(soleVertices);
            foreach(var v in soleVertices)min=Mathf.Min(min,shoe.transform.TransformPoint(v).y);
            return min;
        }
        float MeasureSoles(){float min=float.PositiveInfinity;foreach(var shoe in footwear)min=Mathf.Min(min,MeasureSole(shoe));return min;}
        void OnDisable()
        {
            RenderPipelineManager.beginCameraRendering-=BeginAvatarCamera;
            RenderPipelineManager.endCameraRendering-=EndAvatarCamera;
            foreach(var r in localHeadVisuals)if(r)r.forceRenderingOff=false;
            foreach(var r in interiorVisuals)if(r){r.enabled=false;r.forceRenderingOff=false;}
            if(teleport)teleport.locomotionStarted-=OnTeleport;
            if(previousVisualStates==null)return;
            for(int i=0;i<originalLocalVisuals.Length;i++)if(originalLocalVisuals[i])originalLocalVisuals[i].enabled=previousVisualStates[i];
        }
        void OnDestroy(){if(soleMesh)Destroy(soleMesh);}
    }
}
