# Player falling on Play Mode — fixed

15 November 2026. Target: saved lobby startup and role selection; no networking configuration or shared XR prefab edits.

Root causes:
1. The saved scene XR Origin was at (0, -1.31, -12), outside OperationsFloor (floor Z range -2.5 to 4.5). With control role None, lobby placement never ran.
2. The original CharacterResetter was enabled in the scene. Its OnEnable subscribed to disconnected state and queued the old (0, 0, -12) teleport before StationLocalRig.Awake disabled it. That request remained pending while TeleportationProvider was disabled and executed when Assistant enabled teleport, sending the player outside the floor. This second fault was reproduced in live Play Mode after the initial position fix.

Changes:
- Assets/S1Test.unity: initial XR Origin moved to the measured clear midpoint between existing lobby anchors, (0,0,0). Floor support was raycast-verified; a 0.28 m radius standing capsule found no obstructing scene colliders. CharacterResetter is disabled in this scene before OnEnable/Start can queue a request, and its scene-specific offline reset backup is now (0,0,0).
- Assets/FirefighterAvatar/Station/SeatedDispatchLobby.cs: initial unassigned-role placement is performed once and again when initial tracking becomes available, using the existing anchor midpoint. Unassigned locomotion remains blocked. Selecting Assistant enables its existing controller/movement; Driver uses existing seated eye placement. There is no continuous camera correction.
- Assets/FirefighterAvatar/Station/StationSpawnPlacement.cs: existing placement accepts a position/rotation overload, preserving camera-local tracking and the same CharacterController handling.

Actual Play Mode observations after both fixes (see SpawnFallPlayMode.json):
- Frame 661, time 5.66 s: unassigned root (0,0,0), resetter disabled.
- Frame 1593, time 12.83 s: Assistant root (0.85,0,0), Smooth Move and Teleport enabled.
- Frame 5333, time 41.80 s: Assistant still (0.85,0,0), no falling.
- Frame 6136, time 47.88 s: Driver seated, exact seated eye target, all locomotion providers disabled. Its small negative root Y is the intentional HMD eye-offset alignment, not a fall.
- Frame 6791, time 52.87 s: switching back to Assistant restores (0.85,0,0), enabled CharacterController, Smooth Move and Teleport.
- Frame counts and game time advanced, confirming an actual running player loop. Runtime console: zero errors; six warnings. Compilation: no errors.

Test limits: no physical Quest/G29 session, walking input, full mission journey, or connected multiplayer was exercised. CharacterController.isGrounded read false at the sampled idle Assistant frame; transform height remained stable for the sustained observation and actual floor support was verified, so no claim of that flag being true is made.

Test ended; temporary background override removed; Unity left open outside Play Mode. No Git, packages, builds, shutdown or sleep operations.
