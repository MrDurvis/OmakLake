using UnityEngine;

[RequireComponent(typeof(CharacterController))]
public class MCMovement : MonoBehaviour
{
    public float walkSpeed = 3f;
    public float runSpeed = 6f;
    public float gravity = -9.81f;
    public float groundCheckDistance = 0.2f;
  

    private CharacterController characterController;
    private Animator animator;
    private Vector3 velocity;
    private bool isGrounded;
    private Quaternion currentRotation;

    void Start()
    {
        characterController = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();
        currentRotation = transform.rotation; // Track last facing rotation
    }

    void Update()
    {
        // Check if grounded
        isGrounded = Physics.Raycast(transform.position, Vector3.down, groundCheckDistance);

        // Get input
        float horizontal = Input.GetAxis("Horizontal");
        float vertical = Input.GetAxis("Vertical");

        Vector3 moveDirection = new Vector3(-horizontal, 0, -vertical);
        moveDirection = transform.TransformDirection(moveDirection);
        moveDirection.Normalize();

        bool isMoving = moveDirection.magnitude > 0;
        bool isRunning = Input.GetKey(KeyCode.LeftShift);
        float currentSpeed = isRunning ? runSpeed : walkSpeed;

        Vector3 move = moveDirection * currentSpeed;

        // If moving, update the target rotation to face movement direction
        if (isMoving)
        {
       
            // Rotate the player to face movement direction
            float targetAngle = Mathf.Atan2(moveDirection.x, moveDirection.z) * Mathf.Rad2Deg;
            transform.rotation = Quaternion.Euler(0, targetAngle, 0);

            // Calculate move vector
            Vector3 moving = moveDirection * currentSpeed * Time.deltaTime;

            // Move the character
            characterController.Move(transform.forward * moving.magnitude);

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

        // Apply gravity
        if (isGrounded)
        {
            velocity.y = 0f;
        }
        else
        {
            velocity.y += gravity * Time.deltaTime;
        }
       
    }
}
