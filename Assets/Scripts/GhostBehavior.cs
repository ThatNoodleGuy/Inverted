using System;
using System.Collections;
using UnityEngine;

public class GhostBehavior : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;
    private float lifetime;
    private Color startColor;
    private Color endColor;
    private bool useGradientFade;
    private Action onComplete;

    private float startTime;
    private bool isActive = false;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer == null)
        {
            spriteRenderer = gameObject.AddComponent<SpriteRenderer>();
        }
    }

    public void Initialize(float ghostLifetime, Color startCol, Color endCol, bool gradient, Action completionCallback)
    {
        lifetime = ghostLifetime;
        startColor = startCol;
        endColor = endCol;
        useGradientFade = gradient;
        onComplete = completionCallback;

        startTime = Time.time;
        isActive = true;

        // Set initial color
        if (spriteRenderer != null)
        {
            spriteRenderer.color = startColor;
        }

        // Start fade coroutine
        StartCoroutine(FadeCoroutine());
    }

    private IEnumerator FadeCoroutine()
    {
        while (isActive && Time.time - startTime < lifetime)
        {
            float elapsed = Time.time - startTime;
            float progress = elapsed / lifetime;

            if (spriteRenderer != null)
            {
                if (useGradientFade)
                {
                    // Smooth gradient fade
                    spriteRenderer.color = Color.Lerp(startColor, endColor, progress);
                }
                else
                {
                    // Linear alpha fade only
                    Color currentColor = startColor;
                    currentColor.a = Mathf.Lerp(startColor.a, endColor.a, progress);
                    spriteRenderer.color = currentColor;
                }
            }

            yield return null;
        }

        // Fade complete
        Complete();
    }

    private void Complete()
    {
        isActive = false;
        onComplete?.Invoke();
    }

    void OnDisable()
    {
        isActive = false;
        StopAllCoroutines();
    }
}