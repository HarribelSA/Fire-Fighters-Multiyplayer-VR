using UnityEngine;

namespace CityNightPresentation.Fireground
{
    // Visual/audio presentation of one independently simulated burn area. The authoritative
    // intensity comes from FiregroundOperation; this component never decides success.
    public sealed class FireZone : MonoBehaviour
    {
        public string label = "Fire";
        [Min(.3f)] public float radius = 1.7f;
        public ParticleSystem flames, smoke, embers, steam;
        public Light glow;
        public AudioSource crackle, steamAudio;
        [Min(1)] public float residualSmokeSeconds = 14;

        float flameRate, flameSize, smokeRate, emberRate, steamRate, lightIntensity, crackleVolume;
        float shown = 1, steamLevel, residual, seed;
        bool cached;

        void Awake() { Cache(); seed = Random.value * 50; }
        void Cache()
        {
            if (cached) return; cached = true;
            if (flames) { flameRate = flames.emission.rateOverTimeMultiplier; flameSize = flames.main.startSizeMultiplier; }
            if (smoke) smokeRate = smoke.emission.rateOverTimeMultiplier;
            if (embers) emberRate = embers.emission.rateOverTimeMultiplier;
            if (steam) steamRate = steam.emission.rateOverTimeMultiplier;
            if (glow) lightIntensity = glow.intensity;
            if (crackle) crackleVolume = crackle.volume;
        }
        public Vector3 Center => transform.position;

        // Distance from the burn centre to the sampled water path.
        public Transform botStandPoint, botApproachPoint;
        public float HitWeight(Vector3[] points, int count)
        {
            float best = MinDistance(points, count);
            return best <= radius ? 1 : best <= radius * 1.6f ? .45f : 0;
        }
        public float MinDistance(Vector3[] points, int count)
        {
            Vector3 c = transform.position; float best = float.PositiveInfinity;
            for (int i = 1; i < count; i++)
            {
                Vector3 a = points[i - 1], ab = points[i] - a; float t = Mathf.Clamp01(Vector3.Dot(c - a, ab) / Mathf.Max(1e-5f, ab.sqrMagnitude));
                best = Mathf.Min(best, (a + ab * t - c).sqrMagnitude);
            }
            return Mathf.Sqrt(best);
        }

        public void Apply(float intensity, bool hit, float dt)
        {
            Cache();
            float before = shown;
            shown = Mathf.MoveTowards(shown, intensity, dt * .6f);
            if (before > 0 && shown <= 0) residual = residualSmokeSeconds;
            residual = Mathf.Max(0, residual - dt);
            steamLevel = Mathf.MoveTowards(steamLevel, hit && (shown > 0 || residual > 0) ? 1 : 0, dt * (hit ? 4 : 1.2f));

            float flicker = .78f + .44f * Mathf.PerlinNoise(Time.time * 7, seed);
            Set(flames, flameRate * shown, shown > .005f);
            if (flames) { var main = flames.main; main.startSizeMultiplier = flameSize * Mathf.Lerp(.3f, 1, shown); }
            float smokeLevel = shown > 0 ? .2f + .8f * shown : .35f * residual / residualSmokeSeconds;
            Set(smoke, smokeRate * smokeLevel, smokeLevel > .01f);
            Set(embers, emberRate * shown, shown > .05f);
            Set(steam, steamRate * steamLevel, steamLevel > .01f);
            if (glow) { glow.intensity = lightIntensity * shown * flicker; glow.enabled = shown > .01f; }
            if (crackle) { crackle.volume = crackleVolume * Mathf.Sqrt(shown); if (shown > .01f && !crackle.isPlaying) crackle.Play(); else if (shown <= .01f && crackle.isPlaying) crackle.Stop(); }
            if (steamAudio) { steamAudio.volume = .5f * steamLevel; if (steamLevel > .01f && !steamAudio.isPlaying) steamAudio.Play(); else if (steamLevel <= .01f && steamAudio.isPlaying) steamAudio.Stop(); }
        }
        static void Set(ParticleSystem ps, float rate, bool active)
        {
            if (!ps) return;
            var e = ps.emission; e.rateOverTimeMultiplier = rate;
            if (active && !ps.isPlaying) ps.Play(); else if (!active && ps.isPlaying) ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }
        void OnDrawGizmosSelected() { Gizmos.color = new Color(1, .4f, 0, .5f); Gizmos.DrawWireSphere(transform.position, radius); }
    }
}
