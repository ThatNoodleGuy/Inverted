using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;


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
    [SerializeField] private LayerMask normalSpaceLayer = 1;
    [SerializeField] private bool enableManualOverride = true;
    [SerializeField] private float transitionSmoothTime = 0.3f;

    [Header("Entry Validation")]
    [SerializeField] private bool restrictSideEntry = true;
    [SerializeField] private bool requireGroundedForInversion = true;
    [SerializeField] private float verticalEntryBias = 0.7f;
    [SerializeField] private float minimumEntryDistance = 0.2f;
    [SerializeField] private bool snapToValidPosition = true;

    [Header("Input Actions")]
    [SerializeField] private InputActionAsset actions;

    [Header("Interaction")]
    [SerializeField] private float pushForce = 2f;
    [SerializeField] private float pushCheckDistance = 0.5f;

    [Header("Afterimage Effect")]
    [SerializeField] private AfterImageEffect afterImageEffect;
    [SerializeField] private bool enableAfterimageInNormal = true;
    [SerializeField] private bool enableAfterimageInMirrored = true;
    [SerializeField] private float stateTransitionAfterimageTime = 2f;
    [SerializeField] private float dashAfterimageTime = 1.5f;
    [SerializeField] private KeyCode dashKey = KeyCode.LeftShift;
    [SerializeField] private float dashForce = 15f;
    [SerializeField] private float dashCooldown = 2f;

    // === Space Types ===
    public enum SpaceType
    {
        Normal,     // Regular world space
        BlackSpace, // Inverted/mirrored space
    }

    // === Runtime Components & State ===
    private Rigidbody2D rb;
    private SpriteRenderer sr;
    private CapsuleCollider2D cc;
    private AudioSource audioSource;
    private IPlayerState currentState;
    private NormalState normalState;
    private MirroredState mirroredState;

    // === Cached Values ===
    private int blackSpaceLayer;
    private int solidLayerMask;
    public bool isGrounded { get; private set; }
    private Vector2 currentNormal;
    private Vector2 entryPoint;
    private float lastFlipTime;
    private Vector2 lastPosition;
    private float lastDashTime;
    private bool isDashing;

    // Space detection
    private SpaceType currentSpaceType;
    private SpaceType previousSpaceType;
    private bool isTransitioning;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        cc = GetComponent<CapsuleCollider2D>();
        audioSource = GetComponent<AudioSource>();

        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();

        // Initialize afterimage effect
        if (afterImageEffect == null)
            afterImageEffect = GetComponent<AfterImageEffect>();

        if (afterImageEffect == null)
            afterImageEffect = gameObject.AddComponent<AfterImageEffect>();

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
        IPlayerState initialState = GetStateForSpaceType(currentSpaceType);
        TransitionTo(initialState);

        lastPosition = transform.position;
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
    }

    void FixedUpdate()
    {
        currentState.HandleFixedUpdate();

        // Track position for effect triggers
        lastPosition = transform.position;
    }

    // === Space Detection System ===
    private SpaceType DetectCurrentSpace()
    {
        Vector2 playerPos = transform.position;

        // Check if we're in black space
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

        Collider2D blackSpaceCollider = Physics2D.OverlapCircle(
            playerPos,
            spaceCheckRadius,
            1 << blackSpaceLayer
        );

        if (blackSpaceCollider == null)
            return false;

        if (currentState == mirroredState)
        {
            if (requireGroundedForInversion)
            {
                bool isGroundedInBlackSpace = CheckMirroredGrounded();
                return isGroundedInBlackSpace || isTransitioning;
            }
            return true;
        }

        return ValidateBlackSpaceEntry(playerPos);
    }

    private IPlayerState GetStateForSpaceType(SpaceType spaceType)
    {
        switch (spaceType)
        {
            case SpaceType.Normal: return normalState;
            case SpaceType.BlackSpace: return mirroredState;
            default: return normalState;
        }
    }

    // === Enhanced Space Transition Handling ===
    private void HandleSpaceTransitions()
    {
        // Handle space transitions
        if (currentSpaceType != previousSpaceType)
        {
            Debug.Log($"Space transition detected: {previousSpaceType} -> {currentSpaceType}");

            // Activate cyberpunk afterimage during state transition with burst effect
            if (afterImageEffect != null)
            {
                afterImageEffect.ActivateForDuration(stateTransitionAfterimageTime);
                afterImageEffect.CreateCyberpunkBurst(12, 2f); // Epic transition effect
            }

            InitiateSpaceTransition(currentSpaceType);
            previousSpaceType = currentSpaceType;
        }

        // Handle manual override if enabled
        if (enableManualOverride && CanFlip() && Input.GetKeyDown(KeyCode.E))
        {
            ManualFlip();
        }

        // Handle dash input
        HandleDashInput();
    }

    private void InitiateSpaceTransition(SpaceType newSpaceType)
    {
        IPlayerState targetState = GetStateForSpaceType(newSpaceType);

        if (currentState != targetState)
        {
            // Special handling for black space entry validation
            if (newSpaceType == SpaceType.BlackSpace && !TryEnterBlackSpaceImproved())
            {
                Debug.Log("Black space entry failed - invalid entry point");
                return;
            }

            TransitionTo(targetState);
            isTransitioning = true;
        }
    }

    private void HandleDashInput()
    {
        if (Input.GetKeyDown(dashKey) && CanDash())
        {
            StartDash();
        }
    }

    private bool CanDash()
    {
        return Time.time - lastDashTime >= dashCooldown && !isDashing;
    }

    private void StartDash()
    {
        lastDashTime = Time.time;
        isDashing = true;

        // Activate intense cyberpunk afterimage effect
        if (afterImageEffect != null)
        {
            afterImageEffect.ActivateForDuration(dashAfterimageTime);

            // Different effects for different states
            if (currentState == mirroredState)
            {
                afterImageEffect.CreateCyberpunkBurst(8, 1.8f);
            }
            else
            {
                afterImageEffect.CreateCyberpunkBurst(6, 1.2f);
            }
        }

        // Apply dash force
        Vector2 dashDirection = sr.flipX ? Vector2.left : Vector2.right;
        rb.AddForce(dashDirection * dashForce, ForceMode2D.Impulse);

        // Stop dashing after a short time
        StartCoroutine(EndDash());
    }

    private IEnumerator EndDash()
    {
        yield return new WaitForSeconds(0.2f);
        isDashing = false;
    }

    // === State Interface ===
    private interface IPlayerState
    {
        void Enter();
        void Exit();
        void HandleUpdate();
        void HandleFixedUpdate();
    }

    // === Enhanced Validation ===
    private bool ValidateBlackSpaceEntry(Vector2 playerPos)
    {
        if (requireGroundedForInversion)
        {
            CheckGrounded();
            if (!isGrounded)
            {
                Debug.Log("Black space entry blocked - not grounded");
                return false;
            }
        }

        if (restrictSideEntry)
        {
            EntryInfo entryInfo = FindBestEntryPoint(playerPos);

            if (!entryInfo.isValid)
                return false;

            float verticalComponent = Mathf.Abs(entryInfo.normal.y);
            if (verticalComponent < verticalEntryBias)
            {
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
        public float quality;
    }

    private EntryInfo FindBestEntryPoint(Vector2 fromPos)
    {
        EntryInfo bestEntry = new EntryInfo { isValid = false };
        float bestQuality = -1f;

        Vector2[] checkDirections = {
            Vector2.down, Vector2.up, Vector2.left, Vector2.right,
            new Vector2(-0.7f, -0.7f), new Vector2(0.7f, -0.7f),
            new Vector2(-0.7f, 0.7f), new Vector2(0.7f, 0.7f)
        };

        for (int i = 0; i < checkDirections.Length; i++)
        {
            Vector2 dir = checkDirections[i];
            RaycastHit2D hit = Physics2D.Raycast(fromPos, dir, detectionRadius, solidLayerMask);

            if (hit.collider != null && hit.collider.gameObject.layer == blackSpaceLayer)
            {
                float verticalBias = Mathf.Abs(dir.y);
                float distanceQuality = 1f - (hit.distance / detectionRadius);
                float quality = (verticalBias * 0.7f + distanceQuality * 0.3f);

                if (i == 0) quality += 0.2f;
                else if (i == 1) quality += 0.1f;

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

    // === State Management ===
    private void TransitionTo(IPlayerState next)
    {
        currentState?.Exit();
        currentState = next;
        currentState.Enter();

    }

    private bool TryEnterBlackSpaceImproved()
    {
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

        entryPoint = transform.position;
        currentNormal = entryInfo.normal;

        if (snapToValidPosition)
        {
            transform.position = entryInfo.position;
        }

        Debug.Log($"Entering black space with quality {entryInfo.quality:F2}");
        return true;
    }

    private void ManualFlip()
    {
        lastFlipTime = Time.time;

        if (currentState == normalState)
        {
            if (requireGroundedForInversion)
            {
                CheckGrounded();
                if (!isGrounded)
                {
                    Debug.Log("Cannot manually flip to black space - not grounded");
                    return;
                }
            }

            if (TryFindNearbyBlackSpace(out Vector2 entryPos, out Vector2 normal))
            {
                transform.position = entryPos;
                currentNormal = normal;
                TransitionTo(mirroredState);
            }
        }
        else if (currentState == mirroredState)
        {
            if (requireGroundedForInversion)
            {
                bool isGroundedInBlackSpace = CheckMirroredGrounded();
                if (!isGroundedInBlackSpace)
                {
                    Debug.Log("Cannot manually flip to normal space - not grounded in black space");
                    return;
                }
            }

            Vector2 exitPos = FindExitPoint();
            transform.position = exitPos;
            TransitionTo(normalState);
        }
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

    // === Normal Movement State ===
    private class NormalState : IPlayerState
    {
        private PlayerManager pm;
        private Vector2 input;
        public NormalState(PlayerManager pm) => this.pm = pm;

        public void Enter()
        {
            pm.rb.gravityScale = 1;
            pm.rb.drag = 0f;
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);

            if (pm.maintainUpright)
            {
                pm.StartCoroutine(pm.SmoothRotationTransition(Quaternion.identity));
            }

            // Configure cyberpunk afterimage for normal state
            if (pm.afterImageEffect != null && pm.enableAfterimageInNormal)
            {
                pm.afterImageEffect.SetSpawnRate(0.12f);
                pm.afterImageEffect.SetFadeTime(0.8f);
                pm.afterImageEffect.SetColorCount(4); // Less intense in normal state
            }

            Debug.Log("Player entered normal state");
        }

        public void Exit() { }

        public void HandleUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            if (input.x != 0) pm.sr.flipX = input.x < 0;

            pm.CheckGrounded();

            // Handle continuous afterimage based on speed
            if (pm.afterImageEffect != null && pm.enableAfterimageInNormal)
            {
                float velocityMagnitude = pm.rb.velocity.magnitude;

                // Auto-activate afterimage when moving fast
                if (velocityMagnitude > 8f && !pm.afterImageEffect.IsEffectActive())
                {
                    pm.afterImageEffect.ActivateEffect();
                }
                else if (velocityMagnitude < 3f && pm.afterImageEffect.IsEffectActive())
                {
                    pm.afterImageEffect.DeactivateEffect();
                }
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (pm.isGrounded)
                {
                    float jumpForce = pm.GetCurrentJumpForce();
                    pm.rb.velocity = new Vector2(pm.rb.velocity.x, jumpForce);
                }
            }

            HandlePushInteraction();
        }

        public void HandleFixedUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            float currentMoveSpeed = pm.GetCurrentMoveSpeed();

            // Increase speed while dashing
            if (pm.isDashing)
            {
                currentMoveSpeed *= 1.5f;
            }

            pm.rb.velocity = new Vector2(input.x * currentMoveSpeed, pm.rb.velocity.y);

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
                    pm.rb.velocity = new Vector2(pm.rb.velocity.x * 0.7f, pm.rb.velocity.y);
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
            pm.rb.drag = 0f;
            pm.rb.velocity = Vector2.zero;
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);

            if (pm.maintainUpright)
            {
                pm.StartCoroutine(pm.SmoothRotationTransition(Quaternion.Euler(0, 0, 180)));
            }

            // Configure cyberpunk afterimage for mirrored state - MORE INTENSE
            if (pm.afterImageEffect != null && pm.enableAfterimageInMirrored)
            {
                pm.afterImageEffect.SetSpawnRate(0.06f); // Faster spawning
                pm.afterImageEffect.SetFadeTime(1.5f); // Longer lasting
                pm.afterImageEffect.SetColorCount(8); // Full rainbow effect
            }

            Debug.Log("Player entered mirrored state");
        }

        public void Exit()
        {
            pm.rb.gravityScale = 1f;
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);

            // Reset cyberpunk afterimage settings
            if (pm.afterImageEffect != null)
            {
                pm.afterImageEffect.SetSpawnRate(0.1f);
                pm.afterImageEffect.SetFadeTime(0.8f);
                pm.afterImageEffect.SetColorCount(4);
            }
        }

        public void HandleUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            if (input.x != 0) pm.sr.flipX = input.x < 0;

            bool isGroundedInBlackSpace = pm.CheckMirroredGrounded();

            // Handle continuous afterimage in mirrored state
            if (pm.afterImageEffect != null && pm.enableAfterimageInMirrored)
            {
                float velocityMagnitude = pm.rb.velocity.magnitude;

                // In mirrored state, activate afterimage more easily
                if (velocityMagnitude > 4f && !pm.afterImageEffect.IsEffectActive())
                {
                    pm.afterImageEffect.ActivateEffect();
                }
                else if (velocityMagnitude < 2f && pm.afterImageEffect.IsEffectActive())
                {
                    pm.afterImageEffect.DeactivateEffect();
                }
            }

            if (Input.GetKeyDown(KeyCode.Space))
            {
                if (isGroundedInBlackSpace)
                {
                    float jumpForce = pm.GetCurrentJumpForce();
                    // FIXED: In mirrored state with negative gravity, jump force should be negative to go "up"
                    pm.rb.velocity = new Vector2(pm.rb.velocity.x, -jumpForce);
                    Debug.Log($"Mirrored jump applied: {-jumpForce}");
                }
            }
        }

        public void HandleFixedUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            float currentMoveSpeed = pm.GetCurrentMoveSpeed();

            // Increase speed while dashing (even faster in mirrored state)
            if (pm.isDashing)
            {
                currentMoveSpeed *= 2f; // Extra speed boost in mirrored state
            }

            pm.rb.velocity = new Vector2(input.x * currentMoveSpeed, pm.rb.velocity.y);

            if (pm.maintainUpright)
            {
                pm.transform.rotation = Quaternion.Slerp(
                    pm.transform.rotation,
                    Quaternion.Euler(0, 0, 180),
                    Time.deltaTime * pm.rotationSpeed * 2f
                );
            }

            pm.sr.color = Color.Lerp(pm.sr.color, pm.mirroredColor, Time.deltaTime * 10f);
        }
    }

    // === Public API ===
    public bool IsInMirroredState() => currentState == mirroredState;
    public bool CanFlip() => Time.time - lastFlipTime >= flipCooldown;
    public SpaceType GetCurrentSpaceType() => currentSpaceType;
    public bool IsTransitioning() => isTransitioning;

    public Vector2 GetVelocity() => rb.velocity;
    public float GetMovementIntensity() => Mathf.Clamp01(rb.velocity.magnitude / (moveSpeed * 2f));

    public float GetCurrentMoveSpeed()
    {
        float baseSpeed = moveSpeed;

        return baseSpeed;
    }

    public float GetCurrentJumpForce()
    {
        float baseJump = jumpForce;

        return baseJump;
    }

    // === Helper Coroutines ===
    private IEnumerator SmoothRotationTransition(Quaternion targetRotation)
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

    // === Utility Functions ===
    private bool CheckMirroredGrounded()
    {
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.up, 0.2f, 1 << blackSpaceLayer);
        return hit.collider != null;
    }

    private void CheckGrounded()
    {
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.down, 0.2f, solidLayerMask);
        isGrounded = hit.collider != null;
    }

    private Vector2 DetectGroundNormal()
    {
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.down, 1f, solidLayerMask);
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
        Collider2D[] nearbyColliders = Physics2D.OverlapCircleAll(
            transform.position,
            detectionRadius,
            normalSpaceLayer
        );

        if (nearbyColliders.Length > 0)
        {
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

        return transform.position + Vector3.up * edgeFollowDistance * 2f;
    }

    public bool IsGrounded()
    {
        return isGrounded;
    }
}