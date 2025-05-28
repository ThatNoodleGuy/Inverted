using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Seb.Helpers;

public class FluidSim2DTo3DAdapter : MonoBehaviour
{
    [Header("References")]
    public FluidSim2D fluidSim2D;
    public MarchingSquaresFluidRenderer fluidRenderer;
    
    [Header("Conversion Settings")]
    public float fluidThickness = 1.0f;
    public int textureResolution = 256;
    public float densityScale = 1.0f;
    
    private RenderTexture densityTexture;
    private ComputeShader densityMapCompute;
    
    void Start()
    {
        // Create the density texture
        densityTexture = new RenderTexture(textureResolution, textureResolution, 0, RenderTextureFormat.RFloat);
        densityTexture.enableRandomWrite = true;
        densityTexture.dimension = UnityEngine.Rendering.TextureDimension.Tex3D;
        densityTexture.volumeDepth = Mathf.Max(1, Mathf.RoundToInt(fluidThickness * textureResolution));
        densityTexture.Create();
        
        // Load the compute shader (you'll need to create this)
        densityMapCompute = Resources.Load<ComputeShader>("ParticlesToDensity3D");
    }
    
    void Update()
    {
        if (fluidSim2D.positionBuffer != null && fluidSim2D.densityBuffer != null)
        {
            // Update the 3D density texture based on 2D particle data
            UpdateDensityTexture();
            
            // Expose the density texture through a property
            fluidRenderer.SendMessage("SetDensityTexture", densityTexture);
        }
    }
    
    void UpdateDensityTexture()
    {
        // Set up compute shader parameters
        densityMapCompute.SetBuffer(0, "Positions", fluidSim2D.positionBuffer);
        densityMapCompute.SetBuffer(0, "Densities", fluidSim2D.densityBuffer);
        densityMapCompute.SetTexture(0, "DensityTexture", densityTexture);
        densityMapCompute.SetInt("NumParticles", fluidSim2D.numParticles);
        densityMapCompute.SetFloat("SmoothingRadius", fluidSim2D.smoothingRadius);
        densityMapCompute.SetFloat("DensityScale", densityScale);
        densityMapCompute.SetFloat("FluidThickness", fluidThickness);
        
        // Dispatch the compute shader
        int threadGroupsX = Mathf.CeilToInt(textureResolution / 8.0f);
        int threadGroupsY = Mathf.CeilToInt(textureResolution / 8.0f);
        int threadGroupsZ = Mathf.CeilToInt(densityTexture.volumeDepth / 8.0f);
        densityMapCompute.Dispatch(0, threadGroupsX, threadGroupsY, threadGroupsZ);
    }
    
    void OnDestroy()
    {
        if (densityTexture != null)
        {
            densityTexture.Release();
        }
    }
}