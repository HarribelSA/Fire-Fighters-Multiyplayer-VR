# Transition and VR stutter performance implementation

Completed 15 November 2026. Targeted changes retain the existing single local XR Origin, additive scene flow, role selection and NGO authority model.

## Outcome and limits

Confirmed improvements: repeated sole measurement is substantially cheaper for unchanged skin poses; duplicate traffic-path initialization was removed; unused assets can be released safely after returning to the lobby. Transition-stage telemetry now distinguishes local loading/activation, measured initialization scopes, rendered black, tracking/placement readiness and network acknowledgements.

The major first-pass asset-loading stalls are NOT fully resolved. Multi-second player-loop gaps remain in Editor loading measurements. These changes must not be presented as a complete cure for Quest stuttering or a guaranteed faster full mission transition. No physical headset, G29, two-peer session, build or complete mission was tested.

## Measurements

Offline additive load probes used the existing running lobby/player, loaded Station then City, observed activation/Start, then unloaded City and Station. They did not invoke Ready or bypass XR/network/mission checks. They are controlled offline loading exercises, not full tracked mission transitions. Editor caching, asset residency, native integration, rendering and host scheduling affect the results; disk caches were not forced cold.

| Editor pass | Station load through activation | City load through activation |
|---|---:|---:|
| Initial reference | 8.096 s | 9.831 s |
| Warm low-priority trial | 0.094 s | 0.643 s |
| Warm NORMAL-priority control | 0.087 s | 0.595 s |
| Intermediate after-change pass | 7.780 s | 9.803 s |
| Final implementation pass | 6.087 s | 9.146 s |

The warm normal-priority control was faster than the low-priority trial. The low-priority change was rejected and is not shipped. The much faster warm passes demonstrate the importance of resident assets; they do not demonstrate a code speedup. The final numbers are observations from single passes, not statistically established loading-speed improvements.

Initial maximum observed frame gaps were 5.045 s for Station and 6.712 s for City. Final gaps were 4.767 s and 6.636 s: substantial stalls still remain. The final load samples recorded no Gen0 collections and only about 1.9 MB / 1.4 MB managed-heap growth, alongside about 844 MB / 1,078 MB Unity allocated-memory growth. This supports investigating native asset integration/residency rather than assuming XR recreation or managed GC caused these particular gaps. Exact shader compilation, GPU upload, mesh/collider integration and OS scheduling costs were not isolated.

Instrumented initial managed scopes were small relative to the multi-second envelope: Station setup Awake approximately 1.14 ms, mission view Awake 3.40 ms and Start 1.35 ms; city vehicle/input Awake 8.39 ms and traffic initialization 1.81 ms. Scope timings vary by pass and do not account for every Unity/native initialization function.

Sole microbenchmark on a clone of the actual current avatar, 160 static-pose samples per shoe:

| Work for both boots | Mean combined time |
|---|---:|
| Original BakeMesh + complete vertex scan | 0.3707 ms |
| Unchanged-pose cache | 0.00931 ms |

This is approximately 97.5% less time for this measured static-pose work, not a 97.5% frame-rate improvement. Changed skin poses still use BakeMesh. Thirty-six changed-foot poses and 24 rigid world motions per shoe were compared against the original full-vertex measurement. Maximum observed sole error was below 0.001 mm. All 24 rigid-motion samples per shoe reused skin geometry. Runtime idle-lobby observation also showed reuse (17 bakes and 190 hits per boot in the sampled session); walking/deforming-pose hit rates will differ.

Memory probes after BOTH content scenes were already unloaded:
- Raw unused-asset probe: 2,755,141,058 -> 1,933,551,397 Unity allocated bytes, about 822 MB released in 344 ms.
- Actual guarded cleanup under the existing rendered-black fade: 2,755,014,745 -> 1,625,092,177 bytes, about 1,130 MB released in 415 ms. One XR Origin remained.

A bounded Editor camera fade probe completed with rendered-black confirmation: about 1.131 s out and 1.323 s in on the throttled Editor loop. Inspector durations remain 0.35 / 0.45 seconds; missed/throttled frames and the anti-jump cap extend wall time in that environment. This is not Quest stereo-fade timing.

## Implemented changes

1. Exact-pose sole cache. Cache all baked vertices and all four skin-bone transforms for each current boot. Unchanged deformation reuses that geometry; rigid movement reprojects the vertices with the current renderer matrix. Actual bone-pose changes, mesh changes, blend-shape meshes and cloth use the original bake path. Existing head, arm, leg, finger and wheel-grip IK remain. No approximate replacement foot target or second IK solver was added. Each avatar retains its original single scratch mesh; the small managed vertex cache adds roughly tens of kilobytes, not scene-scale assets.
2. Traffic paths initialize once per distinct path during setup rather than once per vehicle. Vehicle positions and all path/signal references are still initialized before use.
3. Traffic simulation starts only on the authority when the existing crew-ready gate is satisfied. Initial meshes, paths, poses, signal references and the vehicle controller remain initialized before reveal. No readiness requirement was removed.
4. The existing fade no longer consumes an arbitrarily large hitch delta in one update and jumps from black to fully visible. Its maximum visual step uses 0.05 seconds; no fixed loading sleep was added. This prioritizes a gradual reveal on a slow frame and may extend fade wall time at low frame rates.
5. Safe return-only memory release. After normal content unload and restoration of the lobby, release unused assets only if enabled on StationLocalRig, both Station and City are absent, no mission session remains active, Unity allocated memory exceeds 1.5 GiB, and the existing fade has confirmed rendered black. No referenced scene assets are forcibly unloaded. Cleanup is not run during driving or while Station content is still retained. It is an explicit memory-versus-return-latency tradeoff and may add the measured cleanup cost under black.
6. Bounded local diagnostics. At most 128 samples are retained; no new manager, RPC, player, XR Origin or EventSystem. Load events and readiness marks are recorded rather than replaced. One completion summary is logged per measured transition. Data are accessible via TransitionDiagnostics.Samples; profiler markers Firefighter.SoleMeasure and Firefighter.SoleBake expose the IK work.

The Station and Lobby remain loaded during City as before. Active station/tablet/bot/return references were not severed to chase memory savings. Releasing more memory during City would require deliberate content separation and further reference/lighting/return validation; that was not attempted blindly.

## Files

New runtime helpers:
- Assets/FirefighterAvatar/Scripts/AvatarSoleCache.cs
- Assets/FirefighterAvatar/Station/TransitionDiagnostics.cs
- Assets/FirefighterAvatar/Station/TransitionMemory.cs

Modified runtime scripts:
- Assets/FirefighterAvatar/Scripts/FirefighterAvatarDriver.cs
- Assets/FirefighterAvatar/Station/StationViewFade.cs
- Assets/FirefighterAvatar/Station/StationLocalRig.cs
- Assets/FirefighterAvatar/Station/StationSessionFlow.cs
- Assets/FirefighterAvatar/Station/StationSessionFlow.Solo.cs
- Assets/FirefighterAvatar/Station/StationMissionView.cs
- Assets/FirefighterAvatar/Station/StationSceneSetup.cs
- Assets/FirefighterAvatar/StationBot/StationSoloBotTest.cs
- Assets/CityNightPresentation/Scripts/CityTrafficManager.cs
- Assets/CityNightPresentation/Mission/Runtime/FireTruckMission.cs
- Assets/CityNightPresentation/Mission/Runtime/FireTruckSeating.cs
- Assets/CityNightPresentation/Mission/Runtime/FireTruckWheelController.cs

New Editor measurement helpers:
- Assets/FirefighterAvatar/Station/Editor/TransitionPerformanceProbe.cs
- Assets/FirefighterAvatar/Station/Editor/TransitionPerformanceVerification.cs

No scene, prefab, material, shared Input Action, NetworkManager setting or package configuration was intentionally modified. TransitionPerformanceProtection.json confirms unchanged hashes for all three target scene files, checked project settings, manifest, StationLobbyState and source FirefighterNetworkVisual prefab. Ownership/RPC architecture remains the same.

## Verification

- Compilation passed.
- 30 focused bidirectional Solo/state/seated-avatar checks and 35 existing gameplay regression checks passed. These cover role permutations/conflicts, human/bot complement, dispatch/tablet/garage/boarding gates, seated body under synthetic head movement, Assistant locomotion, wheel-grip release, handle, signal phases, timer braking, serializers and existing computer activities.
- No missing scene scripts/materials found by the focused regression suite.
- Final runtime load probe: exactly one XR Origin and one active HMD camera for BOTH loaded content stages, controller initialized, and unused-asset cleanup correctly rejected while content scenes remained loaded.
- Actual guarded return cleanup and rendered-black fade exercised with the existing local player/camera, without a real headset or mission readiness bypass.
- No duplicate scene-load mechanism was added. Existing issued-epoch, busy, cityLoadIssued and mission-stage protections remain.

Detailed evidence: TransitionProbe_before.json, TransitionProbe_low_priority_trial.json, TransitionProbe_warm_normal_control.json, TransitionProbe_after.json, TransitionProbe_final_after.json, SolePerformanceComparison.json, TransitionUnusedAssetsProbe.json, TransitionGuardedCleanupProbe.json, TransitionFadeProbe.json, and the existing refreshed regression JSON reports.

Remaining testing: device CPU/GPU profiler capture on Meta Quest; actual scene activation/shader-upload breakdown; both full Solo role transitions with tracked input; two-peer fade/load/seat acknowledgements and authority handover; walking/crouching feet and wheel grips during real tracking; G29 driving and post-return memory. No measured network-wait or XR-tracking-ready duration is claimed without those sessions. The new instrumentation is prepared to separate those stages when run.

All completed source/report work is saved. Unity is left running outside Play Mode with the saved lobby. No Git, packages, builds or Windows power operations were performed.
