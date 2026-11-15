# First mission intersection — visible signals, civilian queue and emergency response

Completed 15 November 2026. Target scene: Assets/Versatile Studio Assets/Demo City By Versatile Studio/Scenes/demo_city_night.unity.

The earlier claim that three existing signals were sufficient did not verify the user's actual first-junction experience. Inspection found that neither civilian loop visited junction 1. Existing lenses were about 5.92 m above the road and 7.3 m to the side of the lane stop line, making the intended challenge easy to miss.

Implemented:
- Repositioned the existing roadside signals closer to the controlled lanes, with red lenses about 3.97 m above road level and poles 4.1 m tall. Existing stop lines and route remain unchanged.
- Existing scene-specific Signal_Red, Signal_Amber and Signal_Green materials now use lightweight URP Unlit colors, with double-sided rendering where supported, so night visibility does not depend on external lighting. No new real-time lights, bloom, shared URP changes or rebake.
- Reused Civilian_01_Hatchback Car_15 and Civilian_02_N Van_10 as a visible queue at the first signal. Six moving vehicles total; no extra vehicle package or duplicated traffic manager.
- Added one path object, FirstJunction_ResponseQueue, under existing vehicle paths. Its approach follows the existing firefighter route, then continuously merges into the existing southern civilian loop. No unsupported northern road, off-road closure or teleport back to the prefix.
- First approach remains red until a valid stopped truck starts its 60-second wait. Without siren, mission time continues decreasing; after 60 seconds the existing phase controller grants green through safe clearance, and normal civilian traffic resumes.
- Pulling the existing emergency handle after the stopped/designated-red checks requests priority before the minute expires. Conflicting traffic still gets yellow and all-red clearance; never conflicting greens.
- Both queued civilians move gradually 3.3 m onto checked clear roadside space and hold there while emergency yielding is requested, even after the signal turns green. They do not immediately reserve the intersection and merge back into the truck lane. Yield requests clear after the truck passes using the existing spatial rules.
- Added bounded following checks between different path references where the new prefix merges into existing traffic.
- Expanded the designated-junction distance check to 65 m while retaining the stopped/stop-line conditions, allowing handle activation from behind the queue.
- Normal signal cycling is restored after the truck crosses the approach and leaves the junction, including its intended right turn.

Source files:
- Assets/CityNightPresentation/Scripts/CitySignalController.cs
- Assets/CityNightPresentation/Scripts/CityTrafficPath.cs
- Assets/CityNightPresentation/Scripts/CityTrafficManager.cs
- Assets/CityNightPresentation/Mission/Runtime/FireTruckMission.cs
- New Editor helper: Assets/CityNightPresentation/Editor/ResponseIntersectionUpdate.cs

Verification:
- 16 focused deterministic phase/traffic/path checks passed. They include red holding, no early natural green, safe normal opening after a minute, emergency yellow/all-red/exclusive green, ordinary queue resuming, both emergency vehicles holding aside, path merge continuity and active signal geometry.
- Real scene clearance overlap checks at both 3.3 m shifted civilian positions found no blocking colliders.
- A driver-height Scene View image visibly shows the red signal and queued van: response-signal-driver.png in the Codex visualization output folder. This is an Editor preview, not a headset screenshot.
- Actual isolated city Play Mode test (no room/network session): ordinary vehicles stopped at red with zero yield offset; mission timer decreased. A manually staged Rigidbody truck stopped behind the queue, starting the minute wait. RequestFromPull(true) was accepted through the existing mission conditions. Later the signal was Green with 32.45 seconds of ordinary wait still remaining, SirenActive true, AudioSource.isPlaying true, and both civilians stopped aside at yieldOffset 3.3 with no intersection reservation. Remaining mission time was about 222.13 seconds. Runtime frames advanced from 8 to over 12,000; this was not a frozen-frame test.
- The first test transform-only truck placement was overwritten by Rigidbody interpolation; that fixture was corrected using Rigidbody position/rotation before the valid stopped-approach test. No gameplay teleport was added.
- Runtime console had zero errors. AudioSource playback was observed; actual sound audibility on Quest was not tested.
- No physical Quest/G29, complete mission drive, connected two-peer session, or new Quest build was tested. The normal minute branch was checked by deterministic traffic simulation, not a physical one-minute headset wait. A previously installed headset build must be rebuilt/deployed to include these asset/code changes; no build was launched automatically.

No Git operations, package installations, networking configuration changes, avatar/input replacement, lighting bake or full build. Runtime test ended before final saving. Final save/idle checks are in ResponseIntersectionSave.json. Windows Sleep is requested only after all writes and verification finish; Unity is left open.
