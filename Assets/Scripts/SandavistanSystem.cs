using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Complete Sandavistan system with time manipulation, Cyberpunk after-images, and visual effects
/// Just drag this onto your player and you're done!
/// </summary>
[RequireComponent(typeof(SpriteRenderer), typeof(Rigidbody2D))]
public class SandavistanSystem : MonoBehaviour
{
    [Header("== SANDAVISTAN CORE ==")]
    [SerializeField] private KeyCode activationKey = KeyCode.Tab;
    [SerializeField] private float slowMotionScale = 0.1f;
    [SerializeField] private float maxDuration = 3f;
    [SerializeField] private float cooldownTime = 8f;
    [SerializeField] private float transitionSpeed = 5f;
    
    [Header("== PLAYER ENHANCEMENT ==")]
    [SerializeField] private float playerSpeedMultiplier = 1.5f;
    [SerializeField] private float playerJumpMultiplier = 1.2f;
    [SerializeField] private float enhancedAirControl = 2f;
    
    [Header("== AFTER-IMAGE SYSTEM ==")]
    [SerializeField] private bool enableAfterImages = true;
    [SerializeField] private int maxAfterImages = 6;
    [SerializeField] private float afterImageSpawnRate = 0.12f;
    [SerializeField] private float afterImageLifetime = 0.8f;
    [SerializeField] private float minimumMovementDistance = 0.15f;
    [SerializeField] private AnimationCurve afterImageFade = AnimationCurve.EaseInOut(0, 1, 1, 0);
    
    [Header("== CYBERPUNK COLORS ==")]
    [SerializeField] private Color[] afterImageColors = {
        new Color(0f, 0.8f, 1f, 0.9f),     // Cyan
        new Color(1f, 0.2f, 0.6f, 0.8f),   // Hot Pink
        new Color(0.2f, 1f, 0.4f, 0.7f),   // Neon Green
        new Color(1f, 0.4f, 0.1f, 0.7f),   // Orange
        new Color(0.6f, 0.2f, 1f, 0.6f),   // Purple
        new Color(1f, 1f, 0.2f, 0.6f)      // Yellow
    };
    
    [Header("== VISUAL EFFECTS ==")]
    [SerializeField] private Camera playerCamera;
    [SerializeField] private Volume postProcessVolume;
    [SerializeField] private Color sandavistanTint = new Color(0.3f, 0.8f, 1f, 0.1f);
    [SerializeField] private float chromaticAberrationIntensity = 0.5f;
    [SerializeField] private float vignetteIntensity = 0.4f;
    [SerializeField] private float cameraShakeIntensity = 0.2f;
    [SerializeField] private float cameraShakeDuration = 0.3f;
    
    [Header("== AUDIO ==")]
    [SerializeField] private AudioClip activationSound;
    [SerializeField] private AudioClip deactivationSound;
    [SerializeField] private AudioClip warningSound;
    [SerializeField] private float slowedPitchScale = 0.7f;
    
    [Header("== SORTING LAYERS ==")]
    [SerializeField] private string afterImageSortingLayer = "Effects";
    [SerializeField] private int afterImageSortingOrder = 45;
    
    // === RUNTIME STATE ===
    private bool isActive = false;
    private float currentCharge = 100f;
    private float currentCooldown = 0f;
    private float targetTimeScale = 1f;
    private float originalTimeScale = 1f;
    
    // === COMPONENTS ===
    private SpriteRenderer playerSR;
    private Rigidbody2D playerRB;
    private AudioSource audioSource;
    private PlayerManager playerManager; // Reference to your existing PlayerManager
    
    // === POST-PROCESSING ===
    private ChromaticAberration chromaticAberration;
    private Vignette vignette;
    private ColorAdjustments colorAdjustments;
    
    // === AFTER-IMAGE SYSTEM ===
    private Queue<AfterImageInstance> activeAfterImages = new Queue<AfterImageInstance>();
    private Queue<GameObject> afterImagePool = new Queue<GameObject>();
    private Transform afterImageParent;
    private Vector3 lastAfterImagePosition;
    private int colorIndex = 0;
    private Coroutine afterImageCoroutine;
    private Material additiveMaterial;
    
    // === AUDIO TRACKING ===
    private Dictionary<AudioSource, float> originalPitches = new Dictionary<AudioSource, float>();
    
    // === CAMERA SHAKE ===
    private Vector3 originalCameraPosition;
    private Coroutine cameraShakeCoroutine;
    
    [System.Serializable]
    private class AfterImageInstance
    {
        public GameObject gameObject;
        public SpriteRenderer spriteRenderer;
        public Coroutine fadeCoroutine;
        public float spawnTime;
    }
    
    // === EVENTS ===
    public static event System.Action OnSandavistanActivated;
    public static event System.Action OnSandavistanDeactivated;
    public static event System.Action<float> OnChargeChanged;

    void Awake()
    {
        // Get components
        playerSR = GetComponent<SpriteRenderer>();
        playerRB = GetComponent<Rigidbody2D>();
        playerManager = GetComponent<PlayerManager>();
        
        // Setup audio
        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
            audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        
        // Setup camera
        if (playerCamera == null)
            playerCamera = Camera.main;
        if (playerCamera != null)
            originalCameraPosition = playerCamera.transform.localPosition;
        
        // Setup post-processing
        SetupPostProcessing();
        
        // Setup after-image system
        SetupAfterImageSystem();
        
        // Cache audio sources
        CacheAudioSources();
        
        originalTimeScale = Time.timeScale;
        lastAfterImagePosition = transform.position;
    }

    void Start()
    {
        // Initial UI update
        OnChargeChanged?.Invoke(currentCharge);
    }

    void Update()
    {
        HandleInput();
        UpdateSystem();
        UpdateVisualEffects();
        HandleTimeScale();
        HandlePlayerEnhancement();
    }

    // === INPUT HANDLING ===
    private void HandleInput()
    {
        if (Input.GetKeyDown(activationKey))
        {
            if (!isActive && CanActivate())
            {
                ActivateSandavistan();
            }
            else if (isActive)
            {
                DeactivateSandavistan();
            }
        }
    }

    // === CORE SYSTEM ===
    private bool CanActivate()
    {
        return currentCharge >= 20f && currentCooldown <= 0f;
    }

    private void ActivateSandavistan()
    {
        if (isActive) return;
        
        isActive = true;
        targetTimeScale = slowMotionScale;
        
        // Visual effects
        StartCoroutine(ActivationEffects());
        
        // Audio
        PlaySound(activationSound, 1f);
        SlowDownAudio();
        
        // After-images
        if (enableAfterImages)
            StartAfterImages();
        
        // Events
        OnSandavistanActivated?.Invoke();
        
        Debug.Log("🔥 SANDAVISTAN ACTIVATED! 🔥");
    }

    private void DeactivateSandavistan()
    {
        if (!isActive) return;
        
        isActive = false;
        targetTimeScale = originalTimeScale;
        currentCooldown = cooldownTime;
        
        // Visual effects
        StartCoroutine(DeactivationEffects());
        
        // Audio
        PlaySound(deactivationSound, 1f);
        RestoreAudioPitches();
        
        // After-images
        StopAfterImages();
        
        // Events
        OnSandavistanDeactivated?.Invoke();
        
        Debug.Log("Sandavistan deactivated");
    }

    private void UpdateSystem()
    {
        // Update charge
        if (isActive && currentCharge > 0)
        {
            float drainRate = 100f / maxDuration;
            currentCharge -= drainRate * Time.unscaledDeltaTime;
            
            if (currentCharge <= 0)
            {
                currentCharge = 0;
                DeactivateSandavistan();
            }
            
            // Low charge warning
            if (currentCharge <= 20f && warningSound != null)
            {
                if (Time.unscaledTime % 1f < 0.1f)
                {
                    PlaySound(warningSound, 0.3f, 1f);
                }
            }
            
            OnChargeChanged?.Invoke(currentCharge);
        }
        
        // Update cooldown
        if (currentCooldown > 0)
        {
            currentCooldown -= Time.unscaledDeltaTime;
        }
        
        // Recharge
        if (!isActive && currentCooldown <= 0 && currentCharge < 100f)
        {
            currentCharge += 15f * Time.unscaledDeltaTime;
            currentCharge = Mathf.Clamp(currentCharge, 0f, 100f);
            OnChargeChanged?.Invoke(currentCharge);
        }
    }

    // === TIME MANIPULATION ===
    private void HandleTimeScale()
    {
        Time.timeScale = Mathf.Lerp(Time.timeScale, targetTimeScale, transitionSpeed * Time.unscaledDeltaTime);
        Time.fixedDeltaTime = Time.timeScale * 0.02f;
    }

    // === PLAYER ENHANCEMENT ===
    private void HandlePlayerEnhancement()
    {
        if (!isActive || playerManager == null) return;
        
        // Enhanced air control
        if (!playerManager.IsGrounded() && Input.GetAxisRaw("Horizontal") != 0)
        {
            float horizontal = Input.GetAxisRaw("Horizontal");
            playerRB.AddForce(Vector2.right * horizontal * enhancedAirControl, ForceMode2D.Force);
        }
        
        // Floaty feeling
        if (playerRB.velocity.y > 0 && playerRB.velocity.y < 2f)
        {
            playerRB.AddForce(Vector2.up * 0.5f, ForceMode2D.Force);
        }
    }

    // === AFTER-IMAGE SYSTEM ===
    private void SetupAfterImageSystem()
    {
        if (!enableAfterImages) return;
        
        // Create parent
        GameObject parentObj = new GameObject("SandavistanAfterImages");
        afterImageParent = parentObj.transform;
        
        // Create additive material
        additiveMaterial = new Material(Shader.Find("Sprites/Default"));
        additiveMaterial.SetInt("_BlendOp", (int)UnityEngine.Rendering.BlendOp.Add);
        additiveMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        additiveMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        
        // Pre-create pool
        for (int i = 0; i < maxAfterImages * 2; i++)
        {
            GameObject afterImage = CreateAfterImageObject();
            afterImage.SetActive(false);
            afterImagePool.Enqueue(afterImage);
        }
    }

    private GameObject CreateAfterImageObject()
    {
        GameObject obj = new GameObject("AfterImage");
        obj.transform.SetParent(afterImageParent);
        
        SpriteRenderer sr = obj.AddComponent<SpriteRenderer>();
        sr.sortingLayerName = afterImageSortingLayer;
        sr.sortingOrder = afterImageSortingOrder;
        sr.material = additiveMaterial;
        
        return obj;
    }

    private void StartAfterImages()
    {
        if (afterImageCoroutine != null)
            StopCoroutine(afterImageCoroutine);
        
        afterImageCoroutine = StartCoroutine(AfterImageLoop());
        
        // Create initial burst
        StartCoroutine(CreateAfterImageBurst(4));
    }

    private void StopAfterImages()
    {
        if (afterImageCoroutine != null)
        {
            StopCoroutine(afterImageCoroutine);
            afterImageCoroutine = null;
        }
    }

    private IEnumerator AfterImageLoop()
    {
        while (isActive)
        {
            float distanceMoved = Vector3.Distance(transform.position, lastAfterImagePosition);
            
            if (distanceMoved >= minimumMovementDistance)
            {
                CreateAfterImage();
                lastAfterImagePosition = transform.position;
            }
            
            // Maintain max count
            while (activeAfterImages.Count > maxAfterImages)
            {
                RemoveOldestAfterImage();
            }
            
            yield return new WaitForSecondsRealtime(afterImageSpawnRate);
        }
    }

    private void CreateAfterImage()
    {
        GameObject afterImageObj = GetPooledAfterImage();
        if (afterImageObj == null) return;
        
        SpriteRenderer sr = afterImageObj.GetComponent<SpriteRenderer>();
        
        // Copy player state exactly
        afterImageObj.transform.position = transform.position;
        afterImageObj.transform.rotation = transform.rotation;
        afterImageObj.transform.localScale = transform.localScale;
        
        sr.sprite = playerSR.sprite;
        sr.flipX = playerSR.flipX;
        sr.flipY = playerSR.flipY;
        sr.color = GetNextAfterImageColor();
        
        afterImageObj.SetActive(true);
        
        // Create instance
        AfterImageInstance instance = new AfterImageInstance
        {
            gameObject = afterImageObj,
            spriteRenderer = sr,
            spawnTime = Time.unscaledTime
        };
        
        instance.fadeCoroutine = StartCoroutine(FadeAfterImage(instance));
        activeAfterImages.Enqueue(instance);
    }

    private Color GetNextAfterImageColor()
    {
        Color color = afterImageColors[colorIndex];
        colorIndex = (colorIndex + 1) % afterImageColors.Length;
        
        // Intensity based on speed
        float speed = playerRB.velocity.magnitude;
        if (speed > 5f)
        {
            color.a *= 1.2f;
        }
        
        return color;
    }

    private IEnumerator FadeAfterImage(AfterImageInstance instance)
    {
        float elapsed = 0f;
        Color originalColor = instance.spriteRenderer.color;
        Vector3 originalScale = instance.gameObject.transform.localScale;
        
        while (elapsed < afterImageLifetime)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / afterImageLifetime;
            float fade = afterImageFade.Evaluate(progress);
            
            // Fade color
            Color currentColor = originalColor;
            currentColor.a = originalColor.a * (1f - fade);
            instance.spriteRenderer.color = currentColor;
            
            // Slight scale reduction
            float scaleMultiplier = Mathf.Lerp(1f, 0.85f, fade);
            instance.gameObject.transform.localScale = originalScale * scaleMultiplier;
            
            yield return null;
        }
        
        ReturnAfterImageToPool(instance.gameObject);
    }

    private GameObject GetPooledAfterImage()
    {
        if (afterImagePool.Count > 0)
            return afterImagePool.Dequeue();
        else
            return CreateAfterImageObject();
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
            StopCoroutine(oldest.fadeCoroutine);
        
        ReturnAfterImageToPool(oldest.gameObject);
    }

    public IEnumerator CreateAfterImageBurst(int count)
    {
        for (int i = 0; i < count; i++)
        {
            CreateAfterImage();
            yield return new WaitForSecondsRealtime(0.05f);
        }
    }

    // === VISUAL EFFECTS ===
    private void SetupPostProcessing()
    {
        if (postProcessVolume == null) return;
        
        var profile = postProcessVolume.profile;
        if (profile == null) return;
        
        profile.TryGet(out chromaticAberration);
        profile.TryGet(out vignette);
        profile.TryGet(out colorAdjustments);
    }

    private void UpdateVisualEffects()
    {
        if (!isActive) return;
        
        float intensity = 1f - (currentCharge / 100f) * 0.3f;
        
        if (chromaticAberration != null)
        {
            chromaticAberration.intensity.value = Mathf.Lerp(
                chromaticAberration.intensity.value,
                chromaticAberrationIntensity * intensity,
                Time.unscaledDeltaTime * 3f
            );
        }
        
        if (vignette != null)
        {
            vignette.intensity.value = Mathf.Lerp(
                vignette.intensity.value,
                vignetteIntensity * intensity,
                Time.unscaledDeltaTime * 3f
            );
        }
        
        if (colorAdjustments != null)
        {
            colorAdjustments.colorFilter.value = Color.Lerp(
                colorAdjustments.colorFilter.value,
                Color.white + sandavistanTint * intensity,
                Time.unscaledDeltaTime * 2f
            );
        }
    }

    private IEnumerator ActivationEffects()
    {
        // Screen shake
        ShakeCamera();
        
        // Screen flash/distortion
        if (playerCamera != null && !playerCamera.orthographic)
        {
            float originalFOV = playerCamera.fieldOfView;
            float timer = 0f;
            float duration = 0.2f;
            
            while (timer < duration)
            {
                timer += Time.unscaledDeltaTime;
                float progress = timer / duration;
                playerCamera.fieldOfView = Mathf.Lerp(originalFOV, originalFOV * 1.1f,
                                                     Mathf.Sin(progress * Mathf.PI));
                yield return null;
            }
            
            playerCamera.fieldOfView = originalFOV;
        }
        
        yield return null;
    }

    private IEnumerator DeactivationEffects()
    {
        float timer = 0f;
        float duration = 1f;
        
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float progress = timer / duration;
            float invProgress = 1f - progress;
            
            if (chromaticAberration != null)
                chromaticAberration.intensity.value = chromaticAberrationIntensity * invProgress;
            
            if (vignette != null)
                vignette.intensity.value = vignetteIntensity * invProgress;
            
            if (colorAdjustments != null)
                colorAdjustments.colorFilter.value = Color.Lerp(Color.white, Color.white + sandavistanTint, invProgress);
            
            yield return null;
        }
        
        // Reset completely
        if (chromaticAberration != null) chromaticAberration.intensity.value = 0f;
        if (vignette != null) vignette.intensity.value = 0f;
        if (colorAdjustments != null) colorAdjustments.colorFilter.value = Color.white;
    }

    private void ShakeCamera()
    {
        if (playerCamera == null) return;
        
        if (cameraShakeCoroutine != null)
            StopCoroutine(cameraShakeCoroutine);
        
        cameraShakeCoroutine = StartCoroutine(CameraShakeCoroutine());
    }

    private IEnumerator CameraShakeCoroutine()
    {
        float elapsed = 0f;
        
        while (elapsed < cameraShakeDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / cameraShakeDuration;
            float intensity = Mathf.Lerp(cameraShakeIntensity, 0f, progress);
            
            Vector3 randomOffset = Random.insideUnitSphere * intensity;
            randomOffset.z = 0;
            
            playerCamera.transform.localPosition = originalCameraPosition + randomOffset;
            
            yield return null;
        }
        
        playerCamera.transform.localPosition = originalCameraPosition;
        cameraShakeCoroutine = null;
    }

    // === AUDIO SYSTEM ===
    private void CacheAudioSources()
    {
        AudioSource[] allSources = FindObjectsOfType<AudioSource>();
        foreach (AudioSource source in allSources)
        {
            originalPitches[source] = source.pitch;
        }
    }

    private void SlowDownAudio()
    {
        foreach (var kvp in originalPitches)
        {
            if (kvp.Key != null && kvp.Key != audioSource)
                kvp.Key.pitch = kvp.Value * slowedPitchScale;
        }
    }

    private void RestoreAudioPitches()
    {
        foreach (var kvp in originalPitches)
        {
            if (kvp.Key != null)
                kvp.Key.pitch = kvp.Value;
        }
    }

    private void PlaySound(AudioClip clip, float volume = 1f, float pitch = 1f)
    {
        if (clip != null && audioSource != null)
        {
            audioSource.pitch = pitch;
            audioSource.PlayOneShot(clip, volume);
        }
    }

    // === PUBLIC API ===
    public bool IsActive() => isActive;
    public float GetChargePercentage() => currentCharge;
    public float GetCooldownPercentage() => currentCooldown > 0 ? currentCooldown / cooldownTime : 0f;
    public bool CanUse() => CanActivate();
    
    // For PlayerManager integration
    public float GetSpeedMultiplier() => isActive ? playerSpeedMultiplier : 1f;
    public float GetJumpMultiplier() => isActive ? playerJumpMultiplier : 1f;

    // === CLEANUP ===
    void OnDestroy()
    {
        Time.timeScale = originalTimeScale;
        Time.fixedDeltaTime = 0.02f;
        RestoreAudioPitches();
        
        if (afterImageParent != null)
            Destroy(afterImageParent.gameObject);
    }

    void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus && isActive)
            DeactivateSandavistan();
    }

    // === EDITOR HELPERS ===
    [ContextMenu("Test Activate")]
    public void TestActivate()
    {
        if (Application.isPlaying)
        {
            if (!isActive && CanActivate())
                ActivateSandavistan();
        }
    }

    [ContextMenu("Force Deactivate")]
    public void ForceDeactivate()
    {
        if (Application.isPlaying && isActive)
            DeactivateSandavistan();
    }

    [ContextMenu("Refill Charge")]
    public void RefillCharge()
    {
        currentCharge = 100f;
        currentCooldown = 0f;
        OnChargeChanged?.Invoke(currentCharge);
    }
}