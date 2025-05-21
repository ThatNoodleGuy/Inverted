using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Splines;
using Unity.Mathematics;

[RequireComponent(typeof(Rigidbody2D))]
public class SplineShapeGenerator : MonoBehaviour
{
    [SerializeField] private PhysicsMaterial2D physicsMaterial;
    [SerializeField] private float splineSamplingResolution = 0.1f;
    [SerializeField] private Material defaultMaterial;
    [SerializeField] private Color defaultColor = Color.white;

    // Combined SplineCreator functionality directly into this component
    [ContextMenu("Create New Square Spline")]
    public void CreateNewSquareSpline()
    {
        // Create a new GameObject for the spline
        GameObject splineObj = new GameObject("SquareSpline");
        splineObj.transform.SetParent(transform);
        splineObj.transform.localPosition = Vector3.zero;

        // Add a SplineContainer component
        SplineContainer container = splineObj.AddComponent<SplineContainer>();
        
        // Create a new spline with a simple square shape
        Spline spline = new Spline();

        // Add some default control points (square shape)
        float size = 1f;
        spline.Add(new BezierKnot(new float3(-size, -size, 0)));
        spline.Add(new BezierKnot(new float3(size, -size, 0)));
        spline.Add(new BezierKnot(new float3(size, size, 0)));
        spline.Add(new BezierKnot(new float3(-size, size, 0)));

        // Make the spline closed
        spline.Closed = true;

        // Set the spline to the container
        container.Spline = spline;

        Debug.Log("New square spline created. You can now edit it in the Scene view.");
    }

    [ContextMenu("Create Circle Spline")]
    public void CreateCircleSpline()
    {
        // Create a new GameObject for the spline
        GameObject splineObj = new GameObject("CircleSpline");
        splineObj.transform.SetParent(transform);
        splineObj.transform.localPosition = Vector3.zero;

        // Add a SplineContainer component
        SplineContainer container = splineObj.AddComponent<SplineContainer>();

        // Create a new spline
        Spline spline = new Spline();

        // Add control points for a circle shape
        int segments = 8;
        float radius = 1f;

        for (int i = 0; i < segments; i++)
        {
            float angle = (i / (float)segments) * Mathf.PI * 2;
            float x = Mathf.Cos(angle) * radius;
            float y = Mathf.Sin(angle) * radius;

            BezierKnot knot = new BezierKnot(new float3(x, y, 0));

            // Add tangents to make it a smooth circle
            float tangentLength = radius * 0.55f; // Approximation for a circle
            float tx = -Mathf.Sin(angle) * tangentLength;
            float ty = Mathf.Cos(angle) * tangentLength;

            knot.TangentIn = new float3(-tx, -ty, 0);
            knot.TangentOut = new float3(tx, ty, 0);

            spline.Add(knot);
        }

        // Make the spline closed
        spline.Closed = true;

        // Set the spline to the container
        container.Spline = spline;

        Debug.Log("New circle spline created. You can now edit it in the Scene view.");
    }

    [ContextMenu("Generate Spline Shapes")]
    public void GenerateSplineShapes()
    {
        // First, remove any previously generated shapes
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            Transform child = transform.GetChild(i);
            if (child.name.StartsWith("SplineShape_"))
            {
                DestroyImmediate(child.gameObject);
            }
        }

        // Setup Rigidbody2D
        Rigidbody2D rb = GetComponent<Rigidbody2D>();
        rb.bodyType = RigidbodyType2D.Static;
        rb.simulated = true;

        // Setup CompositeCollider2D
        CompositeCollider2D compositeCollider = GetComponent<CompositeCollider2D>();
        if (compositeCollider == null)
            compositeCollider = gameObject.AddComponent<CompositeCollider2D>();

        if (physicsMaterial != null)
            compositeCollider.sharedMaterial = physicsMaterial;

        compositeCollider.geometryType = CompositeCollider2D.GeometryType.Outlines;

        // Process all child SplineContainers
        ProcessSplineContainers();

        // Force update the composite collider
        compositeCollider.generationType = CompositeCollider2D.GenerationType.Synchronous;

        Debug.Log("Spline shape generation complete with " +
                  compositeCollider.pathCount + " paths and " +
                  compositeCollider.pointCount + " points");
    }

    private void ProcessSplineContainers()
    {
        // Find all SplineContainer components in children
        SplineContainer[] containers = GetComponentsInChildren<SplineContainer>();
        
        Debug.Log($"Found {containers.Length} SplineContainer components");

        foreach (SplineContainer container in containers)
        {
            // Process the spline in the container
            Spline spline = container.Spline;
            
            if (spline == null)
            {
                Debug.LogWarning($"Container {container.name} has no spline!");
                continue;
            }

            // Only process closed splines (open splines can't define a shape)
            if (spline.Closed)
            {
                CreateShapeFromSpline(container.transform, spline, container.name);
            }
            else
            {
                Debug.LogWarning($"Spline in {container.name} is not closed. Only closed splines can be used for shapes.");
            }
        }
    }

    private void CreateShapeFromSpline(Transform splineTransform, Spline spline, string splineName)
    {
        try
        {
            // Calculate approximate length of the spline
            float length = SplineUtility.CalculateLength(spline, Matrix4x4.identity);

            // Determine number of sample points based on resolution
            int sampleCount = Mathf.Max(8, Mathf.CeilToInt(length / splineSamplingResolution));

            // Create a new GameObject to hold both the renderer and collider
            GameObject shapeObj = new GameObject($"SplineShape_{splineName}");
            shapeObj.transform.SetParent(transform); // Parent to this object, not the spline
            shapeObj.transform.localPosition = Vector3.zero;
            shapeObj.transform.localRotation = Quaternion.identity;
            shapeObj.transform.localScale = Vector3.one;

            // Sample the spline to get points
            Vector2[] points = new Vector2[sampleCount];
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleCount;
                float3 position = SplineUtility.EvaluatePosition(spline, t);

                // Convert from spline's local space to world space, then to this transform's local space
                Vector3 worldPos = splineTransform.TransformPoint(position);
                Vector3 localPos = transform.InverseTransformPoint(worldPos);

                points[i] = new Vector2(localPos.x, localPos.y);
            }

            // Add a polygon collider
            PolygonCollider2D polyCollider = shapeObj.AddComponent<PolygonCollider2D>();
            polyCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            polyCollider.SetPath(0, points);

            // Create a mesh from the points
            Mesh mesh = CreateMeshFromPoints(points);

            // Add MeshFilter and MeshRenderer
            MeshFilter meshFilter = shapeObj.AddComponent<MeshFilter>();
            meshFilter.mesh = mesh;

            MeshRenderer meshRenderer = shapeObj.AddComponent<MeshRenderer>();

            meshRenderer.sortingLayerName = "Background"; // Create this layer in Unity
            meshRenderer.sortingOrder = -1; // Lower number = further back

            // Setup material
            if (defaultMaterial != null)
            {
                meshRenderer.material = new Material(defaultMaterial);
            }
            else
            {
                // Create a default material if none is specified
                meshRenderer.material = new Material(Shader.Find("Sprites/Default"));
            }

            // Set the color
            meshRenderer.material.color = defaultColor;

            // Add a SplineShapeColor component to allow color customization
            SplineShapeColor colorComponent = shapeObj.AddComponent<SplineShapeColor>();
            colorComponent.SetColor(defaultColor);
            
            Debug.Log($"Created shape for {splineName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating shape from spline {splineName}: {e.Message}\n{e.StackTrace}");
        }
    }

    private Mesh CreateMeshFromPoints(Vector2[] points)
    {
        // Create a new mesh
        Mesh mesh = new Mesh();

        // Convert 2D points to 3D vertices
        Vector3[] vertices = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            vertices[i] = new Vector3(points[i].x, points[i].y, 0);
        }

        // Triangulate the points to create triangles
        // Here we use a simple approach that requires the shape to be convex
        // For complex concave shapes, you would need a more advanced triangulation algorithm
        int[] triangles = new int[(points.Length - 2) * 3];
        for (int i = 0; i < points.Length - 2; i++)
        {
            triangles[i * 3] = 0;
            triangles[i * 3 + 1] = i + 1;
            triangles[i * 3 + 2] = i + 2;
        }

        // Calculate UVs
        Vector2[] uvs = new Vector2[vertices.Length];
        for (int i = 0; i < vertices.Length; i++)
        {
            uvs[i] = new Vector2(vertices[i].x, vertices[i].y);
        }

        // Set mesh data
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uvs;

        // Recalculate normals and bounds
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        return mesh;
    }
}

// Custom component to allow changing the color in the inspector
public class SplineShapeColor : MonoBehaviour
{
    [SerializeField] private Color color = Color.white;

    private MeshRenderer meshRenderer;

    void OnValidate()
    {
        UpdateColor();
    }

    void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        UpdateColor();
    }

    public void SetColor(Color newColor)
    {
        color = newColor;
        UpdateColor();
    }

    private void UpdateColor()
    {
        if (meshRenderer != null && meshRenderer.material != null)
        {
            meshRenderer.material.color = color;
        }
    }
}