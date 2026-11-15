> Superseded by MissionCompletionReport.md. Windows Sleep is cancelled; its helper was removed. Leave Windows running.

# Night city implementation report

Target scene: Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity

## Completed environment work

The existing city, integrated roads, major buildings, original assets, and previous scene-specific lighting materials were retained. This pass adds physical road infrastructure, civilian traffic, parking, a prepared incident frontage, road illumination, and route data.

- Reorganized 614 existing outer prop prefab instances under CITY_PROPS by commercial, residential, and incident-frontage use, with furniture/planter/barrier groups. Their world transforms and prefab source references were preserved.
- Added ROAD_NETWORK with RoadMarkings, RoadSigns, Intersections, and ParkingAreas.
- Added four conformed lane arrows, nine stop lines, six marked parking bays, and a yellow-bordered incident stopping bay. Existing crossings, curbs, sidewalks, and lane textures were reused.
- Added seven fitted street/direction/location signs and four incident-frontage bollards.
- Added TRAFFIC_SYSTEM with TrafficManager, TrafficLights, VehiclePaths, MovingVehicles, and ParkedVehicles.
- Added FIREFIGHTER_DRIVING_ROUTE and CITY_OPTIMIZATION. CITY_LIGHTING contains the new local lighting.
- No original mesh geometry, building placement, or original prop world transform was altered. This avoids invalidating preserved baked shadows. New infrastructure and vehicles occupy measured road shoulders and sidewalks.
- No fire station, fire truck, fire, smoke, mission logic, player autopilot, XR changes, network changes, input changes, other scene edits, package installations, Git operations, or standalone builds.

## Route and clearance

The refined route is 597.89 m with 81 ordered WP_001–WP_081 transforms. It retains the proposed eastern-city journey and four turns (right, right, left, left).

FIREFIGHTER_DRIVING_ROUTE contains FireTruck_DrivingSpawnPoint, Route_Waypoints, Traffic_Intersections, Incident_ApproachRoad, Incident_ArrivalPoint, and FireTruck_StopZone. CityDrivingRoute exposes the ordered data and draws only selected-object editor Gizmos.

The spawn remains near (20, 0.92, 22), facing east. Arrival was refined to the asphalt shoulder near (140, road height, -192), leaving the moving eastbound lane separate from the stopping bay. The stop-zone preview is 18 m long by 4 m wide.

329 preliminary footprint samples used a 10 m long, 2.6 m wide truck envelope. No sampled footprint corners were outside asphalt; upward static ray checks found no obstruction within the preliminary 4.5 m clearance envelope. These are preliminary geometric checks, not actual truck suspension, collider, mirror, swept-turn, or VR validation.

## Traffic lights and civilian movement

Three actual three-leg intersections have nine signal heads in total. Unused approaches were omitted. Each head has emissive red, amber, and green lenses and a stop-line transform.

Phases are exclusive by approach: eastbound=0, southbound=1, westbound=2, northbound=3. Only existing approaches are included. Defaults are 13 seconds green, 3 seconds amber, and 2 seconds all-red. Intersections have phase offsets. Exclusive approach phases avoid opposing-left-turn conflicts.

Six moving prefab instances and six parked prefab instances use the following six imported prefabs once in each group:

- Hatchback Car_15
- N Van_10
- Pick Up_11
- Classic Car_9
- N_Muscle Car_10
- Sport Car_39

Two closed civilian lane paths are approximately 510.46 m and 457.25 m. Moving traffic follows road-conformed paths at about 6.5–7 m/s, slows for turns, brakes at signal gates, reserves occupied signal intersections, maintains following gaps, and performs bounded obstacle checks. Central simulation runs at 20 Hz; distant poses and obstacle checks update less frequently. Density is configurable up to the supplied six agents. There are no WheelColliders, per-car rigidbodies, NavMeshes, or NetworkObjects.

The 180-second deterministic Edit Mode check exercised actual manager stepping and static/vehicle physics queries without entering Play Mode. All vehicles progressed (approximately 466–715 m), all four isolated gate tests stopped at red and resumed on green, and sampled oriented vehicle footprints had zero overlaps. This is prototype validation, not live multiplayer or headset testing.

Traffic is local only. Future synchronization should disable localSimulation on clients and replicate stable vehicle IDs, lane/path progress, speed, authoritative simulation time, signal phases, and late-join snapshots. Public step/clock methods and generic hold fields are extension points; emergency priority/yielding and penalties are not implemented.

## Lighting and materials

- Preserved the 12 existing lightmaps, LightingData, 13 baked reflection probes, baked lights, and all 2,769 original renderer lightmap assignments.
- Retained and extended scene-specific emission/window, asphalt, sidewalk, and roadside material treatment.
- Added a low-cost cool-blue gradient sky shader on the existing scene-specific sky material.
- Added 24 road-conformed, localized additive light pools. These approximate warm illumination on asphalt without a scene-wide post effect or a rebake.
- Added five localized, shadow-free spotlights at key intersections and arrival for actual road/frontage illumination. Existing URP additional-light limits are unchanged.
- Signals and vehicle lamp surfaces use emissive materials, with no realtime signal/headlight sources.
- New road paint and sign panels use restrained unlit colors for legibility.
- Vehicle materials are scene-specific shared copies; imported vehicle materials and prefabs remain unchanged.
- All 35 scene-specific materials are instancing-ready. There are 64 new reusable/conformed mesh assets. Full asset and object lists are in SceneAssetManifest.json.

The road pools are a localized visual approximation, not new baked GI. Existing baked light pools, reflections, shadows, and probe illumination remain unchanged. A rebake was skipped because the system had under 1 GB free physical memory during inspection and the existing 449,673-probe dataset makes an uncontrolled bake unsafe.

## Geometry, visibility, and performance

- Prepared static batching flags for 2,825 static mesh objects without manually combining or replacing city meshes.
- Disabled redundant dynamic shadow casting on 2,510 lightmapped static renderers.
- Added LODGroup distance culling to all 12 civilian vehicle instances; no unnecessary generated low-detail meshes.
- Central distance culling covers 1,123 decorative renderers: small furniture/planters at 140 m, building lamps at 190 m, vegetation at 230 m, with 20 m hysteresis. Essential road geometry and signal heads are not aggressively distance-culled.
- The future local camera may be explicitly assigned to CityVisibilityController; otherwise it discovers Camera.main without reconfiguring it.
- No occlusion bake, lighting bake, heavy post-processing, runtime reflection refresh, or new texture package.
- Existing lightmap/probe memory has not been rebuilt or compressed. This remains a significant Quest memory concern.

The final static mesh-instance inventory is 388,895 triangles before stereo and additional rendering-pass effects. The unique-mesh inventory is 145,728 triangles; shared meshes are counted once in that second figure. This is the sum of instances, not a unique-mesh count or a measured rendered-triangle count.

48 visibility estimates cover spawn, the straight, all three signal approaches, final turn, arrival, and a dense district, each with six head directions. A 90-degree camera and a 2.3 m driver eye height were used. The peak estimate was 123,304 visible mesh-instance/glyph triangles for one eye, or a simple two-eye geometry estimate of 246,608. These estimates include configured decoration/vehicle culling but do not establish actual GPU pass costs or VR stereo implementation behavior.

**The 800,000 rendered-triangle limit is UNVERIFIED.** There is no active player XR camera in this scene. The available Editor counters stayed fixed across changed Scene View captures (247,260 triangles / 346 batches / 82 SetPass calls), so they cannot be attributed reliably to the driver camera. No live Quest frame rate, draw-call, SetPass, stereo, GPU, or texture-memory compliance is claimed. Final on-device profiling must include head motion, shadow/additional passes, actual local camera settings, and late mission assets.

## Files and scripts

All added assets are under Assets/CityNightPresentation, alongside the preserved earlier material work. The only scene saved by this task is demo_city_night.

Runtime scripts:
- Scripts/CityDrivingRoute.cs — ordered route data and editor-only preview.
- Scripts/CitySignalController.cs — safe exclusive phases, durations, signal visuals, generic external hold.
- Scripts/CityTrafficPath.cs — cached lane distances and signal gates.
- Scripts/CityTrafficManager.cs — shared local civilian simulation and following/reservation/obstacle behavior.
- Scripts/CityVisibilityController.cs — local-camera decoration culling.

Editor scripts:
- Editor/CityNightBuilder.cs — guarded, repeatable target-scene construction using source prefab instances.
- Editor/CityTrafficValidation.cs — deterministic Edit Mode traffic checks with scene-state restoration.

Shaders:
- Shaders/RoadLightPool.shader
- Shaders/NightSky.shader

Reports:
- LayoutValidation.json
- TrafficValidation.json
- VisibilityEstimates.json
- PreservationValidation.json
- SceneAssetManifest.json
- FinalSaveVerification.json

## Validation and future work

Reference checks found zero missing scripts, zero missing materials, zero missing prefab assets, and zero shader errors. All 2,769 original renderer transforms and lightmap assignments were preserved. The shared URP asset is unchanged.

Remaining work is future mission integration: actual fire truck dimensional/swept-path checks, cinematic positioning, G29 controls, navigation UI, siren and emergency priority, multiplayer authority/replication, and standalone Quest runtime profiling. These systems were intentionally not added.

No active bake, build, compilation, or asset import may remain before Sleep. FinalSaveVerification.json records the final save/readiness checks. Windows Sleep is requested only after that verification passes; shutdown, restart, hibernate, logoff, and forced application closure are not used.


