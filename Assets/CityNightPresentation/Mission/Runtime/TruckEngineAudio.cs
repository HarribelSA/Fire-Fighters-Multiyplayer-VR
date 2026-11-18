using UnityEngine;

namespace CityNightPresentation.Mission
{
    // Fire-truck driving sounds, heard by both crew members:
    //  - engine: idle + driving loops cross-faded by speed; pressing the throttle revs it up (louder, higher)
    //  - throttle press: short rev burst when the pedal is pushed
    //  - speed: road / wind noise rising with speed
    //  - brake: tyre/brake squeal while braking at speed, and an air-brake "pssh" when the brake is released
    // On the session authority the real pedal input is used; other peers derive it from the replicated speed.
    public sealed class TruckEngineAudio : MonoBehaviour
    {
        public FireTruckMission mission;
        public AudioClip idleClip, driveClip, revClip, roadClip, brakeSqueal, airRelease;
        [Range(0, 1)] public float engineVolume = .45f, roadVolume = .35f, brakeVolume = .4f, revVolume = .45f;
        public float idlePitch = .85f, topPitch = 1.65f;
        public UnityEngine.Audio.AudioMixerGroup output;
        AudioSource idle, drive, road, squeal, oneShot;
        float lastSpeed, throttle, brake, smoothedAccel, revCooldown;
        bool wasThrottle, wasBrake;

        AudioSource Make(string name, AudioClip clip, bool loop)
        {
            var g = new GameObject(name); g.transform.SetParent(transform, false); g.transform.localPosition = new Vector3(0, 1.2f, 2.6f);
            var a = g.AddComponent<AudioSource>(); a.clip = clip; a.loop = loop; a.playOnAwake = false; a.volume = 0; a.spatialBlend = .85f;
            a.minDistance = 3; a.maxDistance = 60; a.dopplerLevel = 0; a.outputAudioMixerGroup = output; return a;
        }
        void Awake()
        {
            if (!mission) mission = GetComponent<FireTruckMission>();
            idle = Make("Engine_Idle", idleClip, true); drive = Make("Engine_Drive", driveClip, true); road = Make("Road_Noise", roadClip, true);
            squeal = Make("Brake_Squeal", brakeSqueal, true); oneShot = Make("Truck_OneShots", null, false); oneShot.volume = 1;
        }
        bool Running
        {
            get
            {
                if (!mission) return false;
                if (Fireground.MissionDebrief.Active) return true;            // cutscene drive back
                var fire = Fireground.FiregroundOperation.Instance;
                if (fire && fire.Current.phase >= Fireground.FiregroundPhase.Dismount) return false; // parked, engine off
                return mission.CrewReady || mission.HasArrived;
            }
        }
        void Update()
        {
            float dt = Mathf.Max(Time.deltaTime, .0001f);
            float speed = mission && mission.controller ? mission.controller.SpeedKmh : 0;
            float accel = (speed - lastSpeed) / 3.6f / dt; lastSpeed = speed;
            smoothedAccel = Mathf.Lerp(smoothedAccel, accel, 1 - Mathf.Exp(-6 * dt));
            if (mission && mission.Authority && mission.controller && !Fireground.MissionDebrief.Active)
            { var input = mission.controller.AppliedInput; throttle = Mathf.Clamp01(input.y); brake = speed > .5f ? Mathf.Clamp01(input.z) : 0; }
            else
            { throttle = Mathf.Clamp01(smoothedAccel / 1.2f); brake = speed > 2 ? Mathf.Clamp01(-smoothedAccel / 2.5f) : 0; }

            bool on = Running;
            float k = Mathf.Clamp01(speed / 60f);
            float targetIdle = on ? engineVolume * Mathf.Lerp(1, .25f, k * 2) : 0;
            float targetDrive = on ? engineVolume * Mathf.Clamp01(k * 2.5f + throttle * .5f) : 0;
            float pitch = Mathf.Lerp(idlePitch, topPitch, k) + throttle * .18f;
            Fade(idle, targetIdle * (1 + throttle * .3f), dt, 2); idle.pitch = Mathf.Lerp(idle.pitch, idlePitch + throttle * .25f, 1 - Mathf.Exp(-5 * dt));
            Fade(drive, targetDrive, dt, 2); drive.pitch = Mathf.Lerp(drive.pitch, pitch, 1 - Mathf.Exp(-4 * dt));
            Fade(road, on ? roadVolume * Mathf.Clamp01(speed / 50f) : 0, dt, 1.5f); road.pitch = .8f + k * .5f;
            Fade(squeal, on ? brakeVolume * brake * Mathf.Clamp01(speed / 25f) : 0, dt, 6); squeal.pitch = .9f + Mathf.Clamp01(speed / 50f) * .3f;

            bool pressed = on && throttle > .35f, braking = on && brake > .3f;
            revCooldown -= dt;
            if (pressed && !wasThrottle && revCooldown <= 0 && revClip) { oneShot.pitch = .9f + k * .4f; oneShot.PlayOneShot(revClip, revVolume); revCooldown = 1.2f; }
            if (!braking && wasBrake && airRelease) { oneShot.pitch = 1; oneShot.PlayOneShot(airRelease, brakeVolume * .9f); }
            wasThrottle = pressed; wasBrake = braking;
        }
        static void Fade(AudioSource a, float target, float dt, float rate)
        {
            if (!a || !a.clip) return;
            a.volume = Mathf.MoveTowards(a.volume, target, dt * rate);
            if (a.volume > .001f && !a.isPlaying) { a.time = Random.value * Mathf.Max(0, a.clip.length - .1f); a.Play(); }
            else if (a.volume <= .001f && a.isPlaying) a.Stop();
        }
    }
}
