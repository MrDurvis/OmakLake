using UnityEngine;
using UnityEngine.EventSystems;

public class BoardCamera : MonoBehaviour
{
    [Header("Required")]
    [SerializeField] private RectTransform viewport; // fixed window
    [SerializeField] private RectTransform content;  // pans & zooms

    [Header("Zoom")]
    [SerializeField] private float minZoom = 0.25f;
    [SerializeField] private float maxZoom = 3.0f;
    [SerializeField] private float zoomLerpSpeed = 14f;  // how quickly to reach target
    [SerializeField] private float panLerpSpeed  = 18f;

    [Header("Bounds (optional but recommended)")]
    [SerializeField] private bool clampToContentBounds = true;
    [SerializeField] private Vector2 extraPadding = new Vector2(200f, 200f);

    private float targetScale = 1f;
    private Vector2 targetPos; // world-space target for content.position
    private bool hasTarget;

    public float CurrentZoom => content ? content.localScale.x : 1f;

    void Awake()
    {
        if (!viewport && content) viewport = (RectTransform)content.parent;
        if (content)
        {
            targetScale = content.localScale.x;
            targetPos = content.position;
            hasTarget = true;
        }
    }

    void LateUpdate()
    {
        if (!hasTarget || !content) return;

        // Smooth zoom
        float s = Mathf.Lerp(content.localScale.x, targetScale,
                             1f - Mathf.Exp(-zoomLerpSpeed * Time.unscaledDeltaTime));
        var cs = content.localScale; cs.x = cs.y = s; content.localScale = cs;

        // Smooth pan
        Vector3 p = Vector3.Lerp(content.position, targetPos,
                                 1f - Mathf.Exp(-panLerpSpeed * Time.unscaledDeltaTime));
        content.position = p;
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    public void FocusOn(RectTransform node, bool immediate = false)
    {
        if (!node || !content || !viewport) return;

        Vector2 nodeCenterInViewport = (Vector2)viewport.InverseTransformPoint(node.TransformPoint(node.rect.center));
        Vector2 viewportCenter = viewport.rect.center;

        Vector2 contentPosInViewport = (Vector2)viewport.InverseTransformPoint(content.position);
        Vector2 desiredContentPosInViewport = contentPosInViewport + (viewportCenter - nodeCenterInViewport);
        Vector3 desiredWorld = viewport.TransformPoint(desiredContentPosInViewport);

        if (clampToContentBounds) desiredWorld = ClampContentWorld(desiredWorld, content.localScale.x);

        if (immediate) { content.position = desiredWorld; targetPos = desiredWorld; hasTarget = true; }
        else           { targetPos = desiredWorld; hasTarget = true; }
    }

    public void ZoomAround(float factor, Vector2 focalPointViewportLocal)
    {
        if (!content || !viewport) return;

        float beforeScale = content.localScale.x;
        float afterScale  = Mathf.Clamp(beforeScale * factor, minZoom, maxZoom);
        targetScale = afterScale;

        Vector3 focalWorld = viewport.TransformPoint(focalPointViewportLocal);
        Vector3 pre = content.InverseTransformPoint(focalWorld);

        Vector3 oldScale = content.localScale;
        content.localScale = new Vector3(afterScale, afterScale, oldScale.z); // temp
        Vector3 postWIfScaled = content.TransformPoint(pre);
        content.localScale = oldScale;

        Vector3 delta = focalWorld - postWIfScaled;
        Vector3 desiredWorld = content.position + delta;

        if (clampToContentBounds) desiredWorld = ClampContentWorld(desiredWorld, afterScale);

        targetPos = desiredWorld; hasTarget = true;
    }

    public void ZoomAroundCenter(float factor)
    {
        ZoomAround(factor, viewport.rect.center);
    }

    /// Immediate pan in content local units (used by freelook).
    public void Nudge(Vector2 contentLocalDelta)
    {
        if (!content) return;

        Vector3 worldDelta = content.TransformVector(new Vector3(contentLocalDelta.x, contentLocalDelta.y, 0f));
        Vector3 desiredWorld = content.position + worldDelta;

        if (clampToContentBounds) desiredWorld = ClampContentWorld(desiredWorld, content.localScale.x);

        content.position = desiredWorld;   // immediate for responsiveness
        targetPos = desiredWorld;
        hasTarget = true;
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private Vector3 ClampContentWorld(Vector3 desiredWorldPos, float scale)
    {
        var b = RectTransformUtility.CalculateRelativeRectTransformBounds(content, content);
        Vector2 contentSizeScaled = new Vector2(b.size.x * scale, b.size.y * scale);

        Vector2 vpSize = viewport.rect.size;
        Vector2 half = vpSize * 0.5f;

        Vector2 desiredInViewport = (Vector2)viewport.InverseTransformPoint(desiredWorldPos);

        Vector2 maxOffset = (contentSizeScaled * 0.5f) + extraPadding;
        float minX = -maxOffset.x + half.x;
        float maxX =  maxOffset.x - half.x;
        float minY = -maxOffset.y + half.y;
        float maxY =  maxOffset.y - half.y;

        if (contentSizeScaled.x + 2f * extraPadding.x < vpSize.x) { minX = maxX = desiredInViewport.x; }
        if (contentSizeScaled.y + 2f * extraPadding.y < vpSize.y) { minY = maxY = desiredInViewport.y; }

        desiredInViewport.x = Mathf.Clamp(desiredInViewport.x, minX, maxX);
        desiredInViewport.y = Mathf.Clamp(desiredInViewport.y, minY, maxY);

        return viewport.TransformPoint(desiredInViewport);
    }
}