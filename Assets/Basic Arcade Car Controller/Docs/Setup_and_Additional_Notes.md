# Script Explanations

**BasicCarController.cs**
This script needs to know which wheels are for driving and which are for steering. After adding this script, don't forget to assign the corresponding Wheel Colliders.

**Acceleration Curve**
This curve defines the vehicle's acceleration. It comes empty by default. If you forget to adjust it, the car won't move. Also, if the starting value is too close to 0.000, the car might struggle to start moving.

**Drag Force For Brakes**
Applies extra drag force to the Rigidbody when braking.

**Min Speed For Handbrake Drift**
Decides the minimum speed required for the car to drift when using the handbrake. If the speed is lower than this value, drifting won't be triggered.

**Steering Settings**
Allows you to adjust steering speed and maximum steer angle at high speeds.

**Counter Steering Settings**
* **Max Slip Angle Deg:** The upper limit for slippage before maximum counter-steer force is applied.
* **Counter Steer Response:** Adjusts the speed of the counter-steer.
* **Min Slip For Counter:** Adjusts the minimum slip degree required for the car to start counter-steering.
* **Counter Steer Strength:** Adjusts the intensity of the counter-steering. The higher the value, the stronger the steering correction.

**Weight Distribution Settings**
* **XCOM Value:** Makes the car more stable when cornering. Lowering the value might result in a rollover.
* **Z Value:** Controls weight distribution along the length of the car. Values above 0 shift weight to the front; values below 0 shift it to the rear.
* **Y Value:** Determines how high the vehicle's center of gravity (COM) is.

**BasicWheelFriction.cs**
Used to adjust friction and curves for both sideways and forward grip. Make sure to adjust these curves; otherwise, it won't work properly.
*Note: Increasing the sideways grip too much may cause the vehicle to roll over. If you go over 1.5-1.6, you should balance it by lowering the sideways curve.*

**BasicNitroSystem.cs**
Adjusts nitro power and duration. This system is not WheelCollider-based; it adds acceleration force directly to the Rigidbody.
* **ForwardW & UpW:** These determine the position where the force is applied. For example, if ForwardW is below 0, the force is applied from the front. You can see the force application point in-game via a green debug sphere.

**BasicGearBox.cs**
A fake gear system primarily for sound effects. It does not simulate engine RPM or physics. It is dependent on car speed.
* **Element 0:** Represents the speed limit for the N gear. It should always be 1. If the speed is higher than 1, the car will stay in Neutral (N) until it reaches that speed.

------------------------------------

# How to Setup

### Dependencies
* **Unity Input System:** You must enable it from: `Edit → Project Settings → Player → Active Input Handling → Input System Package`.
* **Cinemachine:** Not required for the controller itself, but necessary for the camera in the DemoScene.
* **TextMeshPro:** Required for UI (included by default in most Unity projects).

### 1. Add your car to the scene
After adding your car model, ensure the hierarchy structure matches the following:

Car Model (Empty GameObject with Rigidbody & Scripts)
├── Body (Contains colliders)
├── Wheels (Empty GameObject with WheelVisuals.cs)
│   ├── WheelMeshes
│   │   ├── FLM (Front Left Mesh)
│   │   ├── FRM
│   │   ├── RLM
│   │   └── RRM
│   └── WheelColliders
│       ├── FLC (Front Left Collider)
│       ├── FRC
│       ├── RLC
│       └── RRC
└── EngineSound (Prefab from "Prefabs -> Audio", used for sound only)

### 2. Add Scripts to the "Car Model" Object
Add the following scripts:
* `BasicCarController.cs`
* `BasicWheelFriction.cs`
* `BasicNitroSystem.cs`
* `BasicGearBox.cs`

**PlayerInput (Optional):**
This asset uses the Unity Input System via generated Input Actions. The `PlayerInput` component is **not required**.
However, if you decide to add it:
1.  Click "Actions" and choose "Controls".
2.  Choose "Default Map" and set it to "Drive".
3.  Ensure "Behavior" is set to "Send Messages".

**Rigidbody Settings:**
Don't forget to add a Rigidbody component.
* **Interpolate:** Interpolate
* **Collision Detection:** Continuous
* **Mass:** Recommended between 1000 - 1500 (experimental values allowed).
* **Linear Damping:** Recommended 0 - 0.05.
* **Angular Damping:** Recommended 0.05 - 0.1.

------------------------------------

### Very Important Note:
The Wheel Order must be exactly as shown in the structure above: **Front Left, Front Right, Rear Left, Rear Right.** This order applies to colliders and must be consistent when assigning them in the scripts!

**Wheel Visuals:**
After adding `WheelVisuals.cs` to the "Wheels" game object, add all wheel **meshes** to the script.
*Note: Add the meshes, not the WheelColliders!*

------------------------------------

### WheelCollider Settings
Change the radius to fit your wheel model, then set the **Center Y value to 0.15**.

------------------------------------

### Audio
Add `EngineSoundByGear.cs` to the "EngineSound" object. The provided audio files are placeholders; it is recommended to replace them with your own sounds.

------------------------------------

### UI
If you want to use the UI prefab:
1.  Expand the UI game object to find the `UIScript` object.
2.  Click on it and assign your "Car Model" to the "Basic Car Controller" field.

------------------------------------

### Camera
If you use the `CarFollowCam` prefab:
1.  Install **Cinemachine** from the Package Manager.
2.  Set the "Tracking Target" to your "Car Model".
3.  It will ask you to add a `CinemachineBrain` to the main camera. Click "Add Brain," and you are good to go!

------------------------------------

# Known Issues
* **Throttle Stick:** In some situations, the car may continue accelerating slightly after the throttle is released. This is related to force-based acceleration and can be tuned via drag and damping values.
* **Rollover Risk:** Increasing the "Default Y" value in *Weight Distribution Settings* too much (more than 0.3) may cause the car to roll over. Adjusting the `XCOM` value might help, but it is not guaranteed.

# Recommended Usage
This controller is designed for **arcade-style driving**. It is **not intended for realistic vehicle simulation** without major modifications.