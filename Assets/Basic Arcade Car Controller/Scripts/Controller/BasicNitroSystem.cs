using UnityEngine;

public class BasicNitroSystem : MonoBehaviour
{
    Rigidbody carBody;
    Controls carInputs;

    [Header("Nitro Info")]
    [HideInInspector] public float nitroTimer = 0f;
    [HideInInspector] public float cooldownFromZeroTimer = 0f;
    [HideInInspector] public float continueRefillTimer = 0f;

    [Header("Apply Nitro Power to RigidBody")]
    [SerializeField] float forwardW = 0.0f;
    [SerializeField] float upW = -0.1f;

    [Header("Nitro Settings")]
    public float nitroDuration = 4f;
    public float nitroCooldown = 0.8f;
    public float nitroCooldownStart = 1f;
    public float nitroPower = 10f;
    public float nitroRefillStart = 3f;
    public float continueRefillTime = 1f;

    bool isNitroActive;
    bool reUseNitro = true; // Prevents nitro reuse until refill conditions are met

    // This is just for UI. Not necessary if you don't want to use UI
    public bool refillTimer { get; private set; }

    void Awake()
    {
        carInputs = new Controls();
    }

    void OnEnable()
    {
        carInputs.Enable();
    }

    void OnDisable()
    {
        carInputs.Disable();
    }

    void Start()
    {
        carBody = GetComponent<Rigidbody>();
    }

    void FixedUpdate()
    {
        float throttle = carInputs.Drive.Throttle.ReadValue<float>();
        float nitro = carInputs.Drive.Nitro.ReadValue<float>();

        UseNitro(throttle, nitro);
        NitroRefillTimer();
        RefillNitro();
    }

    void UseNitro(float throttle, float nitro)
    {
        Vector3 worldPoint = carBody.worldCenterOfMass - transform.forward * forwardW - transform.up * upW;

        if (throttle >= 0.1f && nitro > 0.1f && nitroTimer < nitroDuration && reUseNitro)
        {
            nitroTimer += Time.fixedDeltaTime;
            isNitroActive = true;

            carBody.AddForceAtPosition(transform.forward * nitroPower, worldPoint, ForceMode.Acceleration);
            cooldownFromZeroTimer = 0;
            continueRefillTimer = 0;
        }
        else
        {
            isNitroActive = false;
            if (nitroTimer >= nitroDuration) nitroTimer = nitroDuration;
        }
    }

    void NitroRefillTimer()
    {
        if (nitroTimer >= nitroDuration && cooldownFromZeroTimer <= nitroRefillStart)
        {
            cooldownFromZeroTimer += Time.fixedDeltaTime;
        }
        else if (nitroTimer < nitroDuration && nitroTimer > 0f && !isNitroActive)
        {
            continueRefillTimer += Time.fixedDeltaTime;
            reUseNitro = false;
            refillTimer = true;
        }
    }

    void RefillNitro()
    {
        if (!isNitroActive)
        {
            if((nitroTimer <= nitroDuration && cooldownFromZeroTimer >= nitroRefillStart) || (nitroTimer < nitroDuration && continueRefillTimer >= continueRefillTime))
            {
                nitroTimer -= Time.fixedDeltaTime * nitroCooldown;
                refillTimer = false;
                reUseNitro = true;
            }

            if (nitroTimer <= 0) nitroTimer = 0f;
            
            if (continueRefillTimer >= continueRefillTime) continueRefillTimer = continueRefillTime;
        }
    }

    void OnDrawGizmos()
    {
        if (!carBody) return;

        Vector3 p = carBody.worldCenterOfMass - transform.forward * forwardW - transform.up * upW;

        Gizmos.color = Color.green;
        Gizmos.DrawSphere(p, 0.1f);
    }
}
