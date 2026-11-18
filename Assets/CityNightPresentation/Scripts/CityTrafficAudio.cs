using UnityEngine;

namespace CityNightPresentation
{
    // Engine sound for every civilian traffic car: a 3D looping engine per car whose volume and pitch
    // follow the car's speed (works on every peer: remote peers receive the replicated speed).
    [RequireComponent(typeof(CityTrafficManager))]
    public sealed class CityTrafficAudio : MonoBehaviour
    {
        public AudioClip[] engineClips = new AudioClip[0];
        [Range(0, 1)] public float idleVolume = .12f, cruiseVolume = .42f;
        public float idlePitch = .75f, cruisePitch = 1.25f, cruiseSpeedForPitch = 10;
        public float minDistance = 3, maxDistance = 45;
        public UnityEngine.Audio.AudioMixerGroup output;
        CityTrafficManager traffic;
        AudioSource[] sources = new AudioSource[0];

        void Start()
        {
            traffic = GetComponent<CityTrafficManager>();
            sources = new AudioSource[traffic.vehicles.Length];
            for (int i = 0; i < traffic.vehicles.Length; i++)
            {
                var v = traffic.vehicles[i]; if (!v.body || engineClips.Length == 0) continue;
                var clip = engineClips[i % engineClips.Length]; if (!clip) continue;
                var go = new GameObject("EngineAudio"); go.transform.SetParent(v.body, false); go.transform.localPosition = new Vector3(0, .6f, v.length * .3f);
                var a = go.AddComponent<AudioSource>();
                a.clip = clip; a.loop = true; a.playOnAwake = false; a.spatialBlend = 1; a.rolloffMode = AudioRolloffMode.Linear;
                a.minDistance = minDistance; a.maxDistance = maxDistance; a.dopplerLevel = .3f; a.volume = 0; a.outputAudioMixerGroup = output;
                a.time = Random.value * Mathf.Max(0, clip.length - .1f); // de-phase identical loops
                sources[i] = a;
            }
        }
        void Update()
        {
            bool hidden = Fireground.MissionDebrief.HideTraffic;
            for (int i = 0; i < sources.Length; i++)
            {
                var a = sources[i]; if (!a) continue;
                var v = traffic.vehicles[i];
                bool active = !hidden && v.body && v.body.gameObject.activeInHierarchy;
                if (!active) { if (a.isPlaying) a.Stop(); continue; }
                float k = Mathf.Clamp01(Mathf.Abs(v.speed) / Mathf.Max(1, cruiseSpeedForPitch));
                a.volume = Mathf.MoveTowards(a.volume, Mathf.Lerp(idleVolume, cruiseVolume, k), Time.deltaTime);
                a.pitch = Mathf.Lerp(a.pitch, Mathf.Lerp(idlePitch, cruisePitch, k), 1 - Mathf.Exp(-4 * Time.deltaTime));
                if (!a.isPlaying) a.Play();
            }
        }
    }
}
