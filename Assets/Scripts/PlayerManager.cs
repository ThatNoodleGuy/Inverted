using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Optimized PlayerManager script that combines elements from both versions.
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

    [Header("Input Actions")]
    [SerializeField] private InputActionAsset actions;

    [Header("Interaction")]
    [SerializeField] private float pushForce = 2f;
    [SerializeField] private float pushCheckDistance = 0.5f;

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
        TransitionTo(normalState);
    }

    void OnEnable() => actions?.Enable();
    void OnDisable() => actions?.Disable();

    void Update() => currentState.HandleUpdate();
    void FixedUpdate() => currentState.HandleFixedUpdate();

    private void TransitionTo(IPlayerState next)
    {
        currentState?.Exit();
        currentState = next;
        currentState.Enter();
    }

    public bool CanFlip() => Time.time - lastFlipTime >= flipCooldown;

    public void Flip()
    {
        lastFlipTime = Time.time;
        TransitionTo(currentState == normalState ? mirroredState : normalState);
    }

    // === State Interface ===
    private interface IPlayerState
    {
        void Enter();
        void Exit();
        void HandleUpdate();
        void HandleFixedUpdate();
    }

    // === Normal Movement ===
    private class NormalState : IPlayerState
    {
        private PlayerManager pm;
        private Vector2 input;
        public NormalState(PlayerManager pm) => this.pm = pm;

        public void Enter()
        {
            pm.rb.gravityScale = 1;
            // Ensure normal collision with black space is disabled
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);
        }

        public void Exit() { }

        public void HandleUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            if (input.x != 0) pm.sr.flipX = input.x < 0;

            pm.CheckGrounded();
            if (Input.GetKeyDown(KeyCode.Space) && pm.isGrounded)
                pm.rb.linearVelocity = new Vector2(pm.rb.linearVelocity.x, pm.jumpForce);

            if (pm.CanFlip() && Input.GetKeyDown(KeyCode.E)) pm.Flip();
            
            // Handle pushing objects
            HandlePushInteraction();
        }

        public void HandleFixedUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            pm.rb.linearVelocity = new Vector2(input.x * pm.moveSpeed, pm.rb.linearVelocity.y);

            Vector2 normal = pm.DetectGroundNormal();
            if (pm.maintainUpright) pm.RotateToMatchNormal(normal);
            pm.sr.color = Color.Lerp(pm.sr.color, pm.normalColor, Time.deltaTime * 10f);
        }

                private void HandlePushInteraction()
        {
            // Determine push direction based on player facing
            Vector2 pushDirection = pm.sr.flipX ? Vector2.left : Vector2.right;
            
            // Cast ray to detect objects in the black space layer
            RaycastHit2D hit = Physics2D.Raycast(
                pm.transform.position,
                pushDirection,
                pm.pushCheckDistance,
                1 << pm.blackSpaceLayer);
                
            // Debug visualization
            Debug.DrawRay(pm.transform.position, pushDirection * pm.pushCheckDistance, 
                         hit.collider != null ? Color.green : Color.red, 0.1f);
            
            // If we hit something and player is pressing the push button
            if (hit.collider != null && Input.GetKey(KeyCode.F))
            {
                Rigidbody2D objRb = hit.collider.GetComponent<Rigidbody2D>();
                
                // Check if object has Rigidbody2D and is not static
                if (objRb != null && objRb.bodyType == RigidbodyType2D.Dynamic)
                {
                    // Apply force in push direction
                    objRb.AddForce(pushDirection * pm.pushForce, ForceMode2D.Force);
                    
                    // Optional: Slow down player while pushing
                    pm.rb.linearVelocity = new Vector2(pm.rb.linearVelocity.x * 0.7f, pm.rb.linearVelocity.y);
                }
            }
        }
    }

    // === Mirrored (Negative Space) Movement ===
    private class MirroredState : IPlayerState
    {
        private PlayerManager pm;
        private Vector2 input;

        public MirroredState(PlayerManager pm) => this.pm = pm;

        public void Enter()
        {
            // Try to enter black space, return to normal state if failed
            if (!pm.TryEnterBlackSpace())
            {
                pm.TransitionTo(pm.normalState);
                return;
            }

            // Important: First set upright orientation to make entry smoother
            if (pm.maintainUpright)
            {
                float deg = Mathf.Atan2(pm.currentNormal.x, pm.currentNormal.y) * Mathf.Rad2Deg;
                pm.transform.rotation = Quaternion.Euler(0, 0, -deg);
            }

            // Configure physics for mirrored state - do this AFTER positioning
            pm.rb.gravityScale = -1f; // Invert gravity
            pm.rb.linearVelocity = Vector2.zero; // Reset velocity for clean transition

            // Enable collisions with black space (we need to stand on it)
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);
        }

        public void Exit()
        {
            // Calculate exit position using the older version's reliable method
            Vector2 exitPos = pm.FindExitPoint();

            // Reset physics state
            pm.rb.linearVelocity = Vector2.zero;
            pm.transform.rotation = Quaternion.identity;
            pm.rb.gravityScale = 1f; // Reset to normal gravity

            // Move to exit position
            pm.transform.position = exitPos;

            // Reset collision
            Physics2D.IgnoreLayerCollision(pm.gameObject.layer, pm.blackSpaceLayer, false);
        }

        public void HandleUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");
            if (input.x != 0) pm.sr.flipX = input.x < 0;

            // Check if player is standing on black space
            bool isGroundedInBlackSpace = pm.CheckMirroredGrounded();

            // Jump DOWNWARD (since gravity is inverted)
            if (Input.GetKeyDown(KeyCode.Space) && isGroundedInBlackSpace)
            {
                pm.rb.linearVelocity = new Vector2(pm.rb.linearVelocity.x, -pm.jumpForce);
            }

            if (pm.CanFlip() && Input.GetKeyDown(KeyCode.E)) pm.Flip();
        }

        public void HandleFixedUpdate()
        {
            input.x = Input.GetAxisRaw("Horizontal");

            // Basic horizontal movement
            pm.rb.linearVelocity = new Vector2(input.x * pm.moveSpeed, pm.rb.linearVelocity.y);

            // Keep player rotated upside down
            pm.transform.rotation = Quaternion.Slerp(
                pm.transform.rotation,
                Quaternion.Euler(0, 0, 180), // Upside down
                Time.deltaTime * pm.rotationSpeed * 2f // Double rotation speed for quicker flipping
            );

            // Update surface normal and entry point
            pm.UpdateSurfaceNormal();
            pm.UpdateEntryPoint();

            // Update color
            pm.sr.color = Color.Lerp(pm.sr.color, pm.mirroredColor, Time.deltaTime * 10f);
        }
    }

    private bool CheckMirroredGrounded()
    {
        // In mirrored state, check UPWARD to see if we're standing on black space
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.up, 0.2f, 1 << blackSpaceLayer);
        bool isOnBlackSpace = hit.collider != null;

        // Debug visualization
        Debug.DrawRay(groundChecker.position, Vector2.up * 0.2f,
                     isOnBlackSpace ? Color.green : Color.red, 0.1f);

        return isOnBlackSpace;
    }

    private void MaintainEdgeDistance()
    {
        var hit = Physics2D.Raycast(transform.position, -currentNormal,
                                    detectionRadius, solidLayerMask);
        if (hit.collider != null &&
            hit.collider.gameObject.layer == blackSpaceLayer)
        {
            Vector2 desired = hit.point + currentNormal * edgeFollowDistance;
            transform.position = Vector2.Lerp(transform.position,
                                              desired,
                                              Time.deltaTime * 10f);
        }
    }

    // Improved edge direction sampling
    private Vector2 CalculateEdgeDirection()
    {
        List<Vector2> pts = new List<Vector2>();
        for (float ang = 0; ang < 360; ang += 15f)
        {
            float r = ang * Mathf.Deg2Rad;
            Vector2 d = new Vector2(Mathf.Cos(r), Mathf.Sin(r));
            var hit = Physics2D.Raycast(transform.position, d,
                                         detectionRadius, solidLayerMask);
            if (hit.collider != null &&
                hit.collider.gameObject.layer == blackSpaceLayer)
                pts.Add(hit.point);
        }
        if (pts.Count < 2)
            return new Vector2(-currentNormal.y, currentNormal.x).normalized;

        Vector2 a = pts[0], b = pts[0]; float maxDist = 0f;
        for (int i = 0; i < pts.Count; i++)
            for (int j = i + 1; j < pts.Count; j++)
            {
                float d = (pts[i] - pts[j]).sqrMagnitude;
                if (d > maxDist)
                {
                    maxDist = d;
                    a = pts[i]; b = pts[j];
                }
            }
        Vector2 edge = (b - a).normalized;
        return Vector2.Dot(edge, Vector2.right) >= 0 ? edge : -edge;
    }

    // === Utility Functions ===
    private void CheckGrounded()
    {
        RaycastHit2D hit = Physics2D.Raycast(groundChecker.position, Vector2.down, 0.2f, solidLayerMask);
        isGrounded = hit.collider != null;
        Debug.DrawRay(groundChecker.position, Vector2.down * 0.2f,
                      isGrounded ? Color.green : Color.red, 0.1f);
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

    private bool TryEnterBlackSpace()
    {
        RaycastHit2D bestHit = default;
        float bestDist = Mathf.Infinity;

        // Cast rays in all directions to find closest black space
        for (int i = 0; i < 12; i++)
        {
            float angle = i * 30f * Mathf.Deg2Rad;
            Vector2 dir = new Vector2(Mathf.Cos(angle), Mathf.Sin(angle));
            var hit = Physics2D.Raycast(transform.position, dir,
                                         detectionRadius, solidLayerMask);
            if (hit.collider != null &&
                hit.collider.gameObject.layer == blackSpaceLayer &&
                hit.distance < bestDist)
            {
                bestDist = hit.distance;
                bestHit = hit;
            }
        }

        // No black space found nearby
        if (bestDist == Mathf.Infinity) return false;

        // Store entry data
        entryPoint = transform.position;
        currentNormal = bestHit.normal;

        // Move just inside black space
        transform.position = bestHit.point - currentNormal * edgeFollowDistance;

        // Initially rotate player to match entry angle
        if (maintainUpright)
        {
            float deg = Mathf.Atan2(currentNormal.x, currentNormal.y) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, 0, -deg);
        }

        // Important: Ignore collisions with black space during edge following
        Physics2D.IgnoreLayerCollision(gameObject.layer, blackSpaceLayer, true);

        return true;
    }

    private Vector2 FindExitPoint()
    {
        // First try to exit from current position
        RaycastHit2D hit = Physics2D.Raycast(transform.position, currentNormal,
                                             detectionRadius, solidLayerMask);

        // If we hit something that's not black space, exit toward that point
        if (hit.collider != null && hit.collider.gameObject.layer != blackSpaceLayer)
        {
            return hit.point + currentNormal * edgeFollowDistance;
        }

        // If we don't hit a valid exit point, try casting multiple rays
        Vector2 bestExitPos = transform.position + (Vector3)(currentNormal * edgeFollowDistance);
        float bestDistance = Mathf.Infinity;

        // Try rays in a fan pattern around the current normal
        for (int i = -2; i <= 2; i++)
        {
            float angle = i * 30f * Mathf.Deg2Rad;
            Vector2 dir = RotateVector(currentNormal, angle);

            hit = Physics2D.Raycast(transform.position, dir, detectionRadius * 2f, solidLayerMask);
            if (hit.collider != null && hit.collider.gameObject.layer != blackSpaceLayer)
            {
                float dist = hit.distance;
                if (dist < bestDistance)
                {
                    bestDistance = dist;
                    bestExitPos = hit.point + dir * edgeFollowDistance;
                }
            }
        }

        // If all else fails, just use the current position and normal
        if (bestDistance == Mathf.Infinity)
        {
            // Just move outward from current position along normal
            return transform.position + (Vector3)(currentNormal * edgeFollowDistance * 2f);
        }

        return bestExitPos;
    }

    private Vector2 RotateVector(Vector2 v, float angle)
    {
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        return new Vector2(
            v.x * cos - v.y * sin,
            v.x * sin + v.y * cos
        );
    }

    private void UpdateSurfaceNormal()
    {
        var hit = Physics2D.Raycast(transform.position, -currentNormal,
                                    detectionRadius, solidLayerMask);
        if (hit.collider != null &&
            hit.collider.gameObject.layer == blackSpaceLayer)
        {
            currentNormal = hit.normal;
        }
    }

    private void UpdateEntryPoint()
    {
        // Update the entry point to be the current position
        entryPoint = transform.position;
    }

    void OnDrawGizmosSelected()
    {
        if (!Application.isPlaying) return;
        Gizmos.color = Color.gray;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
        Gizmos.color = Color.green;
        Gizmos.DrawSphere(entryPoint, 0.1f);

        if (currentState == mirroredState)
        {
            // Draw the predicted exit point
            Vector2 exitPos = FindExitPoint();
            Gizmos.color = Color.blue;
            Gizmos.DrawSphere(exitPos, 0.1f);

            // Draw the direction of exit
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position, transform.position + (Vector3)(currentNormal * 0.5f));
        }
    }
}