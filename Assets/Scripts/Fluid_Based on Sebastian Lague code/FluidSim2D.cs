using Seb.Helpers;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Serialization;
using UnityEngine.U2D;
using System.Collections.Generic;
using System.Linq;

public class FluidSim2D : MonoBehaviour
{
	public event System.Action SimulationStepCompleted;

	[Header("Simulation Settings")]
	public float timeScale = 1;
	public float maxTimestepFPS = 60;
	public int iterationsPerFrame;
	public float gravity;
	[Range(0, 1)] public float collisionDamping = 0.95f;
	public float smoothingRadius = 2;
	public float targetDensity;
	public float pressureMultiplier;
	public float nearPressureMultiplier;
	public float viscosityStrength;

	[Header("Boundary Settings")]
	public CompositeCollider2D boundaryComposite;
	[Tooltip("The margin within which particles will react to the boundary. If left empty, will search for the 'BlackSpace' tagged collider at Awake().")]
	public float boundaryCollisionMargin = 0.2f;
	public bool invertBoundary = true;
	[Tooltip("Stronger collision response to prevent leaking")]
	public float collisionResponseStrength = 1.5f;

	[Header("Player Interaction Settings")]
	[Tooltip("Reference to the player for fluid interaction")]
	public PlayerManager player;
	[Tooltip("Radius around player that affects fluid particles")]
	public float playerInteractionRadius = 3f;
	[Tooltip("Strength of player displacement force")]
	public float playerDisplacementStrength = 5f;
	[Tooltip("Strength of player attraction/repulsion in mirrored state")]
	public float mirroredInteractionStrength = 8f;
	[Tooltip("How much the player's velocity affects nearby particles")]
	public float velocityTransferStrength = 2f;
	[Tooltip("Multiplier for interaction when player is moving")]
	public float movementMultiplier = 1.5f;

	[Header("Fluid-to-Player Forces")]
	[Tooltip("How much the fluid pushes the player upward (buoyancy)")]
	public float buoyancyStrength = 15f;
	[Tooltip("Drag force when player moves through fluid")]
	public float fluidDrag = 2f;
	[Tooltip("Maximum depth for calculating buoyancy (world units)")]
	public float maxBuoyancyDepth = 3f;
	[Tooltip("Multiplier for buoyancy when player is in mirrored state")]
	public float mirroredBuoyancyMultiplier = 1.5f;
	[Tooltip("How responsive the buoyancy force is")]
	public float buoyancyResponsiveness = 5f;
	[Tooltip("Minimum submersion depth to start applying buoyancy")]
	public float buoyancyThreshold = 0.3f;
	[Tooltip("Smoothing factor for submersion depth changes")]
	public float submersionSmoothingFactor = 0.15f;
	[Tooltip("Maximum particles to consider for submersion calculation")]
	public int maxParticlesForSubmersion = 50;
	[Tooltip("Deadband around neutral buoyancy to prevent oscillation")]
	public float buoyancyDeadband = 0.1f;
	[Tooltip("Factor to reduce buoyancy near fluid edges")]
	public float edgeDetectionRadius = 1.5f;
	[Tooltip("Reduction factor for buoyancy near edges")]
	[Range(0f, 1f)] public float edgeBuoyancyReduction = 0.6f;

	// Add these private fields to your existing private fields section
	private float smoothedSubmersionDepth;
	private float lastSubmersionDepth;
	private int framesSinceLastBuoyancyUpdate;
	private bool isNearFluidEdge;

	[Header("Particle-Based Player Detection")]
	[Tooltip("Number of nearby particles needed to consider player 'in fluid'")]
	public int minParticlesForFluidDetection = 5;
	[Tooltip("Distance to check for nearby particles")]
	public float fluidDetectionRadius = 2f;
	[Tooltip("Enable particle-based fluid detection instead of polygon")]
	public bool useParticleBasedDetection = true;

	// Cache for particle positions (to avoid reading from GPU every frame)
	private Vector2[] cachedParticlePositions;
	private bool particlePositionsCached = false;
	private float lastCacheTime = 0f;
	private float cacheUpdateInterval = 0.1f; // Update cache 10 times per second

	[Header("Boundary Toggle")]
	[Tooltip("When false, particles flow freely — no collider at all.")]
	public bool useBoundary = true;

	[Header("Mouse Interaction Settings")]
	public float interactionRadius;
	public float interactionStrength;

	[Header("References")]
	public ComputeShader compute;
	public Spawner2D spawner2D;

	// New ComputeBuffers for Polygon Boundary
	ComputeBuffer _boundaryPointsBuffer;
	int _numBoundaryPoints;

	// Buffers
	public ComputeBuffer positionBuffer { get; private set; }
	public ComputeBuffer velocityBuffer { get; private set; }
	public ComputeBuffer densityBuffer { get; private set; }

	ComputeBuffer sortTarget_Position;
	ComputeBuffer sortTarget_PredicitedPosition;
	ComputeBuffer sortTarget_Velocity;

	ComputeBuffer predictedPositionBuffer;
	SpatialHash spatialHash;

	// Player tracking
	private Vector2 previousPlayerPosition;
	private Vector2 playerVelocity;
	private Rigidbody2D playerRigidbody;

	// Buoyancy tracking
	private float currentSubmersionDepth;
	private float targetBuoyancyForce;
	private float currentBuoyancyForce;

	// Kernel IDs
	const int externalForcesKernel = 0;
	const int spatialHashKernel = 1;
	const int reorderKernel = 2;
	const int copybackKernel = 3;
	const int densityKernel = 4;
	const int pressureKernel = 5;
	const int viscosityKernel = 6;
	const int updatePositionKernel = 7;
	const int polygonCollisionKernel = 8;

	// State
	bool isPaused;
	Spawner2D.ParticleSpawnData spawnData;
	bool pauseNextFrame;

	public int numParticles { get; private set; }

	[Header("Editor Tools")]
	[Tooltip("Click to load the points from the assigned Polygon Collider 2D.")]
	public bool loadPolygonPoints = false;

	void OnValidate()
	{
		if (loadPolygonPoints)
		{
			LoadBoundaryPolygonPoints();
			loadPolygonPoints = false;
		}
	}

	void Awake()
	{
		// Auto-find player if not assigned
		if (player == null)
		{
			player = FindFirstObjectByType<PlayerManager>();
			if (player == null)
			{
				Debug.LogError("FluidSim2D: No PlayerManager found! Player interaction will not work.");
			}
		}


		// if you forgot to hook it up in the Inspector, grab it now:
		if (boundaryComposite == null)
		{
			var blackSpaceGO = GameObject.FindWithTag("BlackSpace");
			if (blackSpaceGO != null)
				boundaryComposite = blackSpaceGO.GetComponent<CompositeCollider2D>();
		}
	}

	void Start()
	{
		Debug.Log("Controls:LMB = Attract, RMB = Repel");
		Init();

		// Get player rigidbody for velocity tracking
		if (player != null)
		{
			playerRigidbody = player.GetComponent<Rigidbody2D>();
			previousPlayerPosition = player.transform.position;
		}
	}

	void Init()
	{
		float deltaTime = 1 / 60f;
		Time.fixedDeltaTime = deltaTime;

		spawnData = spawner2D.GetSpawnData();
		numParticles = spawnData.positions.Length;
		spatialHash = new SpatialHash(numParticles);

		if (boundaryComposite != null)
		{
			var keep = new List<float2>();
			var keepV = new List<float2>();
			for (int i = 0; i < spawnData.positions.Length; i++)
			{
				Vector2 candidate = spawnData.positions[i];
				if (!boundaryComposite.OverlapPoint(candidate))
				{
					keep.Add(candidate);
					keepV.Add(spawnData.velocities[i]);
				}
			}

			spawnData.positions = keep.ToArray();
			spawnData.velocities = keepV.ToArray();
		}

		// Create buffers
		positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

		sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

		SetInitialBufferData(spawnData);

		// Init compute
		ComputeHelper.SetBuffer(compute, positionBuffer, "Positions", externalForcesKernel, updatePositionKernel, reorderKernel, copybackKernel, polygonCollisionKernel);
		ComputeHelper.SetBuffer(compute, predictedPositionBuffer, "PredictedPositions", externalForcesKernel, spatialHashKernel, densityKernel, pressureKernel, viscosityKernel, reorderKernel, copybackKernel, polygonCollisionKernel);
		ComputeHelper.SetBuffer(compute, velocityBuffer, "Velocities", externalForcesKernel, pressureKernel, viscosityKernel, updatePositionKernel, reorderKernel, copybackKernel, polygonCollisionKernel);
		ComputeHelper.SetBuffer(compute, densityBuffer, "Densities", densityKernel, pressureKernel, viscosityKernel);

		ComputeHelper.SetBuffer(compute, spatialHash.SpatialIndices, "SortedIndices", spatialHashKernel, reorderKernel);
		ComputeHelper.SetBuffer(compute, spatialHash.SpatialOffsets, "SpatialOffsets", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);
		ComputeHelper.SetBuffer(compute, spatialHash.SpatialKeys, "SpatialKeys", spatialHashKernel, densityKernel, pressureKernel, viscosityKernel);

		ComputeHelper.SetBuffer(compute, sortTarget_Position, "SortTarget_Positions", reorderKernel, copybackKernel);
		ComputeHelper.SetBuffer(compute, sortTarget_PredicitedPosition, "SortTarget_PredictedPositions", reorderKernel, copybackKernel);
		ComputeHelper.SetBuffer(compute, sortTarget_Velocity, "SortTarget_Velocities", reorderKernel, copybackKernel);

		compute.SetInt("numParticles", numParticles);

		// Initialize polygon boundary buffers
		if (useBoundary && boundaryComposite != null)
		{
			LoadBoundaryPolygonPoints();

			ComputeHelper.SetBuffer(compute, _boundaryPointsBuffer, "_BoundaryPoints", polygonCollisionKernel);
			compute.SetInt("_NumBoundaryPoints", _numBoundaryPoints);
			compute.SetFloat("_BoundaryCollisionMargin", boundaryCollisionMargin);
			compute.SetBool("_InvertBoundary", invertBoundary);
			compute.SetFloat("_CollisionResponseStrength", collisionResponseStrength);
		}
	}

	void LoadBoundaryPolygonPoints()
	{
		List<float2> worldPoints = new List<float2>();

		// 1) If you’ve got a CompositeCollider2D, iterate all its paths…
		if (boundaryComposite != null)
		{
			for (int p = 0; p < boundaryComposite.pathCount; ++p)
			{
				var pts = new Vector2[boundaryComposite.GetPathPointCount(p)];
				boundaryComposite.GetPath(p, pts);

				// Transform each into world‐space
				foreach (var lp in pts)
				{
					Vector3 wp = boundaryComposite.transform.TransformPoint(lp);
					worldPoints.Add(new float2(wp.x, wp.y));
				}
			}
		}
		else
		{
			Debug.LogWarning("FluidSim2D: no boundaryComposite or boundaryComposite assigned");
			return;
		}

		// 2) Upload to GPU
		_numBoundaryPoints = worldPoints.Count;
		ComputeHelper.CreateStructuredBuffer<float2>(ref _boundaryPointsBuffer, _numBoundaryPoints);
		_boundaryPointsBuffer.SetData(worldPoints.ToArray());

		ComputeHelper.SetBuffer(compute, _boundaryPointsBuffer, "_BoundaryPoints", polygonCollisionKernel);
		compute.SetInt("_NumBoundaryPoints", _numBoundaryPoints);
		compute.SetFloat("_BoundaryCollisionMargin", boundaryCollisionMargin);
		compute.SetBool("_InvertBoundary", invertBoundary);
		compute.SetFloat("_CollisionResponseStrength", collisionResponseStrength);

		Debug.Log($"Loaded {_numBoundaryPoints} boundary points from composite collider");
	}

	void Update()
	{
		// Update player tracking
		UpdatePlayerTracking();

		if (!isPaused)
		{
			float maxDeltaTime = maxTimestepFPS > 0 ? 1 / maxTimestepFPS : float.PositiveInfinity;
			float dt = Mathf.Min(Time.deltaTime * timeScale, maxDeltaTime);
			RunSimulationFrame(dt);
		}
	}

	void FixedUpdate()
	{
		// Apply fluid forces to player
		if (player != null && playerRigidbody != null)
		{
			ApplyFluidForcesToPlayer();
		}
	}

	void UpdatePlayerTracking()
	{
		if (player == null) return;

		Vector2 currentPlayerPosition = player.transform.position;

		// Calculate player velocity
		if (playerRigidbody != null)
		{
			playerVelocity = playerRigidbody.linearVelocity;
		}
		else
		{
			playerVelocity = (currentPlayerPosition - previousPlayerPosition) / Time.deltaTime;
		}

		previousPlayerPosition = currentPlayerPosition;

		// Calculate submersion depth for buoyancy
		UpdateSubmersionDepth();
	}

void UpdateSubmersionDepth()
{
    if (player == null)
    {
        currentSubmersionDepth = 0f;
        smoothedSubmersionDepth = 0f;
        return;
    }

    float rawSubmersion = 0f;

    if (useParticleBasedDetection)
    {
        rawSubmersion = CalculateImprovedParticleBasedSubmersion();
    }
    else
    {
        // Keep original polygon-based calculation as fallback
        if (boundaryComposite == null)
        {
            rawSubmersion = 0f;
        }
        else
        {
            Vector2 playerPos = player.transform.position;
            if (!boundaryComposite.OverlapPoint(playerPos))
            {
                rawSubmersion = 0f;
            }
            else
            {
                float minDistToBoundary = float.MaxValue;
                int pathCount = boundaryComposite.pathCount;
                for (int p = 0; p < pathCount; ++p)
                {
                    int count = boundaryComposite.GetPathPointCount(p);
                    Vector2[] pts = new Vector2[count];
                    boundaryComposite.GetPath(p, pts);

                    for (int i = 0; i < count; ++i)
                    {
                        Vector3 w1 = boundaryComposite.transform.TransformPoint(pts[i]);
                        Vector3 w2 = boundaryComposite.transform.TransformPoint(pts[(i + 1) % count]);
                        float dist = DistanceToLineSegment(playerPos, w1, w2);
                        minDistToBoundary = Mathf.Min(minDistToBoundary, dist);
                    }
                }
                rawSubmersion = Mathf.Clamp(minDistToBoundary, 0f, maxBuoyancyDepth);
            }
        }
    }

    // Apply threshold - only register submersion above minimum threshold
    if (rawSubmersion < buoyancyThreshold)
    {
        rawSubmersion = 0f;
    }

    // Smooth the submersion depth to prevent oscillations
    if (Time.deltaTime > 0)
    {
        smoothedSubmersionDepth = Mathf.Lerp(smoothedSubmersionDepth, rawSubmersion, 
            submersionSmoothingFactor * (1f / Time.deltaTime) * Time.deltaTime);
    }

    // Detect if we're near a fluid edge and reduce buoyancy accordingly
    isNearFluidEdge = IsPlayerNearFluidEdge();
    if (isNearFluidEdge)
    {
        smoothedSubmersionDepth *= edgeBuoyancyReduction;
    }

    currentSubmersionDepth = smoothedSubmersionDepth;
    lastSubmersionDepth = rawSubmersion;
}

	bool IsPlayerNearFluidParticles()
	{
		if (player == null || positionBuffer == null) return false;

		// Update cached positions if needed
		if (!particlePositionsCached || Time.time - lastCacheTime > cacheUpdateInterval)
		{
			UpdateCachedParticlePositions();
		}

		if (cachedParticlePositions == null) return false;

		Vector2 playerPos = player.transform.position;
		int nearbyParticleCount = 0;
		float detectionRadiusSqr = fluidDetectionRadius * fluidDetectionRadius;

		// Count particles within detection radius
		for (int i = 0; i < cachedParticlePositions.Length; i++)
		{
			Vector2 particlePos = cachedParticlePositions[i];
			float distSqr = (playerPos - particlePos).sqrMagnitude;

			if (distSqr <= detectionRadiusSqr)
			{
				nearbyParticleCount++;
				if (nearbyParticleCount >= minParticlesForFluidDetection)
				{
					return true;
				}
			}
		}

		return false;
	}

	// ==== NEW: Method to cache particle positions from GPU ====
	void UpdateCachedParticlePositions()
	{
		if (positionBuffer == null || numParticles == 0) return;

		// Initialize array if needed
		if (cachedParticlePositions == null || cachedParticlePositions.Length != numParticles)
		{
			cachedParticlePositions = new Vector2[numParticles];
		}

		// Read particle positions from GPU buffer
		try
		{
			positionBuffer.GetData(cachedParticlePositions);
			particlePositionsCached = true;
			lastCacheTime = Time.time;
		}
		catch (System.Exception e)
		{
			Debug.LogWarning($"Failed to read particle positions: {e.Message}");
			particlePositionsCached = false;
		}
	}

	float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
	{
		Vector2 lineDir = lineEnd - lineStart;
		float lineLength = lineDir.magnitude;

		if (lineLength < 0.0001f) return Vector2.Distance(point, lineStart);

		float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, lineDir) / (lineLength * lineLength));
		Vector2 projection = lineStart + t * lineDir;
		return Vector2.Distance(point, projection);
	}

	bool IsPlayerNearFluidEdge()
{
    if (!particlePositionsCached || cachedParticlePositions == null) return false;

    Vector2 playerPos = player.transform.position;
    float edgeRadiusSqr = edgeDetectionRadius * edgeDetectionRadius;
    
    // Count particles in different directions around the player
    int[] directionCounts = new int[8]; // 8 directions around player
    Vector2[] directions = {
        Vector2.up, Vector2.down, Vector2.left, Vector2.right,
        new Vector2(1, 1).normalized, new Vector2(-1, 1).normalized,
        new Vector2(1, -1).normalized, new Vector2(-1, -1).normalized
    };

    foreach (Vector2 particlePos in cachedParticlePositions)
    {
        Vector2 offset = particlePos - playerPos;
        float distSqr = offset.sqrMagnitude;
        
        if (distSqr <= edgeRadiusSqr && distSqr > 0.01f)
        {
            // Determine which direction this particle is in
            float angle = Mathf.Atan2(offset.y, offset.x);
            int dirIndex = Mathf.FloorToInt(((angle + Mathf.PI) / (2f * Mathf.PI)) * 8f) % 8;
            directionCounts[dirIndex]++;
        }
    }

    // If any direction has significantly fewer particles, we're near an edge
    int avgCount = 0;
    foreach (int count in directionCounts) avgCount += count;
    avgCount /= 8;

    foreach (int count in directionCounts)
    {
        if (count < avgCount * 0.3f) // Less than 30% of average
        {
            return true;
        }
    }

    return false;
}

	void ApplyFluidForcesToPlayer()
	{
		if (currentSubmersionDepth <= buoyancyThreshold)
		{
			// Gradually reduce buoyancy force when not submerged
			currentBuoyancyForce = Mathf.Lerp(currentBuoyancyForce, 0f,
				Time.fixedDeltaTime * buoyancyResponsiveness * 2f);
			return;
		}

		// Calculate buoyancy force based on submersion depth with deadband
		float depthRatio = Mathf.Clamp01(currentSubmersionDepth / maxBuoyancyDepth);

		// Apply deadband to prevent oscillation around neutral buoyancy
		float adjustedDepthRatio = depthRatio;
		if (depthRatio < buoyancyDeadband)
		{
			adjustedDepthRatio = 0f;
		}
		else
		{
			adjustedDepthRatio = (depthRatio - buoyancyDeadband) / (1f - buoyancyDeadband);
		}

		targetBuoyancyForce = buoyancyStrength * adjustedDepthRatio;

		// Apply multiplier for mirrored state
		if (player.IsInMirroredState())
		{
			targetBuoyancyForce *= mirroredBuoyancyMultiplier;
		}

		// Reduce buoyancy if player is moving upward too fast (prevent ejection)
		float upwardVelocity = playerVelocity.y;
		if (upwardVelocity > 2f) // If moving up faster than 2 m/s
		{
			float velocityDamping = Mathf.Clamp01(1f - (upwardVelocity - 2f) / 3f);
			targetBuoyancyForce *= velocityDamping;
		}

		// Smooth the buoyancy force for stability
		float responsiveness = buoyancyResponsiveness;

		// Make buoyancy more responsive when entering fluid, less when exiting
		if (targetBuoyancyForce > currentBuoyancyForce)
		{
			responsiveness *= 1.5f; // Faster response when increasing buoyancy
		}
		else
		{
			responsiveness *= 0.7f; // Slower response when decreasing buoyancy
		}

		currentBuoyancyForce = Mathf.Lerp(currentBuoyancyForce, targetBuoyancyForce,
			Time.fixedDeltaTime * responsiveness);

		// Apply upward buoyancy force
		Vector2 buoyancyForce = Vector2.up * currentBuoyancyForce;
		playerRigidbody.AddForce(buoyancyForce, ForceMode2D.Force);

		// Apply improved fluid drag
		float dragMultiplier = fluidDrag * depthRatio;

		// Apply different drag for horizontal vs vertical movement
		Vector2 horizontalVelocity = new Vector2(playerVelocity.x, 0f);
		Vector2 verticalVelocity = new Vector2(0f, playerVelocity.y);

		Vector2 horizontalDrag = -horizontalVelocity * dragMultiplier;
		Vector2 verticalDrag = -verticalVelocity * dragMultiplier * 0.6f; // Less vertical drag

		playerRigidbody.AddForce(horizontalDrag + verticalDrag, ForceMode2D.Force);

		// Reduce turbulence and make it more subtle
		if (playerVelocity.magnitude > 2f && !isNearFluidEdge)
		{
			float turbulence = Mathf.Sin(Time.time * 8f) * 0.2f; // Reduced turbulence
			Vector2 turbulenceForce = Vector2.right * turbulence * depthRatio * 0.5f;
			playerRigidbody.AddForce(turbulenceForce, ForceMode2D.Force);
		}

		// Debug info (reduced frequency)
		framesSinceLastBuoyancyUpdate++;
		if (framesSinceLastBuoyancyUpdate >= 120) // Every 2 seconds at 60fps
		{
			Debug.Log($"Buoyancy - Depth: {currentSubmersionDepth:F2}, Smoothed: {smoothedSubmersionDepth:F2}, " +
					 $"Force: {currentBuoyancyForce:F2}, Edge: {isNearFluidEdge}, Particles: {GetNearbyParticleCount()}");
			framesSinceLastBuoyancyUpdate = 0;
		}
	}

	void RunSimulationFrame(float frameTime)
	{
		float timeStep = frameTime / iterationsPerFrame;
		UpdateSettings(timeStep);

		for (int i = 0; i < iterationsPerFrame; i++)
		{
			RunSimulationStep();
			SimulationStepCompleted?.Invoke();
		}
	}

	void RunSimulationStep()
	{
		// External forces and prediction (includes player interaction)
		ComputeHelper.Dispatch(compute, numParticles, kernelIndex: externalForcesKernel);

		// Apply polygon collision to predicted positions BEFORE spatial operations
		if (useBoundary)
		{
			ComputeHelper.Dispatch(compute, numParticles, kernelIndex: polygonCollisionKernel);
		}

		// Spatial hash operations
		RunSpatial();

		// Fluid dynamics
		ComputeHelper.Dispatch(compute, numParticles, kernelIndex: densityKernel);
		ComputeHelper.Dispatch(compute, numParticles, kernelIndex: pressureKernel);
		ComputeHelper.Dispatch(compute, numParticles, kernelIndex: viscosityKernel);

		// Update positions
		ComputeHelper.Dispatch(compute, numParticles, kernelIndex: updatePositionKernel);
	}

	void RunSpatial()
	{
		ComputeHelper.Dispatch(compute, numParticles, kernelIndex: spatialHashKernel);
		spatialHash.Run();
		ComputeHelper.Dispatch(compute, numParticles, kernelIndex: reorderKernel);
		ComputeHelper.Dispatch(compute, numParticles, kernelIndex: copybackKernel);
	}

	void UpdateSettings(float deltaTime)
	{
		compute.SetFloat("deltaTime", deltaTime);
		compute.SetFloat("gravity", gravity);
		compute.SetFloat("collisionDamping", collisionDamping);
		compute.SetFloat("smoothingRadius", smoothingRadius);
		compute.SetFloat("targetDensity", targetDensity);
		compute.SetFloat("pressureMultiplier", pressureMultiplier);
		compute.SetFloat("nearPressureMultiplier", nearPressureMultiplier);
		compute.SetFloat("viscosityStrength", viscosityStrength);

		compute.SetFloat("Poly6ScalingFactor", 4 / (Mathf.PI * Mathf.Pow(smoothingRadius, 8)));
		compute.SetFloat("SpikyPow3ScalingFactor", 10 / (Mathf.PI * Mathf.Pow(smoothingRadius, 5)));
		compute.SetFloat("SpikyPow2ScalingFactor", 6 / (Mathf.PI * Mathf.Pow(smoothingRadius, 4)));
		compute.SetFloat("SpikyPow3DerivativeScalingFactor", 30 / (Mathf.Pow(smoothingRadius, 5) * Mathf.PI));
		compute.SetFloat("SpikyPow2DerivativeScalingFactor", 12 / (Mathf.Pow(smoothingRadius, 4) * Mathf.PI));

		// Mouse interaction settings
		Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
		bool isPullInteraction = Input.GetMouseButton(0);
		bool isPushInteraction = Input.GetMouseButton(1);
		float currInteractStrength = 0;

		if (isPushInteraction || isPullInteraction)
		{
			currInteractStrength = isPushInteraction ? -interactionStrength : interactionStrength;
		}

		compute.SetVector("interactionInputPoint", mousePos);
		compute.SetFloat("interactionInputStrength", currInteractStrength);
		compute.SetFloat("interactionInputRadius", interactionRadius);

		// Player interaction settings
		UpdatePlayerInteractionSettings();

		// Update boundary settings if they've changed
		if (useBoundary)
		{
			compute.SetFloat("_BoundaryCollisionMargin", boundaryCollisionMargin);
			compute.SetFloat("_CollisionResponseStrength", collisionResponseStrength);
		}
	}

	void UpdatePlayerInteractionSettings()
	{
		if (player == null)
		{
			// Disable player interaction
			compute.SetVector("playerPosition", Vector2.zero);
			compute.SetFloat("playerInteractionRadius", 0f);
			compute.SetFloat("playerInteractionStrength", 0f);
			compute.SetVector("playerVelocity", Vector2.zero);
			compute.SetFloat("velocityTransferStrength", 0f);
			compute.SetBool("isPlayerInMirroredState", false);
			return;
		}

		Vector2 playerPos = player.transform.position;
		bool isInMirroredState = player.IsInMirroredState();

		// NEW: Use particle-based detection instead of polygon
		bool isNearFluid = useParticleBasedDetection ? IsPlayerNearFluidParticles() : IsPlayerInsidePolygon();

		// Only apply interaction if player is near fluid particles
		if (!isNearFluid)
		{
			compute.SetFloat("playerInteractionStrength", 0f);
			// Still set position for potential future interaction
			compute.SetVector("playerPosition", playerPos);
			compute.SetFloat("playerInteractionRadius", playerInteractionRadius);
			compute.SetVector("playerVelocity", playerVelocity);
			compute.SetFloat("velocityTransferStrength", 0f);
			compute.SetBool("isPlayerInMirroredState", isInMirroredState);
			return;
		}

		// Calculate interaction strength based on player state and movement
		float baseStrength = isInMirroredState ? mirroredInteractionStrength : playerDisplacementStrength;
		float movementFactor = playerVelocity.magnitude > 0.1f ? movementMultiplier : 1f;
		float finalStrength = baseStrength * movementFactor;

		// In mirrored state, attraction/repulsion behavior
		if (isInMirroredState)
		{
			// Attract particles when stationary, repel when moving
			finalStrength *= playerVelocity.magnitude > 1f ? -1f : 1f;
		}

		// Set compute shader parameters
		compute.SetVector("playerPosition", playerPos);
		compute.SetFloat("playerInteractionRadius", playerInteractionRadius);
		compute.SetFloat("playerInteractionStrength", finalStrength);
		compute.SetVector("playerVelocity", playerVelocity);
		compute.SetFloat("velocityTransferStrength", velocityTransferStrength);
		compute.SetBool("isPlayerInMirroredState", isInMirroredState);

		// Debug output
		if (Time.frameCount % 60 == 0) // Every second at 60fps
		{
			Debug.Log($"Player Fluid Interaction - Strength: {finalStrength:F2}, Near Fluid: {isNearFluid}, State: {(isInMirroredState ? "Mirrored" : "Normal")}");
		}
	}

	void SetInitialBufferData(Spawner2D.ParticleSpawnData spawnData)
	{
		positionBuffer.SetData(spawnData.positions);
		predictedPositionBuffer.SetData(spawnData.positions);
		velocityBuffer.SetData(spawnData.velocities);
	}

	void OnDestroy()
	{
		ComputeHelper.Release(positionBuffer, predictedPositionBuffer, velocityBuffer, densityBuffer, sortTarget_Position, sortTarget_Velocity, sortTarget_PredicitedPosition);
		spatialHash.Release();
		ComputeHelper.Release(_boundaryPointsBuffer);
	}

	void OnDrawGizmos()
	{
		// // Draw polygon boundary gizmo
		// if (boundaryComposite != null && boundaryComposite.points.Length > 1)
		// {
		// 	Gizmos.color = new Color(0, 1, 0, 0.7f);
		// 	Vector2[] localPoints = boundaryComposite.points;

		// 	for (int i = 0; i < localPoints.Length; i++)
		// 	{
		// 		Vector3 p1 = boundaryComposite.transform.TransformPoint(localPoints[i]);
		// 		Vector3 p2 = boundaryComposite.transform.TransformPoint(localPoints[(i + 1) % localPoints.Length]);
		// 		Gizmos.DrawLine(p1, p2);
		// 	}

		// 	// Draw collision margin
		// 	Gizmos.color = new Color(1, 1, 0, 0.3f);
		// 	for (int i = 0; i < localPoints.Length; i++)
		// 	{
		// 		Vector3 p1 = boundaryComposite.transform.TransformPoint(localPoints[i]);
		// 		Vector3 p2 = boundaryComposite.transform.TransformPoint(localPoints[(i + 1) % localPoints.Length]);

		// 		Vector3 segmentDir = (p2 - p1).normalized;
		// 		Vector3 normal = new Vector3(-segmentDir.y, segmentDir.x, 0) * boundaryCollisionMargin;

		// 		if (invertBoundary)
		// 			normal = -normal;

		// 		Gizmos.DrawLine(p1 + normal, p2 + normal);
		// 	}
		// }

		// 		// Draw player interaction radius
		// 		if (Application.isPlaying && player != null)
		// 		{
		// 			Vector3 playerPos = player.transform.position;

		// 			// Draw interaction radius with intensity based on settings
		// 			if (player.IsInMirroredState())
		// 			{
		// 				Gizmos.color = new Color(1, 0, 1, 0.3f); // Magenta for mirrored state
		// 			}
		// 			else
		// 			{
		// 				Gizmos.color = new Color(0, 1, 1, 0.3f); // Cyan for normal state
		// 			}

		// 			// Draw multiple rings to show interaction falloff
		// 			for (int i = 1; i <= 3; i++)
		// 			{
		// 				float alpha = 0.3f / i;
		// 				Color ringColor = Gizmos.color;
		// 				ringColor.a = alpha;
		// 				Gizmos.color = ringColor;
		// 				Gizmos.DrawWireSphere(playerPos, playerInteractionRadius * i * 0.4f);
		// 			}

		// 			// Draw buoyancy visualization
		// 			if (currentSubmersionDepth > 0f)
		// 			{
		// 				Gizmos.color = Color.blue;
		// 				Vector3 buoyancyViz = playerPos + Vector3.up * (currentBuoyancyForce * 0.1f);
		// 				Gizmos.DrawLine(playerPos, buoyancyViz);
		// 				Gizmos.DrawWireCube(buoyancyViz, Vector3.one * 0.2f);

		// 				// Draw submersion depth indicator
		// 				Gizmos.color = new Color(0, 0, 1, 0.3f);
		// 				Gizmos.DrawWireSphere(playerPos, currentSubmersionDepth);
		// 			}

		// 			// Draw velocity vector with magnitude indication
		// 			if (playerVelocity.magnitude > 0.1f)
		// 			{
		// 				Gizmos.color = Color.yellow;
		// 				Gizmos.DrawLine(playerPos, playerPos + (Vector3)playerVelocity);

		// 				// Draw interaction strength indicator
		// 				float strengthIndicator = GetCurrentPlayerInteractionStrength();
		// 				Gizmos.color = strengthIndicator > 0 ? Color.green : Color.red;
		// 				Gizmos.DrawWireCube(playerPos + Vector3.up * 0.5f, Vector3.one * 0.1f * Mathf.Abs(strengthIndicator));
		// 			}

		// 			// Debug text in scene view
		// #if UNITY_EDITOR
		// 			UnityEditor.Handles.Label(playerPos + Vector3.up * 1f,
		// 				$"Interaction: {GetCurrentPlayerInteractionStrength():F2}\n" +
		// 				$"Buoyancy: {currentBuoyancyForce:F2}\n" +
		// 				$"Depth: {currentSubmersionDepth:F2}\n" +
		// 				$"Velocity: {playerVelocity.magnitude:F1}\n" +
		// 				$"State: {(player.IsInMirroredState() ? "Mirrored" : "Normal")}");
		// #endif
		// 		}

		// Draw mouse interaction
		if (Application.isPlaying)
		{
			Vector2 mousePos = Camera.main.ScreenToWorldPoint(Input.mousePosition);
			bool isPullInteraction = Input.GetMouseButton(0);
			bool isPushInteraction = Input.GetMouseButton(1);
			bool isInteracting = isPullInteraction || isPushInteraction;

			if (isInteracting)
			{
				Gizmos.color = isPullInteraction ? Color.green : Color.red;
				Gizmos.DrawWireSphere(mousePos, interactionRadius);
			}
		}
	}

	private float GetCurrentPlayerInteractionStrength()
	{
		if (player == null) return 0f;

		bool isInMirroredState = player.IsInMirroredState();
		float baseStrength = isInMirroredState ? mirroredInteractionStrength : playerDisplacementStrength;
		float movementFactor = playerVelocity.magnitude > 0.1f ? movementMultiplier : 1f;
		float finalStrength = baseStrength * movementFactor;

		if (isInMirroredState)
		{
			finalStrength *= playerVelocity.magnitude > 1f ? -1f : 1f;
		}

		return finalStrength;
	}

	// method to check if player is actually inside the polygon
	private bool IsPlayerInsidePolygon()
	{
		// Fallback to particle-based detection if polygon not available
		if (useParticleBasedDetection || boundaryComposite == null)
		{
			return IsPlayerNearFluidParticles();
		}

		if (player == null) return false;

		Vector2 playerPos = player.transform.position;
		return boundaryComposite.OverlapPoint(playerPos);
	}

float CalculateImprovedParticleBasedSubmersion()
{
    if (!particlePositionsCached || cachedParticlePositions == null) return 0f;

    Vector2 playerPos = player.transform.position;
    
    // Use a more conservative radius for submersion calculation
    float submersionRadius = Mathf.Min(maxBuoyancyDepth, playerInteractionRadius * 1.2f);
    float submersionRadiusSqr = submersionRadius * submersionRadius;

    // Collect nearby particles with their weights
    var nearbyParticles = new List<(Vector2 pos, float weight, float distance)>();

    for (int i = 0; i < cachedParticlePositions.Length; i++)
    {
        Vector2 particlePos = cachedParticlePositions[i];
        float distSqr = (playerPos - particlePos).sqrMagnitude;

        if (distSqr <= submersionRadiusSqr)
        {
            float dist = Mathf.Sqrt(distSqr);
            // Use smoother falloff curve
            float normalizedDist = dist / submersionRadius;
            float weight = Mathf.Pow(1f - normalizedDist, 2f); // Quadratic falloff
            
            nearbyParticles.Add((particlePos, weight, dist));
        }
    }

    // Limit the number of particles to prevent performance issues and edge effects
    if (nearbyParticles.Count > maxParticlesForSubmersion)
    {
        nearbyParticles.Sort((a, b) => a.distance.CompareTo(b.distance));
        nearbyParticles = nearbyParticles.GetRange(0, maxParticlesForSubmersion);
    }

    if (nearbyParticles.Count == 0) return 0f;

    // Calculate weighted average submersion with better particle distribution consideration
    float totalWeight = 0f;
    float weightedDepth = 0f;
    float particleDensity = 0f;

    foreach (var particle in nearbyParticles)
    {
        totalWeight += particle.weight;
        // Calculate depth based on how "surrounded" the player is by particles
        float depth = Mathf.Max(0f, submersionRadius - particle.distance);
        weightedDepth += particle.weight * depth;
        particleDensity += particle.weight;
    }

    if (totalWeight <= 0f) return 0f;

    float averageDepth = weightedDepth / totalWeight;
    
    // Normalize based on particle density - need minimum density for full buoyancy
    float requiredDensityForFullBuoyancy = minParticlesForFluidDetection * 0.8f;
    float densityFactor = Mathf.Clamp01(particleDensity / requiredDensityForFullBuoyancy);
    
    // Apply a more conservative scaling to prevent over-buoyancy
    float finalSubmersion = averageDepth * densityFactor * 0.7f; // Scale down by 30%
    
    return Mathf.Clamp(finalSubmersion, 0f, maxBuoyancyDepth);
}

	public bool IsPlayerInFluid()
	{
		return useParticleBasedDetection ? IsPlayerNearFluidParticles() : IsPlayerInsidePolygon();
	}

	public float GetPlayerSubmersionDepth()
	{
		return currentSubmersionDepth;
	}

	public int GetNearbyParticleCount()
	{
		if (!IsPlayerNearFluidParticles()) return 0;

		Vector2 playerPos = player.transform.position;
		int count = 0;
		float radiusSqr = fluidDetectionRadius * fluidDetectionRadius;

		for (int i = 0; i < cachedParticlePositions.Length; i++)
		{
			if ((playerPos - cachedParticlePositions[i]).sqrMagnitude <= radiusSqr)
				count++;
		}

		return count;
	}

}