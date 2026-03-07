using UnityEngine;

[RequireComponent(typeof(Animator))]
public class PlayerAnimationController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private PlayerManager player;
    [SerializeField] private Animator animator;

    [Header("Speed thresholds")]
    [SerializeField] private float walkSpeedThreshold = 0.1f;
    [SerializeField] private float runSpeedThreshold = 3f;
    [SerializeField] private float airborneSpeedThreshold = 0.1f;

    // State names must match the states in your Player Animator Controller
    private static readonly int IdleState = Animator.StringToHash("Player_Idle");
    private static readonly int WalkState = Animator.StringToHash("Player_Walk");
    private static readonly int RunState  = Animator.StringToHash("Player_Run");
    private static readonly int JumpState = Animator.StringToHash("Player_Jump");

    private int currentState;

    void Awake()
    {
        if (animator == null)
            animator = GetComponent<Animator>();
        if (player == null)
            player = GetComponent<PlayerManager>();
    }

    void Update()
    {
        if (animator == null || player == null) return;

        Vector2 vel = player.GetVelocity();
        bool grounded = player.IsGrounded;

        int nextState = DetermineState(vel, grounded);

        if (nextState != currentState)
        {
            // Small crossfade for smooth transitions
            animator.CrossFade(nextState, 0.1f);
            currentState = nextState;
        }
    }

    int DetermineState(Vector2 velocity, bool grounded)
    {
        float horizontalSpeed = Mathf.Abs(velocity.x);
        float verticalSpeed   = velocity.y;

        if (!grounded && Mathf.Abs(verticalSpeed) > airborneSpeedThreshold)
        {
            return JumpState;
        }

        if (horizontalSpeed > runSpeedThreshold)
        {
            return RunState;
        }

        if (horizontalSpeed > walkSpeedThreshold)
        {
            return WalkState;
        }

        return IdleState;
    }
}

