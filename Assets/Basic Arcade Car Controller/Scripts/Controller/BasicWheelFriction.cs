using UnityEngine;

public class BasicWheelFriction : MonoBehaviour
{
    WheelCollider[] allWheels;
    Rigidbody rb;

    [Header("Friction Settings")]
    [Range(0f, 2f)] public float forwardGrip = 1.5f;
    [Range(0f, 2f)] public float sidewaysGrip = 1.5f;
    public AnimationCurve forwardCurve;
    public AnimationCurve sidewayCurve;

    float lastForwardGrip;
    float lastSidewaysGrip;

    float currentSpeedKmh;

    [Tooltip("Speed at which grip curves reach their end")]
    public float referenceMaxSpeed = 180f;

    void Start()
    {
        rb = GetComponent<Rigidbody>();
        allWheels = GetComponentsInChildren<WheelCollider>();
    }

    void FixedUpdate()
    {
        currentSpeedKmh = rb.linearVelocity.magnitude * 3.6f;

        if (forwardGrip != lastForwardGrip || sidewaysGrip != lastSidewaysGrip)
        {
            UpdateWheelFriction();
            lastForwardGrip = forwardGrip;
            lastSidewaysGrip = sidewaysGrip;
        }
    }

    void UpdateWheelFriction()
    {
        float t = Mathf.InverseLerp(0, referenceMaxSpeed, currentSpeedKmh);

        float forwardGripValue = forwardGrip * forwardCurve.Evaluate(t);
        float sidewaysGripValue = sidewaysGrip * sidewayCurve.Evaluate(t);

        foreach (WheelCollider wheel in allWheels)
            ApplyGrip(wheel, forwardGripValue, sidewaysGripValue);
    }

    public void ApplyGrip(WheelCollider wheel, float forwardStiffnes, float sidewaysStiffnes)
    {
        WheelFrictionCurve forward = wheel.forwardFriction;
        forward.stiffness = forwardStiffnes;
        wheel.forwardFriction = forward;

        WheelFrictionCurve sideway = wheel.sidewaysFriction;
        sideway.stiffness = sidewaysStiffnes;
        wheel.sidewaysFriction = sideway;
    }
}
