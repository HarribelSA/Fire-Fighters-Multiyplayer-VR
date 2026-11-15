using UnityEngine;

public class BasicGearBox : MonoBehaviour
{
    BasicCarController carController; // This is for ground check and getting car speed

    Rigidbody rb;

    [Tooltip("First index (element 0) always always should be 1")]
    public float[] gearUpSpeeds = { 1, 25, 65, 95, 125, 155 }; // First index means N. After speed pass 1 km/h, gears will be 1
    public int currentGear { get; private set; }

    float carSpeedKmh;
    float velocityZ;
    
    void Start()
    {
        rb = GetComponent<Rigidbody>();
        carController = GetComponent<BasicCarController>();
    }

    void FixedUpdate()
    {
        carSpeedKmh = rb.linearVelocity.magnitude * 3.6f;

        velocityZ = transform.InverseTransformDirection(rb.linearVelocity).z;

        HandleGears();
        IsReversing();
    }

    void HandleGears()
    {
        int newGear = 0;

        for (int i = 0; i < gearUpSpeeds.Length; i++)
        {
            if (carSpeedKmh >= gearUpSpeeds[i])
                newGear = i + 1;
        }

        if (carController.IsGrounded())
            currentGear = newGear;
    }

    public bool IsReversing() // This is for UI
    {
        if (velocityZ < -0.1f && carController.IsGrounded())
            return true;

        return false;
    }
}
