# Firefighter driving mission — completion report

Target: demo_city_night. Date: 14 November 2026.

Windows Sleep is cancelled. The old task-owned helper was removed, no matching helper process was running, and no new power action is scheduled. Windows is left running; unrelated Windows tasks were not changed.

## Implementation

The actual firetruck from Assets/FireStation/Scenes/Sample.unity was reused, including its Perfectvertex meshes, cabin, steering wheel and four wheel models. It previously had a chassis collider but no driving system. Neither the large FireEngine.fbx nor the placeholder car was used.

MISSION_FIRE_TRUCK is now an instance of the scene-specific MissionFireTruck.prefab, containing one Rigidbody, four WheelColliders, one active input/physics adapter, two crew seat anchors, one NetworkObject, the existing owner-authoritative ClientNetworkTransform, siren lever/lightbar/audio, passenger tablet and windshield timer.

The imported BasicCarController is a disabled tuning reference consumed by the active adapter. Its source package and original Input Actions were preserved. Imported nitro, drift, duplicate PlayerInput, gearbox and WheelVisuals scripts were not added. No player autopilot was implemented.

The physics setup is a prototype: 12,000 kg, 65 km/h forward limit, 12 km/h reverse limit, 0.59 m wheel radius, 4.87 m wheelbase, analog torque, separate braking, direction-change braking and speed-sensitive steering. It is not manufacturer-validated vehicle dynamics.

## Existing session architecture

StationSessionFlow extends the existing authority-owned flow. After the original two-player readiness and station spawn acknowledgements, the same network scene manager loads the city additively. Loading has both-client validation and a timeout; driving remains locked until it completes.

StationLocalRig reuses its existing fade during city loading and unloads the city during normal post-shutdown lobby recovery. No station or lobby scene asset was edited. The city alone was appended to EditorBuildSettings, retaining existing entries.

FireTruckSeating reuses the existing local XROrigin and avatar. Both seats follow vehicle motion while preserving tracked head/hand offsets. Only local locomotion providers and the local CharacterController are disabled while seated, with restoration on release. The existing Seated, SeatPosition and SeatYaw fields feed the original avatar/IK replication. No XR Origin or network player was duplicated.

Seat floor anchors are (-0.75,1.15,2.20) and (0.75,1.15,2.20) in truck-local coordinates. Initial eye height is 1.35 m above the anchors, refined after inspecting the real driver cabin. Device-specific body/hand calibration remains untested.

Static station roots are hidden locally during city driving and restored on exit; persistent network/XR roots are retained. The lobby directional light is temporarily suppressed to protect the nighttime presentation. Station readiness retains the original input checks; city driving remains available through temporary optical hand occlusion at the wheel while the headset is tracked.

## G29 and authority

The connected Logitech G29 layout exposes stick/x, z, rz and button6. The new FireTruckDriving.inputactions copies the imported Drive map and uses:
- Steering: stick/x.
- Throttle: z.
- Brake: rz.
- Pedals: Invert, then Normalize(min=-1,max=1,zero=-1).
- Reverse toggle: button6. R is available only with explicit keyboard testing.
- Existing handbrake bindings.

Keyboard and offline driver testing default to disabled. A private runtime asset is enabled only for the local assigned driver. Device filtering/hot-plug handling exclude unrelated joysticks. Missing devices, tracking loss or stale input lead to authority braking. Steering center/range/deadzone and processed pedal rest/full endpoints are configurable in the Inspector.

The session authority runs the sole truck physics simulation. Driver RPCs validate the sender against the existing lobby driver role and clamp finite input. The passenger cannot enable vehicle controls. The existing NetworkTransform synchronizes the truck.

Civilian simulation is authority-only in a live session. One 10 Hz snapshot carries six vehicle distances, speeds, shoulder offsets, density and three signal states. Clients present snapshots rather than independently simulating traffic. Initial snapshots snap to current authoritative progress before interpolation. No civilian NetworkObjects were added.

Serialization, first-snapshot behavior and density roundtrips were checked. Live two-peer synchronization, latency, migration and device operation were not tested. Both peers require matching updated code/builds; disconnects block driving and use the existing reconnection flow.

## Emergency systems

The ceiling-mounted lever reuses XRLever, with a configured handle/collider and runtime event bindings. Replicated updates suppress recursive RPCs. Interaction and lever visuals respect authoritative mission state.

The red/blue lightbar uses emissive shared materials and the shared network clock. A generated four-second siren loops through the existing SFX mixer group. There are no realtime flashing lights.

Priority uses exclusive movements, yellow and all-red clearance. Occupancy delays conflicting greens, including while the truck clears the junction. Normal phases restore after the priority request ends and clearance is safe.

Nearby civilians seek prevalidated shoulder space, slow and stop after pulling aside, clear already-reserved intersections first and resume afterwards. The arrival frontage is excluded from shoulder yielding. This remains a lightweight prototype, not unrestricted autonomous lane planning.

## Route, navigation and mission

The original 597.89 m route and 81 waypoints remain. There are 598 derived arrow samples, spaced 0.993–1.001 m apart. Only nearby arrow instances are submitted, with a 35 m range and smooth near/far fading. No permanent arrow GameObjects or truck autopilot were added.

The tablet displays the route, truck position/heading and the residential incident location. Its Follow/Route button is bound to the map and restricted to the assistant at runtime. CanvasRenderer, tracked-device raycaster and UI-layer configuration are present. A missing map renderer found during review was fixed.

The windshield timer displays authority elapsed time plus red-light penalties. Correct-direction red stop-line crossings add 10 seconds on the authority. Arrival requires the truck to be nearly stopped, upright, correctly oriented and inside the prepared bay for two seconds. Completion displays ARRIVED, brakes the truck and releases emergency traffic behavior.

The incident target is the existing mid_house_1_2 (3) near (151,-206) in X/Z. The stopping bay is near (140,-192), separate from the moving lane. No fire, smoke, rescue or firefighting phase was added.

## Physical cleanup and lighting

- Deactivated 63 redundant bench instances and two sightline-obstructing hedge instances.
- Removed two empty former PROPS groups.
- Retained original imported assets, major buildings and integrated road meshes.
- Reduced active decorative culling entries from 1,123 to 1,058.
- Reused all six moving and six parked civilian prefabs.
- Retained organized signs, parking, stop lines, crossings, bollards and incident frontage.
- Preserved the 12 city lightmaps, 13 baked reflection probes and existing lighting data.
- Retained 24 localized road-light pools and five shadow-free junction/arrival lights.
- Reused the truck light as a forward shadow-free driving headlight.
- Preserved cool-blue atmosphere, warm streetlights, emissive windows and scene-specific materials.

There was no lighting/occlusion bake or shared URP modification. Existing baked shading cannot be redistributed or completely removed beneath hidden furniture. Road pools approximate local illumination; they are not new GI. The 449,673-probe dataset remains a memory concern.

## Verification

| Item | Result |
|---|---|
| 1. Compilation | Completed without C# compilation errors. |
| 2. Missing references | Final scan recorded in MissionFinalVerification.json. |
| 3. Vehicle controller | Isolated preview physics: acceleration, braking, reverse and steering passed. |
| 4. Input Actions | Scene-specific bindings and one active reader configured. |
| 5. G29 calibration | Device/control paths detected; endpoint math passed; physical calibration untested. |
| 6. Seat anchors | Two distinct anchors and real cabin views reviewed; headset posture untested. |
| 7. Locomotion | Local gating/restoration implemented; physical XR testing unperformed. |
| 8. Multiplayer | Role validation and serialization checked; two-peer play untested. |
| 9. Siren lever | Handle/collider/event wiring inspected; physical grabbing untested. |
| 10. Lights/audio | Components, shared clock and SFX routing configured; hardware audio comfort untested. |
| 11. Yielding | Isolated agent pulled aside 3.3 m, stopped on asphalt and resumed. |
| 12. Signal priority | Exclusive green, occupied clearance, grant and restoration checks passed. |
| 13. Navigation | 598 approximately one-metre samples checked; near-only fading implemented. |
| 14. Tablet | 327 vertices generated; map visible; runtime-only button binding and callback verified. |
| 15. Timer/penalties | Authority logic and stop-line cases checked; full driven mission untested. |
| 16. Destination | Residential building reference bound to mission and map. |
| 17. Arrival | Parking-envelope cases passed; complete route drive untested. |
| 18. Cleanup | 65 clutter instances hidden, two empty groups removed; source assets retained. |
| 19. Lighting | Driver views reviewed; preserved bake; no expensive rebake. |
| 20. Rendering | Quest rendered-triangle/frame-time budget remains UNVERIFIED. |

The isolated physics world advanced the truck 10.33 m and reached 17.14 km/h under test input, braked to approximately zero, reversed at 1.97 m/s and turned 20.48 degrees. Half pedal produced half torque. This did not run Play Mode or actuate the G29.

The civilian recheck simulated 180 seconds: all six vehicles progressed, all four tested gates held at red and resumed on green, and sampled oriented footprints had zero overlaps.

Actual art-envelope checks used approximately 9.62 m length and 3.34 m width including mirrors. All 1,316 sampled corners were on asphalt. This is not a complete live swept-path or hardware validation.

## Rendering and memory limitations

Active city-plus-truck mesh-instance inventory: 483,940 triangles. Each complete existing avatar prefab: 55,251 skinned triangles. These are inventories, not rendered counts.

Forty-eight analytical samples relocate the actual truck/driver viewpoint along the route, use a 100-degree field of view and 1000 m far plane, and account for head turns, configured decoration culling, both complete avatar inventories and a navigation/UI allowance. The peak estimate is 360,242 per eye, or 720,484 for simple two-eye geometry.

THE 800,000 CAMERA-VISIBLE RENDERED-TRIANGLE LIMIT IS UNVERIFIED. Actual stereo behavior, additional passes, additive scenes, avatar interiors, overdraw, draw/SetPass calls, CPU timing, memory and runtime culling require active XR-camera profiling on Quest. Editor counters were not reliably attributable to that camera. No Quest build or live XR profiling was performed. The approximately 3 GB Editor allocation observation is not a Quest memory measurement.

Vehicle LOD culling, centralized decoration culling, instancing-ready shared materials, static batching flags, limited shadow-free lighting and bounded traffic updates remain. No costly geometry replacement, texture reimport, memory snapshot or package installation was used.

## Files

Only demo_city_night.unity was saved as a scene. MissionAssetManifest.json contains the full file/object inventory.

New runtime scripts in CityNightPresentation/Mission/Runtime:
- FireTruckWheelController.cs
- FireTruckMission.cs
- FireTruckSeating.cs
- FireTruckEmergency.cs
- MissionTabletMap.cs
- FireTruckPresentation.cs

New editor script: Mission/Editor/FireTruckMissionBuilder.cs.

New Mission/Assets include MissionFireTruck.prefab, FireTruckDriving.inputactions, MissionArrow.asset, NavigationArrow.shader, NavigationArrows.mat, SirenWail.wav, cabin/lightbar materials and truck material copies.

Extended existing scripts:
- CityNightPresentation/Scripts/CitySignalController.cs
- CityNightPresentation/Scripts/CityTrafficPath.cs
- CityNightPresentation/Scripts/CityTrafficManager.cs
- FirefighterAvatar/Station/StationSessionFlow.cs
- FirefighterAvatar/Station/StationLocalRig.cs

ProjectSettings/EditorBuildSettings.asset now includes the city. Existing scene entries and XR configuration references remain.

No XR/player prefabs, avatar/IK scripts, original truck/controller assets, original Input Actions, other scene assets or Git state were edited by the implementation.

## Remaining checks and use

Start through the existing S1Test lobby/role/readiness flow with matching updated peers. The city reuses their rigs through the original network manager. Starting the city alone does not create substitute XR/network players.

Remaining device checks: physical G29 axes/calibration and reverse mapping; tracked seating/hands/body alignment; two-player transitions/authority; actual lever/tablet interaction; audio comfort; and a complete manual drive. Quest rendering, frame time and memory must be profiled before claiming the performance budget.

Unity still reports NativeFormatImporter consistency warnings for Assets/XR/Settings/OpenXR Package Settings.asset. XR configuration was not rewritten to suppress them. They remain documented for runtime investigation.

Force feedback and the firefighting phase were not implemented. Windows remains running; no Sleep, hibernation, shutdown, restart or logoff is scheduled or executed.

Additional reference check: a read-only S1Test preview found exactly one XROrigin with MainCamera, both controller references, modality, the original local avatar/coordinator, network avatar prefab and fade renderer correctly assigned. The scene was not saved or modified.
