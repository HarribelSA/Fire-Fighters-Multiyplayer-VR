using UnityEngine;

public class EngineSoundByGear : MonoBehaviour
{
    BasicCarController car;
    BasicGearBox gearbox;
    AudioSource[] gearSources;

    [Header("Pitch Settings")]
    public float idlePitch = 0.9f;
    public float minGearPitch = 0.95f;
    public float maxGearPitch = 2.4f;
    public float pitchSmooth = 3f;

    [Header("Volume Settings")]
    public float maxVolume = 0.4f;
    public float fadeSpeed = 5f;

    void Start()
    {
        car = GetComponentInParent<BasicCarController>();
        gearbox = GetComponentInParent<BasicGearBox>();
        gearSources = GetComponentsInChildren<AudioSource>();

        foreach (var s in gearSources)
        {
            s.volume = 0f;
            s.loop = true;
            s.Play();
        }
    }

    void Update()
    {
        int gear = gearbox.currentGear;

        for (int i = 0; i < gearSources.Length; i++)
        {
            float targetVolume = (i == gear) ? maxVolume : 0f;
            gearSources[i].volume = Mathf.MoveTowards(gearSources[i].volume, targetVolume, fadeSpeed * Time.deltaTime);

            UpdatePitch(i, gearSources[i]);
        }
    }

    void UpdatePitch(int index, AudioSource source)
    {
        float throttle = car.carInputs.Drive.Throttle.ReadValue<float>();

        if (index == 0 && throttle < 0.1f)
        {
            source.pitch = Mathf.Lerp(source.pitch, idlePitch, pitchSmooth * Time.deltaTime);
            return;
        }
        else if (throttle > 0.1 && car.carSpeedKmh < 0.1f)
        {
            source.pitch = Mathf.Lerp(source.pitch, maxGearPitch, pitchSmooth * Time.deltaTime);
            return;
        }

        float speed01 = Mathf.Clamp01(car.carSpeedKmh / car.maxSpeed);
        float targetPitch = Mathf.Lerp(minGearPitch, maxGearPitch, speed01);

        if (!car.IsGrounded() && throttle > 0.1f)
            source.pitch = Mathf.Lerp(source.pitch, maxGearPitch, pitchSmooth * Time.deltaTime);
        else if ((!car.IsGrounded() && throttle < 0.1f) || throttle < 0.1f)
            source.pitch = Mathf.Lerp(source.pitch, idlePitch, pitchSmooth * Time.deltaTime);
        else
            source.pitch = Mathf.Lerp(source.pitch, targetPitch, pitchSmooth * Time.deltaTime);    

    }
}