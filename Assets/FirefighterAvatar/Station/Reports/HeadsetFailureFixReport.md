# Headset failure follow-up

Saved implementation follow-up, 2026-11-14. Physical headset and live two-peer retesting have not been performed.

## Confirmed input defect and fix

The old StationHandTeleportInput called XRInputButtonReader.QueueManualState on every Update. The installed XRI reader schedules each queued state for Time.frameCount + 1. With the adapter running before the interaction update, repeatedly replacing that pending state can defer its application indefinitely. Native reader scheduling was checked directly. Compilation success did not detect this behavior.

The existing adapter now queues press/release edges once. Point with the right index finger while curling middle/ring/little fingers, aim the existing arc at a valid floor or available seat marker, then pinch. The adapter briefly holds the last native-valid ray pose (0.35 seconds) while the index curls, presses native selection, then releases it after selection has been processed. This invokes the existing OnSelectExit teleport path without requiring a second deliberate release gesture. The arc disappears afterward.

Palm-up activation and its palm-normal cancellation were removed. Aim follows index proximal-to-tip direction. Activation uses the actual right-hand rig activation and right XR Hands subsystem tracking, rather than the global modality value; no left-hand joint is required. Grab selection/hover and UI interaction retain priority. Existing hand-interaction/pose-driver enabled states are restored accurately after aiming. Controller teleportation, existing Input Actions, ray/reticle, provider, native arc parameters, floor validation and fade implementation remain in use.

## Mission diagnosis: established conditions versus unknown history

The saved authority-owned state machine already called TickCrewMission automatically. It requires exactly two real connected players and StationReady; StationReady is set only after both assigned roles acknowledge station spawn. Solo Bot deliberately does not send cooperative Ready/spawn RPCs or count as a second real player. The chair is intentionally unavailable until EmergencyAlarm/DispatchReview. These source conditions explain why a one-real-player station test never starts the alarm or chair interaction.

The prior headset session's connected-player count, phase, authority and acknowledgement state were not provided or retained in accessible runtime diagnostics. The Editor was not playing during inspection. Therefore the exact branch that blocked that specific headset session is **unverified**, and this report does not claim a proven two-peer countdown failure cause. Saved inspection ruled out the coordinator being inside its hidden lobby roots; it was enabled and active. Do not bypass Ready, fabricate a player, or enable the full mission in Solo to hide this limitation.

Startup now explicitly validates both real-player roles/Ready flags, both spawn acknowledgements, StationReady and existing presentation references before initializing the one shared deadline. The existing deadline comparison still triggers dispatch at 60 seconds; the alarm stage cannot initialize a second countdown. No additional action starts it, and city-seat acknowledgements remain relevant only to the existing later CityTransition stage.

StationSessionFlow now exposes runtimeMissionDiagnostics in its Inspector and emits concise [StationMission] logs on condition changes and approximately ten-second countdown boundaries. Diagnostics include mission/lobby stage, connected real-player count, local authority/network-spawn status, Driver/Assistant IDs, both Ready, spawn acknowledgement bits, countdown-started status, seconds remaining, alarm/chair status and blocking reason. The existing computer/report objective displays waiting-for-second-player, countdown and locked-chair messages, including Solo exploration-only status. No new UI, timer, manager or NetworkVariable was created.

## Chair binding

The saved chair anchor/destination/collider were present; its serialized provider reference was null. XRI normally auto-discovers a provider, so null alone is not proof of the headset failure. StationMissionView now explicitly binds the existing local rig's provider to all three existing seat anchors and retries the binding if additive scene initialization precedes the rig. This also installs the existing locomotion-completion listener when the late binding succeeds. Seat request generation validates the native anchor request before marking seating pending, preventing an unsuccessful generated request from locking out later attempts. No chair, seat, rig or provider was duplicated.

## Modified files

- Assets/FirefighterAvatar/Station/StationHandTeleportInput.cs
- Assets/FirefighterAvatar/Station/StationLocalRig.cs (gesture instruction text only)
- Assets/FirefighterAvatar/Station/StationSessionFlow.cs (diagnostic calls only)
- Assets/FirefighterAvatar/Station/StationSessionFlow.Mission.cs (explicit startup validation, diagnostics and named deadline predicate; post-acceptance stages retained)
- Assets/FirefighterAvatar/Station/StationMissionView.cs (existing provider binding/retry and status text)
- Assets/FirefighterAvatar/Station/StationMissionSeatAnchor.cs (native request validation ordering)
- Assets/FirefighterAvatar/Station/Editor/StationMissionInstaller.cs (new activation timing on the existing adapter)
- Assets/S1Test.unity (existing adapter activation hold set to 0.08 seconds), report/receipt files and Unity metadata.

No garage-button, emergency-bell asset, tablet asset, G29, city route, network configuration, room logic, avatar or IK changes. No new script, scene object, XR system, package, build, Git operation or Windows power operation.

## Focused checks and remaining validation

HeadsetFailureFixVerification.json records 21 passing Edit Mode checks: extended/curled finger fixtures, native reader scheduling emulation, two-player/Ready/acknowledgement/presentation gates, 60-second deadline boundary, no repeated dispatch countdown, chair role/stage gating, tablet prerequisites, existing adapter/seat bindings, one origin/provider, and zero missing scripts in lobby/station. Scheduling tests emulate target-frame arrival using the native reader; they are not a live tracked-hand test. Compilation completed with zero errors/warnings reported by the compile command.

Still required on hardware: actual right-hand poses/arc/reticle, pinch transition and teleport completion, grab/UI coexistence, controller regression, both-peer spawn acknowledgement and 60-second countdown, bell and chair unlock, seated computer interaction, tablet presentation/acceptance and unchanged subsequent boarding. If a two-real-player mission remains blocked, [StationMission] logs now identify the actual blocking state instead of silently waiting. Do not claim these physical scenarios passed from the Edit Mode checks.

Unity and Windows remain running. Final saved-scene/idle/compiler receipt is in HeadsetFailureFinalReceipt.json.
