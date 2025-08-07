using System.Collections;
using UnityEngine;

/// <summary>
/// Simple camera shake component for screen effects
/// </summary>
public class CameraShake : MonoBehaviour
{
    [SerializeField] private float shakeDuration = 0.3f;
    [SerializeField] private float shakeIntensity = 0.1f;
    [SerializeField] private AnimationCurve shakeCurve = AnimationCurve.EaseInOut(0, 1, 1, 0);
    
    private Vector3 originalPosition;
    private Coroutine shakeCoroutine;
    private Transform cameraTransform;

    void Awake()
    {
        cameraTransform = transform;
        originalPosition = cameraTransform.localPosition;
    }

    public void ShakeCamera(float duration = 0.3f, float intensity = 0.1f)
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
        }
        
        shakeCoroutine = StartCoroutine(ShakeCoroutine(duration, intensity));
    }

    private IEnumerator ShakeCoroutine(float duration, float intensity)
    {
        Vector3 startPosition = cameraTransform.localPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = elapsed / duration;
            float curveValue = shakeCurve.Evaluate(progress);
            
            // Generate random shake offset
            Vector3 randomOffset = Random.insideUnitSphere * intensity * curveValue;
            randomOffset.z = 0; // Keep camera at same Z position
            
            cameraTransform.localPosition = startPosition + randomOffset;
            
            yield return null;
        }
        
        // Return to original position
        cameraTransform.localPosition = startPosition;
        shakeCoroutine = null;
    }

    public void StopShake()
    {
        if (shakeCoroutine != null)
        {
            StopCoroutine(shakeCoroutine);
            cameraTransform.localPosition = originalPosition;
            shakeCoroutine = null;
        }
    }
}