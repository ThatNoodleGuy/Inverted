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
    [SerializeField] private bool useSharedMaterial = false;

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
        // First, remove any previous mesh components from spline objects
        SplineContainer[] containers = GetComponentsInChildren<SplineContainer>();
        foreach (SplineContainer container in containers)
        {
            // Remove old renderers and colliders but keep the SplineContainer
            MeshFilter meshFilter = container.GetComponent<MeshFilter>();
            if (meshFilter != null)
                DestroyImmediate(meshFilter);

            MeshRenderer meshRenderer = container.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
                DestroyImmediate(meshRenderer);

            PolygonCollider2D polyCollider = container.GetComponent<PolygonCollider2D>();
            if (polyCollider != null)
                DestroyImmediate(polyCollider);

            SplineShapeColor colorComponent = container.GetComponent<SplineShapeColor>();
            if (colorComponent != null)
                DestroyImmediate(colorComponent);
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
                // Add shape components directly to the spline object
                CreateShapeOnSplineObject(container);
            }
            else
            {
                Debug.LogWarning($"Spline in {container.name} is not closed. Only closed splines can be used for shapes.");
            }
        }
    }

    private void CreateShapeOnSplineObject(SplineContainer container)
    {
        try
        {
            Spline spline = container.Spline;
            Transform splineTransform = container.transform;
            string splineName = container.name;

            // Calculate approximate length of the spline
            float length = SplineUtility.CalculateLength(spline, Matrix4x4.identity);

            // Determine number of sample points based on resolution
            int sampleCount = Mathf.Max(8, Mathf.CeilToInt(length / splineSamplingResolution));

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

            // Add a polygon collider to the spline object
            PolygonCollider2D polyCollider = container.gameObject.AddComponent<PolygonCollider2D>();
            polyCollider.compositeOperation = Collider2D.CompositeOperation.Merge;
            polyCollider.SetPath(0, points);

            // Create a mesh from the collider path
            Mesh mesh = CreateMeshFromCollider(polyCollider);

            // Add MeshFilter and MeshRenderer to the spline object
            MeshFilter meshFilter = container.gameObject.AddComponent<MeshFilter>();
            meshFilter.sharedMesh = mesh;

            MeshRenderer meshRenderer = container.gameObject.AddComponent<MeshRenderer>();
            meshRenderer.sortingLayerName = "Background"; // Create this layer in Unity
            meshRenderer.sortingOrder = -1; // Lower number = further back

            // Setup material
            Material mat;
            if (defaultMaterial != null)
            {
                mat = useSharedMaterial ? defaultMaterial : new Material(defaultMaterial);
            }
            else
            {
                // Create a default material if none is specified
                mat = new Material(Shader.Find("Sprites/Default"));
            }

            // Set the color
            mat.color = defaultColor;
            meshRenderer.material = mat;

            // Add a SplineShapeColor component to allow color customization
            SplineShapeColor colorComponent = container.gameObject.AddComponent<SplineShapeColor>();
            colorComponent.SetColor(defaultColor);

            Debug.Log($"Created shape components for {splineName}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Error creating shape from spline {container.name}: {e.Message}\n{e.StackTrace}");
        }
    }

    private Mesh CreateMeshFromCollider(PolygonCollider2D collider)
    {
        // Get the collider path points
        Vector2[] points = collider.GetPath(0);

        // Create a new mesh
        Mesh mesh = new Mesh();

        // Create a vertex for each point
        Vector3[] vertices = new Vector3[points.Length];
        for (int i = 0; i < points.Length; i++)
        {
            vertices[i] = new Vector3(points[i].x, points[i].y, 0);
        }

        // Create the triangles using Triangulator
        Triangulator triangulator = new Triangulator(points);
        int[] triangles = triangulator.Triangulate();

        // Calculate UVs
        Vector2[] uvs = new Vector2[vertices.Length];
        Bounds bounds = new Bounds(vertices[0], Vector3.zero);
        for (int i = 0; i < vertices.Length; i++)
        {
            bounds.Encapsulate(vertices[i]);
        }

        for (int i = 0; i < vertices.Length; i++)
        {
            uvs[i] = new Vector2(
                (vertices[i].x - bounds.min.x) / bounds.size.x,
                (vertices[i].y - bounds.min.y) / bounds.size.y
            );
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

// Triangulator class to handle concave polygons
public class Triangulator
{
    private List<Vector2> m_points = new List<Vector2>();

    public Triangulator(Vector2[] points)
    {
        m_points = new List<Vector2>(points);
    }

    public int[] Triangulate()
    {
        List<int> indices = new List<int>();

        int n = m_points.Count;
        if (n < 3)
            return indices.ToArray();

        int[] V = new int[n];
        if (Area() > 0)
        {
            for (int v = 0; v < n; v++)
                V[v] = v;
        }
        else
        {
            for (int v = 0; v < n; v++)
                V[v] = (n - 1) - v;
        }

        int nv = n;
        int count = 2 * nv;
        for (int v = nv - 1; nv > 2;)
        {
            if ((count--) <= 0)
                return indices.ToArray();

            int u = v;
            if (nv <= u)
                u = 0;

            v = u + 1;
            if (nv <= v)
                v = 0;

            int w = v + 1;
            if (nv <= w)
                w = 0;

            if (Snip(u, v, w, nv, V))
            {
                int a, b, c, s, t;
                a = V[u];
                b = V[v];
                c = V[w];
                indices.Add(a);
                indices.Add(b);
                indices.Add(c);

                for (s = v, t = v + 1; t < nv; s++, t++)
                    V[s] = V[t];

                nv--;
                count = 2 * nv;
            }
        }

        indices.Reverse();
        return indices.ToArray();
    }

    private float Area()
    {
        int n = m_points.Count;
        float A = 0.0f;
        for (int p = n - 1, q = 0; q < n; p = q++)
        {
            Vector2 pval = m_points[p];
            Vector2 qval = m_points[q];
            A += pval.x * qval.y - qval.x * pval.y;
        }
        return (A * 0.5f);
    }

    private bool Snip(int u, int v, int w, int n, int[] V)
    {
        Vector2 A = m_points[V[u]];
        Vector2 B = m_points[V[v]];
        Vector2 C = m_points[V[w]];

        if (Mathf.Epsilon > (((B.x - A.x) * (C.y - A.y)) - ((B.y - A.y) * (C.x - A.x))))
            return false;

        for (int p = 0; p < n; p++)
        {
            if ((p == u) || (p == v) || (p == w))
                continue;

            Vector2 P = m_points[V[p]];

            if (InsideTriangle(A, B, C, P))
                return false;
        }

        return true;
    }

    private bool InsideTriangle(Vector2 A, Vector2 B, Vector2 C, Vector2 P)
    {
        float ax, ay, bx, by, cx, cy, apx, apy, bpx, bpy, cpx, cpy;
        float cCROSSap, bCROSScp, aCROSSbp;

        ax = C.x - B.x; ay = C.y - B.y;
        bx = A.x - C.x; by = A.y - C.y;
        cx = B.x - A.x; cy = B.y - A.y;
        apx = P.x - A.x; apy = P.y - A.y;
        bpx = P.x - B.x; bpy = P.y - B.y;
        cpx = P.x - C.x; cpy = P.y - C.y;

        aCROSSbp = ax * bpy - ay * bpx;
        cCROSSap = cx * apy - cy * apx;
        bCROSScp = bx * cpy - by * cpx;

        return ((aCROSSbp >= 0.0f) && (bCROSScp >= 0.0f) && (cCROSSap >= 0.0f));
    }
}