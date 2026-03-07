using Seb.Helpers;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;

namespace Seb.Fluid.Rendering
{
    /// <summary>
    /// Lightweight screen-space fluid renderer for FluidSim2D.
    /// Renders particle depth/thickness to render textures, smooths them, reconstructs normals,
    /// and composites with Sebastian Lague's Fluid/FluidRender shader.
    /// </summary>
    public class ScreenSpaceFluid2DRenderer : MonoBehaviour
    {
        [Header("Main Settings")]
        public bool useFullSizeThicknessTex = true;
        public Vector3 extinctionCoefficients = new Vector3(4.75f, 0.53f, 0.33f);
        public float extinctionMultiplier = 1.0f;
        public float depthParticleSize = 0.1f;
        public float thicknessParticleScale = 0.07f;
        public float refractionMultiplier = 1.0f;

        [Header("Smoothing Settings")]
        public BlurType smoothType = BlurType.Gaussian;
        public BilateralSmooth2D.BilateralFilterSettings bilateralSettings = new BilateralSmooth2D.BilateralFilterSettings
        {
            WorldRadius = 0.02f,
            MaxScreenSpaceSize = 32,
            Strength = 0.45f,
            DiffStrength = 3.7f,
            Iterations = 5
        };

        public GaussSmooth.GaussianBlurSettings gaussSmoothSettings = new GaussSmooth.GaussianBlurSettings
        {
            UseWorldSpaceRadius = false,
            Radius = 0.0f,
            MaxScreenSpaceRadius = 32,
            Strength = 0.13f,
            Iterations = 3
        };

        [Header("Debug Settings")]
        public DisplayMode displayMode = DisplayMode.Composite;
        public float depthDisplayScale = 50f;
        public float thicknessDisplayScale = 10f;

        [Header("2D Composite Settings")]
        public Color waterColor = new Color(0.4f, 0.7f, 1f, 1f);
        public float thicknessToAlpha = 4f;
        public float thicknessDarken = 1.5f;

        [Header("References")]
        [Tooltip("Fluid/FluidRender")]
        public Shader renderShader;
        [Tooltip("Fluid/DepthDownsampleCopy (optional, can be left null)")]
        public Shader depthDownsampleCopyShader;
        [Tooltip("Fluid/ParticleDepth")]
        public Shader depthShader;
        [Tooltip("Fluid/NormalsFromDepth")]
        public Shader normalShader;
        [Tooltip("Fluid/ParticleThickness")]
        public Shader thicknessShader;
        [Tooltip("Fluid/SmoothThickPrepare")]
        public Shader smoothThickPrepareShader;
        [Tooltip("2D fluid simulation to render")]
        public FluidSim2D sim;
        [Tooltip("Optional light used for directional lighting in the fluid shader")]
        public Light sun;

        // Internal state
        Mesh quadMesh;
        Material matDepth;
        Material matThickness;
        Material matNormal;
        Material matComposite;
        Material smoothPrepareMat;
        Material depthDownsampleCopyMat;

        RenderTexture compRt;
        RenderTexture depthRt;
        RenderTexture normalRt;
        RenderTexture thicknessRt;
        RenderTexture shadowRt;

        CommandBuffer cmd;

        Bilateral1D bilateral1D = new Bilateral1D();
        BilateralSmooth2D bilateral2D = new BilateralSmooth2D();
        GaussSmooth gaussSmooth = new GaussSmooth();

        ComputeBuffer argsBuffer;
        ComputeBuffer foamCountBufferDummy;

        bool initialized;

        void OnEnable()
        {
            TryAutoAssignReferences();
            Init();
            RenderCamSetup();
        }

        void OnDisable()
        {
            Cleanup();
        }

        void LateUpdate()
        {
            if (sim == null || sim.positionBuffer == null || sim.numParticles <= 0)
            {
                return;
            }

            if (!initialized)
            {
                Init();
                RenderCamSetup();
            }

            BuildCommands();
            UpdateSettings();
        }

        void TryAutoAssignReferences()
        {
            if (sim == null)
            {
                sim = FindFirstObjectByType<FluidSim2D>();
            }

            if (renderShader == null)
            {
                // Prefer simpler 2D composite shader by default
                renderShader = Shader.Find("Fluid/Fluid2DComposite");
                if (renderShader == null)
                {
                    renderShader = Shader.Find("Fluid/FluidRender");
                }
            }

            if (depthShader == null)
            {
                depthShader = Shader.Find("Fluid/ParticleDepth");
            }

            if (thicknessShader == null)
            {
                thicknessShader = Shader.Find("Fluid/ParticleThickness");
            }

            if (smoothThickPrepareShader == null)
            {
                smoothThickPrepareShader = Shader.Find("Fluid/SmoothThickPrepare");
            }

            if (normalShader == null)
            {
                normalShader = Shader.Find("Fluid/NormalsFromDepth");
            }

            if (sun == null)
            {
                sun = FindFirstObjectByType<Light>();
            }
        }

        void Init()
        {
            if (sim == null || sim.positionBuffer == null || sim.numParticles <= 0)
            {
                return;
            }

            if (cmd == null)
            {
                cmd = new CommandBuffer { name = "ScreenSpace Fluid 2D" };
            }

            if (quadMesh == null)
            {
                quadMesh = QuadGenerator.GenerateQuadMesh();
            }

            // Create/update args buffer for instanced indirect rendering
            ComputeHelper.CreateArgsBuffer(ref argsBuffer, quadMesh, sim.positionBuffer.count);

            InitTextures();
            InitMaterials();

            // Dummy foam count buffer so FluidRender shader's debug bar works safely
            if (foamCountBufferDummy == null)
            {
                foamCountBufferDummy = new ComputeBuffer(1, sizeof(uint));
                var zeros = new uint[1];
                foamCountBufferDummy.SetData(zeros);
            }

            initialized = true;
        }

        void InitMaterials()
        {
            if (depthDownsampleCopyShader != null && depthDownsampleCopyMat == null)
            {
                depthDownsampleCopyMat = new Material(depthDownsampleCopyShader);
            }

            if (depthShader != null && matDepth == null)
            {
                matDepth = new Material(depthShader);
            }

            if (normalShader != null && matNormal == null)
            {
                matNormal = new Material(normalShader);
            }

            if (thicknessShader != null && matThickness == null)
            {
                matThickness = new Material(thicknessShader);
            }

            if (smoothThickPrepareShader != null && smoothPrepareMat == null)
            {
                smoothPrepareMat = new Material(smoothThickPrepareShader);
            }

            // Recreate composite material if the assigned shader changed (e.g. switch to Fluid2DComposite in inspector)
            if (renderShader != null && matComposite != null && matComposite.shader != renderShader)
            {
                if (Application.isPlaying)
                    Destroy(matComposite);
                else
                    DestroyImmediate(matComposite);
                matComposite = null;
            }
            if (renderShader != null && matComposite == null)
            {
                matComposite = new Material(renderShader);
            }
        }

        void InitTextures()
        {
            // Match render textures to the actual game view size, not the full desktop.
            int width = 0;
            int height = 0;

            var cam = Camera.main;
            if (cam != null)
            {
                var rect = cam.pixelRect;
                width = Mathf.Max(1, (int)rect.width);
                height = Mathf.Max(1, (int)rect.height);
            }
            else
            {
                width = Screen.width;
                height = Screen.height;
            }

            if (width <= 0 || height <= 0)
            {
                width = 1280;
                height = 720;
            }

            // Thickness texture size (can be reduced for performance)
            int thicknessTexWidth = width;
            int thicknessTexHeight = height;

            if (!useFullSizeThicknessTex)
            {
                thicknessTexWidth = Mathf.Max(width / 2, 1);
                thicknessTexHeight = Mathf.Max(height / 2, 1);
            }

            int shadowTexWidth = Mathf.Max(width / 4, 1);
            int shadowTexHeight = Mathf.Max(height / 4, 1);

            GraphicsFormat fmtRGBA = GraphicsFormat.R32G32B32A32_SFloat;
            GraphicsFormat fmtR = GraphicsFormat.R32_SFloat;

            ComputeHelper.CreateRenderTexture(ref depthRt, width, height, FilterMode.Bilinear, fmtR, "Fluid2D_Depth", DepthMode.Depth16);
            ComputeHelper.CreateRenderTexture(ref thicknessRt, thicknessTexWidth, thicknessTexHeight, FilterMode.Bilinear, fmtR, "Fluid2D_Thickness", DepthMode.Depth16);
            ComputeHelper.CreateRenderTexture(ref normalRt, width, height, FilterMode.Bilinear, fmtRGBA, "Fluid2D_Normals", DepthMode.None);
            ComputeHelper.CreateRenderTexture(ref compRt, width, height, FilterMode.Bilinear, fmtRGBA, "Fluid2D_Comp", DepthMode.None);
            ComputeHelper.CreateRenderTexture(ref shadowRt, shadowTexWidth, shadowTexHeight, FilterMode.Bilinear, fmtR, "Fluid2D_Shadow", DepthMode.None);
        }

        void RenderCamSetup()
        {
            var cam = Camera.main;
            if (cam == null || cmd == null)
            {
                return;
            }

            // Ensure we only have one instance of this command buffer.
            // Use AfterImageEffects so we composite on top of the fully rendered scene.
            cam.RemoveCommandBuffer(CameraEvent.AfterEverything, cmd);
            cam.RemoveCommandBuffer(CameraEvent.AfterImageEffects, cmd);
            cam.AddCommandBuffer(CameraEvent.AfterImageEffects, cmd);
            cam.depthTextureMode |= DepthTextureMode.Depth;
        }

        void BuildCommands()
        {
            if (cmd == null || quadMesh == null || matDepth == null || matThickness == null || matComposite == null)
            {
                return;
            }

            cmd.Clear();

            // Copy current camera color buffer so we can composite fluid on top of the scene.
            int cameraColorRTId = Shader.PropertyToID("_Fluid2D_CameraColorRT");
            cmd.GetTemporaryRT(cameraColorRTId, compRt.descriptor);
            cmd.Blit(BuiltinRenderTextureType.CameraTarget, cameraColorRTId);

            // Optional: clear shadow RT (we don't render real shadows in 2D, but keep the texture valid)
            if (shadowRt != null)
            {
                cmd.SetRenderTarget(shadowRt);
                cmd.ClearRenderTarget(true, true, Color.black);
            }

            // Render particles to depth texture
            cmd.SetRenderTarget(depthRt);
            cmd.ClearRenderTarget(true, true, Color.white * 10000000f, 1f);
            cmd.DrawMeshInstancedIndirect(quadMesh, 0, matDepth, 0, argsBuffer);

            // Render particles to thickness texture
            cmd.SetRenderTarget(thicknessRt);
            cmd.ClearRenderTarget(true, true, Color.black, 1f);
            cmd.DrawMeshInstancedIndirect(quadMesh, 0, matThickness, 0, argsBuffer);

            // Pack thickness and depth into compRt
            cmd.Blit(null, compRt, smoothPrepareMat);

            // Apply smoothing (only RGB; A is kept as hard depth)
            ApplyActiveSmoothingType(cmd, compRt, compRt, compRt.descriptor, new Vector3(1, 1, 0));

            // Reconstruct normals from smooth depth
            cmd.Blit(compRt, normalRt, matNormal);

            // Composite final image to screen, using the captured camera color as background.
            cmd.Blit(cameraColorRTId, BuiltinRenderTextureType.CameraTarget, matComposite);
            cmd.ReleaseTemporaryRT(cameraColorRTId);
        }

        void ApplyActiveSmoothingType(CommandBuffer commandBuffer, RenderTargetIdentifier src, RenderTargetIdentifier target, RenderTextureDescriptor desc, Vector3 smoothMask)
        {
            if (smoothType == BlurType.Bilateral1D)
            {
                bilateral1D.Smooth(commandBuffer, src, target, desc, bilateralSettings, smoothMask);
            }
            else if (smoothType == BlurType.Bilateral2D)
            {
                bilateral2D.Smooth(commandBuffer, src, target, desc, bilateralSettings, smoothMask);
            }
            else if (smoothType == BlurType.Gaussian)
            {
                gaussSmooth.Smooth(commandBuffer, src, target, desc, gaussSmoothSettings, smoothMask);
            }
        }

        void UpdateSettings()
        {
            if (sim == null || matComposite == null || smoothPrepareMat == null || matDepth == null || matThickness == null || matNormal == null)
            {
                return;
            }

            // Smooth prepare
            smoothPrepareMat.SetTexture("Depth", depthRt);
            smoothPrepareMat.SetTexture("Thick", thicknessRt);

            // Thickness
            matThickness.SetBuffer("Positions", sim.positionBuffer);
            matThickness.SetFloat("scale", thicknessParticleScale);

            // Depth
            matDepth.SetBuffer("Positions", sim.positionBuffer);
            matDepth.SetFloat("scale", depthParticleSize);

            // Normals
            matNormal.SetInt("useSmoothedDepth", 1);

            // Composite material settings
            matComposite.SetTexture("Comp", compRt);

            // If using the 2D composite shader, set its parameters (check actual material shader so it always applies).
            if (matComposite != null && matComposite.shader != null && matComposite.shader.name.Contains("Fluid2DComposite"))
            {
                matComposite.SetColor("_WaterColor", waterColor);
                matComposite.SetFloat("_ThicknessScale", thicknessToAlpha);
                matComposite.SetFloat("_ThicknessDarken", thicknessDarken);
                matComposite.SetTexture("Normals", normalRt);
            }
            else
            {
                // Fallback: keep original 3D-flavoured settings for Fluid/FluidRender
                matComposite.SetInt("debugDisplayMode", (int)displayMode);
                matComposite.SetTexture("Normals", normalRt);
                matComposite.SetTexture("ShadowMap", shadowRt != null ? shadowRt : Texture2D.blackTexture);

                matComposite.SetVector("testParams", Vector3.zero);
                matComposite.SetVector("extinctionCoefficients", extinctionCoefficients * extinctionMultiplier);

                // Approximate fluid bounds from 2D boundary, if available
                Vector3 boundsSize = new Vector3(10f, 5f, 2f);
                if (sim.boundaryComposite != null)
                {
                    var b = sim.boundaryComposite.bounds;
                    boundsSize = new Vector3(b.size.x, b.size.y, 2f);
                }
                matComposite.SetVector("boundsSize", boundsSize);
                matComposite.SetFloat("refractionMultiplier", refractionMultiplier);

                // Simple lighting setup
                if (sun != null)
                {
                    matComposite.SetVector("dirToSun", -sun.transform.forward);
                }
                else
                {
                    matComposite.SetVector("dirToSun", new Vector3(0.3f, 0.7f, 0.6f));
                }

                matComposite.SetMatrix("shadowVP", Matrix4x4.identity);
                matComposite.SetFloat("depthDisplayScale", depthDisplayScale);
                matComposite.SetFloat("thicknessDisplayScale", thicknessDisplayScale);

                // Dummy foam info so shader's debug bar is well-defined
                if (foamCountBufferDummy != null)
                {
                    matComposite.SetBuffer("foamCountBuffer", foamCountBufferDummy);
                    matComposite.SetInt("foamMax", 1);
                }
            }
        }

        void Cleanup()
        {
            if (cmd != null && Camera.main != null)
            {
                Camera.main.RemoveCommandBuffer(CameraEvent.AfterEverything, cmd);
            }

            ComputeHelper.Release(argsBuffer);
            ComputeHelper.Release(depthRt, thicknessRt, normalRt, compRt, shadowRt);

            if (foamCountBufferDummy != null)
            {
                foamCountBufferDummy.Release();
                foamCountBufferDummy = null;
            }

            initialized = false;
        }

        public enum DisplayMode
        {
            Composite,
            Depth,
            SmoothDepth,
            Normal,
            Thickness,
            SmoothThickness
        }

        public enum BlurType
        {
            Gaussian,
            Bilateral2D,
            Bilateral1D
        }
    }
}

