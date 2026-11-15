# Cooperative station mission — implementation receipt

Saved on 2026-11-14. Implementation and focused Edit Mode checks completed. Physical Quest, G29 and live two-peer operation have NOT been verified. Start testing from S1Test only; do not preload the station or city.

## Spawn and teleportation

- StationLocalRig now suppresses overlapping lobby reset triggers during station exploration and guards queued legacy reset destinations at the existing teleport-provider boundary. NetworkObjectDestroyer can call CharacterResetter.ResetPlayer even when that component is disabled; those legacy positions are a source-level rollback hazard. The exact headset-observed outside-spawn sequence remains unconfirmed until hardware testing.
- Ground-aligned XR placement preserves tracked head offsets, camera local pose, avatar and IK. Controller role selection and original Ready handshake are retained.
- StationHandTeleportInput uses the existing right-hand host, ray, line, reticle and teleport provider. Hold the open right palm upward for 0.35 seconds, aim along the fingers, then pinch and release to confirm. Lowering the hand, turning the palm down or losing tracking cancels. Grabbing/UI interaction blocks gesture activation. No replacement hand bindings or additional XR rig/provider were created.
- Right-hand availability is sufficient for post-spawn Driver station interaction when the left hand is occluded; original lobby readiness remains unchanged.
- Station movement is teleportation-only, retaining physical roomscale, turning, grabbing and tracking. Existing four repaired teleport meshes, native 10 m/s arc, 50 samples and safe capsule/path validation are retained. Stair destinations are limited to safe landings rather than individual narrow steps.
- Existing local stereo fade implementation is reused for teleportation, seating and scene transitions. Hardware confirmation of stereo coverage remains required.

## Cooperative mission

The existing persistent StationSessionFlow coordinates one synchronized mission state; no new NetworkObject, fake client, manager or network registration was added.

1. Both real players complete Ready and station-spawn acknowledgements.
2. Configurable 60-second shared exploration countdown triggers existing emergency-bell audio/presentation.
3. Driver enters the dispatch chair. Assistant picks up the original tablet and presents it to the seated Driver.
4. Only the seated Driver can accept the report after tablet pickup/presentation.
5. Acceptance moves Driver, under a local fade, into the stationary existing station truck.
6. Assistant uses green OPEN / red CLOSE controls. Door animation, shared server-time progress and collision curtain follow authoritative state. Closing is checked against the garage sweep and can reopen for safety.
7. Assistant explicitly boards only after the door is fully open. Boarding is rejected/rolled back if the door ceases to be safe.
8. A short departure stage covers both local views. Both black-screen acknowledgements precede the existing network city-load request.
9. Both city-seat acknowledgements, completed scene load, existing controller initialization and route readiness gate the existing G29 driving mission and timer.

Solo Bot remains station exploration only. Existing opposite-role selection, bot following/animations, two-player no-bot behavior and original Ready/room flow are retained. Live late-join, authority handoff, disconnect and concurrent-interaction behavior need multi-device regression.

## Reused assets and presentation

- Station: Monitor (6), OfficeChair (4), Tablet (1), original firetruck, rollup-door controller/open clip and EmergencyBell.wav. Garage proximity DoorAnimation components were removed only from garage scene instances; room-door behavior is retained.
- Added STATION_MISSION_PRESENTATION, scene seat/approach anchors, small XR entry markers, garage buttons/support, safety curtain, computer/tablet report canvases and scene-specific hardware/red/green materials. No duplicate truck, station, tablet model or city navigation canvas.
- Computer canvas fits the existing screen; tablet canvas fits mesh bounds without changing source mesh readability/import settings. Dispatch display received a final Scene View visual check. VR-distance readability and handheld orientation need headset testing.
- One StationIncident asset and persistent incident/epoch identify the same logical tablet/report across station and city. Existing city mounted tablet/map now shows route-projected remaining distance, ETA, progress, segment and upcoming turn. Coordinates are Unity world coordinates, not invented GPS/street data.
- Existing G29 actions, car physics, route, traffic and siren remain the driving implementation. City presentation no longer activates solely because the scene was loaded.

## Files

Modified scenes: Assets/S1Test.unity; Assets/FireStation/Scenes/Sample.unity; Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity.

New station scripts: StationHandTeleportInput.cs, StationIncidentDefinition.cs, StationSessionFlow.Mission.cs, StationMissionView.cs, StationMissionSeatAnchor.cs, StationGarageButton.cs, Editor/StationMissionInstaller.cs, Editor/StationCoopVerification.cs (under Assets/FirefighterAvatar/Station).

Modified station scripts: StationSessionFlow.cs, StationLocalRig.cs, StationSceneSetup.cs, StationViewFade.cs; StationBot/StationSoloBotTest.cs integrates the remembered safe spawn into the existing Solo entry.

Modified city mission scripts under Assets/CityNightPresentation/Mission/Runtime: FireTruckMission.cs, FireTruckSeating.cs, FireTruckWheelController.cs, FireTruckPresentation.cs. Related scene-specific incident/material assets and Unity metadata are saved alongside the station implementation. No package installation or imported source-model changes.

## Verification and limits

- CooperativeMissionVerification.json: mission ordering/role gates, garage/boarding checks and duplicate-event guards pass; missing scripts 0, missing materials 0; one XR Origin, teleport provider, hand ray and station mission view.
- Earlier TransitionVerification.json records six tracked-offset spawn fixtures, approximately 0.000002 m placement error, unchanged local camera pose/height, bidirectional safe stair-landing arc and invalid-destination checks. These are geometry/Edit Mode tests, not tracked-user observations.
- Final Unity ground-truth console: compilationFailed false, consoleErrors 0, consoleWarnings 4 (existing OpenXR warnings). CLI history contains earlier tool/evaluation errors; they are not current compiler failures.
- Targeted hashes still match for ProjectSettings, build settings, layers, physics settings, StationLobbyState, SeatedDispatchLobby and source network visual prefab. Core bot assets/source XR prefab were preserved. Lobby scene instance overrides necessarily changed; byte identity of the whole scene is not claimed.
- Final save succeeded. Only Assets/S1Test.unity remains open, clean; compiling, importing, baking, building and Play Mode were false at final receipt. Unity and Windows remain running normally.
- No Play Mode, standalone build, Git, package installation, lighting bake or Windows power operation was performed. No runtime frame rate or rendered-triangle compliance is claimed by this station task.

Required physical regression: both control-mode spawn positions and absence of rollback; palm-up arc/reticle/confirm/cancel plus pinch/grab; both-direction staircase access; two-player alarm/tablet/garage replication and occupied-door safety; explicit boarding and local stereo fades; city seat synchronization, G29 input, navigation/timer; Solo station-only behavior. Implementation is saved, but these end-to-end results remain unverified.
