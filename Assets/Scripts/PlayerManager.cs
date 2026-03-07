using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Enhanced PlayerManager with automatic space-based state transitions
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(SpriteRenderer), typeof(Collider2D))]
public class PlayerManager : MonoBehaviour
{
    // === Serialized Fields ===

    [Header("Movement")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float jumpForce = 8f;
    [SerializeField] private float flipCooldown = 0.5f;
    [SerializeField] private bool maintainUpright = true;
    [SerializeField] private float rotationSpeed = 10f;
    [SerializeField] private float maxMoveSpeed = 6f;
    [SerializeField] private float accel = 30f;
    [SerializeField] private float decel = 40f;
    [SerializeField] private float coyoteTime = 0.12f;
    [SerializeField] private float jumpBufferTime = 0.12f;
    [SerializeField] private float jumpCutMultiplier = 0.4f;

    [Header("Sprite")]
    [SerializeField] private Color normalColor = Color.white;
    [SerializeField] private Color mirroredColor = Color.black;

    [Header("Surface and Ground")]
    [SerializeField] private LayerMask solidLayer;
    [SerializeField] private Transform groundChecker;
    [SerializeField] private float detectionRadius = 2f;
    [SerializeField] private float edgeFollowDistance = 0.3f;
    [SerializeField] private float groundCheckDistance = 0.3f;

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

    // === Space Types ===
    public enum SpaceType
    {
        Normal,                 // Regular world space
        InvertedSpace,          // Inverted/mirrored space
        Fluid,                  // Water/fluid space
    }

    // === Runtime Components & State ===
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private Collider2D playerCollider;
    private IPlayerState currentState;
    private NormalState normalState;
    private MirroredState mirroredState;
    private PlayerWaterInteraction water;

    // === Cached Values ===
    private int invertedSpaceLayer;
    private int solidLayerMask;
    private bool isGrounded;
    public bool IsGrounded => isGrounded;
    private Vector2 currentNormal;
    private Vector2 entryPoint;

    // === Movement Tracking ===
    private float currentXSpeed;
    private float lastGroundedTime;
    private float lastJumpPressedTime;
    private float lastFlipTime;
    private Vector2 lastPosition;

    // Space detection
    private SpaceType currentSpaceType;
    private SpaceType previousSpaceType;
    private bool isTransitioning;

#if UNITY_6000_0_OR_NEWER
    private Vector2 BodyVelocity
    {
        get => rb.linearVelocity;
        set => rb.linearVelocity = value;
    }
#else
    private Vector2 BodyVelocity
    {
        get => rb.velocity;
        set => rb.velocity = value;
    }
#endif

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        playerCollider = GetComponent<Collider2D>();
        water = GetComponent<PlayerWaterInteraction>();
        // Draw on top of terrain (Background) and other 2D so we don't vanish when moving left
        sr.sortingLayerName = "Characters";
        sr.sortingOrder = 32767;
        invertedSpaceLayer = LayerMask.NameToLayer("InvertedSpace");
        solidLayerMask = solidLayer;
        normalState = new NormalState(this);
        mirroredState = new MirroredState(this);

        // Always start in normal space; entering inverted space is driven manually.
        currentSpaceType = SpaceType.Normal;
        previousSpaceType = currentSpaceType;
        TransitionTo(normalState);

        lastPosition = transform.position;

        // Initialize jump timers so we don't trigger a jump on the very first frame
        lastGroundedTime = coyoteTime + 1f;
        lastJumpPressedTime = jumpBufferTime + 1f;
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
        water?.Tick();

        // Track coyote-time window
        lastGroundedTime = isGrounded ? 0f : lastGroundedTime + Time.deltaTime;
        // Track jump-buffer window
        if (Input.GetKeyDown(KeyCode.Space))
            lastJumpPressedTime = 0f;
        else
            lastJumpPressedTime += Time.deltaTime;
        // Variable jump cut: apply when space released and still moving up
        if (Input.GetKeyUp(KeyCode.Space) && BodyVelocity.y > 0f)
            BodyVelocity = new Vector2(BodyVelocity.x, BodyVelocity.y * jumpCutMultiplier);

    }

    void FixedUpdate()
    {
        currentState.HandleFixedUpdate();
        water?.UpdateVelocityTracking();
    }

    // === Space Detection System ===
    // InvertedSpace geometry comes from SplineShapeGenerator_InvertedSpace: closed splines
    // become a CompositeCollider2D on the "InvertedSpace" layer. We differentiate:
    // - Free movement in level: Normal state, no overlap with InvertedSpace required.
    // - Moving inside InvertedSpace: Mirrored state only (entered via E when grounded and
    //   raycast hits InvertedSpace). Entry/exit use that layer for raycasts and ground checks.
    private SpaceType DetectCurrentSpace()
    {
        // Inversion is manual (E key). Space type = current state:
        // Mirrored => InvertedSpace, else Fluid if in water, else Normal.
        if (currentState == mirroredState)
        {
            return SpaceType.InvertedSpace;
        }

        if (water != null && water.IsInFluid)
        {
            return SpaceType.Fluid;
        }

        return SpaceType.Normal;
    }

    private bool ValidateInvertedSpaceEntry(Vector2 playerPos)
    {
        // First check if we're in fluid - prevent inversion in fluid
        if (preventInversionInFluid && water != null && water.IsInFluid)
        {
            Debug.Log("Inverted space entry blocked - cannot invert while in fluid");
            return false;
        }

        // Then check grounded requirement if enabled
        if (requireGroundedForInversion)
        {
            // Consider both normal ground and inverted-space ground as valid.
            bool groundedNormal = isGrounded;
            bool groundedInverted = CheckMirroredGrounded();
            if (!groundedNormal && !groundedInverted)
            {
                Debug.Log("Inverted space entry blocked - not grounded in normal or inverted space");
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
                Debug.Log("Inverted space entry blocked - side entry not allowed");
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

            if (hit.collider != null && hit.collider.gameObject.layer == invertedSpaceLayer)
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
                        // Move slightly *into* black space along the surface normal
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
        // Track space type changes only for debugging/FX; do NOT auto-switch states.
        if (currentSpaceType != previousSpaceType)
        {
            Debug.Log($"Space transition detected (no auto-state change): {previousSpaceType} -> {currentSpaceType}");
            previousSpaceType = currentSpaceType;
        }

        // Manual inversion remains the only way to enter/exit inverted space.
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

            case SpaceType.InvertedSpace:
                if (currentState != mirroredState)
                {
                    // Use improved entry positioning
                    if (TryEnterInvertedSpaceImproved())
                    {
                        TransitionTo(mirroredState);
                    }
                    else
                    {
                        // Entry failed, stay in normal space
                        Debug.Log("Inverted space entry failed - invalid entry point");
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

    /// <summary>
    /// Simple entry: look straight above/below the player for InvertedSpace
    /// and enter there. This is more forgiving than the side-entry logic and
    /// matches the behaviour you expect when standing where you want to invert.
    /// </summary>
    private bool TryEnterInvertedSpaceImproved()
    {
        // Check fluid restriction first
        if (preventInversionInFluid && water != null && water.IsInFluid)
        {
            Debug.Log("Cannot enter inverted space - in fluid");
            return false;
        }
        // Check grounded requirement
        if (requireGroundedForInversion)
        {
            if (!isGrounded)
            {
                Debug.Log("Cannot enter inverted space - not grounded");
                return false;
            }
        }
        float maxDist = detectionRadius > 0 ? detectionRadius : 2f;
        Vector2 origin = transform.position;
        LayerMask invertedMask = 1 << invertedSpaceLayer;
        // Prefer a hit directly below (standing on inverted floor), else above (ceiling).
        RaycastHit2D hitDown = Physics2D.Raycast(origin, Vector2.down, maxDist, invertedMask);
        RaycastHit2D hitUp = hitDown.collider == null
            ? Physics2D.Raycast(origin, Vector2.up, maxDist, invertedMask)
            : default;
        RaycastHit2D hit = hitDown.collider != null ? hitDown : hitUp;
        if (hit.collider == null)
        {
            Debug.Log("No valid inverted space entry point found (no InvertedSpace above/below player)");
            return false;
        }
        // Store entry information
        entryPoint = origin;
        currentNormal = hit.normal;
        // Place the player fully inside inverted space using collider height,
        // so the "feet" side isn't glued to the world-space edge, and clear velocity.
        if (snapToValidPosition)
        {
            float halfHeight = playerCollider.bounds.extents.y;
            float entryOffset = 0.05f;
            Vector2 targetPos = hit.point - hit.normal * (halfHeight + entryOffset);
            rb.position = targetPos;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
        }
        Debug.Log($"Entering inverted space at {hit.point}, normal {hit.normal}, grounded: {isGrounded}");
        return true;
    }

    private void ManualFlip()
    {
        lastFlipTime = Time.time;

        // Manual flip overrides automatic detection temporarily
        if (currentState == normalState)
        {
            // Full entry validation (fluid, grounded, entry quality)
            if (!ValidateInvertedSpaceEntry(transform.position))
            {
                return;
            }

            // Reuse the same entry logic as the automatic transition so behaviour is consistent.
            if (TryEnterInvertedSpaceImproved())
            {
                TransitionTo(mirroredState);
            }
        }
        else
        {
            // Exit: transition first (restore scale + gravity), then teleport to entry.
            Vector2 exitPos = FindExitPoint();
            Debug.Log($"Exiting inverted space to entryPoint ({exitPos.x:F2}, {exitPos.y:F2})");
            TransitionTo(normalState);
            rb.position = exitPos;
            rb.velocity = Vector2.zero;
            rb.angularVelocity = 0f;
            transform.position = exitPos;
        }

        // Optional: splash visual now handled by PlayerWaterInteraction if desired
    }

    private bool TryFindNearbyInvertedSpace(out Vector2 entryPos, out Vector2 normal)
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

    public bool IsInMirroredState() => currentState == mirroredState;

    public float MoveSpeedForFluidControl => moveSpeed;

    public Vector2 LastPosition
    {
        get => lastPosition;
        set => lastPosition = value;
    }

    public bool CanFlip() => Time.time - lastFlipTime >= flipCooldown;

    public SpaceType GetCurrentSpaceType() => currentSpaceType;
    public bool IsTransitioning() => isTransitioning;

    // === Movement Property Getters ===
    public Vector2 GetVelocity() => BodyVelocity;

    public float GetMovementIntensity()
    {
        return Mathf.Clamp01(rb.velocity.magnitude / (moveSpeed * 2f));
    }

    public float GetCurrentMoveSpeed()
    {
        return water != null ? water.ModifyMoveSpeed(moveSpeed) : moveSpeed;
    }

    public float GetCurrentJumpForce()
    {
        return water != null ? water.ModifyJumpForce(jumpForce) : jumpForce;
    }

    public bool CanSwim()
    {
        if (water == null || !water.IsInFluid) return false;

        // Require minimum submersion to swim
        float submersion = water.SubmersionDepth;
        float minSubmersionForSwimming = 0.5f;

        return submersion >= minSubmersionForSwimming && IsInMirroredState();
    }

    // === State Interface ===
    private interface IPlayerState
    {
        void Enter();
        void Exit();
        void HandleUpdate();
        void HandleFixedUpdate();
    }

            private Vector2 DetectInvertedGroundNormal()
        {
            // Raycast up into the inverted space layer to find the inverted "ground"
            RaycastHit2D hit = Physics2D.Raycast(
                groundChecker.position,
                Vector2.up,
                1f,
                1 << invertedSpaceLayer);
            return hit ? hit.normal : Vector2.down; // default to down if nothing hit
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
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.invertedSpaceLayer, false);
            // Ensure scale is upright in normal space
            Vector3 s = pm.transform.localScale;
            s.y = Mathf.Abs(s.y);
            pm.transform.localScale = s;

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
                // handled in outer Update via jumpBufferTime; nothing here
            }

            HandlePushInteraction();
        }

        public void HandleFixedUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");

            // 1) Horizontal accel/decel (same feel as mirrored state)
            float targetSpeed = input.x * pm.maxMoveSpeed;
            float speedDiff = targetSpeed - pm.currentXSpeed;
            float accelRate = Mathf.Abs(targetSpeed) > Mathf.Abs(pm.currentXSpeed)
                ? pm.accel
                : pm.decel;
            pm.currentXSpeed += speedDiff * accelRate * Time.fixedDeltaTime;
            pm.BodyVelocity = new Vector2(pm.currentXSpeed, pm.BodyVelocity.y);

            // 2) Jump (coyote + buffer)
            bool canCoyote = pm.lastGroundedTime <= pm.coyoteTime;
            bool hasBufferedJump = pm.lastJumpPressedTime <= pm.jumpBufferTime;
            if (canCoyote && hasBufferedJump)
            {
                float jumpForce = pm.GetCurrentJumpForce();
                pm.BodyVelocity = new Vector2(pm.BodyVelocity.x, jumpForce);

                // consume buffers
                pm.lastGroundedTime = pm.coyoteTime + 1f;
                pm.lastJumpPressedTime = pm.jumpBufferTime + 1f;
            }

            // 3) Ground-aligned rotation & color
            if (pm.maintainUpright)
            {
                Vector2 groundNormal = pm.DetectGroundNormal();
                pm.RotateToMatchNormal(groundNormal);
            }

            pm.sr.color = Color.Lerp(pm.sr.color, pm.normalColor, Time.deltaTime * 10f);
        }

        private void HandlePushInteraction()
        {
            Vector2 pushDirection = pm.sr.flipX ? Vector2.left : Vector2.right;
            RaycastHit2D hit = Physics2D.Raycast(
                pm.transform.position,
                pushDirection,
                pm.pushCheckDistance,
                1 << pm.invertedSpaceLayer);

            if (hit.collider != null && Input.GetKey(KeyCode.F))
            {
                Rigidbody2D objRb = hit.collider.GetComponent<Rigidbody2D>();

                if (objRb != null && objRb.bodyType == RigidbodyType2D.Dynamic)
                {
                    objRb.AddForce(pushDirection * pm.pushForce, ForceMode2D.Force);
                    pm.BodyVelocity = new Vector2(pm.BodyVelocity.x * 0.7f, pm.BodyVelocity.y);
                }
            }
        }
    }

    // === Mirrored (Inverted Space) Movement State ===
    private class MirroredState : IPlayerState
    {
        private PlayerManager pm;
        private Vector2 input;

        public MirroredState(PlayerManager pm) => this.pm = pm;

        public void Enter()
        {
            pm.rb.gravityScale = -1f;
            pm.BodyVelocity = Vector2.zero;
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.invertedSpaceLayer, false);
            // Flip vertically via scale so the character appears
            // upside‑down while colliders stay consistent.
            Vector3 s = pm.transform.localScale;
            s.y = -Mathf.Abs(s.y);
            pm.transform.localScale = s;

            Debug.Log("Player entered mirrored state");
        }

        public void Exit()
        {
            pm.rb.gravityScale = 1f;
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.invertedSpaceLayer, false);
        }

        public void HandleUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            if (input.x != 0) pm.sr.flipX = input.x < 0;

            bool isGroundedInInvertedSpace = pm.CheckMirroredGrounded();

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (isGroundedInInvertedSpace || pm.CanSwim())
                {
                    float jumpForce = pm.GetCurrentJumpForce();
                    Vector2 jumpDirection = pm.CanSwim() ? Vector2.up : Vector2.down;
                    pm.BodyVelocity = new Vector2(pm.BodyVelocity.x, jumpDirection.y * jumpForce);
                }
            }
        }

        public void HandleFixedUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            // 1) Horizontal accel/decel
            float targetSpeed = input.x * pm.maxMoveSpeed;
            float speedDiff = targetSpeed - pm.currentXSpeed;
            float accelRate = Mathf.Abs(targetSpeed) > Mathf.Abs(pm.currentXSpeed)
                ? pm.accel
                : pm.decel;
            pm.currentXSpeed += speedDiff * accelRate * Time.fixedDeltaTime;
            pm.BodyVelocity = new Vector2(pm.currentXSpeed, pm.BodyVelocity.y);
            // 2) Jump (coyote + buffer)
            bool canCoyote = pm.lastGroundedTime <= pm.coyoteTime;
            bool hasBufferedJump = pm.lastJumpPressedTime <= pm.jumpBufferTime;
            if (canCoyote && hasBufferedJump)
            {
                float jumpForce = pm.GetCurrentJumpForce();
                pm.BodyVelocity = new Vector2(pm.BodyVelocity.x, jumpForce);
                // consume buffers
                pm.lastGroundedTime = pm.coyoteTime + 1f;
                pm.lastJumpPressedTime = pm.jumpBufferTime + 1f;
            }
            // 3) Color only (no extra rotation in mirrored space)
            pm.sr.color = Color.Lerp(pm.sr.color, pm.normalColor, Time.deltaTime * 10f);
        }

        // Fluid control visuals are now handled by PlayerWaterInteraction if needed
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
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.up, groundCheckDistance, 1 << invertedSpaceLayer);
        bool isOnInvertedSpace = hit.collider != null;
        return isOnInvertedSpace;
    }

    private void CheckGrounded()
    {
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.down, groundCheckDistance, solidLayerMask);
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

    /// <summary>
    /// Exit inverted space: place the player at the mirrored position across the boundary
    /// (same as the gizmo capsule), so they land in the corresponding spot on the normal side.
    /// Falls back to entryPoint if the boundary can't be found.
    /// </summary>
    private Vector2 FindExitPoint()
    {
        if (TryGetBoundaryHit(out Vector2 boundaryPoint, out Vector2 boundaryNormal))
        {
            return ReflectPointAcrossBoundary(transform.position, boundaryPoint, boundaryNormal);
        }
        return entryPoint;
    }

    public bool IsInFluidArea()
    {
        return water != null && water.IsInFluid;
    }

    public float GetFluidSubmersionLevel()
    {
        return water != null ? water.SubmersionDepth : 0f;
    }

    public int GetNearbyFluidParticleCount()
    {
        return water != null ? water.NearbyParticleCount : 0;
    }

    // Public wrappers for FluidSim2D / other systems to query water-control info via PlayerManager
    public bool IsPlayerControllingFluid()
    {
        return water != null && water.IsControllingFluid;
    }

    public float GetPlayerFluidControlStrength()
    {
        return water != null ? water.ControlStrength : 0f;
    }

    public float GetPlayerFluidControlRadius()
    {
        return water != null ? water.ControlRadius : 0f;
    }

    public Vector2 GetPlayerFluidControlPosition()
    {
        return water != null ? water.ControlPosition : (Vector2)transform.position;
    }

    public float GetCurrentPlayerInteractionStrength()
    {
        return water != null ? water.BaseInteractionStrength : 0f;
    }

    /// <summary>
    /// Raycast to the InvertedSpace boundary from the current position (down when mirrored, up when normal).
    /// Returns true and the hit point/normal if we hit the boundary.
    /// </summary>
    public bool TryGetBoundaryHit(out Vector2 boundaryPoint, out Vector2 boundaryNormal)
    {
        boundaryPoint = Vector2.zero;
        boundaryNormal = Vector2.zero;
        float maxDist = detectionRadius > 0 ? detectionRadius : 4f;
        Vector2 origin = transform.position;
        LayerMask invertedMask = 1 << invertedSpaceLayer;

        // When mirrored we're on the ceiling → raycast down. When normal we're below → raycast up.
        Vector2 primaryDir = currentState == mirroredState ? Vector2.down : Vector2.up;
        RaycastHit2D hit = Physics2D.Raycast(origin, primaryDir, maxDist, invertedMask);
        if (hit.collider == null)
            hit = Physics2D.Raycast(origin, -primaryDir, maxDist, invertedMask);

        if (hit.collider == null)
            return false;

        boundaryPoint = hit.point;
        boundaryNormal = hit.normal;
        return true;
    }

    /// <summary>
    /// Reflect a point across the boundary line (point B, normal N). Returns the mirrored position.
    /// </summary>
    public static Vector2 ReflectPointAcrossBoundary(Vector2 point, Vector2 boundaryPoint, Vector2 boundaryNormal)
    {
        Vector2 n = boundaryNormal.sqrMagnitude > 0.001f ? boundaryNormal.normalized : boundaryNormal;
        float d = Vector2.Dot(point - boundaryPoint, n);
        return point - 2f * d * n;
    }

    /// <summary>
    /// Get capsule dimensions from the player collider for gizmos/positioning (half-height and radius).
    /// </summary>
    public void GetCapsuleSize(out float halfHeight, out float radius)
    {
        if (playerCollider == null)
        {
            halfHeight = 0.5f;
            radius = 0.25f;
            return;
        }

        var cap = playerCollider as CapsuleCollider2D;
        if (cap != null)
        {
            float w = cap.size.x * 0.5f;
            float h = cap.size.y * 0.5f;
            if (cap.direction == CapsuleDirection2D.Vertical)
            {
                radius = w;
                halfHeight = h;
            }
            else
            {
                radius = h;
                halfHeight = w;
            }
            // Scale by transform
            halfHeight *= Mathf.Abs(transform.lossyScale.y);
            radius *= Mathf.Abs(transform.lossyScale.x);
            return;
        }

        Bounds b = playerCollider.bounds;
        halfHeight = b.extents.y;
        radius = b.extents.x;
    }

    /// <summary>
    /// Draw a vertical capsule outline in the XY plane (for 2D gizmo).
    /// </summary>
    private static void DrawCapsuleGizmo(Vector2 center, float halfHeight, float radius)
    {
        float cylinderHalf = Mathf.Max(0f, halfHeight - radius);
        Vector3 topCenter = new Vector3(center.x, center.y + cylinderHalf, 0f);
        Vector3 bottomCenter = new Vector3(center.x, center.y - cylinderHalf, 0f);

        Gizmos.DrawWireSphere(topCenter, radius);
        Gizmos.DrawWireSphere(bottomCenter, radius);
        Gizmos.DrawLine(new Vector3(center.x - radius, topCenter.y, 0f), new Vector3(center.x - radius, bottomCenter.y, 0f));
        Gizmos.DrawLine(new Vector3(center.x + radius, topCenter.y, 0f), new Vector3(center.x + radius, bottomCenter.y, 0f));
    }

void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;

        // Space detection radius
        Gizmos.color = currentSpaceType == SpaceType.InvertedSpace ? Color.red :
                      currentSpaceType == SpaceType.Fluid ? Color.blue : Color.green;
        Gizmos.DrawWireSphere(transform.position, spaceCheckRadius);

        // Detection radius
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);

        // Mirrored capsule: same size as player, on the other side of the InvertedSpace boundary
        if (TryGetBoundaryHit(out Vector2 boundaryPoint, out Vector2 boundaryNormal))
        {
            Vector2 mirroredPos = ReflectPointAcrossBoundary(transform.position, boundaryPoint, boundaryNormal);
            GetCapsuleSize(out float halfHeight, out float radius);

            Gizmos.color = Color.cyan;
            DrawCapsuleGizmo(mirroredPos, halfHeight, radius);

            // Boundary point and normal for reference
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(boundaryPoint, 0.08f);
            Gizmos.DrawLine(boundaryPoint, boundaryPoint + boundaryNormal * 0.5f);
        }

        // Draw entry validation rays if restricting side entry
        if (restrictSideEntry)
        {
            Vector2[] checkDirections = {
                Vector2.down, Vector2.up, Vector2.left, Vector2.right,
                new Vector2(-0.7f, -0.7f), new Vector2(0.7f, -0.7f),
                new Vector2(-0.7f, 0.7f), new Vector2(0.7f, 0.7f)
            };

            EntryInfo bestEntry = FindBestEntryPoint(transform.position);

            for (int i = 0; i < checkDirections.Length; i++)
            {
                Vector2 dir = checkDirections[i];
                float verticalBias = Mathf.Abs(dir.y);

                // Color code based on entry quality
                if (i == 0) Gizmos.color = Color.green; // Down - preferred
                else if (i == 1) Gizmos.color = Color.yellow; // Up - good
                else if (verticalBias >= verticalEntryBias) Gizmos.color = new Color(1f, 0.5f, 0f); // Acceptable (orange)
                else Gizmos.color = Color.red; // Poor side entry

                Gizmos.DrawRay(transform.position, dir * detectionRadius * 0.8f);
            }

            // Draw best entry point
            if (bestEntry.isValid)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawSphere(bestEntry.position, 0.1f);
                Gizmos.DrawLine(transform.position, bestEntry.position);
            }
        }

        // Current space type indicator
        Gizmos.color = Color.yellow;
        Vector3 textPos = transform.position + Vector3.up * 1.5f;

#if UNITY_EDITOR
        UnityEditor.Handles.Label(textPos, $"Space: {currentSpaceType}");
        if (restrictSideEntry && currentSpaceType != SpaceType.InvertedSpace)
        {
            EntryInfo info = FindBestEntryPoint(transform.position);
            if (info.isValid)
            {
                UnityEditor.Handles.Label(textPos + Vector3.up * 0.3f, $"Entry Quality: {info.quality:F2}");
            }
        }
#endif
    }


}
