using UnityEngine;

/// <summary>
/// Lightweight 2D camera follow with dead zone and optional look-ahead.
/// Cinemachine-like feel: camera stays still until the player leaves the dead zone, then follows smoothly.
/// </summary>
public class CameraFollow : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private Transform target;
    [Tooltip("World offset from target (e.g. slight vertical offset). Z is camera distance.")]
    [SerializeField] private Vector3 offset = new Vector3(0f, 0.5f, -10f);

    [Header("Dead zone (Cinemachine-style)")]
    [Tooltip("Half-size of the inner zone where the camera does not move (X = horizontal, Y = vertical).")]
    [SerializeField] private Vector2 deadZoneHalfSize = new Vector2(0.4f, 0.35f);
    [Tooltip("If true, no dead zone; camera always follows (tight follow).")]
    [SerializeField] private bool deadZoneDisabled;

    [Header("Follow")]
    [Tooltip("Time to reach target; lower = snappier, higher = more floaty.")]
    [SerializeField] private float followSmoothTime = 0.2f;
    [Tooltip("Use different smooth time for vertical (e.g. slightly slower for platformers).")]
    [SerializeField] private bool useSeparateYSmoothTime;
    [SerializeField] private float followSmoothTimeY = 0.25f;

    [Header("Look-ahead")]
    [SerializeField] private bool lookAheadEnabled = true;
    [SerializeField] private float lookAheadFactor = 2f;
    [SerializeField] private float lookAheadReturnSpeed = 4f;
    [SerializeField] private float lookAheadMoveThreshold = 0.15f;

    [Header("Start")]
    [Tooltip("Snap to target on Start so the camera doesn't start behind.")]
    [SerializeField] private bool snapToTargetOnStart = true;

    private Vector2 _lastTargetPosition;
    private float _currentLookAheadX;
    private float _targetLookAheadX;
    private float _lookAheadVelocity;
    private float _velocityX;
    private float _velocityY;

    private void Start()
    {
        if (target == null)
        {
            var player = GameObject.FindGameObjectWithTag("Player");
            if (player != null) target = player.transform;
        }

        Vector2 t = target != null ? (Vector2)target.position : Vector2.zero;
        _lastTargetPosition = t;

        if (snapToTargetOnStart && target != null)
        {
            Vector3 desired = TargetCameraPosition(t);
            transform.position = new Vector3(desired.x, desired.y, offset.z);
        }
    }

    private void LateUpdate()
    {
        if (target == null) return;

        Vector2 targetPos = target.position;
        Vector2 cameraPos2 = new Vector2(transform.position.x, transform.position.y);

        // Look-ahead (horizontal only)
        float lookAheadX = 0f;
        if (lookAheadEnabled)
        {
            float moveX = targetPos.x - _lastTargetPosition.x;
            if (Mathf.Abs(moveX) > lookAheadMoveThreshold)
                _targetLookAheadX = Mathf.Sign(moveX) * lookAheadFactor;
            else
                _targetLookAheadX = Mathf.MoveTowards(_targetLookAheadX, 0f, lookAheadReturnSpeed * Time.deltaTime);

            _currentLookAheadX = Mathf.SmoothDamp(_currentLookAheadX, _targetLookAheadX, ref _lookAheadVelocity, followSmoothTime * 0.5f);
            lookAheadX = _currentLookAheadX;
        }
        _lastTargetPosition = targetPos;

        // Desired camera position (target + offset + look-ahead)
        Vector2 desired = new Vector2(targetPos.x + offset.x + lookAheadX, targetPos.y + offset.y);

        // Dead zone: only move desired so that target stays at the edge of the zone when outside it
        if (!deadZoneDisabled)
        {
            Vector2 delta = desired - cameraPos2;
            float clampX = Mathf.Clamp(delta.x, -deadZoneHalfSize.x, deadZoneHalfSize.x);
            float clampY = Mathf.Clamp(delta.y, -deadZoneHalfSize.y, deadZoneHalfSize.y);
            desired = cameraPos2 + new Vector2(delta.x - clampX, delta.y - clampY);
        }

        Vector3 targetCameraPos = new Vector3(desired.x, desired.y, offset.z);

        // Smooth follow (optionally different Y smooth time)
        float smoothX = followSmoothTime;
        float smoothY = useSeparateYSmoothTime ? followSmoothTimeY : followSmoothTime;

        float newX = Mathf.SmoothDamp(transform.position.x, targetCameraPos.x, ref _velocityX, smoothX);
        float newY = Mathf.SmoothDamp(transform.position.y, targetCameraPos.y, ref _velocityY, smoothY);

        transform.position = new Vector3(newX, newY, offset.z);
    }

    private Vector2 TargetCameraPosition(Vector2 targetPos)
    {
        return new Vector2(targetPos.x + offset.x, targetPos.y + offset.y);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        if (target == null || deadZoneDisabled) return;

        Vector2 center = Application.isPlaying
            ? new Vector2(transform.position.x, transform.position.y)
            : new Vector2(target.position.x + offset.x, target.position.y + offset.y);

        Gizmos.color = new Color(1f, 1f, 0f, 0.4f);
        Gizmos.DrawWireCube(center, new Vector3(deadZoneHalfSize.x * 2f, deadZoneHalfSize.y * 2f, 0.01f));
    }
#endif
}
