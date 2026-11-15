# Existing bell and garage-arrow integration

This is a targeted follow-up to the cooperative mission implementation. The original countdown, authority-owned mission state, Ready flow, spawn, hand teleportation, city transition, vehicle input, avatars and IK were retained.

## Existing objects reused

- All six station EmergencyBell instances: existing EmergencyBell.wav AudioSources, Bell animators, EmergencyBellLight lens animators/renderers and Point Light animators/red lights (18 original animators in total).
- Primary `Details/RollupDoor/RollupDoorFrame`: its original UP/DOWN arrow faces are integrated in the imported RollupDoor.fbx mesh. The mesh, geometry, transform and arrow symbols are retained.
- Existing garage door Animator, original opening animation, collision curtain, authority commands and fully-open boarding gate.

## Exact changes

- StationMissionView.cs: alarm is active only during EmergencyAlarm and DispatchReview. Acceptance stops the sound and samples the original animation clips back to dark standby; disabling the view also stops the alarm. Existing clip phases use shared mission time (`alarmAt`), keeping both participants' presentation tied to the same synchronized event, including a participant observing an already-active dispatch. The looping bell, lens and light clips themselves provide vibration/flashing; no flashing manager, timer, animation or light was added.
- StationGarageButton.cs: the existing adapter can discriminate the original upper/lower arrow faces using the existing interactor's ray endpoint or near attach position. Hits outside the small button regions are rejected. Commands still call StationMissionView.GarageOpen/Close -> StationSessionFlow.CommandGarage -> the existing authority RPC and role/safety validation.
- Editor/StationMissionInstaller.cs: a focused IntegrateExistingHardware entry point binds the original hardware. The general installer now uses that same connection instead of constructing a replacement panel.
- Sample.unity: removed the previously added Garage_ControlPanel and its housing, stand, buttons and text labels. The sole garage-button adapter and native XRSimpleInteractable are now on the original RollupDoorFrame, with two small solid BoxCollider hit regions on that same object. No new button objects or meshes. Solid hit regions work with the existing near/far casters that ignore triggers.
- Scene-specific URP materials/textures in `Assets/FirefighterAvatar/Station/Materials`: OriginalGarageArrowPanel.mat, OriginalGarageArrows_Base.png, OriginalGarageArrows_Emission.png and EmergencyLens_Mission.mat. Only red backplate texels are recolored/emissive; original yellow arrows and grey housing are preserved. The source atlas and imported source materials are unchanged. Derived panel textures are compressed, non-readable 2048px assets. No bloom, post-processing or extra lights are required. URP emission-validation flags are explicitly retained on these copies; no lighting bake was requested.

## Mission synchronization retained

The existing StationSessionFlow alone owns the 60-second countdown and mission event. Audio starts once for the observed alarm timestamp, loops while acceptance is pending, and stops after acceptance. Original clip phases derive from shared time rather than a second event/timer. Garage progress continues to derive from the existing synchronized garage target/start time. A press alone cannot enable boarding: CanBoardAssistant still checks the fully-open fraction and existing mission prerequisites. Existing duplicate boarding/city-load guards remain unchanged. No new NetworkVariables, network objects, managers, registrations or multiplayer configuration.

## Focused verification

See ExistingHardwareVerification.json and ExistingHardwareProtection.json. Checks cover original references, one remaining panel adapter, upper/open and lower/close hit discrimination, invalid-hit rejection, existing fully-open boarding requirement, alarm-active stages and isolated original-animation sampling. Sampled existing red light intensity reaches 3 and returns to 0 with all warning lights disabled; lens emission returns to black. The station scan found zero missing scripts and zero missing materials. Targeted protected settings/Ready/role/source-avatar hashes match their recorded baseline.

Physical headset/controller and live two-peer testing remains unverified. Required hardware checks: aim/press each original arrow with Assistant controllers; compatible near-hand selection; hear/see one shared alarm after the countdown; confirm animation, red flashes and sound stop upon acceptance; verify repeated/reversed garage commands, collision safety, both-peer door presentation, passenger boarding and one city transition. No claim of live synchronization or Quest readability/performance is made from Edit Mode checks.

Final Scene View inspection confirmed a green glowing UP backplate and red glowing DOWN backplate on the original panel, with the original arrows and housing preserved. Both scene-specific material emission keywords survived saving and domain reload. Final compiler ground truth: compilationFailed false, consoleErrors 0; four existing warnings remain. One CLI recompile request encountered a transient reload/HTTP error; the subsequent current Editor compiler/console state and successful evaluation confirm compilation completed.

No Git, packages, Play Mode, build, bake, Windows power action or application restart was performed. Unity and Windows are left running. The final save/idle receipt is recorded separately in ExistingHardwareFinalReceipt.json.
