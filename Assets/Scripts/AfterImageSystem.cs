using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public class AfterImageSystem : MonoBehaviour
{
    [Header("After-Image Settings")]
    [SerializeField] private int maxAfterImages = 6;
    [SerializeField] private float afterImageSpawnRate = 0.12f;
    [SerializeField] private float afterImageLifetime = 0.8f;
    [SerializeField] private AnimationCurve fadeOutCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    
    [Header("Cyberpunk Colors")]
    [SerializeField] private Color[] afterImageColors = {
        new Color(0f, 0.8f, 1f, 0.8f),     // Cyan
        new Color(1f, 0.2f, 0.6f, 0.7f),   // Hot Pink/Magenta
        new Color(0.2f, 1f, 0.4f, 0.6f),   // Neon Green
        new Color(1f, 0.4f, 0.1f, 0.6f),   // Orange
        new Color(0.6f, 0.2f, 1f, 0.5f),   // Purple
        new Color(1f, 1f, 0.2f, 0.4f)      // Yellow
    };
    
    [Header("Movement Threshold")]
    [SerializeField] private float minimumMovementDistance = 0.15f;
    [SerializeField] private bool onlyCreateOnMovement = true;
    [SerializeField] private float highSpeedThreshold = 5f;
    
    [Header("Visual Settings")]
    [SerializeField] private string afterImageSortingLayer = "Effects";
    [SerializeField] private int afterImageSortingOrder = 45;
    [SerializeField] private bool useAdditiveBlending = true;
    [SerializeField] private Material additiveSpriteMaterial;
    
    // Runtime components
    private SpriteRenderer playerSpriteRenderer;
    private Rigidbody2D playerRigidbody;
    private SandavistanSystem sandavistanSystem;
    
    // After-image tracking
    private Queue<AfterImageInstance> activeAfterImages = new Queue<AfterImageInstance>();
    private Queue<GameObject> afterImagePool = new Queue<GameObject>();
    
    // Position tracking
    private Vector3 lastAfterImagePosition;
    private Coroutine afterImageCoroutine;
    private Transform afterImageParent;
    
    // Performance
    private int colorIndex = 0;
    private float currentMovementSpeed;
    
    [System.Serializable]
    private class AfterImageInstance
    {
        public GameObject gameObject;
        public SpriteRenderer spriteRenderer;
        public Coroutine fadeCoroutine;
        public float spawnTime;
        public Vector3 originalScale;
        public Color originalColor;
    }

    void Awake()
    {
        playerSpriteRenderer = GetComponent<SpriteRenderer>();
        playerRigidbody = GetComponent<Rigidbody2D>();
        
        if (playerSpriteRenderer == null)
        {
            Debug.LogError("CyberpunkAfterImageSystem: No SpriteRenderer found on player!");
            enabled = false;
            return;
        }
        
        InitializeSystem();
    }

    private void InitializeSystem()
    {
        // Create parent container
        GameObject parentObj = new GameObject("CyberpunkAfterImages");
        afterImageParent = parentObj.transform;
        
        // Find Sandavistan system
        sandavistanSystem = FindObjectOfType<SandavistanSystem>();
        
        // Create additive material if not provided
        if (useAdditiveBlending && additiveSpriteMaterial == null)
        {
            CreateAdditiveMaterial();
        }
        
        // Initialize object pool
        InitializeAfterImagePool();
        
        lastAfterImagePosition = transform.position;
    }

    private void CreateAdditiveMaterial()
    {
        additiveSpriteMaterial = new Material(Shader.Find("Sprites/Default"));
        additiveSpriteMaterial.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
        additiveSpriteMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        additiveSpriteMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        additiveSpriteMaterial.name = "CyberpunkAfterImage_Additive";
    }

    private void InitializeAfterImagePool()
    {
        // Pre-create after-image objects for performance
        for (int i = 0; i < maxAfterImages * 2; i++)
        {
            GameObject afterImageObj = CreateAfterImageObject();
            afterImageObj.SetActive(false);
            afterImagePool.Enqueue(afterImageObj);
        }
    }

    private GameObject CreateAfterImageObject()
    {
        GameObject obj = new GameObject("CyberpunkAfterImage");
        obj.transform.SetParent(afterImageParent);
        
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = afterImageSortingLayer;
        sr.sortingOrder = afterImageSortingOrder;
        
        // Apply additive material for glow effect
        if (useAdditiveBlending && additiveSpriteMaterial != null)
        {
            sr.material = additiveSpriteMaterial;
        }
        
        return obj;
    }

    void Start()
    {
        // Subscribe to Sandavistan events
        if (sandavistanSystem != null)
        {
            SandavistanSystem.OnSandavistanActivated += StartAfterImages;
            SandavistanSystem.OnSandavistanDeactivated += StopAfterImages;
        }
    }

    void Update()
    {
        // Track movement speed for dynamic effects
        if (playerRigidbody != null)
        {
            currentMovementSpeed = playerRigidbody.velocity.magnitude;
        }
        else
        {
            currentMovementSpeed = (transform.position - lastAfterImagePosition).magnitude / Time.unscaledDeltaTime;
        }
    }

    private void StartAfterImages()
    {
        Debug.Log("Starting Cyberpunk after-image system");
        
        if (afterImageCoroutine != null)
        {
            StopCoroutine(afterImageCoroutine);
        }
        
        afterImageCoroutine = StartCoroutine(AfterImageCreationLoop());
        lastAfterImagePosition = transform.position;
        colorIndex = 0; // Reset color cycle
    }

    private void StopAfterImages()
    {
        Debug.Log("Stopping Cyberpunk after-image system");
        
        if (afterImageCoroutine != null)
        {
            StopCoroutine(afterImageCoroutine);
            afterImageCoroutine = null;
        }
        
        // Let existing after-images fade naturally
    }

    private IEnumerator AfterImageCreationLoop()
    {
        while (sandavistanSystem != null && sandavistanSystem.IsActive())
        {
            bool shouldCreate = true;
            
            // Check movement requirement
            if (onlyCreateOnMovement)
            {
                float distanceMoved = Vector3.Distance(transform.position, lastAfterImagePosition);
                shouldCreate = distanceMoved >= minimumMovementDistance;
            }
            
            if (shouldCreate)
            {
                CreateAfterImage();
                lastAfterImagePosition = transform.position;
            }
            
            // Maintain maximum count
            while (activeAfterImages.Count > maxAfterImages)
            {
                RemoveOldestAfterImage();
            }
            
            // Dynamic spawn rate based on speed
            float dynamicSpawnRate = afterImageSpawnRate;
            if (currentMovementSpeed > highSpeedThreshold)
            {
                dynamicSpawnRate *= 0.6f; // Spawn faster when moving quickly
            }
            
            yield return new WaitForSecondsRealtime(dynamicSpawnRate);
        }
    }

    private void CreateAfterImage()
    {
        GameObject afterImageObj = GetPooledAfterImage();
        if (afterImageObj == null) return;
        
        SpriteRenderer afterImageSR = afterImageObj.GetComponent<SpriteRenderer>();
        
        // Copy current player state EXACTLY
        afterImageObj.transform.position = transform.position;
        afterImageObj.transform.rotation = transform.rotation;
        afterImageObj.transform.localScale = transform.localScale;
        
        // Copy sprite properties exactly
        afterImageSR.sprite = playerSpriteRenderer.sprite;
        afterImageSR.flipX = playerSpriteRenderer.flipX;
        afterImageSR.flipY = playerSpriteRenderer.flipY;
        
        // Apply cyberpunk color
        Color currentColor = GetNextAfterImageColor();
        afterImageSR.color = currentColor;
        
        afterImageObj.SetActive(true);
        
        // Create after-image instance
        AfterImageInstance afterImage = new AfterImageInstance
        {
            gameObject = afterImageObj,
            spriteRenderer = afterImageSR,
            spawnTime = Time.unscaledTime,
            originalScale = afterImageObj.transform.localScale,
            originalColor = currentColor
        };
        
        // Start fade effect
        afterImage.fadeCoroutine = StartCoroutine(FadeAfterImage(afterImage));
        
        activeAfterImages.Enqueue(afterImage);
    }

    private Color GetNextAfterImageColor()
    {
        Color color = afterImageColors[colorIndex];
        
        // Cycle through colors
        colorIndex = (colorIndex + 1) % afterImageColors.Length;
        
        // Add some variation based on speed
        if (currentMovementSpeed > highSpeedThreshold)
        {
            color.a *= 1.2f; // More visible when moving fast
            color.r *= 1.1f;
            color.g *= 1.1f;
            color.b *= 1.1f;
        }
        
        return color;
    }

    private IEnumerator FadeAfterImage(AfterImageInstance afterImage)
    {
        float elapsed = 0f;
        Color startColor = afterImage.originalColor;
        Vector3 startScale = afterImage.originalScale;
        
        while (elapsed < afterImageLifetime)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / afterImageLifetime;
            float curveValue = fadeOutCurve.Evaluate(progress);
            
            // Fade out color
            Color currentColor = startColor;
            currentColor.a = startColor.a * (1f - curveValue);
            afterImage.spriteRenderer.color = currentColor;
            
            // Slight scale reduction for extra effect
            float scaleMultiplier = Mathf.Lerp(1f, 0.85f, curveValue);
            afterImage.gameObject.transform.localScale = startScale * scaleMultiplier;
            
            yield return null;
        }
        
        // Return to pool
        ReturnAfterImageToPool(afterImage.gameObject);
    }

    private GameObject GetPooledAfterImage()
    {
        if (afterImagePool.Count > 0)
        {
            return afterImagePool.Dequeue();
        }
        else
        {
            return CreateAfterImageObject();
        }
    }

    private void ReturnAfterImageToPool(GameObject obj)
    {
        obj.SetActive(false);
        afterImagePool.Enqueue(obj);
    }

    private void RemoveOldestAfterImage()
    {
        if (activeAfterImages.Count == 0) return;
        
        AfterImageInstance oldest = activeAfterImages.Dequeue();
        
        if (oldest.fadeCoroutine != null)
        {
            StopCoroutine(oldest.fadeCoroutine);
        }
        
        ReturnAfterImageToPool(oldest.gameObject);
    }

    // Public API for special effects
    public void CreateInstantAfterImageBurst(int count = 3)
    {
        if (sandavistanSystem == null || !sandavistanSystem.IsActive()) return;
        
        StartCoroutine(CreateAfterImageBurst(count));
    }

    private IEnumerator CreateAfterImageBurst(int count)
    {
        for (int i = 0; i < count; i++)
        {
            CreateAfterImage();
            yield return new WaitForSecondsRealtime(0.05f);
        }
    }

    public void SetCustomColors(Color[] newColors)
    {
        if (newColors != null && newColors.Length > 0)
        {
            afterImageColors = newColors;
            colorIndex = 0;
        }
    }

    public void SetAfterImageSettings(float spawnRate, float lifetime, int maxCount)
    {
        afterImageSpawnRate = spawnRate;
        afterImageLifetime = lifetime;
        maxAfterImages = maxCount;
    }

    // Cleanup
    void OnDestroy()
    {
        if (sandavistanSystem != null)
        {
            SandavistanSystem.OnSandavistanActivated -= StartAfterImages;
            SandavistanSystem.OnSandavistanDeactivated -= StopAfterImages;
        }
        
        StopAllCoroutines();
        
        // Clear active after-images
        while (activeAfterImages.Count > 0)
        {
            RemoveOldestAfterImage();
        }
        
        if (afterImageParent != null)
        {
            Destroy(afterImageParent.gameObject);
        }
    }

    // Debug visualization
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, minimumMovementDistance);
        
        if (afterImageColors != null)
        {
            for (int i = 0; i < afterImageColors.Length; i++)
            {
                Gizmos.color = afterImageColors[i];
                Vector3 pos = transform.position + Vector3.right * (i * 0.3f);
                Gizmos.DrawSphere(pos, 0.1f);
            }
        }
    }
}