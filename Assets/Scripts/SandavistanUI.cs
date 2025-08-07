using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System.Data;

/// <summary>
/// Simple UI for the Complete Sandavistan System
/// Just drag this onto a UI GameObject and assign the elements
/// </summary>
public class SandavistanUI : MonoBehaviour
{
    [Header("== UI ELEMENTS ==")]
    [SerializeField] private Image batteryFill;
    [SerializeField] private Image cooldownFill;
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private GameObject activationPrompt;
    [SerializeField] private GameObject warningPanel;
    [SerializeField] private Image backgroundPanel;
    
    [Header("== COLORS ==")]
    [SerializeField] private Color fullChargeColor = new Color(0f, 1f, 0.5f, 1f);      // Green
    [SerializeField] private Color mediumChargeColor = new Color(1f, 1f, 0f, 1f);      // Yellow  
    [SerializeField] private Color lowChargeColor = new Color(1f, 0.2f, 0.2f, 1f);     // Red
    [SerializeField] private Color cooldownColor = new Color(0.3f, 0.3f, 0.3f, 1f);   // Gray
    [SerializeField] private Color activeColor = new Color(0f, 0.8f, 1f, 1f);         // Cyan
    
    [Header("== SETTINGS ==")]
    [SerializeField] private bool showPercentageText = true;
    [SerializeField] private bool enablePulseEffects = true;
    [SerializeField] private float warningThreshold = 25f;
    [SerializeField] private KeyCode activationKey = KeyCode.Tab;
    
    // Runtime references
    private SandavistanSystem sandavistanSystem;
    private Animator uiAnimator;
    private bool lastWarningState = false;
    private Coroutine pulseCoroutine;

    void Start()
    {
        // Find the sandavistan system
        sandavistanSystem = FindObjectOfType<SandavistanSystem>();
        
        if (sandavistanSystem == null)
        {
            Debug.LogError("SandavistanUI: No SandavistanSystem found in scene!");
            gameObject.SetActive(false);
            return;
        }
        
        // Get animator if available
        uiAnimator = GetComponent<Animator>();
        
        // Subscribe to events
        SandavistanSystem.OnSandavistanActivated += OnActivated;
        SandavistanSystem.OnSandavistanDeactivated += OnDeactivated;
        SandavistanSystem.OnChargeChanged += OnChargeChanged;
        
        // Initial update
        UpdateUI();
        
        Debug.Log("Sandavistan UI initialized successfully!");
    }

    void Update()
    {
        UpdateUI();
        UpdateActivationPrompt();
        UpdateWarningState();
    }

    private void UpdateUI()
    {
        if (sandavistanSystem == null) return;
        
        float charge = sandavistanSystem.GetChargePercentage();
        float cooldown = sandavistanSystem.GetCooldownPercentage();
        bool isActive = sandavistanSystem.IsActive();
        
        // Update battery fill
        if (batteryFill != null)
        {
            batteryFill.fillAmount = charge / 100f;
            batteryFill.color = GetChargeColor(charge, isActive);
        }
        
        // Update cooldown fill
        if (cooldownFill != null)
        {
            cooldownFill.fillAmount = cooldown > 0 ? (1f - cooldown) : 1f;
            cooldownFill.color = cooldown > 0 ? cooldownColor : GetChargeColor(charge, isActive);
        }
        
        // Update status text
        if (statusText != null && showPercentageText)
        {
            UpdateStatusText(charge, cooldown, isActive);
        }
        
        // Update background panel color
        if (backgroundPanel != null)
        {
            Color bgColor = backgroundPanel.color;
            if (isActive)
            {
                bgColor = Color.Lerp(bgColor, activeColor * 0.3f, Time.unscaledDeltaTime * 5f);
            }
            else
            {
                bgColor = Color.Lerp(bgColor, Color.black * 0.5f, Time.unscaledDeltaTime * 5f);
            }
            backgroundPanel.color = bgColor;
        }
    }

    private Color GetChargeColor(float charge, bool isActive)
    {
        Color baseColor;
        
        if (charge > 60f)
            baseColor = Color.Lerp(mediumChargeColor, fullChargeColor, (charge - 60f) / 40f);
        else if (charge > 30f)
            baseColor = Color.Lerp(lowChargeColor, mediumChargeColor, (charge - 30f) / 30f);
        else
            baseColor = lowChargeColor;
        
        // Add active effect
        if (isActive)
        {
            baseColor = Color.Lerp(baseColor, activeColor, 0.4f);
        }
        
        return baseColor;
    }

    private void UpdateStatusText(float charge, float cooldown, bool isActive)
    {
        if (isActive)
        {
            statusText.text = $"<color=#00CCFF>ACTIVE</color> {charge:F0}%";
            if (enablePulseEffects && pulseCoroutine == null)
            {
                pulseCoroutine = StartCoroutine(PulseText(statusText, activeColor));
            }
        }
        else if (cooldown > 0)
        {
            statusText.text = $"<color=#888888>COOLDOWN</color> {(cooldown * 100):F0}%";
            statusText.color = cooldownColor;
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
                pulseCoroutine = null;
            }
        }
        else
        {
            statusText.text = $"<color=#FFFF00>READY</color> {charge:F0}%";
            statusText.color = GetChargeColor(charge, isActive);
            if (pulseCoroutine != null)
            {
                StopCoroutine(pulseCoroutine);
                pulseCoroutine = null;
            }
        }
    }

    private void UpdateActivationPrompt()
    {
        if (activationPrompt == null) return;
        
        bool canUse = sandavistanSystem.CanUse();
        bool isActive = sandavistanSystem.IsActive();
        
        // Show prompt when ready, hide when active or on cooldown
        bool shouldShow = canUse && !isActive;
        activationPrompt.SetActive(shouldShow);
        
        // Pulse the prompt when available
        if (shouldShow && enablePulseEffects)
        {
            Text promptText = activationPrompt.GetComponentInChildren<Text>();
            if (promptText != null)
            {
                promptText.text = $"Press [{activationKey}] to activate";
                float pulse = (Mathf.Sin(Time.unscaledTime * 4f) + 1f) * 0.5f;
                promptText.color = new Color(1f, 1f, 1f, 0.7f + pulse * 0.3f);
            }
        }
    }

    private void UpdateWarningState()
    {
        if (warningPanel == null) return;
        
        float charge = sandavistanSystem.GetChargePercentage();
        bool isActive = sandavistanSystem.IsActive();
        bool shouldWarn = isActive && charge <= warningThreshold;
        
        if (shouldWarn != lastWarningState)
        {
            warningPanel.SetActive(shouldWarn);
            
            if (shouldWarn && uiAnimator != null)
            {
                uiAnimator.SetTrigger("Warning");
            }
            
            lastWarningState = shouldWarn;
        }
        
        // Pulse warning panel
        if (shouldWarn)
        {
            Image warningImage = warningPanel.GetComponent<Image>();
            if (warningImage != null)
            {
                float pulse = Mathf.PingPong(Time.unscaledTime * 6f, 1f);
                warningImage.color = new Color(1f, 0f, 0f, 0.2f + pulse * 0.4f);
            }
            
            // Update warning text
            Text warningText = warningPanel.GetComponentInChildren<Text>();
            if (warningText != null)
            {
                warningText.text = "⚠️ LOW BATTERY ⚠️";
                float pulse = Mathf.PingPong(Time.unscaledTime * 6f, 1f);
                warningText.color = Color.Lerp(Color.red, Color.yellow, pulse);
            }
        }
    }

    // === EVENT HANDLERS ===
    private void OnActivated()
    {
        Debug.Log("UI: Sandavistan activated!");
        
        // Trigger activation animation
        if (uiAnimator != null)
        {
            uiAnimator.SetTrigger("Activate");
        }
        
        // Screen flash effect
        StartCoroutine(ActivationFlash());
        
        // Start pulse effects
        if (enablePulseEffects && batteryFill != null)
        {
            StartCoroutine(PulseImage(batteryFill, activeColor));
        }
    }

    private void OnDeactivated()
    {
        Debug.Log("UI: Sandavistan deactivated!");
        
        // Trigger deactivation animation
        if (uiAnimator != null)
        {
            uiAnimator.SetTrigger("Deactivate");
        }
        
        // Stop pulse effects
        StopAllCoroutines();
        
        // Hide warning
        if (warningPanel != null)
        {
            warningPanel.SetActive(false);
        }
        
        lastWarningState = false;
    }

    private void OnChargeChanged(float newCharge)
    {
        // Handle charge depletion
        if (newCharge <= 0f && uiAnimator != null)
        {
            uiAnimator.SetTrigger("Depleted");
        }
    }

    // === VISUAL EFFECTS ===
    private System.Collections.IEnumerator ActivationFlash()
    {
        // Create full-screen flash
        GameObject flashObj = new GameObject("ActivationFlash");
        Canvas canvas = flashObj.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 1000;
        
        Image flashImage = flashObj.AddComponent<Image>();
        flashImage.color = new Color(0f, 0.8f, 1f, 0.4f); // Cyan flash
        
        // Fullscreen setup
        RectTransform rt = flashImage.rectTransform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.sizeDelta = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        
        // Fade out
        float duration = 0.3f;
        float timer = 0f;
        Color startColor = flashImage.color;
        
        while (timer < duration)
        {
            timer += Time.unscaledDeltaTime;
            float alpha = Mathf.Lerp(startColor.a, 0f, timer / duration);
            flashImage.color = new Color(startColor.r, startColor.g, startColor.b, alpha);
            yield return null;
        }
        
        Destroy(flashObj);
    }

    private System.Collections.IEnumerator PulseImage(Image image, Color pulseColor)
    {
        Color originalColor = image.color;
        
        while (sandavistanSystem != null && sandavistanSystem.IsActive())
        {
            float pulse = (Mathf.Sin(Time.unscaledTime * 8f) + 1f) * 0.5f;
            image.color = Color.Lerp(originalColor, pulseColor, pulse * 0.4f);
            yield return null;
        }
        
        // Restore original color
        if (image != null)
            image.color = originalColor;
    }

    private System.Collections.IEnumerator PulseText(TextMeshProUGUI text, Color pulseColor)
    {
        Color originalColor = text.color;
        
        while (sandavistanSystem != null && sandavistanSystem.IsActive())
        {
            float pulse = (Mathf.Sin(Time.unscaledTime * 6f) + 1f) * 0.5f;
            text.color = Color.Lerp(originalColor, pulseColor, pulse * 0.3f);
            yield return null;
        }
        
        // Restore original color
        if (text != null)
            text.color = originalColor;
    }

    // === CLEANUP ===
    void OnDestroy()
    {
        // Unsubscribe from events
        if (sandavistanSystem != null)
        {
            SandavistanSystem.OnSandavistanActivated -= OnActivated;
            SandavistanSystem.OnSandavistanDeactivated -= OnDeactivated;
            SandavistanSystem.OnChargeChanged -= OnChargeChanged;
        }
        
        StopAllCoroutines();
    }

    // === PUBLIC METHODS FOR CUSTOMIZATION ===
    public void SetActivationKey(KeyCode newKey)
    {
        activationKey = newKey;
    }

    public void SetWarningThreshold(float threshold)
    {
        warningThreshold = threshold;
    }

    public void TogglePulseEffects(bool enabled)
    {
        enablePulseEffects = enabled;
        if (!enabled)
        {
            StopAllCoroutines();
        }
    }

    // === MANUAL CONTROLS (for testing) ===
    [ContextMenu("Test Activation Flash")]
    public void TestActivationFlash()
    {
        if (Application.isPlaying)
            StartCoroutine(ActivationFlash());
    }

    [ContextMenu("Force Update UI")]
    public void ForceUpdateUI()
    {
        UpdateUI();
    }
}