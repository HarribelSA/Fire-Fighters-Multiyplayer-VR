using FirefighterVR.Station;
using UnityEngine;

namespace FirefighterVR.StationBot
{
    public sealed class StationBotFollower : MonoBehaviour
    {
        public Animator animator;
        public StationRole HumanRole { get; private set; }
        public StationRole Role { get; private set; }
        public bool FutureDrivingEligible => Role == StationRole.Driver;
        public bool DrivingAdapterActive => Role==StationRole.Driver&&StationSessionFlow.Instance&&StationSessionFlow.Instance.SoloBotDriverActive&&StationSessionFlow.Instance.DrivingMissionReady;
        public string MotionState { get; private set; } = "Idle";
        public float speed = 1.25f, stopDistance = 1.7f, turnSpeed = 110;
        Transform target, humanRoot;
        StationBotNavigator navigation;
        float replan, velocity;
        string animationState;
        bool taskActive,seated,reachActive;
        Vector3 taskPoint,reachPoint;
        Quaternion reachRotation;
        float taskDistance=.4f;
        Transform upperArm,lowerArm,hand;
        Transform leftUpper,leftLower,leftHand;
        bool leftReach;
        Vector3 leftPoint;
        Quaternion leftRotation;
        Transform spine;
        Quaternion spineRestore;
        bool leaned;
        Transform seatAnchor,hips,leftThigh,leftShin,leftFoot,rightThigh,rightShin,rightFoot;
        Quaternion seatedHipsAxes,leftFootAxes,rightFootAxes;
        Quaternion leftUpperRest,leftLowerRest,leftHandRest;
        public Vector3 LeftHandPosition=>leftHand?leftHand.position:transform.position;
        public void ReachLeft(Vector3 point,Quaternion rotation){leftReach=true;leftPoint=point;leftRotation=rotation;}
        public void StopLeftReach(){leftReach=false;if(leftUpper){leftUpper.localRotation=leftUpperRest;leftLower.localRotation=leftLowerRest;leftHand.localRotation=leftHandRest;}}
        public Vector3 HandPosition=>hand?hand.position:transform.position+Vector3.up*1.2f;
        public bool TaskReached=>taskActive&&Vector3.ProjectOnPlane(taskPoint-transform.position,Vector3.up).magnitude<=taskDistance+.06f;
        public bool CanReachFrom(Vector3 floor,Vector3 point,bool left=false)
        {
            var upper=left?leftUpper:upperArm;var lower=left?leftLower:lowerArm;var h=left?leftHand:hand;if(!upper||!lower||!h)return false;
            var d=Vector3.ProjectOnPlane(point-floor,Vector3.up);var yaw=d.sqrMagnitude>.001f?Quaternion.LookRotation(d):transform.rotation;
            var shoulder=floor+yaw*transform.InverseTransformPoint(upper.position);
            return Vector3.Distance(shoulder,point)<=Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,h.position)+.21f;
        }
        public bool IsMissionSeated=>seated;
        // Fireground (city) control: an external task controller moves the bot; station navigation is bypassed.
        public bool ExternalControl {get;set;}
        public void Unseat(Vector3 position,Quaternion rotation)
        {
            seated=false;seatAnchor=null;transform.SetParent(null,true);transform.SetPositionAndRotation(position,rotation);
            if(animator){animator.enabled=true;animationState=null;}
            reachActive=leftReach=false;MotionState="Idle";
        }
        public void SetMotion(string state,float playback)
        {
            MotionState=state;if(!animator||!animator.enabled)return;
            animator.SetFloat(PlaybackSpeed,playback);
            if(state!=animationState){animator.CrossFadeInFixedTime(state,.18f);animationState=state;}
        }
        public Transform HeldObject {get;set;}
        public void TaskGoal(Vector3 point,float distance=.4f){if(!taskActive||(taskPoint-point).sqrMagnitude>.01f){taskPoint=point;replan=0;}taskActive=true;taskDistance=distance;}
        public void ClearTask(){taskActive=false;reachActive=false;}
        public void Reach(Vector3 point,Quaternion rotation){reachActive=true;reachPoint=point;reachRotation=rotation;}
        public void StopReach()=>reachActive=false;
        public void Face(Vector3 point,float dt){var d=Vector3.ProjectOnPlane(point-transform.position,Vector3.up);if(d.sqrMagnitude>.001f)transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(d),turnSpeed*dt);}
        public void Seat(Transform seat)
        {
            if(!seat)return;seated=true;seatAnchor=seat;reachActive=leftReach=false;transform.SetParent(seat,false);transform.localPosition=Vector3.zero;transform.localRotation=Quaternion.identity;
            if(animator)animator.enabled=false;
            ApplySeatedBody();
            StopLeftReach();SolveArm(upperArm,lowerArm,hand,seat.position+seat.up*.65f+seat.forward*.4f,seat.rotation);SolveArm(leftUpper,leftLower,leftHand,seat.position+seat.up*.55f+seat.forward*.35f-seat.right*.12f,seat.rotation);
            MotionState="Seated";
        }
        static readonly int PlaybackSpeed = Animator.StringToHash("PlaybackSpeed");
        public void Initialize(StationSceneSetup station, Transform humanRoot, Transform humanHead, StationRole humanRole)
        {
            target = humanHead; this.humanRoot = humanRoot; HumanRole = humanRole; Role = StationBotRoles.Opposite(humanRole);
            navigation = new StationBotNavigator(station, humanRoot, transform);
            animator.applyRootMotion = false;
            if(animator.isHuman){upperArm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);lowerArm=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);hand=animator.GetBoneTransform(HumanBodyBones.RightHand);}
            if(animator.isHuman){leftUpper=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);leftLower=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);leftHand=animator.GetBoneTransform(HumanBodyBones.LeftHand);}
            if(leftUpper){leftUpperRest=leftUpper.localRotation;leftLowerRest=leftLower.localRotation;leftHandRest=leftHand.localRotation;}
            if(animator.isHuman)spine=animator.GetBoneTransform(HumanBodyBones.Spine);
            if(animator.isHuman){hips=animator.GetBoneTransform(HumanBodyBones.Hips);leftThigh=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);leftShin=animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);rightThigh=animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);rightShin=animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);seatedHipsAxes=Quaternion.Inverse(transform.rotation)*hips.rotation;leftFootAxes=Quaternion.Inverse(transform.rotation)*leftFoot.rotation;rightFootAxes=Quaternion.Inverse(transform.rotation)*rightFoot.rotation;}
        }
        // Lobby presentation: same bot prefab, role and bone bindings, seated at a FireCenterLobby station.
        // No navigation (the bot never walks in the lobby).
        public void InitializeLobby(StationRole humanRole)
        {
            HumanRole = humanRole; Role = StationBotRoles.Opposite(humanRole);
            animator.applyRootMotion = false;
            if(animator.isHuman){upperArm=animator.GetBoneTransform(HumanBodyBones.RightUpperArm);lowerArm=animator.GetBoneTransform(HumanBodyBones.RightLowerArm);hand=animator.GetBoneTransform(HumanBodyBones.RightHand);}
            if(animator.isHuman){leftUpper=animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);leftLower=animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);leftHand=animator.GetBoneTransform(HumanBodyBones.LeftHand);}
            if(leftUpper){leftUpperRest=leftUpper.localRotation;leftLowerRest=leftLower.localRotation;leftHandRest=leftHand.localRotation;}
            if(animator.isHuman)spine=animator.GetBoneTransform(HumanBodyBones.Spine);
            if(animator.isHuman){hips=animator.GetBoneTransform(HumanBodyBones.Hips);leftThigh=animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);leftShin=animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg);leftFoot=animator.GetBoneTransform(HumanBodyBones.LeftFoot);rightThigh=animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);rightShin=animator.GetBoneTransform(HumanBodyBones.RightLowerLeg);rightFoot=animator.GetBoneTransform(HumanBodyBones.RightFoot);seatedHipsAxes=Quaternion.Inverse(transform.rotation)*hips.rotation;leftFootAxes=Quaternion.Inverse(transform.rotation)*leftFoot.rotation;rightFootAxes=Quaternion.Inverse(transform.rotation)*rightFoot.rotation;}
        }
        void Update() => Tick(Time.deltaTime);
        public void Tick(float dt)
        {
            if (seated||ExternalControl||!target || navigation == null || dt <= 0) return;
            var goal = taskActive?taskPoint:target.position;if(!taskActive)goal.y = humanRoot.position.y;
            // Follow the ground under the head rather than the headset's standing height.
            if (navigation.Ground(goal, out var grounded)) goal = grounded;
            var towardHuman = Vector3.ProjectOnPlane(goal - transform.position, Vector3.up);
            float stopping=taskActive?taskDistance:stopDistance;
            bool follow = towardHuman.magnitude > stopping;
            replan -= dt;
            if (follow && replan <= 0) { navigation.Plan(transform.position, goal,taskActive); replan = .9f; }
            var next = follow ? navigation.Next(transform.position) : transform.position;
            var direction = Vector3.ProjectOnPlane(next - transform.position, Vector3.up);
            bool hasPath = follow && direction.sqrMagnitude > .000025f;
            var facing = hasPath ? direction : towardHuman;
            float angle = facing.sqrMagnitude > .01f ? Vector3.SignedAngle(transform.forward, facing, Vector3.up) : 0;
            if (Mathf.Abs(angle) > 5) transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(facing), turnSpeed * dt);
            float desired = hasPath && Mathf.Abs(angle) < 55 ? speed * Mathf.Clamp01((towardHuman.magnitude - stopping) / .7f) : 0;
            velocity = Mathf.MoveTowards(velocity, desired, 2.5f * dt);
            bool moved = false;
            if (hasPath && velocity > .015f)
            {
                var candidate = Vector3.MoveTowards(transform.position, next, Mathf.Min(velocity * dt, Mathf.Max(0, towardHuman.magnitude - stopping)));
                if (navigation.Ground(candidate, out var floor) && navigation.Segment(transform.position, floor)) { transform.position = floor; moved = true; }
                else { velocity = 0; replan = 0; }
            }
            MotionState = moved ? "Walk" : Mathf.Abs(angle) > 12 ? (angle < 0 ? "TurnLeft" : "TurnRight") : "Idle";
            if (animator)
            {
                animator.SetFloat(PlaybackSpeed, moved ? Mathf.Clamp(velocity / .9f, .4f, 1.5f) : .55f);
                if (MotionState != animationState) { animator.CrossFadeInFixedTime(MotionState, .18f); animationState = MotionState; }
            }
        }
        void LateUpdate()
        {
            if(leaned&&spine){spine.localRotation=spineRestore;leaned=false;}
            if(seated){ApplySeatedBody();return;}
            if(spine&&(reachActive||leftReach))
            {
                var target=leftReach?leftPoint:reachPoint;var upper=leftReach?leftUpper:upperArm;var lower=leftReach?leftLower:lowerArm;var h=leftReach?leftHand:hand;
                if(upper&&lower&&h&&Vector3.Distance(upper.position,target)>Vector3.Distance(upper.position,lower.position)+Vector3.Distance(lower.position,h.position)+.03f)
                {var forward=Vector3.ProjectOnPlane(target-upper.position,Vector3.up).normalized;if(forward.sqrMagnitude>.5f){spineRestore=spine.localRotation;spine.rotation=Quaternion.AngleAxis(20,Vector3.Cross(transform.up,forward))*spine.rotation;leaned=true;}}
            }
            if(reachActive)SolveArm(upperArm,lowerArm,hand,reachPoint,reachRotation);
            if(leftReach)SolveArm(leftUpper,leftLower,leftHand,leftPoint,leftRotation);
        }
        void SolveArm(Transform upperArm,Transform lowerArm,Transform hand,Vector3 reachPoint,Quaternion reachRotation)
        {SolveLimb(upperArm,lowerArm,hand,reachPoint,reachRotation,-transform.up+transform.right*.35f);}
        void ApplySeatedBody()
        {
            if(!seatAnchor||!hips)return;
            transform.localPosition=Vector3.zero;transform.localRotation=Quaternion.identity;var f=FirefighterVR.SeatPose.Resolve(seatAnchor);var yaw=Quaternion.Euler(0,f.yaw,0);hips.rotation=seatAnchor.rotation*seatedHipsAxes;
            transform.position+=f.floorPoint+Vector3.up*f.hipHeight-hips.position;
            // Static seated legs: thigh horizontal on the cushion, shin hanging down to the floor (never into the seat).
            SeatLeg(leftThigh,leftShin,leftFoot,yaw,f.floorPoint.y,seatAnchor.rotation*leftFootAxes);
            SeatLeg(rightThigh,rightShin,rightFoot,yaw,f.floorPoint.y,seatAnchor.rotation*rightFootAxes);
        }
        void SeatLeg(Transform thigh,Transform shin,Transform foot,Quaternion yaw,float floor,Quaternion footRotation)
        {
            if(!thigh||!shin||!foot)return;var fwd=yaw*Vector3.forward;float a=Vector3.Distance(thigh.position,shin.position),b=Vector3.Distance(shin.position,foot.position);
            const float lean=.16f;var ankle=thigh.position+fwd*(a*.95f+lean)+Vector3.down*Mathf.Sqrt(Mathf.Max(.01f,b*b*.94f-lean*lean));ankle.y=Mathf.Max(ankle.y,floor+.08f);
            SolveLimb(thigh,shin,foot,ankle,footRotation,fwd+Vector3.up*.4f);
        }
        void SolveLimb(Transform upperArm,Transform lowerArm,Transform hand,Vector3 reachPoint,Quaternion reachRotation,Vector3 preferredBend)
        {
            if(!upperArm||!lowerArm||!hand)return;
            var shoulder=upperArm.position;float a=Vector3.Distance(shoulder,lowerArm.position),b=Vector3.Distance(lowerArm.position,hand.position);
            var delta=reachPoint-shoulder;float d=Mathf.Clamp(delta.magnitude,Mathf.Abs(a-b)+.001f,a+b-.002f);var direction=delta.normalized;
            var bend=Vector3.ProjectOnPlane(preferredBend,direction).normalized;
            float x=(a*a-b*b+d*d)/(2*d);var elbow=shoulder+direction*x+bend*Mathf.Sqrt(Mathf.Max(0,a*a-x*x));
            upperArm.rotation=Quaternion.FromToRotation(lowerArm.position-shoulder,elbow-shoulder)*upperArm.rotation;
            lowerArm.rotation=Quaternion.FromToRotation(hand.position-lowerArm.position,shoulder+direction*d-lowerArm.position)*lowerArm.rotation;
            hand.rotation=reachRotation;
        }
    }
}
