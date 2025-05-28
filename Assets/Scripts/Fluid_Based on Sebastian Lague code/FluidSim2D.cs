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

	[Header("Boundary Settings (Polygon)")]
	public PolygonCollider2D boundaryCollider;
	[Tooltip("The margin within which particles will react to the boundary.")]
	public float boundaryCollisionMargin = 0.2f;
	public bool invertBoundary = false;
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

		// Create buffers
		positionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		predictedPositionBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		velocityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		densityBuffer = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

		sortTarget_Position = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		sortTarget_PredicitedPosition = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);
		sortTarget_Velocity = ComputeHelper.CreateStructuredBuffer<float2>(numParticles);

		// Initialize polygon boundary buffers
		LoadBoundaryPolygonPoints();
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

		// Set polygon boundary buffers for the compute shader
		if (_boundaryPointsBuffer != null)
		{
			ComputeHelper.SetBuffer(compute, _boundaryPointsBuffer, "_BoundaryPoints", polygonCollisionKernel);
			compute.SetInt("_NumBoundaryPoints", _numBoundaryPoints);
			compute.SetFloat("_BoundaryCollisionMargin", boundaryCollisionMargin);
			compute.SetBool("_InvertBoundary", invertBoundary);
			compute.SetFloat("_CollisionResponseStrength", collisionResponseStrength);
		}
		else
		{
			Debug.LogWarning("No PolygonCollider2D assigned or points could not be loaded for fluid boundary.");
		}

		compute.SetInt("numParticles", numParticles);
	}

	void LoadBoundaryPolygonPoints()
	{
		if (boundaryCollider == null)
		{
			Debug.LogWarning("Boundary Collider (PolygonCollider2D) is not assigned in FluidSim2D.");
			_numBoundaryPoints = 0;
			ComputeHelper.Release(_boundaryPointsBuffer);
			return;
		}

		Vector2[] colliderPoints = boundaryCollider.points;
		List<float2> worldPoints = new List<float2>();

		for (int i = 0; i < colliderPoints.Length; i++)
		{
			Vector3 worldPoint = boundaryCollider.transform.TransformPoint((Vector3)colliderPoints[i]);
			worldPoints.Add(new float2(worldPoint.x, worldPoint.y));
		}

		_numBoundaryPoints = worldPoints.Count;
		ComputeHelper.CreateStructuredBuffer<float2>(ref _boundaryPointsBuffer, _numBoundaryPoints);

		if (_numBoundaryPoints > 0)
		{
			_boundaryPointsBuffer.SetData(worldPoints.ToArray());
		}

		if (compute != null && _boundaryPointsBuffer != null)
		{
			ComputeHelper.SetBuffer(compute, _boundaryPointsBuffer, "_BoundaryPoints", polygonCollisionKernel);
			compute.SetInt("_NumBoundaryPoints", _numBoundaryPoints);
			compute.SetFloat("_BoundaryCollisionMargin", boundaryCollisionMargin);
			compute.SetBool("_InvertBoundary", invertBoundary);
			compute.SetFloat("_CollisionResponseStrength", collisionResponseStrength);
			Debug.Log($"Loaded {_numBoundaryPoints} boundary points from {boundaryCollider.name}");
		}
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
		if (boundaryCollider == null || player == null)
		{
			currentSubmersionDepth = 0f;
			return;
		}

		Vector2 playerPos = player.transform.position;
		bool isInsidePolygon = IsPlayerInsidePolygon();

		if (!isInsidePolygon)
		{
			currentSubmersionDepth = 0f;
			return;
		}

		// Calculate distance to closest boundary edge
		float minDistToBoundary = float.MaxValue;
		Vector2[] points = boundaryCollider.points;

		for (int i = 0; i < points.Length; i++)
		{
			Vector2 p1 = boundaryCollider.transform.TransformPoint(points[i]);
			Vector2 p2 = boundaryCollider.transform.TransformPoint(points[(i + 1) % points.Length]);

			float distToSegment = DistanceToLineSegment(playerPos, p1, p2);
			minDistToBoundary = Mathf.Min(minDistToBoundary, distToSegment);
		}

		// Submersion depth is how far inside the boundary the player is
		currentSubmersionDepth = Mathf.Clamp(minDistToBoundary, 0f, maxBuoyancyDepth);
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

	void ApplyFluidForcesToPlayer()
	{
		if (currentSubmersionDepth <= 0f)
		{
			currentBuoyancyForce = Mathf.Lerp(currentBuoyancyForce, 0f, Time.fixedDeltaTime * buoyancyResponsiveness);
			return;
		}

		// Calculate buoyancy force based on submersion depth
		float depthRatio = currentSubmersionDepth / maxBuoyancyDepth;
		targetBuoyancyForce = buoyancyStrength * depthRatio;

		// Apply multiplier for mirrored state
		if (player.IsInMirroredState())
		{
			targetBuoyancyForce *= mirroredBuoyancyMultiplier;
		}

		// Smooth the buoyancy force for stability
		currentBuoyancyForce = Mathf.Lerp(currentBuoyancyForce, targetBuoyancyForce, Time.fixedDeltaTime * buoyancyResponsiveness);

		// Apply upward buoyancy force
		Vector2 buoyancyForce = Vector2.up * currentBuoyancyForce;
		playerRigidbody.AddForce(buoyancyForce, ForceMode2D.Force);

		// Apply fluid drag based on velocity
		Vector2 dragForce = -playerVelocity * fluidDrag * depthRatio;
		playerRigidbody.AddForce(dragForce, ForceMode2D.Force);

		// Optional: Add some turbulence for more dynamic feel
		if (playerVelocity.magnitude > 1f)
		{
			float turbulence = Mathf.Sin(Time.time * 10f) * 0.5f;
			Vector2 turbulenceForce = Vector2.right * turbulence * depthRatio;
			playerRigidbody.AddForce(turbulenceForce, ForceMode2D.Force);
		}

		// Debug info
		if (Time.fixedTime % 1f < Time.fixedDeltaTime) // Every second
		{
			Debug.Log($"Buoyancy - Depth: {currentSubmersionDepth:F2}, Force: {currentBuoyancyForce:F2}, Inside: {IsPlayerInsidePolygon()}");
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
		if (_boundaryPointsBuffer != null && _numBoundaryPoints > 1)
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
		if (_boundaryPointsBuffer != null)
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
		bool isInsidePolygon = IsPlayerInsidePolygon();

		// Only apply interaction if player is inside the fluid boundary
		if (!isInsidePolygon)
		{
			compute.SetFloat("playerInteractionStrength", 0f);
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

		// Boost interaction strength when inside polygon
		finalStrength *= 2f;

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
			Debug.Log($"Player Fluid Interaction - Strength: {finalStrength:F2}, Inside: {isInsidePolygon}, State: {(isInMirroredState ? "Mirrored" : "Normal")}");
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
		// if (boundaryCollider != null && boundaryCollider.points.Length > 1)
		// {
		// 	Gizmos.color = new Color(0, 1, 0, 0.7f);
		// 	Vector2[] localPoints = boundaryCollider.points;

		// 	for (int i = 0; i < localPoints.Length; i++)
		// 	{
		// 		Vector3 p1 = boundaryCollider.transform.TransformPoint(localPoints[i]);
		// 		Vector3 p2 = boundaryCollider.transform.TransformPoint(localPoints[(i + 1) % localPoints.Length]);
		// 		Gizmos.DrawLine(p1, p2);
		// 	}

		// 	// Draw collision margin
		// 	Gizmos.color = new Color(1, 1, 0, 0.3f);
		// 	for (int i = 0; i < localPoints.Length; i++)
		// 	{
		// 		Vector3 p1 = boundaryCollider.transform.TransformPoint(localPoints[i]);
		// 		Vector3 p2 = boundaryCollider.transform.TransformPoint(localPoints[(i + 1) % localPoints.Length]);

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
		if (boundaryCollider == null || player == null) return false;

		Vector2 playerPos = player.transform.position;
		return boundaryCollider.OverlapPoint(playerPos);
	}
}