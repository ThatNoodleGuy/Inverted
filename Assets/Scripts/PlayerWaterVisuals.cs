using UnityEngine;

[RequireComponent(typeof(SpriteRenderer))]
public class PlayerWaterVisuals : MonoBehaviour
{
    [Header("References")]
    public FluidSim2D fluidSim;

    [Header("Colour Settings")]
    public Color normalColor = Color.white;
    public Color submergedColor = new Color(0.5f, 0.7f, 1f, 1f);
    public float colorLerpSpeed = 5f;

    [Header("Scale Settings")]
    public float submergedScaleY = 0.9f;
    public float scaleLerpSpeed = 5f;

    SpriteRenderer spriteRenderer;
    Vector3 originalScale;

    void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        originalScale = transform.localScale;
    }

    void Update()
    {
        if (fluidSim == null)
        {
            return;
        }

        bool inFluid = fluidSim.IsPlayerInFluid();
        float depth = fluidSim.GetPlayerSubmersionDepth();
        float maxDepth = Mathf.Max(0.0001f, fluidSim.maxBuoyancyDepth);
        float depthNorm = Mathf.Clamp01(depth / maxDepth);

        Color targetColor = inFluid
            ? Color.Lerp(normalColor, submergedColor, depthNorm)
            : normalColor;

        spriteRenderer.color = Color.Lerp(
            spriteRenderer.color,
            targetColor,
            Time.deltaTime * colorLerpSpeed
        );

        float targetScaleY = Mathf.Lerp(1f, submergedScaleY, depthNorm);
        Vector3 targetScale = new Vector3(
            originalScale.x,
            originalScale.y * targetScaleY,
            originalScale.z
        );

        transform.localScale = Vector3.Lerp(
            transform.localScale,
            targetScale,
            Time.deltaTime * scaleLerpSpeed
        );
    }
}

