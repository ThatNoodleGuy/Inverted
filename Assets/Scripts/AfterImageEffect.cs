using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AfterImageEffect : MonoBehaviour
{
    [Header("Cyberpunk Afterimage Settings")]
    [SerializeField] private GameObject afterImagePrefab;
    [SerializeField] private float spawnRate = 0.08f;
    [SerializeField] private float fadeTime = 1.2f;
    [SerializeField] private int poolSize = 20; // More for multi-colored effect
    [SerializeField] private AnimationCurve fadeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    
    [Header("Multi-Color Configuration")]
    [SerializeField] private bool enableMultiColor = true;
    [SerializeField] private int colorCount = 6; // Number of different colored afterimages per spawn
    [SerializeField] private float colorSpacing = 0.05f; // Time between each colored afterimage
    [SerializeField] private Color[] cyberpunkColors = new Color[]
    {
        new Color(1f, 0f, 0.5f, 0.6f), // Hot Pink/Magenta
        new Color(0f, 1f, 1f, 0.6f),   // Cyan
        new Color(1f, 0.5f, 0f, 0.6f), // Orange
        new Color(0.5f, 0f, 1f, 0.6f), // Purple
        new Color(1f, 1f, 0f, 0.6f),   // Yellow
        new Color(0f, 1f, 0.5f, 0.6f), // Green-Cyan
        new Color(1f, 0f, 0f, 0.6f),   // Pure Red
        new Color(0f, 0.5f, 1f, 0.6f)  // Blue
    };
    
    [Header("Chromatic Aberration Effect")]
    [SerializeField] private bool enableChromaticAberration = true;
    [SerializeField] private float aberrationOffset = 0.1f;
    [SerializeField] private Vector2[] aberrationOffsets = new Vector2[]
    {
        new Vector2(0f, 0f),        // Center (no offset)
        new Vector2(0.05f, 0f),     // Right offset
        new Vector2(-0.05f, 0f),    // Left offset  
        new Vector2(0f, 0.05f),     // Up offset
        new Vector2(0f, -0.05f),    // Down offset
        new Vector2(0.035f, 0.035f) // Diagonal offset
    };
    
    [Header("Glitch Effects")]
    [SerializeField] private bool enableGlitch = true;
    [SerializeField] private float glitchChance = 0.3f;
    [SerializeField] private float glitchIntensity = 0.15f;
    [SerializeField] private bool enableScaleGlitch = true;
    [SerializeField] private Vector2 scaleGlitchRange = new Vector2(0.8f, 1.3f);
    
    [Header("Activation")]
    [SerializeField] private bool activeOnStart = false;
    [SerializeField] private KeyCode activationKey = KeyCode.LeftShift;
    [SerializeField] private bool requireMovement = true;
    [SerializeField] private float minimumVelocity = 0.1f;
    
    [Header("References")]
    [SerializeField] private Transform player;
    [SerializeField] private SpriteRenderer playerRenderer;
    [SerializeField] private PlayerManager playerManager;
    
    // Private variables
    private Queue<CyberpunkAfterImage> afterImagePool;
    private List<CyberpunkAfterImage> activeAfterImages;
    private float lastSpawnTime;
    private bool isEffectActive;
    private Coroutine multiColorSpawnCoroutine;
    
    void Start()
    {
        InitializePool();
        isEffectActive = activeOnStart;
        
        // Auto-find references if not assigned
        if (player == null)
            player = transform;
            
        if (playerRenderer == null)
            playerRenderer = GetComponent<SpriteRenderer>();
            
        if (playerManager == null)
            playerManager = GetComponent<PlayerManager>();
    }
    
    void Update()
    {
        HandleInput();
        
        if (isEffectActive)
        {
            UpdateAfterImages();
        }
    }
    
    private void HandleInput()
    {
        if (Input.GetKeyDown(activationKey))
        {
            ToggleEffect();
        }
    }
    
    private void InitializePool()
    {
        afterImagePool = new Queue<CyberpunkAfterImage>();
        activeAfterImages = new List<CyberpunkAfterImage>();
        
        // Create afterimage prefab if not assigned
        if (afterImagePrefab == null)
        {
            CreateAfterImagePrefab();
        }
        
        // Create pool of afterimages
        for (int i = 0; i < poolSize; i++)
        {
            GameObject afterImageObj = Instantiate(afterImagePrefab, transform.parent);
            CyberpunkAfterImage afterImage = new CyberpunkAfterImage
            {
                gameObject = afterImageObj,
                spriteRenderer = afterImageObj.GetComponent<SpriteRenderer>(),
                isActive = false,
                originalScale = Vector3.one
            };
            
            afterImageObj.SetActive(false);
            afterImagePool.Enqueue(afterImage);
        }
    }
    
    private void CreateAfterImagePrefab()
    {
        afterImagePrefab = new GameObject("CyberpunkAfterImage");
        SpriteRenderer sr = afterImagePrefab.AddComponent<SpriteRenderer>();
        
        if (playerRenderer != null)
        {
            sr.sprite = playerRenderer.sprite;
            sr.sortingLayerName = playerRenderer.sortingLayerName;
            sr.sortingOrder = playerRenderer.sortingOrder - 1;
        }
        
        afterImagePrefab.SetActive(false);
    }
    
    private void UpdateAfterImages()
    {
        if (ShouldSpawnAfterImage())
        {
            if (enableMultiColor)
            {
                SpawnMultiColorAfterImages();
            }
            else
            {
                SpawnSingleAfterImage(cyberpunkColors[0]);
            }
        }
    }
    
    private bool ShouldSpawnAfterImage()
    {
        if (Time.time - lastSpawnTime < spawnRate)
            return false;
            
        if (requireMovement && playerManager != null)
        {
            float currentVelocity = playerManager.GetVelocity().magnitude;
            if (currentVelocity < minimumVelocity)
                return false;
        }
        
        return true;
    }
    
    private void SpawnMultiColorAfterImages()
    {
        if (multiColorSpawnCoroutine != null)
        {
            StopCoroutine(multiColorSpawnCoroutine);
        }
        
        multiColorSpawnCoroutine = StartCoroutine(SpawnMultiColorSequence());
        lastSpawnTime = Time.time;
    }
    
    private IEnumerator SpawnMultiColorSequence()
    {
        Vector3 spawnPosition = player.position;
        Quaternion spawnRotation = player.rotation;
        bool spawnFlipX = playerRenderer.flipX;
        
        for (int i = 0; i < Mathf.Min(colorCount, cyberpunkColors.Length); i++)
        {
            Color currentColor = cyberpunkColors[i % cyberpunkColors.Length];
            
            // Apply chromatic aberration offset
            Vector3 finalPosition = spawnPosition;
            if (enableChromaticAberration && i < aberrationOffsets.Length)
            {
                Vector2 offset = aberrationOffsets[i] * aberrationOffset;
                finalPosition += new Vector3(offset.x, offset.y, 0);
            }
            
            SpawnSingleAfterImageAt(currentColor, finalPosition, spawnRotation, spawnFlipX);
            
            // Small delay between each color
            yield return new WaitForSeconds(colorSpacing);
        }
    }
    
    private void SpawnSingleAfterImage(Color color)
    {
        SpawnSingleAfterImageAt(color, player.position, player.rotation, playerRenderer.flipX);
    }
    
    private void SpawnSingleAfterImageAt(Color color, Vector3 position, Quaternion rotation, bool flipX)
    {
        if (afterImagePool.Count == 0)
        {
            RecycleOldestAfterImage();
        }
        
        CyberpunkAfterImage afterImage = afterImagePool.Dequeue();
        SetupCyberpunkAfterImage(afterImage, color, position, rotation, flipX);
        activeAfterImages.Add(afterImage);
    }
    
    private void SetupCyberpunkAfterImage(CyberpunkAfterImage afterImage, Color color, Vector3 position, Quaternion rotation, bool flipX)
    {
        // Basic setup
        afterImage.gameObject.transform.position = position;
        afterImage.gameObject.transform.rotation = rotation;
        
        // Sprite settings
        if (playerRenderer != null)
        {
            afterImage.spriteRenderer.sprite = playerRenderer.sprite;
            afterImage.spriteRenderer.flipX = flipX;
        }
        
        // Apply glitch effects
        ApplyGlitchEffects(afterImage, position);
        
        // Set color
        afterImage.spriteRenderer.color = color;
        afterImage.originalColor = color;
        
        // Activate
        afterImage.gameObject.SetActive(true);
        afterImage.isActive = true;
        afterImage.spawnTime = Time.time;
        
        // Start fade routine
        StartCoroutine(FadeCyberpunkAfterImage(afterImage));
    }
    
    private void ApplyGlitchEffects(CyberpunkAfterImage afterImage, Vector3 basePosition)
    {
        if (!enableGlitch || Random.Range(0f, 1f) > glitchChance)
            return;
            
        // Position glitch
        Vector3 glitchOffset = new Vector3(
            Random.Range(-glitchIntensity, glitchIntensity),
            Random.Range(-glitchIntensity, glitchIntensity),
            0
        );
        afterImage.gameObject.transform.position = basePosition + glitchOffset;
        
        // Scale glitch
        if (enableScaleGlitch)
        {
            float randomScale = Random.Range(scaleGlitchRange.x, scaleGlitchRange.y);
            afterImage.gameObject.transform.localScale = afterImage.originalScale * randomScale;
        }
        
        // Color glitch - slight color shift
        Color glitchedColor = afterImage.spriteRenderer.color;
        glitchedColor.r = Mathf.Clamp01(glitchedColor.r + Random.Range(-0.2f, 0.2f));
        glitchedColor.g = Mathf.Clamp01(glitchedColor.g + Random.Range(-0.2f, 0.2f));
        glitchedColor.b = Mathf.Clamp01(glitchedColor.b + Random.Range(-0.2f, 0.2f));
        afterImage.originalColor = glitchedColor;
        afterImage.spriteRenderer.color = glitchedColor;
    }
    
    private IEnumerator FadeCyberpunkAfterImage(CyberpunkAfterImage afterImage)
    {
        Color startColor = afterImage.originalColor;
        float elapsed = 0f;
        
        while (elapsed < fadeTime && afterImage.isActive)
        {
            elapsed += Time.deltaTime;
            float progress = elapsed / fadeTime;
            
            // Use curve for smooth fading
            float alpha = fadeCurve.Evaluate(progress) * startColor.a;
            Color currentColor = startColor;
            currentColor.a = alpha;
            
            afterImage.spriteRenderer.color = currentColor;
            
            // Optional: slight position drift for more dynamic effect
            if (Random.Range(0f, 1f) < 0.1f) // 10% chance per frame
            {
                Vector3 drift = new Vector3(
                    Random.Range(-0.02f, 0.02f),
                    Random.Range(-0.02f, 0.02f),
                    0
                );
                afterImage.gameObject.transform.position += drift;
            }
            
            yield return null;
        }
        
        if (afterImage.isActive)
        {
            RecycleAfterImage(afterImage);
        }
    }
    
    private void RecycleAfterImage(CyberpunkAfterImage afterImage)
    {
        afterImage.isActive = false;
        afterImage.gameObject.SetActive(false);
        afterImage.gameObject.transform.localScale = afterImage.originalScale; // Reset scale
        activeAfterImages.Remove(afterImage);
        afterImagePool.Enqueue(afterImage);
    }
    
    private void RecycleOldestAfterImage()
    {
        if (activeAfterImages.Count > 0)
        {
            CyberpunkAfterImage oldest = activeAfterImages[0];
            RecycleAfterImage(oldest);
        }
    }
    
    // Public methods
    public void ActivateEffect()
    {
        isEffectActive = true;
        Debug.Log("Cyberpunk afterimage effect activated");
    }
    
    public void DeactivateEffect()
    {
        isEffectActive = false;
        
        if (multiColorSpawnCoroutine != null)
        {
            StopCoroutine(multiColorSpawnCoroutine);
            multiColorSpawnCoroutine = null;
        }
        
        while (activeAfterImages.Count > 0)
        {
            RecycleAfterImage(activeAfterImages[0]);
        }
        
        Debug.Log("Cyberpunk afterimage effect deactivated");
    }
    
    public void ToggleEffect()
    {
        if (isEffectActive)
            DeactivateEffect();
        else
            ActivateEffect();
    }
    
    public bool IsEffectActive()
    {
        return isEffectActive;
    }
    
    public void ActivateForDuration(float duration)
    {
        StopCoroutine(nameof(DeactivateAfterDelay));
        ActivateEffect();
        StartCoroutine(DeactivateAfterDelay(duration));
    }
    
    private IEnumerator DeactivateAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        DeactivateEffect();
    }
    
    // Burst effect for special moments
    public void CreateCyberpunkBurst(int burstCount = 8, float burstRadius = 1.5f)
    {
        StartCoroutine(CyberpunkBurstCoroutine(burstCount, burstRadius));
    }
    
    private IEnumerator CyberpunkBurstCoroutine(int burstCount, float burstRadius)
    {
        Vector3 centerPos = player.position;
        
        for (int i = 0; i < burstCount; i++)
        {
            // Create multiple colored afterimages in a burst pattern
            float angle = (360f / burstCount) * i;
            Vector2 direction = new Vector2(
                Mathf.Cos(angle * Mathf.Deg2Rad),
                Mathf.Sin(angle * Mathf.Deg2Rad)
            );
            
            Vector3 burstPosition = centerPos + (Vector3)(direction * burstRadius * Random.Range(0.3f, 1f));
            Color burstColor = cyberpunkColors[i % cyberpunkColors.Length];
            
            SpawnSingleAfterImageAt(burstColor, burstPosition, player.rotation, playerRenderer.flipX);
            
            yield return new WaitForSeconds(0.02f);
        }
    }
    
    // Customization methods
    public void SetSpawnRate(float newRate) { spawnRate = newRate; }
    public void SetFadeTime(float newFadeTime) { fadeTime = newFadeTime; }
    public void SetColorCount(int count) { colorCount = Mathf.Clamp(count, 1, cyberpunkColors.Length); }
    
    // Inner class for cyberpunk afterimages
    [System.Serializable]
    private class CyberpunkAfterImage
    {
        public GameObject gameObject;
        public SpriteRenderer spriteRenderer;
        public bool isActive;
        public float spawnTime;
        public Color originalColor;
        public Vector3 originalScale;
    }
}