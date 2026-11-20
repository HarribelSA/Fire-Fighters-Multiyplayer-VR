# Fire Fighters Multiplayer VR

## Simulator Trailer

https://github.com/user-attachments/assets/85a52547-79ba-4d1d-9487-b459625277a5

---

A cooperative virtual-reality firefighting training simulation for two trainees. A Driver and an Assistant respond to an emergency together: from the dispatch alert at the fire station, through an emergency drive across a night-time city, to extinguishing a residential fire and reviewing the crew's performance on a mission debrief.

The Driver uses a Windows PC VR headset with a Logitech G29 steering wheel. The Assistant uses a standalone Meta Quest 3. Both trainees share the same session over the internet.

---
## Download

The ready-to-run builds are available on the [Releases page](https://github.com/HarribelSA/Fire-Fighters-Multiyplayer-VR/releases). No Unity installation is needed.

| Version | Platform | File | Size |
|---|---|---|---|
| **v1.0.0** (2026-11-20) | Windows 10/11, 64-bit, PC VR | [FireFighters-Windows.zip](https://github.com/HarribelSA/Fire-Fighters-Multiyplayer-VR/releases/download/v1.0.0/FireFighters-Windows.zip) | ~950 MB (2.5 GB extracted) |
| **v1.0.1** | Meta Quest 3 (Standalone VR) | [FireFightersVR.apk](https://github.com/HarribelSA/Fire-Fighters-Multiyplayer-VR/releases/download/v1.0.1/FireFightersVR.apk) | APK |

### How to Run

**Windows (Driver Station)**

1. Download `FireFighters-Windows.zip` and extract it.
2. Connect your PC VR headset and Logitech G29 wheel.
3. Run `FireFighters.exe`.
4. Enter your name and start or join a multiplayer room.

**Meta Quest 3 (Standalone VR)**

1. Download `FireFightersVR.apk`.
2. Enable Developer Mode on your Meta Quest 3.
3. Connect the headset to your PC via USB-C.
4. Install the APK using SideQuest or ADB.
5. Open the game on Meta Quest 3 and join a room using the room code.

### Notes

- Internet connection is required for online multiplayer, voice chat, and mission statistics.
- Logitech G29 is required for the Windows Driver Station.
- Meta Quest 3 runs independently without a gaming PC.
- Both builds must be multiplayer-compatible to join the same session.
- ESP32 emergency lamp is optional (`COM5` by default, configurable via `-esp32Port=COMx`).

---

## Table of Contents

- [Simulator Trailer](#simulator-trailer)
- [Download](#download)
- [Overview](#overview)
- [Mission Flow](#mission-flow)
- [Crew Roles](#crew-roles)
- [Key Features](#key-features)
- [Hardware and Platforms](#hardware-and-platforms)
- [Technology Stack](#technology-stack)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Building the Simulation](#building-the-simulation)
- [Physical Emergency Lamp (ESP32)](#physical-emergency-lamp-esp32)
- [Mission Statistics Website](#mission-statistics-website)
- [Editor Tools](#editor-tools)
- [Development Team and Contributions](#development-team-and-contributions)

---

## Overview

Fire Fighters Multiplayer VR trains teamwork under time pressure. Each trainee has a distinct role and set of controls, and the mission can only be completed when both work together. Every run is timed, penalties are recorded for traffic violations, and the result is stored online so crews can compare their performance with previous teams.

A solo mode is also available: a single trainee can start the mission with an AI partner that takes over the other role.

## Mission Flow

| Stage | Description |
|---|---|
| **Lobby** | Trainees enter their names, create or join a private room with a room code, choose a station (Driver or Assistant) and mark themselves ready. A solo mission with an AI partner can be started from the same screen. |
| **Fire Station** | A short exploration period is followed by the dispatch alarm. The Assistant takes the dispatch tablet to the Driver, the Driver accepts the mission, the Assistant opens the garage door and both crew members board the truck. |
| **Emergency Drive** | The Driver steers the fire truck through live city traffic with the G29 wheel and pedals. The siren handle on the cab ceiling activates the emergency lights, gives the truck priority at intersections and turns on a real lamp through an ESP32 microcontroller. |
| **Fireground** | On arrival the crew dismounts. The Driver operates the pump panel and the hose reel; the Assistant takes the nozzle and extinguishes three separate fires. Approaching a fire too closely causes heat feedback. |
| **Recovery and Debrief** | The water is shut off, the nozzle is returned to its bracket and the hose is rewound. The crew is seated for the return drive while the mission debrief is displayed on the windshield: crew names, total time, penalties and the previous crews' results. |

## Crew Roles

| Role | Platform | Input | Responsibilities |
|---|---|---|---|
| **Driver (Pump Operator)** | Windows PC VR | Logitech G29 wheel and pedals, Meta controllers, hand tracking inside the truck | Accepts the dispatch, drives the truck, activates the siren, operates the pump panel and the hose reel. |
| **Assistant (Nozzle Operator)** | Meta Quest 3 (standalone) | Meta Quest controllers | Presents the dispatch tablet, opens the garage, attacks the fires with the nozzle and returns it to the truck. |

## Key Features

**Cooperative multiplayer**
- Online sessions through Unity Multiplayer Services (Distributed Authority) with private room codes.
- Cross-platform sessions between Windows PC VR and Meta Quest 3.
- Positional voice chat between crew members.
- Solo mode with an AI partner for either role.

**Driving**
- Physics-based fire truck driven with the Logitech G29 wheel and pedals.
- Live city traffic with traffic signals; vehicles yield to the truck while the siren is active.
- Red-light violations without the siren are penalised and reported on the debrief.
- Siren handle on the cab ceiling, with a visual hint when the Driver waits at a red light.
- Engine, throttle, braking, road and siren audio.

**Firefighting**
- Hose reel operated by turning the crank clockwise: three turns pay out the full hose, three turns rewind it. Turning the wrong way is felt as resistance on the controller.
- The reel locks while the nozzle is held, so the hose must be paid out before the nozzle operator advances.
- Water stream simulation with three independent fires that can be extinguished.
- Heat feedback near the fire: audio and a red screen tint that fade when the trainee steps back.

**Embodiment and comfort**
- Full-body firefighter avatars with inverse kinematics, seated poses and crouching.
- Hand tracking for the Driver inside the truck; hands attach to the steering wheel automatically.
- Floor-based tracking with automatic correction after a headset recenter.
- Comfort settings: movement speed, turning style, comfort vignette and height offset.

**Presentation**
- Night-time city, a fully furnished fire station and a residential fire site.
- Station alarm, ambient audio and a night-city view from the garage.
- Loading screens and fade transitions between scenes.
- Mission debrief dashboard with the online results history of previous crews.

## Hardware and Platforms

| Item | Used by | Notes |
|---|---|---|
| Windows PC with a PC VR headset (for example Meta Quest via Link) | Driver | Windows 10 or 11, DirectX 11/12 capable GPU |
| Logitech G29 steering wheel and pedals | Driver | Connected to the Windows PC |
| Meta Quest 3 | Assistant | Standalone Android build |
| ESP32 with a relay module (optional) | Driver PC | Drives a physical emergency lamp over Bluetooth |

## Technology Stack

| Component | Version |
|---|---|
| Unity Editor | 6000.3.25f1 |
| Universal Render Pipeline | 17.3.0 |
| XR Interaction Toolkit | 3.5.1 |
| OpenXR Plugin | 1.17.1 |
| XR Hands | 1.8.1 |
| Netcode for GameObjects | 2.13.2 |
| Unity Multiplayer Services | 2.3.1 |
| Vivox (voice chat) | 16.11.0 |
| Input System | 1.20.0 |
| Firebase Realtime Database and Hosting | Mission statistics and website |

## Project Structure

```
Assets/
  Scenes/Boot.unity                      Entry scene with the loading screen
  S1Test.unity                           Lobby and session system scene
  FireStation/                           Fire station scene, models and audio
  Versatile Studio Assets/.../demo_city_night.unity
                                         Night city, driving mission and fireground
  CityNightPresentation/
    Mission/                             Fire truck, driving, siren, traffic, ESP32 lamp
    Fireground/                          Pump, hose reel, nozzle, fires, debrief
    Scripts/                             City traffic and signals
  FirefighterAvatar/
    Scripts/                             Avatar driver, IK, seating
    Station/                             Session flow, lobby, station mission, transitions
    StationBot/                          AI partner for solo missions
    Stats/                               Mission records and online upload
  FireCenter/                            Lobby interface, settings and comfort options
  VRMPAssets/                            Multiplayer framework (sessions, participants, voice)
  Editor/                                Build and scene installer tools
StatsWebsite/                            Mission statistics website (Firebase Hosting)
```

## Getting Started

### Requirements

- Unity Hub with **Unity 6000.3.25f1**, including the *Windows Build Support* and *Android Build Support* modules.
- [Git LFS](https://git-lfs.com/) (audio, textures and large assets are stored with LFS).
- A Unity account with access to the project's Unity Cloud organisation (required for online sessions).

### Clone and open

```bash
git lfs install
git clone https://github.com/HarribelSA/Fire-Fighters-Multiyplayer-VR.git
cd Fire-Fighters-Multiyplayer-VR
git checkout XR-Developers
```

1. Add the project folder in Unity Hub and open it with Unity 6000.3.25f1. The first import takes a while.
2. Sign in to your Unity account in the Editor and confirm the project is linked under *Edit > Project Settings > Services*.
3. Open `Assets/Scenes/Boot.unity` and press Play.

> If a pull is refused because of local changes to `Assets/XR/Settings/OpenXR Package Settings.asset`, close Unity, discard the local change to that file and pull again. Unity rewrites this file automatically when the project is opened.

## Building the Simulation

Both builds must be made from the **same commit**; otherwise the two platforms cannot join the same session.

### Windows (Driver)

1. *File > Build Profiles* and select **Windows**.
2. Build to a folder such as `Builds/Windows`.

The first build compiles all shader variants and can take several hours. Later builds reuse the shader cache in `Library/` and complete in minutes, as long as graphics and quality settings are not changed.

### Meta Quest 3 (Assistant)

1. *Tools > Fire Fighters > Build Quest APK*, or switch the build profile to **Android** and build.
2. Install the APK on the headset with the Meta Quest Developer Hub or `adb install`.

Switching the active platform re-imports textures. Keeping a separate checkout of the project for Android builds avoids repeated switching.

## Physical Emergency Lamp (ESP32)

The Driver's PC can mirror the truck's emergency state to a real lamp. An ESP32 paired with Windows over Bluetooth exposes a serial port; the simulation sends one line per change.

| Simulation state | Command sent | Lamp |
|---|---|---|
| Siren handle pulled during the drive | `OFF` | On |
| Siren released, arrival at the fire, or simulation closed | `ON` | Off |

- The default port is `COM5`. A different port can be passed on the command line: `FireFighters.exe -esp32Port=COM7`
- The commands and the port can be changed on the `Esp32SirenLight` component of the fire truck.
- The current link state is written to the console with the `[ESP32]` prefix.

## Mission Statistics Website

Every completed mission is saved locally and uploaded to a Firebase Realtime Database. The `StatsWebsite` folder contains a static website that lists the results.

- The database address is configured in `Assets/Resources/MissionStatsConfig.json` (simulation) and `StatsWebsite/config.js` (website).
- Database rules allow reading and creating records only; existing records cannot be modified or deleted.
- Deploy the website with the Firebase CLI from the `StatsWebsite` folder: `firebase deploy`

## Editor Tools

| Menu item | Purpose |
|---|---|
| Tools > Fire Fighters > Build Quest APK | Builds the Android APK for the Meta Quest |
| Tools > Fire Fighters > Apply Crew Update | Applies the comfort, audio, boarding and network settings to the scenes |
| Tools > Fire Fighters > Apply Crew Update 5 | Applies truck audio, wait time, speeds and comfort values |
| Tools > Fire Fighters > Apply Fire Site Update | Places the siren handle, opens the fire-site entrance and adds environment colliders |
| Tools > Fire Fighters > Open Fire-Site Entrance Collider | Opens the entrance in the city's boundary collider |
| Tools > Fire Fighters > Apply Station Outlook Update | Station audio and the city view outside the garage |
| Tools > Fireground > Install | Rebuilds the fireground equipment on the fire truck |

---

## Development Team and Contributions

| Team Member | Role | Responsibilities |
|---|---|---|
| **Rana Al-Rashidi** ([@HarribelSA](https://github.com/HarribelSA)) | Lead Programmer and VR Developer | Core programming, Logitech G29 integration, ESP32 emergency lamp programming and integration, character rigging, IK systems, animations, fire station mission, and fire truck driving mechanics. |
| **Lamar Al-Talhi** ([@EngLamar](https://github.com/EngLamar)) | Gameplay Developer | Firefighting mission, interactive hose reel mechanics, fire extinguishing interactions, and dashboard integration. |
| **Dima Al-Manea** ([@DeemaTM](https://github.com/DeemaTM)) | UI/UX and Cinematics Developer | User interface design, interactive menus, and cinematic cutscenes. |
| **Aryam Al-Anazi** | 3D and Audio Asset Contributor | Sourcing and providing pre-made 3D models and audio assets for the project. |

---

**Developed by XR Developers Team | Tuwaiq Academy, 2026**
