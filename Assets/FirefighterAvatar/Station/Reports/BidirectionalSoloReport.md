# Bidirectional Solo and seated-avatar update

Completed 15 November 2026 (Asia/Riyadh). This is a targeted extension of the completed implementation, not a restart of prior tasks.

## Modified files

New runtime script:
- `Assets/CityNightPresentation/Mission/Runtime/FireTruckBotDriver.cs`

Modified existing runtime scripts:
- `Assets/FirefighterAvatar/StationBot/StationSoloBotTest.cs`
- `Assets/FirefighterAvatar/StationBot/StationBotFollower.cs`
- `Assets/FirefighterAvatar/Station/StationSessionFlow.Solo.cs`
- `Assets/FirefighterAvatar/Station/StationSessionFlow.Mission.cs`
- `Assets/FirefighterAvatar/Station/StationLocalRig.cs`
- `Assets/FirefighterAvatar/Station/SeatedDispatchLobby.cs`
- `Assets/FirefighterAvatar/Station/StationMissionView.cs`
- `Assets/FirefighterAvatar/Scripts/FirefighterAvatarDriver.cs`
- `Assets/CityNightPresentation/Mission/Runtime/FireTruckMission.cs`
- `Assets/CityNightPresentation/Mission/Runtime/FireTruckEmergency.cs`
- `Assets/CityNightPresentation/Mission/Runtime/FireTruckPresentation.cs`

New Editor helper: `Assets/FirefighterAvatar/Station/Editor/BidirectionalSoloVerification.cs`.

Reports: `BidirectionalSoloProtectedBefore.json`, `BidirectionalSoloProtection.json`, `BidirectionalSoloVerification.json`, `BidirectionalSoloSave.json`, and this report. The existing `CompleteImplementationVerification.json` was refreshed by the regression suite.

No scene redesign, replacement prefab, new material, Input Action asset or permanent manager was needed. The existing lobby (`Assets/S1Test.unity`), station (`Assets/FireStation/Scenes/Sample.unity`) and city (`Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity`) were inspected and validated. Runtime bindings reuse their existing references. The bot driving adapter is added only to the existing mission truck when entering a Solo Bot Driver city session; it is not installed in two-human sessions.

## Solo role selection

Removed the Driver-only Ready restriction, hardcoded role labels, hardcoded local Solo role, unconditional human dispatch seating, and Driver-only local task/seat acknowledgement assumptions.

- Hand Tracking + G29: human Driver, bot Assistant.
- Quest Controllers: human Assistant, bot Driver.
- The selected profile is retained. Ready waits for that profile's existing tracking readiness. Labels display the actual complementary roles.
- Neither assignment uses Host/client identity, room ownership or join order. Two real players exclude the bot.

Human Driver/Bot Assistant continues through the existing Assistant navigation, tablet presentation, integrated garage UP control and passenger boarding code. The new Driver branch does not execute those Assistant tasks.

## Bot Driver station and city behavior

The bot is seated at the existing dispatch chair during standby. After the existing alarm it acknowledges that chair through the same CrewMissionState.TrySeat validation. It waits until the human Assistant has actually picked up and presented the tablet before TryAccept can succeed. It then occupies the existing truck Driver seat and advances the existing Driver seat gate. Garage operation and passenger boarding remain the human Assistant's responsibilities. The existing garage timing, departure countdown, fade and additive city transition remain in use. Driver/passenger city ACK bits are assigned according to actual roles.

Solo Assistant tablet updates and garage commands now reach the existing local mission backend with role, timing, pose and mission-stage validation. No fake network client or automatic Assistant task completion was added.

`FireTruckBotDriver` is a deterministic input adapter, not another vehicle simulation. It:

- Reads the existing 81 curved-road waypoints; caches planar path distances once.
- Uses bounded nearest-segment tracking and lookahead steering; computes steering/accelerator/brake values for the existing controller.
- Updates planning/obstacle checks at approximately 12.5 Hz and supplies input on the existing physics cadence.
- Reduces speed for turns, signal stop lines, nearby civilian vehicles and bounded nonallocating obstacle queries.
- Stops for red/yellow signals and waits for legal clearance; normal traffic and penalties remain active.
- Requests emergency activation only in Solo Bot Driver mode through FireTruckEmergency and the existing stopped/designated-red validation. It cannot activate immediately at city spawn. Human Driver physical Grab/pull behavior is unchanged.
- Reuses the existing siren, linear handle state, lightbar and yellow/all-red priority controller. Existing arrival/timeout rules reset emergency mode.
- Slows toward the incident stop zone. Arrival still requires the existing position, heading, upright, speed and dwell checks; the adapter never directly completes arrival.
- Reports a BotBlocked evaluation outcome and supplies safe braking after 40 seconds without progress, excluding legitimate signal waiting, or if it leaves the route corridor. No transform teleport or fake completion recovery exists. A vehicle unable to satisfy the final parking condition can also fail through the existing mission timer.

Defaults are a conservative 7 m/s straight-road target, 3.6 m/s turn target, and up to 2 m/s final approach. These are implementation defaults, not a measured guarantee of reliable full-route physics performance.

The existing mission owns input validation, elapsed time, penalties, emergency state and evaluation. The existing wheel controller, G29 bindings, local-Y wheel rotation and physics were not replaced. Bot input is rejected outside active Solo Bot Driver mode or before crew readiness. Human Assistant input cannot become vehicle input.

## Seated body and physical tracking

The existing avatar already had a seated pelvis target and leg IK, but seated foot placement could still inherit the floor under a displaced HMD, while walking/crouch blending could leak into the seated pose.

The human seated body now uses the chair anchor's floor elevation, zero walk blend, immediate chair-relative foot targets, and upright posture constraints. Pelvis/body yaw stay attached to the assigned chair. HMD movement cannot enable locomotion or advance standing/walking tasks. Lobby disable handling no longer clears a permanent Driver's seated state during presentation changes.

Natural HMD positional and rotational tracking remain untouched. No camera-local writes, tracking disablement, continuous XR Origin counter-motion or physical snapback were introduced. Existing bounded neck/head follow and arm/finger IK remain. A comfortable-position reminder uses the existing lobby/status/objective/windshield presentation when head displacement exceeds roughly 0.45 m horizontally or 0.35 m vertically. Real-world standing is not physically prevented; only avatar/gameplay behavior is constrained.

The bot reuses its existing humanoid Animator and analytic limb pose routine. Seated pelvis is placed at chair-floor +0.59 m; knee bend uses forward pole direction and feet target chair-floor +0.07 m, about 0.43 m ahead. This replaces accumulated local thigh/calf Euler offsets. The same bot pose follows dispatch and truck anchors. No replacement avatar or full-body physics was added.

The human's existing first-person visibility rules are retained. Existing seated position/yaw avatar-state data continues to drive remote appearance; no new networking system or packet fields were required by this update.

## Assistant and multiplayer preservation

Solo and multiplayer Assistant roles retain the existing Smooth Move/Teleport preference, native turning, tracked controller interaction, tablet, garage and boarding behavior. Returning to the lobby restores the selected profile and existing placement/locomotion policy without restarting Unity. Vehicle boarding still uses the existing seated passenger restrictions while in transit.

No NetworkManager configuration, player prefab registration, distributed authority setting, room creation/join configuration, RPC ownership rule, shared Input Action asset or source network avatar prefab was changed.

## Verification

- Compilation: no C# errors in the running Editor.
- 30 new focused Edit Mode checks passed, including actual local mission command routing, complementary roles, bot seated pelvis/knees/feet, alarm/dispatch/tablet/garage/boarding gates, city seat bits, bot-only input eligibility, rejection of early emergency activation, designated-red activation and stop, bounded failure braking, Assistant movement policy, and synthetic displaced-head seated-body tests.
- The synthetic human-avatar test changed a temporary tracked source by 1.2 m sideways and upward; the seated body/pelvis stayed anchored, feet kept chair-floor targets, movement speed remained zero and the real camera-local transform was unchanged.
- All 35 existing focused regression checks passed: profile conflicts, role permutations, seated placement, computer drill/library/emergency interruption, independent rim grip release, linear handle, serializers, signal clearance, timer braking and scene references.
- Zero missing scripts or renderer materials across the three target scenes; existing duplicate-manager checks passed.
- `BidirectionalSoloProtection.json` confirms unchanged hashes for the checked project settings, package manifest, StationLobbyState.cs and source FirefighterNetworkVisual.prefab.

These are synthetic Editor/state/geometry checks, not physical headset tests, a full simulated drive, or a live network session. No Play Mode, builds, package installs or Git operations were performed.

Remaining validation: Quest seated comfort and visible human/bot pose; real standing/leaning, controller locomotion and interaction; physical G29 regression; full bot road tracking, loaded-vehicle braking, collision avoidance, stop-zone alignment, signal timing and emergency audio during live driving; both two-human role/authority configurations. Full-route reliability and hardware frame rate remain unverified. No missing asset or configuration blocker was found.

All completed source/report changes are saved. Unity and Windows remain running; the Editor is returned to the saved lobby with station and city unloaded for the existing runtime transitions.
