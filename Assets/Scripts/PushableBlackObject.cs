using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class PushableBlackObject : MonoBehaviour
{
    [SerializeField] private float mass = 3f;
    [SerializeField] private float drag = 2f; // Higher drag for controlled movement
    [SerializeField] private float maxSpeed = 3f; // Limit max speed
    [SerializeField] private bool canLock = false;
    [SerializeField] private Vector3 lockedPosition; // Position to snap to when locked

    private Rigidbody2D rb;
    private BoxCollider2D boxCollider;
    private bool isLocked = false;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();

        boxCollider = GetComponent<BoxCollider2D>();
        if (boxCollider == null)
            boxCollider = gameObject.AddComponent<BoxCollider2D>();

        // Configure physics for a pushable object
        rb.mass = mass;
        rb.linearDamping = drag;
        rb.angularDamping = 0.5f;
        rb.gravityScale = 0; // No gravity for puzzle objects
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
        rb.constraints = RigidbodyConstraints2D.FreezeRotation; // Prevent rotation
        rb.bodyType = RigidbodyType2D.Dynamic; // Use bodyType instead of isKinematic

        // Ensure object is on BlackSpace layer
        gameObject.layer = LayerMask.NameToLayer("BlackSpace");
    }

    void Update()
    {
        // Limit maximum speed
        if (rb.linearVelocity.magnitude > maxSpeed)
        {
            rb.linearVelocity = rb.linearVelocity.normalized * maxSpeed;
        }

        // Check for lock snapping if near target
        if (canLock && !isLocked && lockedPosition != Vector3.zero)
        {
            float dist = Vector3.Distance(transform.position, lockedPosition);
            if (dist < 0.5f)
            {
                LockInPlace();
            }
        }
    }

    // Lock the object in place (for puzzle solving)
    public void LockInPlace()
    {
        if (canLock)
        {
            isLocked = true;
            rb.linearVelocity = Vector2.zero;
            rb.bodyType = RigidbodyType2D.Kinematic; // Use bodyType instead of isKinematic

            // Snap to locked position if specified
            if (lockedPosition != Vector3.zero)
            {
                transform.position = lockedPosition;
            }

            // Optional: Trigger any puzzle completion events
            SendMessage("OnObjectLocked", SendMessageOptions.DontRequireReceiver);
        }
    }

    // Unlock for pushing again
    public void UnlockForPushing()
    {
        isLocked = false;
        rb.bodyType = RigidbodyType2D.Dynamic; // Use bodyType instead of isKinematic
    }

    // Visual feedback when being pushed
    private void OnCollisionStay2D(Collision2D collision)
    {
        if (collision.gameObject.CompareTag("Player") &&
            Input.GetKey(KeyCode.F) &&
            !isLocked)
        {
            // Visual feedback - subtle pulse or glow
            StartCoroutine(PushFeedback());
        }
    }

    private System.Collections.IEnumerator PushFeedback()
    {
        SpriteRenderer sr = GetComponent<SpriteRenderer>();
        if (sr != null)
        {
            Color originalColor = sr.color;
            sr.color = Color.Lerp(originalColor, Color.cyan, 0.3f);
            yield return new WaitForSeconds(0.1f);
            sr.color = originalColor;
        }

        yield break;
    }
}