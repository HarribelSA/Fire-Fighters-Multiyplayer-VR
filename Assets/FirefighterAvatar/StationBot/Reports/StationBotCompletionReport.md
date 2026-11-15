# Station-only solo bot implementation

Saved 14 November 2026. Windows remains running. No Git operations, packages, builds, Play Mode, lighting bakes, or power actions were used for this change.

## Result and use

Open `Assets/S1Test.unity`. Use the existing hand-tracking/G29 or controller console selection. At the bottom of the selected console, select **PLAY WITH BOT**, then its separate **READY** button. A tracked headset is required for that runtime Ready action.

The existing local XR rig and firefighter avatar move to the station's existing standing spawn anchor. One local firefighter bot appears at the opposite anchor, approximately 2 meters beside the human. The station loads additively; the lobby remains loaded. No extra XR Origin or network player is instantiated. Existing XR locomotion providers, actions and modality configuration remain under the existing rig's control. The bot has no network client, NetworkObject, ownership, or vehicle input.

| Existing selected configuration | Human role data | Bot role data |
| --- | --- | --- |
| Hand tracking / G29, Driver console | Driver | Passenger / Assistant |
| Quest controllers, Assistant console | Passenger / Assistant | Driver |
| Two or more real connected players | Existing network roles | No bot |

The separate adapter reads the existing lobby's `preference` field, which already reflects explicit console selection and the lobby's modality fallback. It does not change that field or the control-selection configuration. A narrowly scoped `link.xml` preserves the field's reflection metadata for stripped player builds. The selected roles are captured at solo Ready; returning to the lobby and selecting the other configuration changes the next test's roles.

The bot exposes `HumanRole`, `Role`, `FutureDrivingEligible` and `DrivingAdapterActive`. The last is always false in this test. No driving input adapter, truck entry, traffic-light logic, navigation assistance gameplay, mission start, or city load is performed by the solo feature.

The station status panel displays both roles and provides **RETURN TO LOBBY**. A second real player detected during the test immediately disables/removes the local bot, cancels solo selection and returns the local test to the lobby. No Ready RPC, role claim, network spawn, ownership write, room creation, room join, or network shutdown is issued by the new scripts.

## Files and objects

Modified scene: `Assets/S1Test.unity`.

Only `STATION_SOLO_BOT_TEST`, its Transform, its `StationSoloBotTest` component, and the scene-root list entry were added. Serialized-block comparison confirms every other original scene block is unchanged and none was removed. See `ProtectionVerification.json`.

New runtime scripts in `Assets/FirefighterAvatar/StationBot/`:

- `StationBotRoles.cs`: read-only selection adapter, opposite-role mapping and solo eligibility policy.
- `StationSoloBotTest.cs`: separate local solo UI, Ready/load/standing-placement workflow, cleanup and teammate suppression.
- `StationBotFollower.cs`: smoothed following, turning, animation state selection and future role data.
- `StationBotNavigator.cs`: bounded local grid search, supported-ground sampling and capsule clearance checks.

New editor scripts in `Assets/FirefighterAvatar/StationBot/Editor/`:

- `StationBotInstaller.cs`: idempotent installation, bot-only visual/controller creation and animation polish.
- `StationBotVerification.cs`: isolated preview-scene role, UI, movement, obstacle, animation and original Ready-state checks.

New assets in `Assets/FirefighterAvatar/StationBot/`:

- `StationFirefighterBot.prefab`: independent copy of the existing firefighter visual; tracking/IK MonoBehaviours removed only from this new copy. It contains one `StationBotFollower`, no XR Origin and no NetworkObject. Full exterior helmet, mask, uniform, gloves, boots and breathing apparatus are visible.
- `StationBotLocomotion.controller`: Idle, Walk, TurnLeft and TurnRight states. Movement uses the existing grounded walk clip, with foot IK. Turning reuses its stepping motion while the follower smoothly rotates the root; it is not a newly imported motion-capture turn clip.
- `StationBotRelaxedIdle.anim`: bot-only copy of the human standing clip, with relaxed arms and fingers. Idle foot IK is disabled because the source clip retains walking foot-target curves. Human source animation assets remain unchanged.
- `link.xml`: selected-field metadata preservation only.
- `Reports/`: verification JSON, protection comparisons, save receipt, asset manifest, this report and appearance/walk screenshots. Unity generated corresponding `.meta` files.

The `Solo_PlayWithBot` and `Solo_Ready` buttons are runtime copies of existing button styling on both consoles. New copies have fresh click events. The original multiplayer Ready listeners are retained and untouched; its visual is covered only while solo is explicitly armed, then restored. No new EventSystem or XR input module is created. The runtime station panel uses a tracked-device graphic raycaster.

## Movement and appearance

The bot walks at up to 1.25 m/s, accelerates smoothly, turns at up to 110 degrees/second and maintains approximately 1.7 m following distance. It follows the human head's horizontal position and the rig's floor height. Ground and capsule checks prevent unsupported steps and detected obstacle penetration. It does not teleport to catch up, use WheelColliders, create a Rigidbody, or bake navigation data.

The planner searches at most 700 nodes per replan, with a 0.55 m cell size and approximately 0.9 seconds between normal replans. Cached collections and fixed overlap buffers reduce repeated allocations. Movement checks use the station's physics scene. Search failure makes the bot stop safely rather than cross walls or unsupported ground.

`StationBotAppearance.png` was visually reviewed in the isolated station. `StationBotWalk.png` shows a sampled walk pose. Temporary visual-review objects were removed and the original station scene was closed without saving changes.

## Validation completed

`StationBotVerification.json` records the final passing checks. All use Edit Mode preview scenes and isolated station physics, not a live XR session.

- Existing lobby selection correctly read for both roles.
- Two separate Play With Bot and Ready control pairs created; copied Ready buttons have no persistent multiplayer listeners.
- Both human and bot standing anchors have supported, clear station positions.
- Both opposite-role assignments correct; future driving remains inactive.
- Both bot scenarios route around a temporary obstacle, remain clear of detected geometry, and stop at approximately 1.708 m from the target.
- Idle, Walk, TurnLeft and TurnRight exercised; the actual Animator settles into Idle.
- Bot visual has no missing script or material, and its exterior skinned renderers are active.
- Bot prefab contains zero NetworkObjects and zero XR Origins.
- Two-or-more-real-player suppression policy and transition-entry guards pass.
- The unchanged two-player lobby-state implementation still accepts its original role claims, requires the two Ready states, and starts its original 10-second countdown.

Final save/reference check (`FinalSaveVerification.json`): lobby saved, one existing XR Origin, valid bot/lobby references, zero missing scripts and zero missing prefab instances in the open lobby. No city or station scene remains loaded in the Editor; only the saved lobby is open. Compilation, asset updating, lighting bake, build and Play Mode were all inactive at the save check. Unity reported no current console errors; four existing OpenXR/import warnings remain.

Hash comparison confirms unchanged: `StationSessionFlow.cs`, `StationLocalRig.cs`, `StationLobbyState.cs`, `SeatedDispatchLobby.cs`, `FirefighterNetworkVisual.prefab`, project settings, build scene settings and the original station scene. The serialized lobby comparison additionally protects the existing NetworkManager, player references, control configuration and other original scene objects.

## Limitations and future work

- Actual headset interaction, tracked-hand/controller locomotion, G29 hardware and a two-device multiplayer join/disconnect test have not been run. The three requested cases are verified at selection/policy/preview level; a live end-to-end network test is still required.
- Offline solo reuses the already present local XR rig/avatar. It intentionally does not manufacture a network-spawned player or fake teammate. If an existing one-person real room is already running, its existing network player is left untouched.
- The protected, pre-existing two-real-player `StationSessionFlow` contains an automatic city transition after `StationReady`. It remains unchanged. The new solo Ready bypasses that flow and stays in the station. Preventing that existing transition for real two-player sessions would require changing the protected flow; that change was not made.
- The lightweight local planner is not a complete multilevel NavMesh. Narrow staircases, all upstairs paths, dynamic doors, prolonged teleport following and the entire station have not been exhaustively traversed. It stops when a supported, clear path cannot be found. It has no network synchronization, which is intentional for the local test.
- Live locomotion restrictions already imposed by the existing selected role are preserved; the new feature does not replace the protected rig's locomotion policy. In hand mode, use the existing hand locomotion/teleport interaction.
- Future driving needs a separate authority/input-gated Basic Arcade Car Controller adapter, route/light/emergency handling, passenger navigation behavior and a deliberate mission-start transition. None is activated by this station test.
