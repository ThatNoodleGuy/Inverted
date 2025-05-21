using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class CombinedColliderGenerator : MonoBehaviour
{
    [SerializeField] private bool usePolygonCollider = true;
    [SerializeField] private PhysicsMaterial2D physicsMaterial;

    [ContextMenu("Setup Composite Collider")]
    public void SetupCompositeCollider()
    {
        // Ensure WorldRoot has a Rigidbody2D set to static
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        if (rb == null)
            rb = gameObject.AddComponent<Rigidbody2D>();

        rb.bodyType = RigidbodyType2D.Static;
        rb.simulated = true;

        // Setup or get the CompositeCollider2D
        CompositeCollider2D compositeCollider = GetComponent<CompositeCollider2D>();
        if (compositeCollider == null)
            compositeCollider = gameObject.AddComponent<CompositeCollider2D>();

        // Apply physics material if assigned
        if (physicsMaterial != null)
            compositeCollider.sharedMaterial = physicsMaterial;

        // Setup child colliders
        SetupChildColliders();

        // Configure the composite collider
        compositeCollider.geometryType = CompositeCollider2D.GeometryType.Outlines;

        // Force update
        compositeCollider.generationType = CompositeCollider2D.GenerationType.Synchronous;

        Debug.Log("Composite collider setup complete with " +
                  compositeCollider.pathCount + " paths and " +
                  compositeCollider.pointCount + " points");
    }

    private void SetupChildColliders()
    {
        // Process all active children
        foreach (Transform child in transform)
        {
            if (!child.gameObject.activeSelf)
                continue;

            SpriteRenderer spriteRenderer = child.GetComponent<SpriteRenderer>();
            if (spriteRenderer == null || spriteRenderer.sprite == null)
                continue;

            // Check if child already has a proper collider
            Collider2D existingCollider = child.GetComponent<Collider2D>();
            bool needNewCollider = (existingCollider == null);

            // If it has a collider but not the right type, remove it
            if (existingCollider != null &&
                ((usePolygonCollider && !(existingCollider is PolygonCollider2D)) ||
                (!usePolygonCollider && !(existingCollider is BoxCollider2D))))
            {
                DestroyImmediate(existingCollider);
                needNewCollider = true;
            }

            // Add appropriate collider if needed
            if (needNewCollider)
            {
                if (usePolygonCollider)
                {
                    PolygonCollider2D polyCollider = child.gameObject.AddComponent<PolygonCollider2D>();
                    polyCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
                }
                else
                {
                    BoxCollider2D boxCollider = child.gameObject.AddComponent<BoxCollider2D>();
                    boxCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
                }
            }
            else if (existingCollider != null)
            {
                // Make sure existing collider is set for composite use
                if (existingCollider is PolygonCollider2D)
                    ((PolygonCollider2D)existingCollider).compositeOperation = Collider2D.CompositeOperation.Merge;
                else if (existingCollider is BoxCollider2D)
                    ((BoxCollider2D)existingCollider).compositeOperation = Collider2D.CompositeOperation.Merge;
                else if (existingCollider is EdgeCollider2D)
                    ((EdgeCollider2D)existingCollider).compositeOperation = Collider2D.CompositeOperation.Merge;
            }
        }
    }
}