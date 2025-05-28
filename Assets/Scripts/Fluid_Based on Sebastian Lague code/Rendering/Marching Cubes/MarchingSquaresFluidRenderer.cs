using UnityEngine;
using Seb.Helpers;

public class MarchingSquaresFluidRenderer : MonoBehaviour
{
    [Header("Rendering Settings")]
    public float isoLevel = 0.5f;
    public Color fluidColor = Color.blue;
    public int textureResolution = 256;
    public Vector2 worldScale = Vector2.one;
    
    [Header("References")]
    public FluidSim2D fluidSim2D;
    public ComputeShader densityFieldCompute;
    public Shader drawShader;
    public ComputeShader renderArgsCompute;
    
    private RenderTexture densityTexture;
    private ComputeBuffer renderArgs;
    private MarchingCubes marchingCubes; // We'll reuse this with a 2D approach
    private ComputeBuffer triangleBuffer;
    private Material drawMat;
    private Bounds bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
    
    void Start()
    {
        // Create 2D texture for density field
        densityTexture = new RenderTexture(textureResolution, textureResolution, 0, RenderTextureFormat.RFloat);
        densityTexture.enableRandomWrite = true;
        // Use a 3D texture with depth=2 (minimum needed for marching cubes to work)
        densityTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        densityTexture.volumeDepth = 2;  // Changed from 1 to 2
        densityTexture.Create();
        
        marchingCubes = new MarchingCubes();
        drawMat = new Material(drawShader);
        
        renderArgs = new ComputeBuffer(5, sizeof(uint), ComputeBufferType.IndirectArguments);
        renderArgsCompute.SetBuffer(0, "RenderArgs", renderArgs);
    }
    
    void LateUpdate()
    {
        if (fluidSim2D.positionBuffer != null && fluidSim2D.densityBuffer != null)
        {
            // Generate 2D density field from particles
            UpdateDensityField();
            
            // Run marching cubes on a flat 3D texture (essentially 2D)
            RenderFluid();
        }
    }
    
    void UpdateDensityField()
    {
        // Set up compute shader parameters
        densityFieldCompute.SetBuffer(0, "Positions", fluidSim2D.positionBuffer);
        densityFieldCompute.SetBuffer(0, "Densities", fluidSim2D.densityBuffer);
        densityFieldCompute.SetTexture(0, "DensityTexture", densityTexture);
        densityFieldCompute.SetInt("NumParticles", fluidSim2D.numParticles);
        densityFieldCompute.SetFloat("SmoothingRadius", fluidSim2D.smoothingRadius);
        
        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(textureResolution / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(textureResolution / 8.0f);
        densityFieldCompute.Dispatch(0, threadGroupsX, threadGroupsY, 1);
    }
    
    void RenderFluid()
    {
        // Use a flat scale for the 3D marching cubes (only X and Y matter)
        Vector3 scale = new Vector3(worldScale.x, worldScale.y, 0.01f);
        
        // Run marching cubes compute shader to generate triangle mesh
        triangleBuffer = marchingCubes.Run(densityTexture, scale, -isoLevel);
        
        drawMat.SetBuffer("VertexBuffer", triangleBuffer);
        drawMat.SetColor("col", fluidColor);
        
        // Copy triangle count and render
        ComputeBuffer.CopyCount(triangleBuffer, renderArgs, 0);
        renderArgsCompute.Dispatch(0, 1, 1, 1);
        
        Graphics.DrawProceduralIndirect(drawMat, bounds, MeshTopology.Triangles, renderArgs);
    }
    
    private void OnDestroy()
    {
        if (densityTexture != null) densityTexture.Release();
        if (renderArgs != null) renderArgs.Release();
        if (marchingCubes != null) marchingCubes.Release();
    }
}