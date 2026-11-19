using UnityEngine;
using UnityEngine.InputSystem;

namespace CityNightPresentation.Mission
{
    [DefaultExecutionOrder(-90),RequireComponent(typeof(Rigidbody))]
    public sealed class FireTruckWheelController : MonoBehaviour
    {
        public FireTruckMission mission;
        public BasicCarController importedTuning;
        public InputActionAsset drivingActions;
        public WheelCollider[] wheels = new WheelCollider[0];
        public Transform[] wheelVisuals = new Transform[0];
        public Quaternion[] wheelVisualOffsets = new Quaternion[0];
        public Transform steeringVisual;
        public Vector3 steeringShaftAxis=Vector3.up;
        [Range(180,1440)]public float physicalSteeringDegrees=900;
        public bool allowKeyboardTesting;
        [Range(-.5f,.5f)] public float steeringCenter;
        [Range(.3f,1)] public float steeringRange = 1;
        [Range(0,.2f)] public float steeringDeadzone = .035f;
        public Vector2 throttleCalibration = new Vector2(0,1), brakeCalibration = new Vector2(0,1);
        public float maximumKmh = 65, reverseKmh = 12, axleTorque = 10000, brakeTorque = 18000;
        public float steeringAngle = 31, steeringRate = 45;
        public float SpeedKmh => mission&&mission.IsSpawned&&!mission.Authority?Mathf.Abs(mission.Motion.Value.y):body?body.linearVelocity.magnitude*3.6f:0;
        public Vector4 AppliedInput { get; private set; }
        public bool DrivingDevicePresent=>hasDrivingDevice;
        public bool Initialized=>body&&runtimeActions&&wheels.Length==4;
        Rigidbody body;
        InputActionAsset runtimeActions;
        InputAction throttle, brake, steer, reverse, handbrake;
        bool reverseSelected, inputEnabled;
        float lastSend, steering;
        Quaternion steeringBase;
        float wheelSpin;
        bool hasDrivingDevice;
        float localSteeringVisual;
        // Visual wheel only: G29 right = rim turns right as seen from the driver's seat (was mirrored in the headset).
        public static Quaternion SteeringRotation(Quaternion mounted,Vector3 shaft,float input,float range)=>mounted*Quaternion.AngleAxis(Mathf.Clamp(input,-1,1)*range*.5f,shaft.normalized);
        void Awake()
        {
            using var timing=FirefighterVR.Station.TransitionDiagnostics.Measure("vehicle_input_and_physics_awake");
            body=GetComponent<Rigidbody>();body.centerOfMass=new Vector3(0,.85f,-.55f);
            if(importedTuning)importedTuning.enabled=false;
            if(importedTuning){maximumKmh=importedTuning.maxSpeed;reverseKmh=importedTuning.maxReverseSpeed;axleTorque=importedTuning.motorPower;brakeTorque=importedTuning.brakePower;steeringAngle=importedTuning.maxSteeringAngle;steeringRate=importedTuning.steeringSpeed;}
            if(steeringVisual)steeringBase=steeringVisual.localRotation;
            if(drivingActions)
            {
                runtimeActions=Instantiate(drivingActions);
                throttle=runtimeActions.FindAction("Drive/Throttle",true);brake=runtimeActions.FindAction("Drive/BrakeReverse",true);
                steer=runtimeActions.FindAction("Drive/Steering",true);reverse=runtimeActions.FindAction("Drive/ReverseGear",true);handbrake=runtimeActions.FindAction("Drive/Handbrake",true);
                // Restrict to this device and optional explicit keyboard testing, never arbitrary joysticks.
                RefreshDevices();
            }
        }
        void RefreshDevices()
        {
            if(!runtimeActions)return;
            var devices=new System.Collections.Generic.List<InputDevice>();
            foreach(var d in InputSystem.devices)
            {
                bool g29=(d.description.product??"").Contains("G29");
                if(g29&&!d.enabled)InputSystem.EnableDevice(d); // never leave the wheel disabled (window focus)
                if(d.enabled&&(g29||(allowKeyboardTesting&&d is Keyboard)))devices.Add(d);
            }
            runtimeActions.devices=devices.ToArray();
            bool had=hasDrivingDevice;hasDrivingDevice=devices.Count>0;
            if(had!=hasDrivingDevice)Debug.Log(hasDrivingDevice?"[Truck] G29 connected: driving input active.":"[Truck] G29 not available: the truck cannot be driven.",this);
        }
        void DeviceChanged(InputDevice device,InputDeviceChange change){RefreshDevices();}
        // Writes the reason once whenever the Driver's control state changes (diagnostics in Player.log).
        string lastBlock="";
        void LogDrivingBlock(bool allowed)
        {
            string reason;
            if(allowed)reason="ok";
            else if(!hasDrivingDevice)reason="no G29 device";
            else if(!mission.CrewReady)reason="crew not ready (waiting for seats / city)";
            else if(mission.HasArrived)reason="already arrived";
            else if(mission.HasFailed)reason="mission stopped";
            else{var flow=FirefighterVR.Station.StationSessionFlow.Instance;var rig=flow?flow.localRig:null;reason=rig&&!rig.InputAvailable?"headset input not ready: "+rig.InputStatus:"not allowed";}
            if(reason==lastBlock)return;lastBlock=reason;
            Debug.Log(reason=="ok"?"[Truck] Driver has control of the truck.":"[Truck] Driver cannot drive: "+reason,this);
        }
        void OnEnable(){InputSystem.onDeviceChange+=DeviceChanged;RefreshDevices();}
        void Update()
        {
            bool allowed=mission&&mission.LocalDriverMayControl&&hasDrivingDevice;
            if(mission&&mission.LocalRole==FirefighterVR.Station.StationRole.Driver)LogDrivingBlock(allowed);
            if(runtimeActions&&inputEnabled!=allowed){inputEnabled=allowed;if(allowed)runtimeActions.Enable();else runtimeActions.Disable();}
            if(allowed&&runtimeActions)
            {
                if(reverse.WasPressedThisFrame())reverseSelected=!reverseSelected;
                float steeringValue=CalibrateSteering(steer.ReadValue<float>(),steeringCenter,steeringRange,steeringDeadzone);
                localSteeringVisual=steeringValue;
                var value=new Vector4(steeringValue,NormalizePedal(throttle.ReadValue<float>(),throttleCalibration),NormalizePedal(brake.ReadValue<float>(),brakeCalibration),reverseSelected?1:0);
                if(handbrake.ReadValue<float>()>.5f)value.z=1;
                if(Time.unscaledTime-lastSend>=.05f){lastSend=Time.unscaledTime;mission.SubmitDriverInput(value);}
            }
            if(steeringVisual){float value=allowed?localSteeringVisual:mission&&mission.IsSpawned&&!mission.Authority?mission.Motion.Value.x:AppliedInput.x;steeringVisual.localRotation=SteeringRotation(steeringBase,steeringShaftAxis,value,physicalSteeringDegrees);}
            if(mission&&mission.IsSpawned&&!mission.Authority)steering=Mathf.Lerp(steering,mission.Motion.Value.x*steeringAngle,1-Mathf.Exp(-10*Time.deltaTime));
            float signedSpeed=mission&&mission.IsSpawned&&!mission.Authority?mission.Motion.Value.y/3.6f:body?Vector3.Dot(transform.forward,body.linearVelocity):0;
            wheelSpin+=signedSpeed/.59f*Mathf.Rad2Deg*Time.deltaTime;
            for(int i=0;i<wheels.Length&&i<wheelVisuals.Length;i++)if(wheels[i]&&wheelVisuals[i])
            {
                if(mission&&mission.Authority){wheels[i].GetWorldPose(out var p,out var r);wheelVisuals[i].SetPositionAndRotation(p,r*(i<wheelVisualOffsets.Length?wheelVisualOffsets[i]:Quaternion.identity));}
                else wheelVisuals[i].localRotation=Quaternion.Euler(0,i<2?steering:0,0)*Quaternion.AngleAxis(wheelSpin,Vector3.right)*(i<wheelVisualOffsets.Length?wheelVisualOffsets[i]:Quaternion.identity);
            }
        }
        void FixedUpdate()
        {
            bool authority=mission&&mission.Authority;
            if(body.isKinematic==authority)body.isKinematic=!authority;
            foreach(var w in wheels)if(w&&w.enabled!=authority)w.enabled=authority;
            if(!authority)return;
            Vector4 input=mission.ConsumeDriverInput();AppliedInput=input;
            StepVehiclePhysics(input,Time.fixedDeltaTime);
        }
        public void StepVehiclePhysics(Vector4 input,float dt)
        {
            if(!body){body=GetComponent<Rigidbody>();body.centerOfMass=new Vector3(0,.85f,-.55f);}
            float forward=Vector3.Dot(transform.forward,body.linearVelocity);
            bool reversing=input.w>.5f;
            float target=steeringAngle*Mathf.Lerp(1,.45f,Mathf.Clamp01(SpeedKmh/maximumKmh))*input.x;
            steering=Mathf.MoveTowards(steering,target,steeringRate*dt);
            bool changingDirection=(reversing&&forward>.35f)||(!reversing&&forward<-.35f);
            float torque=changingDirection?0:axleTorque*input.y*(reversing?-.55f:1)*Mathf.Clamp01(1-SpeedKmh/(reversing?reverseKmh:maximumKmh));
            float stopping=Mathf.Max(input.z,changingDirection?1:0);
            if(input.y<.01f)stopping=Mathf.Max(stopping,.035f);
            for(int i=0;i<wheels.Length;i++)
            {
                var w=wheels[i];if(!w)continue;
                w.steerAngle=i<2?steering:0;w.motorTorque=i>=2?torque:0;w.brakeTorque=brakeTorque*stopping;
            }
            if(importedTuning)importedTuning.carSpeedKmh=SpeedKmh;
        }
        public static float NormalizePedal(float v,Vector2 range)=>Mathf.Abs(range.y-range.x)<.001f?0:Mathf.Clamp01((v-range.x)/(range.y-range.x));
        public static float CalibrateSteering(float v,float center,float range,float deadzone)
        {float x=Mathf.Clamp((v-center)/Mathf.Max(.1f,range),-1,1);return Mathf.Abs(x)<=deadzone?0:Mathf.Sign(x)*Mathf.InverseLerp(deadzone,1,Mathf.Abs(x));}
        void OnDisable(){InputSystem.onDeviceChange-=DeviceChanged;if(runtimeActions)runtimeActions.Disable();inputEnabled=false;}
        void OnDestroy(){if(runtimeActions)Destroy(runtimeActions);}
    }
}
