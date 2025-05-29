using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Enhanced PlayerManager with automatic space-based state transitions
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer), typeof(CapsuleCollider2D))]
public class PlayerManager : MonoBehaviour
{
    // === Serialized Fields ===
    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float flipCooldown = 0.5f;
    [SerializeField] private bool maintainUpright = true;
    [SerializeField] private float rotationSpeed = 10f;

    [Header("Sprite")]
    [SerializeField] private Color normalColor = Color.black;
    [SerializeField] private Color mirroredColor = Color.white;

    [Header("Surface and Ground")]
    [SerializeField] private LayerMask solidLayer;
    [SerializeField] private Transform groundChecker;
    [SerializeField] private float detectionRadius = 2f;
    [SerializeField] private float edgeFollowDistance = 0.3f;

    [Header("Space Detection")]
    [SerializeField] private float spaceCheckRadius = 0.3f;
    [SerializeField] private LayerMask normalSpaceLayer = 1; // Default layer for normal space
    [SerializeField] private bool enableManualOverride = true;
    [SerializeField] private float transitionSmoothTime = 0.3f;

    [Header("Entry Validation")]
    [SerializeField] private bool restrictSideEntry = true; // Prevent problematic side entries
    [SerializeField] private bool requireGroundedForInversion = true; // Only allow inversion when grounded
    [SerializeField] private bool preventInversionInFluid = true; // Prevent inversion when in fluid
    [SerializeField] private float verticalEntryBias = 0.7f; // Prefer vertical entries (0.5 = no bias, 1.0 = only vertical)
    [SerializeField] private float minimumEntryDistance = 0.2f; // Minimum distance from edge to allow entry
    [SerializeField] private bool snapToValidPosition = true; // Snap to safe position when entering

    [Header("Input Actions")]
    [SerializeField] private InputActionAsset actions;

    [Header("Interaction")]
    [SerializeField] private float pushForce = 2f;
    [SerializeField] private float pushCheckDistance = 0.5f;

    [Header("Fluid Interaction")]
    [SerializeField] private FluidSim2D fluidSim;
    [SerializeField] private float fluidPushRadius = 1.5f;
    [SerializeField] private bool enableFluidInteraction = true;
    [SerializeField] private ParticleSystem splashEffect;
    [SerializeField] private float splashThreshold = 3f;

    [Header("Fluid Movement Modifiers")]
    [SerializeField] private float fluidMoveSpeedMultiplier = 0.7f;
    [SerializeField] private float fluidJumpMultiplier = 1.2f;
    [SerializeField] private bool canSwimInMirroredState = true;

    [Header("Player Fluid Control")]
    [SerializeField] private bool enablePlayerFluidControl = true;
    [SerializeField] private float playerFluidInteractionStrength = 10f;
    [SerializeField] private float playerFluidInteractionRadius = 3f;
    [SerializeField] private KeyCode fluidPullKey = KeyCode.Z;
    [SerializeField] private KeyCode fluidPushKey = KeyCode.X;
    [SerializeField] private bool requireFluidProximityForControl = true;
    [SerializeField] private float fluidControlProximityRadius = 2f;

    // === Space Types ===
    public enum SpaceType
    {
        Normal,     // Regular world space
        BlackSpace, // Inverted/mirrored space
        Fluid       // Water/fluid space
    }

    // === Runtime Components & State ===
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private CapsuleCollider2D cc;
    private IPlayerState currentState;
    private NormalState normalState;
    private MirroredState mirroredState;

    // === Cached Values ===
    private int blackSpaceLayer;
    private int solidLayerMask;
    private bool isGrounded;
    private Vector2 currentNormal;
    private Vector2 entryPoint;
    private float lastFlipTime;
    private Vector2 lastPosition;
    private float lastSplashTime;

    // Space detection
    private SpaceType currentSpaceType;
    private SpaceType previousSpaceType;
    private bool isTransitioning;

    // Fluid interaction tracking
    private Vector2 velocityBuffer;
    private bool wasInFluid;
    private float fluidImpactForce;
    private bool isInFluid;
    private float timeInFluid;

    private bool isFluidPulling;
    private bool isFluidPushing;
    private float fluidControlStrength;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        cc = GetComponent<CapsuleCollider2D>();

        sr.sortingLayerName = "Player";
        sr.sortingOrder = 100;

        blackSpaceLayer = LayerMask.NameToLayer("BlackSpace");
        solidLayerMask = solidLayer;

        normalState = new NormalState(this);
        mirroredState = new MirroredState(this);

        // Initialize space detection
        currentSpaceType = DetectCurrentSpace();
        previousSpaceType = currentSpaceType;

        // Start in appropriate state based on initial space
        TransitionTo(currentSpaceType == SpaceType.BlackSpace ? mirroredState : normalState);

        lastPosition = transform.position;

        // Auto-find fluid sim if not assigned
        if (fluidSim == null)
        {
            fluidSim = FindFirstObjectByType<FluidSim2D>();
        }
    }

    void OnEnable() => actions?.Enable();
    void OnDisable() => actions?.Disable();

    void Update()
    {
        // Detect current space type
        currentSpaceType = DetectCurrentSpace();

        // Handle automatic state transitions
        HandleSpaceTransitions();

        currentState.HandleUpdate();
        CheckFluidInteraction();

        // NEW: Add this line to ensure fluid interaction is always updated
        // (This provides a fallback in case states don't call it)
        if (!currentState.GetType().Name.Contains("State"))
        {
            HandleFluidInteraction(); // Fallback call
        }
    }

    void FixedUpdate()
    {
        currentState.HandleFixedUpdate();
        UpdateVelocityTracking();
    }

    // === Space Detection System ===
    private SpaceType DetectCurrentSpace()
    {
        Vector2 playerPos = transform.position;

        // Check for fluid first (highest priority)
        if (IsInFluid())
        {
            return SpaceType.Fluid;
        }

        // Check if we're in black space with entry validation
        if (IsInValidBlackSpace())
        {
            return SpaceType.BlackSpace;
        }

        // Default to normal space
        return SpaceType.Normal;
    }

    private bool IsInValidBlackSpace()
    {
        Vector2 playerPos = transform.position;

        // Basic overlap check
        Collider2D blackSpaceCollider = Physics2D.OverlapCircle(
            playerPos,
            spaceCheckRadius,
            1 << blackSpaceLayer
        );

        if (blackSpaceCollider == null)
            return false;

        // If we're already in mirrored state, allow staying in black space
        // but check if we should exit based on grounded status
        if (currentState == mirroredState)
        {
            // If we require grounded for inversion, check if we're grounded in black space
            if (requireGroundedForInversion)
            {
                bool isGroundedInBlackSpace = CheckMirroredGrounded();

                // Stay in black space only if we're grounded in it OR we're transitioning
                return isGroundedInBlackSpace || isTransitioning;
            }
            return true;
        }

        // For new entries, always validate (this now includes grounded check)
        return ValidateBlackSpaceEntry(playerPos);
    }

    private bool ValidateBlackSpaceEntry(Vector2 playerPos)
    {
        // First check if we're in fluid - prevent inversion in fluid
        if (preventInversionInFluid && IsInFluid())
        {
            Debug.Log("Black space entry blocked - cannot invert while in fluid");
            return false;
        }

        // Then check grounded requirement if enabled
        if (requireGroundedForInversion)
        {
            CheckGrounded();
            if (!isGrounded)
            {
                Debug.Log("Black space entry blocked - not grounded");
                return false;
            }
        }

        // Then check entry direction and quality if side entry restriction is enabled
        if (restrictSideEntry)
        {
            EntryInfo entryInfo = FindBestEntryPoint(playerPos);

            if (!entryInfo.isValid)
                return false;

            // Prefer vertical entries over side entries
            float verticalComponent = Mathf.Abs(entryInfo.normal.y);
            if (verticalComponent < verticalEntryBias)
            {
                // This is mostly a side entry, block it
                Debug.Log("Black space entry blocked - side entry not allowed");
                return false;
            }
        }

        return true;
    }

    private struct EntryInfo
    {
        public bool isValid;
        public Vector2 position;
        public Vector2 normal;
        public float distance;
        public float quality; // 0-1, higher is better
    }

    private EntryInfo FindBestEntryPoint(Vector2 fromPos)
    {
        EntryInfo bestEntry = new EntryInfo { isValid = false };
        float bestQuality = -1f;

        // Check multiple directions, prioritizing vertical
        Vector2[] checkDirections = {
            Vector2.down,    // Primary: down entry
            Vector2.up,      // Secondary: up entry  
            Vector2.left,    // Tertiary: side entries
            Vector2.right,
            new Vector2(-0.7f, -0.7f), // Diagonal entries
            new Vector2(0.7f, -0.7f),
            new Vector2(-0.7f, 0.7f),
            new Vector2(0.7f, 0.7f)
        };

        for (int i = 0; i < checkDirections.Length; i++)
        {
            Vector2 dir = checkDirections[i];
            RaycastHit2D hit = Physics2D.Raycast(fromPos, dir, detectionRadius, solidLayerMask);

            if (hit.collider != null && hit.collider.gameObject.layer == blackSpaceLayer)
            {
                // Calculate entry quality based on direction preference and distance
                float verticalBias = Mathf.Abs(dir.y); // Prefer vertical directions
                float distanceQuality = 1f - (hit.distance / detectionRadius); // Prefer closer entries
                float quality = (verticalBias * 0.7f + distanceQuality * 0.3f);

                // Apply priority bonus for down direction
                if (i == 0) quality += 0.2f; // Down gets bonus
                else if (i == 1) quality += 0.1f; // Up gets smaller bonus

                if (quality > bestQuality && hit.distance >= minimumEntryDistance)
                {
                    bestQuality = quality;
                    bestEntry = new EntryInfo
                    {
                        isValid = true,
                        position = hit.point - hit.normal * edgeFollowDistance,
                        normal = hit.normal,
                        distance = hit.distance,
                        quality = quality
                    };
                }
            }
        }

        return bestEntry;
    }

    private void HandleSpaceTransitions()
    {
        // If space type changed, initiate transition
        if (currentSpaceType != previousSpaceType)
        {
            Debug.Log($"Space transition detected: {previousSpaceType} -> {currentSpaceType}");
            InitiateSpaceTransition(currentSpaceType);
            previousSpaceType = currentSpaceType;
        }

        // Handle manual override if enabled
        if (enableManualOverride && CanFlip() && Input.GetKeyDown(KeyCode.E))
        {
            ManualFlip();
        }
    }

    private void InitiateSpaceTransition(SpaceType newSpaceType)
    {
        switch (newSpaceType)
        {
            case SpaceType.Normal:
                if (currentState != normalState)
                {
                    TransitionTo(normalState);
                }
                break;

            case SpaceType.BlackSpace:
                if (currentState != mirroredState)
                {
                    // Use improved entry positioning
                    if (TryEnterBlackSpaceImproved())
                    {
                        TransitionTo(mirroredState);
                    }
                    else
                    {
                        // Entry failed, stay in normal space
                        Debug.Log("Black space entry failed - invalid entry point");
                        return;
                    }
                }
                break;

            case SpaceType.Fluid:
                // Fluid doesn't change the base state, but affects movement
                break;
        }

        isTransitioning = true;
    }

    private bool TryEnterBlackSpaceImproved()
    {
        // Check fluid restriction first
        if (preventInversionInFluid && IsInFluid())
        {
            Debug.Log("Cannot enter black space - in fluid");
            return false;
        }

        // Check grounded requirement
        if (requireGroundedForInversion)
        {
            CheckGrounded();
            if (!isGrounded)
            {
                Debug.Log("Cannot enter black space - not grounded");
                return false;
            }
        }

        EntryInfo entryInfo = FindBestEntryPoint(transform.position);

        if (!entryInfo.isValid)
        {
            Debug.Log("No valid black space entry point found");
            return false;
        }

        // Store entry information
        entryPoint = transform.position;
        currentNormal = entryInfo.normal;

        // Position player at the entry point
        if (snapToValidPosition)
        {
            transform.position = entryInfo.position;
        }

        Debug.Log($"Entering black space with quality {entryInfo.quality:F2}, normal {entryInfo.normal}, grounded: {isGrounded}");
        return true;
    }

    private void ManualFlip()
    {
        lastFlipTime = Time.time;

        // Manual flip overrides automatic detection temporarily
        if (currentState == normalState)
        {
            // Check fluid restriction for manual flip
            if (preventInversionInFluid && IsInFluid())
            {
                Debug.Log("Cannot manually flip to black space - in fluid");
                return;
            }

            // Check grounded requirement for manual flip
            if (requireGroundedForInversion)
            {
                CheckGrounded();
                if (!isGrounded)
                {
                    Debug.Log("Cannot manually flip to black space - not grounded");
                    return;
                }
            }

            // Try to find black space to enter
            if (TryFindNearbyBlackSpace(out Vector2 entryPos, out Vector2 normal))
            {
                transform.position = entryPos;
                currentNormal = normal;
                TransitionTo(mirroredState);
            }
        }
        else
        {
            // Check grounded requirement for manual exit
            if (requireGroundedForInversion)
            {
                bool isGroundedInBlackSpace = CheckMirroredGrounded();
                if (!isGroundedInBlackSpace)
                {
                    Debug.Log("Cannot manually flip to normal space - not grounded in black space");
                    return;
                }
            }

            // Exit to normal space
            Vector2 exitPos = FindExitPoint();
            transform.position = exitPos;
            TransitionTo(normalState);
        }

        // Create a small splash effect when manually flipping
        CreateSplashEffect();
    }

    private bool TryFindNearbyBlackSpace(out Vector2 entryPos, out Vector2 normal)
    {
        EntryInfo entryInfo = FindBestEntryPoint(transform.position);

        if (entryInfo.isValid)
        {
            entryPos = entryInfo.position;
            normal = entryInfo.normal;
            return true;
        }

        entryPos = Vector2.zero;
        normal = Vector2.zero;
        return false;
    }

    // === State Management ===
    private void TransitionTo(IPlayerState next)
    {
        currentState?.Exit();
        currentState = next;
        currentState.Enter();
    }

    public bool IsInMirroredState()
    {
        return currentState == mirroredState;
    }

    public bool CanFlip() => Time.time - lastFlipTime >= flipCooldown;

    public SpaceType GetCurrentSpaceType() => currentSpaceType;
    public bool IsTransitioning() => isTransitioning;

    // === Fluid Interaction (keeping your existing system) ===
    void UpdateVelocityTracking()
    {
        Vector2 currentPos = transform.position;
        velocityBuffer = (currentPos - lastPosition) / Time.fixedDeltaTime;
        lastPosition = currentPos;
    }

    void CheckFluidInteraction()
    {
        if (!enableFluidInteraction) return;

        bool wasInFluidPreviously = isInFluid;
        isInFluid = IsInFluid();

        if (isInFluid)
        {
            timeInFluid += Time.deltaTime;

            // Create splash effect when entering fluid or moving fast
            if ((!wasInFluidPreviously && rb.linearVelocity.magnitude > splashThreshold) ||
                (Time.time - lastSplashTime > 1f && rb.linearVelocity.magnitude > splashThreshold * 1.5f))
            {
                CreateSplashEffect();
                lastSplashTime = Time.time;
            }

            // NEW: Debug output for particle-based fluid detection
            if (fluidSim != null && Time.frameCount % 60 == 0) // Every second
            {
                Debug.Log($"Player in fluid - Nearby particles: {fluidSim.GetNearbyParticleCount()}, " +
                         $"Submersion depth: {fluidSim.GetPlayerSubmersionDepth():F2}");
            }
        }
        else
        {
            timeInFluid = 0f;
        }

        wasInFluid = isInFluid;
    }

    bool IsInFluid()
    {
        // NEW: Use FluidSim2D's particle-based detection
        if (fluidSim != null)
        {
            return fluidSim.IsPlayerInFluid();
        }

        // FALLBACK: Old proximity-based detection for other fluid objects
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(transform.position, fluidPushRadius);
        foreach (var collider in nearbyColliders)
        {
            if (collider.CompareTag("Fluid") || collider.name.Contains("Particle"))
            {
                return true;
            }
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

    // === Movement Property Getters ===
    public Vector2 GetVelocity() => rb.linearVelocity;

    public float GetMovementIntensity()
    {
        return Mathf.Clamp01(rb.linearVelocity.magnitude / (moveSpeed * 2f));
    }

    public float GetCurrentMoveSpeed()
    {
        if (IsInFluid())
        {
            // NEW: Scale movement based on submersion depth
            float submersion = GetFluidSubmersionLevel();
            float maxSubmersion = fluidSim?.maxBuoyancyDepth ?? 3f;
            float submersionRatio = Mathf.Clamp01(submersion / maxSubmersion);

            // More submersion = more movement reduction
            float fluidModifier = Mathf.Lerp(1f, fluidMoveSpeedMultiplier, submersionRatio);
            return moveSpeed * fluidModifier;
        }
        return moveSpeed;
    }

    public float GetCurrentJumpForce()
    {
        if (IsInFluid())
        {
            // NEW: Scale jump based on submersion depth
            float submersion = GetFluidSubmersionLevel();
            float maxSubmersion = fluidSim?.maxBuoyancyDepth ?? 3f;
            float submersionRatio = Mathf.Clamp01(submersion / maxSubmersion);

            // More submersion = more jump boost
            float fluidModifier = Mathf.Lerp(1f, fluidJumpMultiplier, submersionRatio);
            return jumpForce * fluidModifier;
        }
        return jumpForce;
    }

    public bool CanSwim()
    {
        if (!IsInFluid()) return false;

        // NEW: Require minimum submersion to swim
        float submersion = GetFluidSubmersionLevel();
        float minSubmersionForSwimming = 0.5f; // Minimum depth needed to swim

        return submersion >= minSubmersionForSwimming &&
               IsInMirroredState() &&
               canSwimInMirroredState;
    }

    // === State Interface ===
    private interface IPlayerState
    {
        void Enter();
        void Exit();
        void HandleUpdate();
        void HandleFixedUpdate();
    }

    // === Normal Movement State ===
    private class NormalState : IPlayerState
    {
        private PlayerManager pm;
        private Vector2 input;
        public NormalState(PlayerManager pm) => this.pm = pm;

        public void Enter()
        {
            pm.rb.gravityScale = 1;
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);

            // Smooth transition to upright
            if (pm.maintainUpright)
            {
                pm.StartCoroutine(pm.SmoothRotationTransition(Quaternion.identity));
            }

            Debug.Log("Player entered normal state");
        }

        public void Exit() { }

        public void HandleUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            if (input.x != 0) pm.sr.flipX = input.x < 0;

            pm.CheckGrounded();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (pm.isGrounded || (pm.isInFluid && pm.timeInFluid > 0.1f))
                {
                    float jumpForce = pm.GetCurrentJumpForce();
                    pm.rb.linearVelocity = new Vector2(pm.rb.linearVelocity.x, jumpForce);

                    if (pm.isInFluid)
                    {
                        pm.CreateSplashEffect();
                    }
                }
            }

            // NEW: Add fluid interaction handling
            pm.HandleFluidInteraction();

            HandlePushInteraction();
        }

        public void HandleFixedUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            float currentMoveSpeed = pm.GetCurrentMoveSpeed();
            pm.rb.linearVelocity = new Vector2(input.x * currentMoveSpeed, pm.rb.linearVelocity.y);

            Vector2 normal = pm.DetectGroundNormal();
            if (pm.maintainUpright) pm.RotateToMatchNormal(normal);
            pm.sr.color = Color.Lerp(pm.sr.color, pm.normalColor, Time.deltaTime * 10f);
        }

        private void HandlePushInteraction()
        {
            Vector2 pushDirection = pm.sr.flipX ? Vector2.left : Vector2.right;
            RaycastHit2D hit = Physics2D.Raycast(
                pm.transform.position,
                pushDirection,
                pm.pushCheckDistance,
                1 << pm.blackSpaceLayer);

            if (hit.collider != null && Input.GetKey(KeyCode.F))
            {
                Rigidbody2D objRb = hit.collider.GetComponent<Rigidbody2D>();

                if (objRb != null && objRb.bodyType == RigidbodyType2D.Dynamic)
                {
                    objRb.AddForce(pushDirection * pm.pushForce, ForceMode2D.Force);
                    pm.rb.linearVelocity = new Vector2(pm.rb.linearVelocity.x * 0.7f, pm.rb.linearVelocity.y);
                }
            }
        }
    }

    // === Mirrored (Black Space) Movement State ===
    private class MirroredState : IPlayerState
    {
        private PlayerManager pm;
        private Vector2 input;

        public MirroredState(PlayerManager pm) => this.pm = pm;

        public void Enter()
        {
            pm.rb.gravityScale = -1f;
            pm.rb.linearVelocity = Vector2.zero;
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);

            // Smooth transition to inverted
            if (pm.maintainUpright)
            {
                pm.StartCoroutine(pm.SmoothRotationTransition(Quaternion.Euler(0, 0, 180)));
            }

            Debug.Log("Player entered mirrored state");
        }

        public void Exit()
        {
            pm.rb.gravityScale = 1f;
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);
        }

        public void HandleUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            if (input.x != 0) pm.sr.flipX = input.x < 0;

            bool isGroundedInBlackSpace = pm.CheckMirroredGrounded();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (isGroundedInBlackSpace || pm.CanSwim())
                {
                    float jumpForce = pm.GetCurrentJumpForce();
                    Vector2 jumpDirection = pm.CanSwim() ? Vector2.up : Vector2.down;
                    pm.rb.linearVelocity = new Vector2(pm.rb.linearVelocity.x, jumpDirection.y * jumpForce);

                    if (pm.isInFluid)
                    {
                        pm.CreateSplashEffect();
                    }
                }
            }

            // NEW: Add fluid interaction handling
            pm.HandleFluidInteraction();
        }

        public void HandleFixedUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            float currentMoveSpeed = pm.GetCurrentMoveSpeed();
            pm.rb.linearVelocity = new Vector2(input.x * currentMoveSpeed, pm.rb.linearVelocity.y);

            if (pm.CanSwim())
            {
                pm.transform.rotation = Quaternion.Slerp(
                    pm.transform.rotation,
                    Quaternion.Euler(0, 0, 0),
                    Time.deltaTime * pm.rotationSpeed
                );
            }
            else if (pm.maintainUpright)
            {
                pm.transform.rotation = Quaternion.Slerp(
                    pm.transform.rotation,
                    Quaternion.Euler(0, 0, 180),
                    Time.deltaTime * pm.rotationSpeed * 2f
                );
            }

            pm.sr.color = Color.Lerp(pm.sr.color, pm.mirroredColor, Time.deltaTime * 10f);
        }

        private void HandlePlayerFluidControls()
        {
            pm.HandleFluidInteraction();

            // Optional: Visual feedback for fluid control
            if (pm.IsPlayerControllingFluid())
            {
                // You could add particle effects, screen shake, or other feedback here
                if (pm.isFluidPulling)
                {
                    // Visual feedback for pulling
                    Debug.Log("Player pulling fluid");
                }
                else if (pm.isFluidPushing)
                {
                    // Visual feedback for pushing  
                    Debug.Log("Player pushing fluid");
                }
            }
        }
    }

    // === Helper Coroutines ===
    private System.Collections.IEnumerator SmoothRotationTransition(Quaternion targetRotation)
    {
        Quaternion startRotation = transform.rotation;
        float elapsed = 0f;

        while (elapsed < transitionSmoothTime)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / transitionSmoothTime;
            transform.rotation = Quaternion.Slerp(startRotation, targetRotation, progress);
            yield return null;
        }

        transform.rotation = targetRotation;
        isTransitioning = false;
    }

    // === Utility Functions (keeping your existing ones) ===
    private bool CheckMirroredGrounded()
    {
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.up, 0.2f, 1 << blackSpaceLayer);
        bool isOnBlackSpace = hit.collider != null;
        return isOnBlackSpace;
    }

    private void CheckGrounded()
    {
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.down, 0.2f, solidLayerMask);
        isGrounded = hit.collider != null;
    }

    private Vector2 DetectGroundNormal()
    {
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position,
                                             Vector2.down, 1f, solidLayerMask);
        return hit ? hit.normal : Vector2.up;
    }

    private void RotateToMatchNormal(Vector2 up)
    {
        float angle = Mathf.Atan2(up.x, up.y) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Slerp(transform.rotation,
                                             Quaternion.Euler(0, 0, -angle),
                                             Time.deltaTime * rotationSpeed);
    }

    private Vector2 FindExitPoint()
    {
        // Find nearest normal space
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(
            transform.position,
            detectionRadius,
            normalSpaceLayer
        );

        if (nearbyColliders.Length > 0)
        {
            // Find the closest normal space
            float closestDist = Mathf.Infinity;
            Vector2 closestPoint = transform.position;

            foreach (var collider in nearbyColliders)
            {
                Vector2 point = collider.ClosestPoint(transform.position);
                float dist = Vector2.Distance(transform.position, point);

                if (dist < closestDist)
                {
                    closestDist = dist;
                    closestPoint = point + (Vector2)(transform.position - (Vector3)point).normalized * edgeFollowDistance;
                }
            }

            return closestPoint;
        }

        // Fallback: move up and out
        return transform.position + Vector3.up * edgeFollowDistance * 2f;
    }

    public bool IsInFluidArea()
    {
        return IsInFluid();
    }

    public float GetFluidSubmersionLevel()
    {
        if (fluidSim != null)
        {
            return fluidSim.GetPlayerSubmersionDepth();
        }
        return 0f;
    }

    public int GetNearbyFluidParticleCount()
    {
        if (fluidSim != null)
        {
            return fluidSim.GetNearbyParticleCount();
        }
        return 0;
    }

    private void HandleFluidInteraction()
    {
        if (!enablePlayerFluidControl || !enableFluidInteraction)
        {
            isFluidPulling = false;
            isFluidPushing = false;
            fluidControlStrength = 0f;
            return;
        }

        // Check if player is near fluid (if required)
        if (requireFluidProximityForControl)
        {
            bool nearFluid = false;

            if (fluidSim != null)
            {
                // Check if player is within control proximity of fluid
                Vector2 playerPos = transform.position;

                // Use the cached particle positions from FluidSim2D if available
                if (fluidSim.positionBuffer != null && fluidSim.numParticles > 0)
                {
                    // We'll check this through the fluid sim's existing methods
                    nearFluid = IsInFluid() || GetDistanceToNearestFluidParticle() <= fluidControlProximityRadius;
                }
            }

            if (!nearFluid)
            {
                isFluidPulling = false;
                isFluidPushing = false;
                fluidControlStrength = 0f;
                return;
            }
        }

        // Handle input
        isFluidPulling = Input.GetKey(fluidPullKey);
        isFluidPushing = Input.GetKey(fluidPushKey);

        // Calculate interaction strength
        if (isFluidPushing || isFluidPulling)
        {
            fluidControlStrength = isFluidPushing ? -playerFluidInteractionStrength : playerFluidInteractionStrength;

            // Modify strength based on player state
            if (IsInMirroredState())
            {
                fluidControlStrength *= 1.3f; // Stronger interaction in mirrored state
            }

            // Reduce strength if player is moving fast to prevent overpowered effects
            float movementFactor = Mathf.Clamp01(2f - (rb.linearVelocity.magnitude / moveSpeed));
            fluidControlStrength *= movementFactor;
        }
        else
        {
            fluidControlStrength = 0f;
        }
    }

    // Add this helper method:
    private float GetDistanceToNearestFluidParticle()
    {
        if (fluidSim == null) return float.MaxValue;

        // This is a simplified check - in a full implementation, you'd want to 
        // check against actual particle positions, but this gives a reasonable approximation
        Vector2 playerPos = transform.position;

        // Check if we're in the general fluid area
        if (fluidSim.IsPlayerInFluid())
        {
            return 0f; // Already in fluid
        }

        // For now, return a reasonable estimate based on fluid detection radius
        return fluidSim.fluidDetectionRadius;
    }

    // Add these public methods for FluidSim2D to query:
    public bool IsPlayerControllingFluid()
    {
        return enablePlayerFluidControl && (isFluidPulling || isFluidPushing);
    }

    public float GetPlayerFluidControlStrength()
    {
        return fluidControlStrength;
    }

    public float GetPlayerFluidControlRadius()
    {
        return playerFluidInteractionRadius;
    }

    public Vector2 GetPlayerFluidControlPosition()
    {
        return transform.position;
    }

    public float GetCurrentPlayerInteractionStrength()
    {
        return playerFluidInteractionStrength;
    }

    /*
// ==== OPTIONAL: Enhanced debug gizmos ====
void OnDrawGizmosSelected()
{
    if (!Application.isPlaying) return;

    // Existing gizmos...
    
    // NEW: Draw fluid detection status
    if (enableFluidInteraction && fluidSim != null)
    {
        Vector3 playerPos = transform.position;
        
        // Draw fluid detection radius
        Gizmos.color = IsInFluid() ? Color.cyan : Color.gray;
        if (fluidSim.useParticleBasedDetection)
        {
            Gizmos.DrawWireSphere(playerPos, fluidSim.fluidDetectionRadius);
        }
        
        // Draw submersion depth indicator
        float submersion = GetFluidSubmersionLevel();
        if (submersion > 0f)
        {
            Gizmos.color = new Color(0, 0, 1, 0.3f);
            Gizmos.DrawWireSphere(playerPos, submersion);
            
            // Draw submersion bar
            Gizmos.color = Color.blue;
            Vector3 barStart = playerPos + Vector3.right * 1f;
            Vector3 barEnd = barStart + Vector3.up * submersion;
            Gizmos.DrawLine(barStart, barEnd);
            Gizmos.DrawWireCube(barEnd, Vector3.one * 0.1f);
        }
        
        // Draw particle count indicator
        int particleCount = GetNearbyFluidParticleCount();
        if (particleCount > 0)
        {
            Gizmos.color = Color.yellow;
            for (int i = 0; i < Mathf.Min(particleCount, 10); i++)
            {
                float angle = (i / 10f) * 360f * Mathf.Deg2Rad;
                Vector3 offset = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle)) * 0.5f;
                Gizmos.DrawWireCube(playerPos + Vector3.up * 0.8f + offset, Vector3.one * 0.05f);
            }
        }
    }

#if UNITY_EDITOR
    // Enhanced debug text
    Vector3 textPos = transform.position + Vector3.up * 2f;
    UnityEditor.Handles.Label(textPos, 
        $"Space: {currentSpaceType}\n" +
        $"In Fluid: {IsInFluid()}\n" +
        $"Submersion: {GetFluidSubmersionLevel():F2}\n" +
        $"Particles: {GetNearbyFluidParticleCount()}\n" +
        $"Can Swim: {CanSwim()}");
#endif
}
*/

}