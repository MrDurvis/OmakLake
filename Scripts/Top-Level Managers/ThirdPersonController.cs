
using UnityEditor.VersionControl;
using UnityEngine;

/*
    This file has a commented version with details about how each line works. 
    The commented version contains code that is easier and simpler to read. This file is minified.
*/


/// <summary>
/// Main script for third-person movement of the character in the game.
/// Make sure that the object that will receive this script (the player) 
/// has the Player tag and the Character Controller component.
/// </summary>
public class ThirdPersonController : MonoBehaviour
{

    [Tooltip("Speed ​​at which the character moves. It is not affected by gravity or jumping.")]
    public float velocity = 5f;
    [Tooltip("This value is added to the speed value while the character is sprinting.")]
    public float sprintAdittion = 3.5f;
    [Tooltip("The higher the value, the higher the character will jump.")]
    public float jumpForce = 18f;
    [Tooltip("Stay in the air. The higher the value, the longer the character floats before falling.")]
    public float jumpTime = 0.85f;
    [Space]
    [Tooltip("Force that pulls the player down. Changing this value causes all movement, jumping and falling to be changed as well.")]
    public float gravity = 9.8f;

    float jumpElapsedTime = 0;

    // Player states
    bool isJumping = false;
    bool isWalking;
    bool isRunning = false;
    bool isCrouching = false;

    // Inputs
    float inputHorizontal;
    float inputVertical;
    bool inputJump;
    bool inputCrouch;
    bool inputSprint;

    private Vector3 stairEntryPosition;
    private bool OnStairs = false;
    private bool IsAscending = false;

    private bool IsDescending = false;

    public bool isLocked = false;

    Animator animator;
    CharacterController cc;


    void Start()
    {
        cc = GetComponent<CharacterController>();
        animator = GetComponent<Animator>();

        // Message informing the user that they forgot to add an animator
        if (animator == null)
            Debug.LogWarning("Hey buddy, you don't have the Animator component in your player. Without it, the animations won't work.");
    }


    // Update is only being used here to identify keys and trigger animations
    void Update()
    {

        if (isLocked)
        {
            return;
        }
        if (DoorInteraction.isPlayerNear && (Input.GetKeyDown(KeyCode.JoystickButton0) || Input.GetKeyDown(KeyCode.Return)))

        {
            Debug.Log("[Input] Triggering door sequence");
            // Find the DoorTriggerZone component in the scene and call ActivateDoorSequence
            DoorTriggerZone doorZone = FindFirstObjectByType<DoorTriggerZone>();
            if (doorZone != null)
            {
                doorZone.ActivateDoorSequence();
            }
            else
            {
                Debug.LogWarning("No DoorTriggerZone found in the scene.");
            }
        }

        //Debug.Log("OnStairs = " + OnStairs);
        // Input checkers
        inputHorizontal = Input.GetAxis("Horizontal");
        inputVertical = Input.GetAxis("Vertical");
        inputJump = Input.GetAxis("Jump") == 1f;
        inputSprint = Input.GetKey(KeyCode.JoystickButton8);
        // Unfortunately GetAxis does not work with GetKeyDown, so inputs must be taken individually
        inputCrouch = Input.GetKeyDown(KeyCode.LeftControl) || Input.GetKeyDown(KeyCode.JoystickButton1);

        // Create input direction
        Vector3 inputDirection = new Vector3(inputHorizontal, 0, inputVertical).normalized;

        // Determine if player is moving
        bool isMoving = inputDirection.magnitude >= 0.1f;

        if (isMoving)
        {
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

        // Check if you pressed the crouch input key and change the player's state
        // if (inputCrouch)
        //     isCrouching = !isCrouching;

        // Run and Crouch animation
        // If dont have animator component, this block wont run
        if (cc.isGrounded && animator != null)
        {

            // Crouch
            // Note: The crouch animation does not shrink the character's collider
            // animator.SetBool("crouch", isCrouching);

            // Run
            float minimumSpeed = 0.9f;
            animator.SetBool("IsWalking", cc.velocity.magnitude > minimumSpeed);

            // Sprint
            isRunning = cc.velocity.magnitude > minimumSpeed && inputSprint;
            animator.SetBool("IsRunning", isRunning);

        }

        // Jump animation
        // if (animator != null)
        //   animator.SetBool("air", cc.isGrounded == false);

        // Handle can jump or not
        // if (inputJump && cc.isGrounded)
        //  {
        //    isJumping = true;
        // Disable crounching when jumping
        //isCrouching = false; 
        //}

        HeadHittingDetect();

        if (OnStairs)
        {
            float verticalInput = inputVertical;

            if (verticalInput > 0)
            {
                IsAscending = true;
                IsDescending = false;
            }
            else if (verticalInput < 0)
            {
                IsDescending = true;
                IsAscending = false;
            }
            else
            {
                IsAscending = false;
                IsDescending = false;
            }
        }
        else
        {
            IsAscending = false;
            IsDescending = false;
        }

        // Update animator parameters
        animator.SetBool("IsAscending", IsAscending);
        animator.SetBool("IsDescending", IsDescending);

    }


    // With the inputs and animations defined, FixedUpdate is responsible for applying movements and actions to the player
    private void FixedUpdate()
    {

         if (isLocked)
    {
        // Ensure no motion is applied this frame (CharacterController only moves when Move is called).
        // We do NOT call cc.Move here.
        return;
    }

        // Sprinting velocity boost or crounching desacelerate
        float velocityAdittion = 0;
        if (isRunning)
            velocityAdittion = sprintAdittion;
        if (isCrouching)
            velocityAdittion = -(velocity * 0.50f); // -50% velocity
        if (IsAscending)
            velocityAdittion = -(velocity * 0.6f);
        if (IsDescending)
            velocityAdittion = -(velocity * 0.4f);


        // Direction movement
        float directionX = inputHorizontal * (velocity + velocityAdittion) * Time.deltaTime;
        float directionZ = inputVertical * (velocity + velocityAdittion) * Time.deltaTime;
        float directionY = 0;

        // Jump handler
        if (isJumping)
        {

            // Apply inertia and smoothness when climbing the jump
            // It is not necessary when descending, as gravity itself will gradually pulls
            directionY = Mathf.SmoothStep(jumpForce, jumpForce * 0.30f, jumpElapsedTime / jumpTime) * Time.deltaTime;

            // Jump timer
            jumpElapsedTime += Time.deltaTime;
            if (jumpElapsedTime >= jumpTime)
            {
                isJumping = false;
                jumpElapsedTime = 0;
            }
        }

        // Add gravity to Y axis
        directionY = directionY - gravity * Time.deltaTime;


        // --- Character rotation --- 

        Vector3 forward = Camera.main.transform.forward;
        Vector3 right = Camera.main.transform.right;

        forward.y = 0;
        right.y = 0;

        forward.Normalize();
        right.Normalize();

        // Relate the front with the Z direction (depth) and right with X (lateral movement)
        forward = forward * directionZ;
        right = right * directionX;

        if (directionX != 0 || directionZ != 0)
        {
            float angle = Mathf.Atan2(forward.x + right.x, forward.z + right.z) * Mathf.Rad2Deg;
            Quaternion rotation = Quaternion.Euler(0, angle, 0);
            transform.rotation = Quaternion.Slerp(transform.rotation, rotation, 0.15f);
        }

        // --- End rotation ---


        Vector3 verticalDirection = Vector3.up * directionY;
        Vector3 horizontalDirection = forward + right;

        Vector3 moviment = verticalDirection + horizontalDirection;
        cc.Move(moviment);

    }


    //This function makes the character end his jump if he hits his head on something
    void HeadHittingDetect()
    {
        float headHitDistance = 1.1f;
        Vector3 ccCenter = transform.TransformPoint(cc.center);
        float hitCalc = cc.height / 2f * headHitDistance;

        // Uncomment this line to see the Ray drawed in your characters head
        // Debug.DrawRay(ccCenter, Vector3.up * headHeight, Color.red);

        if (Physics.Raycast(ccCenter, Vector3.up, hitCalc))
        {
            jumpElapsedTime = 0;
            isJumping = false;
        }
    }
    void OnTriggerEnter(Collider other)
    {
        //Debug.Log("Trigger entered with: " + other.gameObject.name);
        if (other.CompareTag("Stairs"))
        {
            //Debug.Log("Player entered stairs trigger.");
            OnStairs = true;
            stairEntryPosition = transform.position; // Save position at entry

        }
    }

    void OnTriggerExit(Collider other)
    {
        //Debug.Log("Trigger exited with: " + other.gameObject.name);
        if (other.CompareTag("Stairs"))
        {
            //Debug.Log("Player exited stairs trigger.");
            OnStairs = false;
            IsAscending = false;
            IsDescending = false;

        }
    }

    public void Teleport(Vector3 position)
    {
        transform.position = position;
    }

    private void OnEnable()
    {
        DialogueManager.OnDialogActiveChanged += HandleDialogToggle;
    }

    private void OnDisable()
    {
        DialogueManager.OnDialogActiveChanged -= HandleDialogToggle;
    }

private void HandleDialogToggle(bool active)
{
    isLocked = active;

    // Stop Animator root motion from moving the character while locked.
    if (animator) animator.applyRootMotion = !active;

    if (active)
    {
        // Nuke any residual input/anim state so the pose settles.
        inputHorizontal = 0f;
        inputVertical = 0f;
        isRunning = false;
        isCrouching = false;

        if (animator)
        {
            animator.SetBool("IsWalking", false);
            animator.SetBool("IsRunning", false);
            animator.SetBool("IsAscending", false);
            animator.SetBool("IsDescending", false);
        }
    }

    Debug.Log($"[ThirdPersonController] Dialog lock={(active ? "ON" : "OFF")} | applyRootMotion={(animator ? animator.applyRootMotion : false)}");
}

}
