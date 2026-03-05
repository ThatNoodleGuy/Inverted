using Seb.Helpers;
using UnityEngine;

public class ParticleDisplay2D : MonoBehaviour
{
	[Header("Required References")]
	public FluidSim2D sim;
	public Mesh mesh;
	public Shader shader;

	[Header("Display Settings")]
	public float scale = 1f;
	public Gradient colourMap;
	public int gradientResolution = 512;
	public float velocityDisplayMax = 10f;

	[Header("Debug")]
	public bool showDebugInfo = false;

	Material material;
	ComputeBuffer argsBuffer;
	Bounds bounds;
	Texture2D gradientTexture;
	bool needsUpdate = true;

	// Initialization tracking
	private bool isInitialized = false;
	private bool hasLoggedError = false;

	void Start()
	{
		// Validate required references
		if (!ValidateReferences())
		{
			enabled = false; // Disable this component if references are missing
			return;
		}

		// Initialize material
		if (shader != null)
		{
			material = new Material(shader);
		}

		// Set up initial bounds (large bounds for particle system)
		bounds = new Bounds(Vector3.zero, Vector3.one * 10000);

		// Force initial update
		needsUpdate = true;

		if (showDebugInfo)
		{
			Debug.Log("ParticleDisplay2D: Initialized successfully");
		}
	}

	void LateUpdate()
	{
		// Early exit if not properly initialized
		if (!ValidateRuntimeReferences())
		{
			return;
		}

		// Wait for FluidSim2D to be ready
		if (!IsSimulationReady())
		{
			return;
		}

		// Mark as initialized on first successful frame
		if (!isInitialized)
		{
			isInitialized = true;
			if (showDebugInfo)
			{
				Debug.Log($"ParticleDisplay2D: Ready to render {sim.numParticles} particles");
			}
		}

		// Update settings and render
		UpdateSettings();
		Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, argsBuffer);
	}

	bool ValidateReferences()
	{
		bool isValid = true;
		System.Text.StringBuilder errors = new System.Text.StringBuilder();

		if (sim == null)
		{
			errors.AppendLine("- FluidSim2D reference is missing");
			isValid = false;
		}

		if (mesh == null)
		{
			errors.AppendLine("- Mesh reference is missing");
			isValid = false;
		}

		if (shader == null)
		{
			errors.AppendLine("- Shader reference is missing");
			isValid = false;
		}

		if (!isValid && !hasLoggedError)
		{
			Debug.LogError($"ParticleDisplay2D on {gameObject.name} is missing required references:\n{errors}", this);
			hasLoggedError = true;
		}

		return isValid;
	}

	bool ValidateRuntimeReferences()
	{
		// Quick null checks for runtime
		return sim != null && mesh != null && shader != null && material != null;
	}

	bool IsSimulationReady()
	{
		// Check if FluidSim2D has been initialized and has valid buffers
		if (sim == null) return false;

		// Check if simulation has been started (numParticles > 0)
		if (sim.numParticles <= 0) return false;

		// Check if required buffers exist
		if (sim.positionBuffer == null || sim.velocityBuffer == null || sim.densityBuffer == null)
		{
			return false;
		}

		return true;
	}

	void UpdateSettings()
	{
		// This should only be called after validation, but add safety checks anyway
		if (material == null || sim == null) return;

		try
		{
			// Set buffers - these should be safe now after IsSimulationReady() check
			material.SetBuffer("Positions2D", sim.positionBuffer);
			material.SetBuffer("Velocities", sim.velocityBuffer);
			material.SetBuffer("DensityData", sim.densityBuffer);

			// Create/update args buffer
			ComputeHelper.CreateArgsBuffer(ref argsBuffer, mesh, sim.positionBuffer.count);

			// Update material properties if needed
			if (needsUpdate)
			{
				UpdateMaterialProperties();
				needsUpdate = false;
			}
		}
		catch (System.Exception e)
		{
			Debug.LogError($"ParticleDisplay2D: Error updating settings - {e.Message}", this);
		}
	}

	void UpdateMaterialProperties()
	{
		if (material == null) return;

		// Create gradient texture
		TextureFromGradient(ref gradientTexture, gradientResolution, colourMap);
		material.SetTexture("ColourMap", gradientTexture);

		// Set material properties
		material.SetFloat("scale", scale);
		material.SetFloat("velocityMax", velocityDisplayMax);

		if (showDebugInfo)
		{
			Debug.Log($"ParticleDisplay2D: Updated material properties - scale: {scale}, velocityMax: {velocityDisplayMax}");
		}
	}

	public static void TextureFromGradient(ref Texture2D texture, int width, Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
	{
		// Validate input parameters
		if (width <= 0)
		{
			Debug.LogWarning("ParticleDisplay2D: Invalid gradient texture width");
			width = 64; // Fallback width
		}

		// Initialize texture if needed
		if (texture == null)
		{
			texture = new Texture2D(width, 1);
		}
		else if (texture.width != width)
		{
			texture.Reinitialize(width, 1);
		}

		// Create default gradient if none provided
		if (gradient == null)
		{
			gradient = new Gradient();
			gradient.SetKeys(
				new GradientColorKey[] {
					new GradientColorKey(Color.blue, 0f),
					new GradientColorKey(Color.cyan, 0.5f),
					new GradientColorKey(Color.white, 1f)
				},
				new GradientAlphaKey[] {
					new GradientAlphaKey(0.7f, 0f),
					new GradientAlphaKey(1f, 1f)
				}
			);
		}

		// Set texture properties
		texture.wrapMode = TextureWrapMode.Clamp;
		texture.filterMode = filterMode;

		// Generate gradient colors
		Color[] cols = new Color[width];
		for (int i = 0; i < cols.Length; i++)
		{
			float t = cols.Length > 1 ? i / (cols.Length - 1f) : 0f;
			cols[i] = gradient.Evaluate(t);
		}

		texture.SetPixels(cols);
		texture.Apply();
	}

	void OnValidate()
	{
		// Clamp values to reasonable ranges
		scale = Mathf.Max(0.01f, scale);
		gradientResolution = Mathf.Clamp(gradientResolution, 16, 1024);
		velocityDisplayMax = Mathf.Max(0.1f, velocityDisplayMax);

		needsUpdate = true;
	}

	void OnDestroy()
	{
		// Clean up resources
		ComputeHelper.Release(argsBuffer);

		if (gradientTexture != null)
		{
			if (Application.isEditor)
			{
				DestroyImmediate(gradientTexture);
			}
			else
			{
				Destroy(gradientTexture);
			}
		}

		if (material != null)
		{
			if (Application.isEditor)
			{
				DestroyImmediate(material);
			}
			else
			{
				Destroy(material);
			}
		}
	}

	// Public method to force refresh (useful for debugging)
	[ContextMenu("Force Refresh Display")]
	public void ForceRefresh()
	{
		needsUpdate = true;
		isInitialized = false;
		hasLoggedError = false;

		if (showDebugInfo)
		{
			Debug.Log("ParticleDisplay2D: Forced refresh");
		}
	}

	// Public method to check system status
	public bool IsReady()
	{
		return isInitialized && ValidateRuntimeReferences() && IsSimulationReady();
	}

	// Debug info for inspector
	void OnGUI()
	{
		if (!showDebugInfo || !Application.isPlaying) return;

		GUI.color = Color.white;
		GUILayout.BeginArea(new Rect(10, 10, 300, 200));
		GUILayout.Label($"Particle Display Status:");
		GUILayout.Label($"- Initialized: {isInitialized}");
		GUILayout.Label($"- Sim Ready: {IsSimulationReady()}");
		GUILayout.Label($"- Particle Count: {(sim != null ? sim.numParticles : 0)}");
		GUILayout.Label($"- Has Buffers: {(sim != null && sim.positionBuffer != null)}");
		GUILayout.Label($"- Material: {(material != null ? "OK" : "NULL")}");
		GUILayout.EndArea();
	}
}