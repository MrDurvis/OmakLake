using UnityEngine;

public class PlayerMovement : MonoBehaviour
{
    [SerializeField] public float walkSpeed = 5f;
    [SerializeField] public float runSpeed = 8f; // Adjust as needed
    private Animator animator;
    private CharacterController characterController;
    private Vector3 velocity;
    private bool isGrounded;
    public float gravity = -9.81f;
    public float groundCheckDistance = 0.2f;

    void Start()
    {
        animator = GetComponent<Animator>();
        characterController = GetComponent<CharacterController>();
        // Initialize animator parameters
        animator.SetBool("IsWalking", false);
        animator.SetBool("IsRunning", false);
    }

    void Update()
    {
         isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);

        // Apply gravity
        if (isGrounded)
        {
            velocity.y = 0f;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }
        



        // Get input axes
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        // Create input direction
        Vector3 inputDirection = new Vector3(horizontal, 0, vertical).normalized;

        // Determine if player is moving
        bool isMoving = inputDirection.magnitude >= 0.1f;

        // Check if the run button (Shift) is held down
        bool isRunning = Input.GetKey(KeyCode.JoystickButton8);

        if (isMoving)
        {
            // Rotate the player to face movement direction
            float targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, targetAngle, 0);

            // Choose speed based on running or walking
            float currentSpeed = isRunning ? runSpeed : walkSpeed;

            // Calculate move vector
            Vector3 move = inputDirection * currentSpeed * Time.deltaTime;

            // Move the character
            characterController.Move(transform.forward * move.magnitude);

            // Set animator parameters
            animator.SetBool("IsWalking", true);
            animator.SetBool("IsRunning", isRunning);
        }
        else
        {
            // Not moving
            animator.SetBool("IsWalking", false);
            animator.SetBool("IsRunning", false);
        }
    }
}