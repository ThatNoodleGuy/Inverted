using UnityEngine;

[RequireComponent(typeof(PlayerManager), typeof(Rigidbody2D))]
public class PlayerWaterInteraction : MonoBehaviour
{
    [Header("Fluid References")]
    [SerializeField] private FluidSim2D fluidSim;
    [SerializeField] private ParticleSystem splashEffect;

    [Header("Fluid Interaction")]
    [SerializeField] private bool enableFluidInteraction = true;
    [SerializeField] private float fluidPushRadius = 1.5f;
    [SerializeField] private float splashThreshold = 3f;

    [Header("Player Fluid Control")]
    [SerializeField] private bool enablePlayerFluidControl = true;
    [SerializeField] private float playerFluidInteractionStrength = 10f;
    [SerializeField] private float playerFluidInteractionRadius = 3f;
    [SerializeField] private KeyCode fluidPullKey = KeyCode.Z;
    [SerializeField] private KeyCode fluidPushKey = KeyCode.X;
    [SerializeField] private bool requireFluidProximityForControl = true;
    [SerializeField] private float fluidControlProximityRadius = 2f;

    [Header("Movement Modifiers")]
    [SerializeField] private float fluidMoveSpeedMultiplier = 0.7f;
    [SerializeField] private float fluidJumpMultiplier = 1.2f;

    // Runtime
    PlayerManager pm;
    Rigidbody2D rb;

    public bool IsInFluid { get; private set; }
    public float TimeInFluid { get; private set; }

    bool wasInFluid;
    Vector2 velocityBuffer;
    float lastSplashTime;

    bool isFluidPulling;
    bool isFluidPushing;
    float fluidControlStrength;

    // Exposed to FluidSim2D / PlayerManager
    public float SubmersionDepth => fluidSim != null ? fluidSim.GetPlayerSubmersionDepth() : 0f;
    public int NearbyParticleCount => fluidSim != null ? fluidSim.GetNearbyParticleCount() : 0;
    public bool IsControllingFluid => enablePlayerFluidControl && (isFluidPulling || isFluidPushing);
    public float ControlStrength => fluidControlStrength;
    public float ControlRadius => playerFluidInteractionRadius;
    public Vector2 ControlPosition => (Vector2)transform.position;
    public float BaseInteractionStrength => playerFluidInteractionStrength;
    public float PushRadius => fluidPushRadius;
    public bool InteractionEnabled => enableFluidInteraction;

    void Awake()
    {
        pm = GetComponent<PlayerManager>();
        rb = GetComponent<Rigidbody2D>();

        if (fluidSim == null)
            fluidSim = FindFirstObjectByType<FluidSim2D>();
    }

    // Called from PlayerManager.FixedUpdate
    public void UpdateVelocityTracking()
    {
        Vector2 currentPos = transform.position;
        velocityBuffer = (currentPos - (Vector2)pm.LastPosition) / Time.fixedDeltaTime;
        pm.LastPosition = currentPos;
    }

    // Called from PlayerManager.Update
    public void Tick()
    {
        if (!enableFluidInteraction) {
            ResetControl();
            return;
        }

        CheckInOutOfFluid();
        HandleFluidControlInput();
    }

    void CheckInOutOfFluid()
    {
        bool wasInFluidPreviously = IsInFluid;
        IsInFluid = ComputeIsInFluid();

        if (IsInFluid)
        {
            TimeInFluid += Time.deltaTime;

            // Splash when entering or moving fast through fluid
            if ((!wasInFluidPreviously && rb.velocity.magnitude > splashThreshold) ||
                (Time.time - lastSplashTime > 1f && rb.velocity.magnitude > splashThreshold * 1.5f))
            {
                CreateSplashEffect();
                lastSplashTime = Time.time;
            }
        }
        else
        {
            TimeInFluid = 0f;
        }

        wasInFluid = IsInFluid;
    }

    bool ComputeIsInFluid()
    {
        // Preferred: FluidSim2D particle-based detection
        if (fluidSim != null)
            return fluidSim.IsPlayerInFluid();

        // Fallback: simple overlap check
        Collider2D[] nearbyColliders =
            Physics2D.OverlapCircleAll(transform.position, fluidPushRadius);
        foreach (var c in nearbyColliders)
        {
            if (c.CompareTag("Fluid") || c.name.Contains("Particle"))
                return true;
        }

        return false;
    }

    void CreateSplashEffect()
    {
        if (splashEffect != null && Time.time - lastSplashTime > 0.5f)
        {
            splashEffect.transform.position = transform.position;
            splashEffect.Play();
        }
    }

    void HandleFluidControlInput()
    {
        // Proximity gating (optional)
        if (requireFluidProximityForControl)
        {
            bool nearFluid = false;

            if (fluidSim != null)
            {
                if (fluidSim.positionBuffer != null && fluidSim.numParticles > 0)
                {
                    nearFluid = IsInFluid || GetDistanceToNearestFluidParticle() <= fluidControlProximityRadius;
                }
            }

            if (!nearFluid)
            {
                ResetControl();
                return;
            }
        }

        // Input
        isFluidPulling = Input.GetKey(fluidPullKey);
        isFluidPushing = Input.GetKey(fluidPushKey);

        if (isFluidPushing || isFluidPulling)
        {
            fluidControlStrength = isFluidPushing
                ? -playerFluidInteractionStrength
                :  playerFluidInteractionStrength;

            if (pm.IsInMirroredState())
                fluidControlStrength *= 1.3f;

            float moveMag = rb.velocity.magnitude;
            float movementFactor = Mathf.Clamp01(2f - moveMag / (pm.MoveSpeedForFluidControl + 0.0001f));
            fluidControlStrength *= movementFactor;
        }
        else
        {
            ResetControl();
        }
    }

    void ResetControl()
    {
        isFluidPulling = false;
        isFluidPushing = false;
        fluidControlStrength = 0f;
    }

    float GetDistanceToNearestFluidParticle()
    {
        if (fluidSim == null) return float.MaxValue;

        if (fluidSim.IsPlayerInFluid())
            return 0f;

        // Approximation using sim’s own detection radius
        return fluidSim.fluidDetectionRadius;
    }

    // Movement modifiers used by PlayerManager
    public float ModifyMoveSpeed(float baseSpeed)
    {
        if (!IsInFluid) return baseSpeed;

        float maxDepth = fluidSim != null ? fluidSim.maxBuoyancyDepth : 3f;
        float ratio = Mathf.Clamp01(SubmersionDepth / maxDepth);
        float t = Mathf.Lerp(1f, fluidMoveSpeedMultiplier, ratio);
        return baseSpeed * t;
    }

    public float ModifyJumpForce(float baseJump)
    {
        if (!IsInFluid) return baseJump;

        float maxDepth = fluidSim != null ? fluidSim.maxBuoyancyDepth : 3f;
        float ratio = Mathf.Clamp01(SubmersionDepth / maxDepth);
        float t = Mathf.Lerp(1f, fluidJumpMultiplier, ratio);
        return baseJump * t;
    }
}